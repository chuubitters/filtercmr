// FILTER CMR — Unity component that owns the tracking UDP socket (port 5065).
// The socket and parsing run on a background thread (UdpReceiverCore);
// this component only starts/stops it and hands the newest message to the main thread.

using System.Net;
using FilterCMR.Core;
using UnityEngine;

namespace FilterCMR.Networking
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class UDPReceiver : MonoBehaviour
    {
        [SerializeField] ApplicationSettings settings;
        [Tooltip("Start listening automatically when enabled.")]
        [SerializeField] bool autoStart = true;

        readonly UdpReceiverCore _core = new UdpReceiverCore();
        long _rateCount;
        float _rateTime;
        float _packetsPerSecond;
        bool _loggedError;

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }

        public ReceiverState State => _core.State;
        public string LastError => _core.LastError;
        public string LastParseError => _core.LastParseError;
        public int Port => _core.Port != 0 ? _core.Port : Settings.trackingPort;
        public long PacketsReceived => _core.PacketsReceived;
        public long MalformedPackets => _core.MalformedPackets;
        public long StalePackets => _core.StalePackets;
        public double SecondsSinceLastPacket => _core.SecondsSinceLastPacket;
        public float PacketsPerSecond => _packetsPerSecond;
        public int Restarts { get; private set; }

        void OnEnable()
        {
            if (autoStart) StartReceiver();
        }

        void OnDisable() => StopReceiver();      // also runs when leaving Play Mode
        void OnApplicationQuit() => StopReceiver();
        void OnDestroy() => StopReceiver();

        public bool StartReceiver()
        {
            var bind = Settings.listenOnAllInterfaces ? IPAddress.Any : IPAddress.Loopback;
            bool ok = _core.Start(Settings.trackingPort, bind);
            _rateCount = 0;
            _rateTime = Time.unscaledTime;
            _packetsPerSecond = 0;
            if (ok)
            {
                _loggedError = false;
                Debug.Log($"[FILTER CMR] UDP receiver listening on {bind}:{Settings.trackingPort}");
            }
            else if (!_loggedError)
            {
                _loggedError = true;
                Debug.LogError("[FILTER CMR] " + _core.LastError);
            }
            return ok;
        }

        public void StopReceiver()
        {
            if (_core.IsRunning) Debug.Log("[FILTER CMR] UDP receiver stopped");
            _core.Stop();
        }

        /// <summary>Stop and start again (R key). Also clears counters.</summary>
        public bool Restart()
        {
            Restarts++;
            StopReceiver();
            return StartReceiver();
        }

        /// <summary>Copies the newest unread message. Main thread, no allocation.</summary>
        public bool TryGetLatest(TrackingMessage dest) => _core.TryGetLatest(dest);

        void Update()
        {
            float now = Time.unscaledTime;
            float dt = now - _rateTime;
            if (dt >= 1f)
            {
                long total = _core.PacketsReceived;
                _packetsPerSecond = (total - _rateCount) / dt;
                _rateCount = total;
                _rateTime = now;
            }
        }
    }
}
