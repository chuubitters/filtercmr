// FILTER CMR — receiver for the optional Python video forwarding (port 5066).
// Pure C# (no UnityEngine). Format: HandTracking/video_stream.py.
//
// Datagram = 20-byte big-endian header + JPEG slice:
//   "FCV1" | frame_id u32 | index u16 | count u16 | total_len u32 | width u16 | height u16
//
// Frames are reassembled from chunks; a frame is delivered only when every
// chunk arrived. A newer frame_id abandons any older incomplete frame.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FilterCMR.Networking
{
    public sealed class VideoFrameAssembler
    {
        public const int HeaderSize = 20;
        public const int MaxFrameBytes = 4 * 1024 * 1024;
        public const int MaxChunks = 1024;

        byte[] _buffer = new byte[256 * 1024];
        readonly bool[] _got = new bool[MaxChunks];
        bool _active;
        uint _frameId;
        int _count, _received, _total;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CompletedLength { get; private set; }
        public uint CompletedFrameId { get; private set; }
        public long DroppedFrames { get; private set; }
        public long BadDatagrams { get; private set; }

        /// <summary>Buffer that holds the last completed JPEG in [0, CompletedLength).</summary>
        public byte[] Buffer => _buffer;

        static uint U32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        static int U16(byte[] b, int o) => b[o] << 8 | b[o + 1];

        /// <summary>Feed one datagram. Returns true when it completed a frame.</summary>
        public bool Accept(byte[] d, int len)
        {
            if (d == null || len < HeaderSize || d[0] != 'F' || d[1] != 'C' || d[2] != 'V' || d[3] != '1')
            {
                BadDatagrams++;
                return false;
            }
            uint fid = U32(d, 4);
            int index = U16(d, 8), count = U16(d, 10);
            int total = (int)Math.Min(U32(d, 12), int.MaxValue);
            int w = U16(d, 16), h = U16(d, 18);
            int payload = len - HeaderSize;
            if (count == 0 || count > MaxChunks || index >= count || total <= 0 || total > MaxFrameBytes || payload <= 0)
            {
                BadDatagrams++;
                return false;
            }

            if (!_active || fid != _frameId)
            {
                // Ignore chunks of frames older than the one in progress (wrap-safe comparison).
                if (_active && (int)(fid - _frameId) < 0) return false;
                if (_active && _received < _count) DroppedFrames++;
                StartFrame(fid, count, total, w, h);
            }
            else if (count != _count || total != _total)
            {
                BadDatagrams++;
                return false;
            }

            if (_got[index]) return false; // duplicate
            // All chunks except the last carry the same size (video_stream.CHUNK_PAYLOAD).
            int offset = index == count - 1 ? total - payload : index * payload;
            if (offset < 0 || offset + payload > total)
            {
                BadDatagrams++;
                return false;
            }
            System.Buffer.BlockCopy(d, HeaderSize, _buffer, offset, payload);
            _got[index] = true;
            _received++;
            if (_received < _count) return false;

            CompletedLength = _total;
            CompletedFrameId = _frameId;
            Width = w;
            Height = h;
            _active = false;
            return true;
        }

        void StartFrame(uint fid, int count, int total, int w, int h)
        {
            if (_buffer.Length < total) _buffer = new byte[Math.Max(total, _buffer.Length * 2)];
            Array.Clear(_got, 0, count);
            _frameId = fid;
            _count = count;
            _total = total;
            _received = 0;
            _active = true;
            Width = w;
            Height = h;
        }
    }

    public sealed class VideoReceiverCore : IDisposable
    {
        readonly object _lock = new object();
        byte[] _latest = new byte[0];
        int _latestLength;
        int _latestW, _latestH;
        bool _hasNew;

        Socket _socket;
        Thread _thread;
        volatile bool _running;
        long _frames, _lastFrameTs, _dropped, _bad;
        volatile string _lastError = "";
        volatile int _state;

        public ReceiverState State => (ReceiverState)_state;
        public string LastError => _lastError;
        public int Port { get; private set; }
        public long FramesReceived => Interlocked.Read(ref _frames);
        public long DroppedFrames => Interlocked.Read(ref _dropped);
        public long BadDatagrams => Interlocked.Read(ref _bad);

        public double SecondsSinceLastFrame
        {
            get
            {
                long t = Interlocked.Read(ref _lastFrameTs);
                return t == 0 ? double.PositiveInfinity : (Stopwatch.GetTimestamp() - t) / (double)Stopwatch.Frequency;
            }
        }

        public bool Start(int port, IPAddress bindAddress = null)
        {
            Stop();
            Port = port;
            Interlocked.Exchange(ref _frames, 0);
            Interlocked.Exchange(ref _lastFrameTs, 0);
            lock (_lock) { _hasNew = false; _latestLength = 0; }
            try
            {
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.ReceiveTimeout = 250;
                s.ReceiveBufferSize = 1 << 21; // a few frames of headroom
                s.Bind(new IPEndPoint(bindAddress ?? IPAddress.Loopback, port));
                _socket = s;
            }
            catch (Exception ex)
            {
                _lastError = $"cannot open video port {port}: {ex.Message}";
                _state = (int)ReceiverState.Error;
                return false;
            }
            _lastError = "";
            _running = true;
            _state = (int)ReceiverState.Listening;
            _thread = new Thread(Loop) { IsBackground = true, Name = "FilterCMR.VideoReceiver" };
            _thread.Start();
            return true;
        }

        public void Stop()
        {
            _running = false;
            Socket s = _socket;
            _socket = null;
            if (s != null) { try { s.Close(); } catch (Exception) { } }
            Thread t = _thread;
            _thread = null;
            if (t != null && t.IsAlive && t != Thread.CurrentThread) t.Join(1000);
            if (_state != (int)ReceiverState.Error) _state = (int)ReceiverState.Stopped;
        }

        public void Dispose() => Stop();

        /// <summary>
        /// Copies the newest complete JPEG into <paramref name="dest"/> (resized only when a frame is larger
        /// than any seen before). Main thread.
        /// </summary>
        public bool TryGetLatest(ref byte[] dest, out int length, out int width, out int height)
        {
            lock (_lock)
            {
                length = _latestLength;
                width = _latestW;
                height = _latestH;
                if (!_hasNew) return false;
                if (dest == null || dest.Length != _latestLength) dest = new byte[_latestLength];
                System.Buffer.BlockCopy(_latest, 0, dest, 0, _latestLength);
                _hasNew = false;
                return true;
            }
        }

        void Loop()
        {
            var buf = new byte[65536];
            var asm = new VideoFrameAssembler();
            Socket socket = _socket;
            while (_running)
            {
                int n;
                try { n = socket.Receive(buf, 0, buf.Length, SocketFlags.None); }
                catch (SocketException ex)
                {
                    if (!_running) break;
                    if (ex.SocketErrorCode == SocketError.TimedOut || ex.SocketErrorCode == SocketError.WouldBlock
                        || ex.SocketErrorCode == SocketError.ConnectionReset) continue;
                    _lastError = "socket error: " + ex.SocketErrorCode;
                    Thread.Sleep(50);
                    continue;
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    if (!_running) break;
                    _lastError = ex.Message;
                    Thread.Sleep(50);
                    continue;
                }

                if (!asm.Accept(buf, n))
                {
                    Interlocked.Exchange(ref _dropped, asm.DroppedFrames);
                    Interlocked.Exchange(ref _bad, asm.BadDatagrams);
                    continue;
                }
                lock (_lock)
                {
                    if (_latest.Length < asm.CompletedLength) _latest = new byte[asm.CompletedLength];
                    System.Buffer.BlockCopy(asm.Buffer, 0, _latest, 0, asm.CompletedLength);
                    _latestLength = asm.CompletedLength;
                    _latestW = asm.Width;
                    _latestH = asm.Height;
                    _hasNew = true;
                }
                Interlocked.Increment(ref _frames);
                Interlocked.Exchange(ref _lastFrameTs, Stopwatch.GetTimestamp());
                Interlocked.Exchange(ref _dropped, asm.DroppedFrames);
            }
        }
    }
}
