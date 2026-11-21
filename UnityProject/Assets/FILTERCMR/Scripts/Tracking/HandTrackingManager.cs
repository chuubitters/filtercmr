// FILTER CMR — main-thread owner of tracking state.
// Pulls the newest message from UDPReceiver each frame, tracks connection
// status (waiting / connected / lost / stopped), and exposes both hands.

using FilterCMR.Core;
using FilterCMR.Networking;
using UnityEngine;

namespace FilterCMR.Tracking
{
    public enum TrackerConnection
    {
        /// <summary>No packet received yet since start / restart.</summary>
        Waiting = 0,
        /// <summary>Packets arriving.</summary>
        Connected,
        /// <summary>Packets stopped arriving for longer than trackerTimeoutSeconds.</summary>
        Lost,
        /// <summary>Tracker said "bye" (clean shutdown).</summary>
        Stopped,
    }

    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class HandTrackingManager : MonoBehaviour
    {
        [SerializeField] ApplicationSettings settings;
        [SerializeField] UDPReceiver receiver;

        readonly TrackingMessage _latest = new TrackingMessage();
        readonly HandView _left = new HandView(Handedness.Left);
        readonly HandView _right = new HandView(Handedness.Right);
        bool _hasMessage;
        float _lastMessageTime = -1f;

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }
        public UDPReceiver Receiver { get => receiver; set => receiver = value; }

        public TrackerConnection Connection { get; private set; } = TrackerConnection.Waiting;
        /// <summary>Last accepted message (valid when HasMessage).</summary>
        public TrackingMessage Latest => _latest;
        public bool HasMessage => _hasMessage;
        public HandView Left => _left;
        public HandView Right => _right;
        public HandView Get(Handedness h) => h == Handedness.Left ? _left : _right;

        /// <summary>Whether the tracker mirrors x (from the last packet; true before any packet, matching the tracker default).</summary>
        public bool Mirrored => !_hasMessage || _latest.mirrored;
        public CameraStatus TrackerCameraStatus => _hasMessage ? _latest.cameraStatus : CameraStatus.Unknown;
        public TrackerStatus TrackerStatus => _hasMessage ? _latest.trackerStatus : TrackerStatus.Unknown;
        public int TrackerFrameWidth => _hasMessage ? _latest.frameWidth : 0;
        public int TrackerFrameHeight => _hasMessage ? _latest.frameHeight : 0;
        /// <summary>Time.unscaledTime of the last message, or -1.</summary>
        public float LastMessageTime => _lastMessageTime;
        public float SecondsSinceLastMessage => _lastMessageTime < 0 ? float.PositiveInfinity : Time.unscaledTime - _lastMessageTime;
        public int HandCount => (_left.Present ? 1 : 0) + (_right.Present ? 1 : 0);
        public long MessagesConsumed { get; private set; }

        public event System.Action<TrackerConnection> ConnectionChanged;

        void Update()
        {
            float now = Time.unscaledTime;
            if (receiver != null && receiver.TryGetLatest(_latest))
            {
                _hasMessage = true;
                _lastMessageTime = now;
                MessagesConsumed++;
                if (_latest.type == MessageType.Bye)
                {
                    ClearHands();
                    SetConnection(TrackerConnection.Stopped);
                }
                else
                {
                    SetConnection(TrackerConnection.Connected);
                    ApplyHand(_left, _latest.left, now);
                    ApplyHand(_right, _latest.right, now);
                }
            }
            else if (Connection == TrackerConnection.Connected && SecondsSinceLastMessage > Settings.trackerTimeoutSeconds)
            {
                ClearHands();
                SetConnection(TrackerConnection.Lost);
            }

            float dt = Time.unscaledDeltaTime;
            float rate = Settings.displaySmoothing;
            _left.Smooth(dt, rate);
            _right.Smooth(dt, rate);
        }

        static void ApplyHand(HandView view, HandData data, float now)
        {
            if (data.present) view.Apply(data, now);
            else view.Clear();
        }

        void ClearHands()
        {
            _left.Clear();
            _right.Clear();
        }

        void SetConnection(TrackerConnection c)
        {
            if (c == Connection) return;
            Connection = c;
            Debug.Log("[FILTER CMR] tracker " + c);
            ConnectionChanged?.Invoke(c);
        }

        /// <summary>Forget all tracking state (R key). The next packet reconnects.</summary>
        public void ResetState()
        {
            ClearHands();
            _hasMessage = false;
            _latest.Reset();
            _lastMessageTime = -1f;
            SetConnection(TrackerConnection.Waiting);
        }
    }
}
