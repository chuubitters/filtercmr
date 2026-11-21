// FILTER CMR — UDP receive loop for tracking messages (pure C#, no UnityEngine).
//
// Threading model
//   * One background thread blocks on Socket.Receive (with a short timeout so
//     it can notice Stop()), parses each datagram with its own HandDataParser
//     into a private TrackingMessage, then copies it into a "pending" slot
//     under a lock (latest wins: older unread messages are overwritten).
//   * The main thread calls TryGetLatest(), which copies the pending slot into
//     the caller's message under the same lock. Both copies are fixed-size
//     (no allocation), so the main thread never parses JSON or touches sockets.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FilterCMR.Networking
{
    public enum ReceiverState { Stopped = 0, Listening, Error }

    public sealed class UdpReceiverCore : IDisposable
    {
        /// <summary>A sequence number this far below the last one means the tracker restarted.</summary>
        public const long SeqRestartThreshold = 1000;
        /// <summary>After this long without packets, any seq is accepted (tracker may have restarted).</summary>
        public const double SeqResetAfterSilenceSeconds = 1.0;

        readonly object _lock = new object();
        readonly TrackingMessage _pending = new TrackingMessage();
        bool _hasPending;

        Socket _socket;
        Thread _thread;
        volatile bool _running;

        // Stats — written by the receive thread, read by the main thread.
        long _packets, _malformed, _stale, _skippedHands, _lastPacketTimestamp;
        volatile string _lastError = "";
        volatile string _lastParseError = "";
        volatile int _state = (int)ReceiverState.Stopped;

        public int Port { get; private set; }
        public IPAddress BindAddress { get; private set; } = IPAddress.Loopback;

        public ReceiverState State => (ReceiverState)_state;
        public string LastError => _lastError;
        public string LastParseError => _lastParseError;
        public long PacketsReceived => Interlocked.Read(ref _packets);
        public long MalformedPackets => Interlocked.Read(ref _malformed);
        public long StalePackets => Interlocked.Read(ref _stale);
        public long SkippedHands => Interlocked.Read(ref _skippedHands);

        /// <summary>Seconds since the last valid packet, or +Infinity if none yet.</summary>
        public double SecondsSinceLastPacket
        {
            get
            {
                long t = Interlocked.Read(ref _lastPacketTimestamp);
                if (t == 0) return double.PositiveInfinity;
                return (Stopwatch.GetTimestamp() - t) / (double)Stopwatch.Frequency;
            }
        }

        public bool IsRunning => _running;

        /// <summary>Opens the socket and starts the receive thread. Returns false (and sets LastError) on failure.</summary>
        public bool Start(int port, IPAddress bindAddress = null)
        {
            Stop();
            Port = port;
            BindAddress = bindAddress ?? IPAddress.Loopback;
            Interlocked.Exchange(ref _packets, 0);
            Interlocked.Exchange(ref _malformed, 0);
            Interlocked.Exchange(ref _stale, 0);
            Interlocked.Exchange(ref _skippedHands, 0);
            Interlocked.Exchange(ref _lastPacketTimestamp, 0);
            lock (_lock) { _hasPending = false; _pending.Reset(); }
            _lastParseError = "";

            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                // No SO_REUSEADDR: if another program (e.g. tools/udp_monitor.py) owns the port we want to know.
                s.ReceiveTimeout = 250; // ms; lets the thread notice Stop() even if Close() doesn't unblock it
                s.ReceiveBufferSize = 1 << 18;
                s.Bind(new IPEndPoint(BindAddress, port));
                _socket = s;
            }
            catch (SocketException ex)
            {
                _lastError = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                    ? $"UDP port {port} is already in use (is tools/udp_monitor.py or another Unity instance running?)"
                    : $"cannot open UDP port {port}: {ex.Message}";
                _state = (int)ReceiverState.Error;
                return false;
            }
            catch (Exception ex)
            {
                _lastError = $"cannot open UDP port {port}: {ex.Message}";
                _state = (int)ReceiverState.Error;
                return false;
            }

            _lastError = "";
            _running = true;
            _state = (int)ReceiverState.Listening;
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "FilterCMR.UdpReceiver" };
            _thread.Start();
            return true;
        }

        /// <summary>Stops the thread and releases the socket. Safe to call repeatedly.</summary>
        public void Stop()
        {
            _running = false;
            Socket s = _socket;
            _socket = null;
            if (s != null)
            {
                try { s.Close(); } catch (Exception) { /* already closed */ }
            }
            Thread t = _thread;
            _thread = null;
            if (t != null && t.IsAlive && t != Thread.CurrentThread)
            {
                if (!t.Join(1000))
                    _lastError = "receive thread did not stop within 1 s";
            }
            if (_state != (int)ReceiverState.Error) _state = (int)ReceiverState.Stopped;
        }

        public void Dispose() => Stop();

        /// <summary>Copies the newest unread message into <paramref name="dest"/>. Main thread; no allocation.</summary>
        public bool TryGetLatest(TrackingMessage dest)
        {
            lock (_lock)
            {
                if (!_hasPending) return false;
                dest.CopyFrom(_pending);
                _hasPending = false;
                return true;
            }
        }

        void ReceiveLoop()
        {
            var buffer = new byte[65536];
            var parser = new HandDataParser();
            var msg = new TrackingMessage();
            long lastSeq = -1;
            long lastAcceptTs = 0;
            Socket socket = _socket;

            while (_running)
            {
                int n;
                try
                {
                    n = socket.Receive(buffer, 0, buffer.Length, SocketFlags.None);
                }
                catch (SocketException ex)
                {
                    if (!_running) break;
                    if (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.WouldBlock)
                        continue;
                    // ConnectionReset can be reported for UDP on some platforms; it is harmless.
                    if (ex.SocketErrorCode == SocketError.ConnectionReset) continue;
                    _lastError = "socket error: " + ex.SocketErrorCode;
                    Thread.Sleep(50);
                    continue;
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!_running) break;
                    _lastError = "receive failed: " + ex.Message;
                    Thread.Sleep(50);
                    continue;
                }

                if (n <= 0) continue;
                if (!parser.TryParse(buffer, n, msg))
                {
                    Interlocked.Increment(ref _malformed);
                    _lastParseError = parser.LastError;
                    continue;
                }
                if (parser.LastSkippedHands > 0) Interlocked.Add(ref _skippedHands, parser.LastSkippedHands);

                long now = Stopwatch.GetTimestamp();
                bool silent = lastAcceptTs == 0 || (now - lastAcceptTs) / (double)Stopwatch.Frequency > SeqResetAfterSilenceSeconds;
                if (!silent && lastSeq >= 0 && msg.seq <= lastSeq && lastSeq - msg.seq < SeqRestartThreshold)
                {
                    Interlocked.Increment(ref _stale); // duplicate or reordered
                    continue;
                }
                lastSeq = msg.type == MessageType.Bye ? -1 : msg.seq; // after "bye", the next tracker starts at seq 1
                lastAcceptTs = now;

                lock (_lock)
                {
                    _pending.CopyFrom(msg);
                    _hasPending = true;
                }
                Interlocked.Increment(ref _packets);
                Interlocked.Exchange(ref _lastPacketTimestamp, now);
            }
        }
    }
}
