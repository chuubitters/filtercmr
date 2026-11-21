// FILTER CMR — exposes the current two-hand gesture and the fingertips it uses.
//
// Python classifies each hand ("point", "peace", ...). This component only
// combines both hands' poses through GestureStateMachine (hold-time confirm,
// grace-period release) — it does not look at finger geometry itself.
//
// Fingertip positions are normalised image coordinates (0–1, origin top-left,
// y down), the same space as the landmarks. Milestone 3/4 build the ribbon and
// frame masks from these.

using FilterCMR.Core;
using FilterCMR.Networking;
using FilterCMR.Tracking;
using UnityEngine;

namespace FilterCMR.Gestures
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GestureManager : MonoBehaviour
    {
        [SerializeField] ApplicationSettings settings;
        [SerializeField] HandTrackingManager tracking;

        readonly GestureStateMachine _machine = new GestureStateMachine();

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }
        public HandTrackingManager Tracking { get => tracking; set => tracking = value; }

        /// <summary>Confirmed gesture (None while confirming or idle).</summary>
        public GestureType CurrentGesture => _machine.Current;
        public GestureState State => _machine.State;
        /// <summary>Gesture being confirmed or held.</summary>
        public GestureType PendingGesture => _machine.Tracked;
        /// <summary>What the hands show this frame, before debouncing.</summary>
        public GestureType Candidate => _machine.Candidate;
        /// <summary>0..1 confirm progress / 1 active / 1..0 releasing.</summary>
        public float Progress => _machine.Progress;

        public HandStatus LeftStatus => tracking != null ? tracking.Left.Status : HandStatus.Lost;
        public HandStatus RightStatus => tracking != null ? tracking.Right.Status : HandStatus.Lost;
        public HandPose LeftPose => tracking != null && tracking.Left.Present ? tracking.Left.Pose : HandPose.Unknown;
        public HandPose RightPose => tracking != null && tracking.Right.Present ? tracking.Right.Pose : HandPose.Unknown;

        public bool HasLeft => LeftStatus != HandStatus.Lost;
        public bool HasRight => RightStatus != HandStatus.Lost;
        public Vector2 LeftIndexTip => Tip(Handedness.Left, HandLandmark.IndexTip);
        public Vector2 RightIndexTip => Tip(Handedness.Right, HandLandmark.IndexTip);
        public Vector2 LeftMiddleTip => Tip(Handedness.Left, HandLandmark.MiddleTip);
        public Vector2 RightMiddleTip => Tip(Handedness.Right, HandLandmark.MiddleTip);
        public Vector2 LeftThumbTip => Tip(Handedness.Left, HandLandmark.ThumbTip);
        public Vector2 RightThumbTip => Tip(Handedness.Right, HandLandmark.ThumbTip);

        /// <summary>Fingers forming the frame gesture (from the settings asset).</summary>
        public FrameFingers FrameFingers => Settings.frameFingers;
        /// <summary>Landmark of the frame's second corner per hand: thumb tip (4) or middle tip (12).</summary>
        public int FrameSecondLandmark => GestureStateMachine.FrameSecondTip(Settings.frameFingers);

        /// <summary>Fired when <see cref="CurrentGesture"/> changes: (previous, current).</summary>
        public event System.Action<GestureType, GestureType> GestureChanged;

        /// <summary>Smoothed fingertip position; false if that hand is not present.</summary>
        public bool TryGetFingertip(Handedness hand, int landmark, out Vector2 position)
        {
            position = default;
            if (tracking == null) return false;
            HandView v = tracking.Get(hand);
            if (!v.Present) return false;
            position = v.Smoothed[landmark];
            return true;
        }

        Vector2 Tip(Handedness h, int lm) => TryGetFingertip(h, lm, out var p) ? p : new Vector2(float.NaN, float.NaN);

        void Update()
        {
            var s = Settings;
            _machine.ConfirmSeconds = s.gestureConfirmSeconds;
            _machine.ReleaseSeconds = s.gestureReleaseSeconds;
            _machine.FrameFingers = s.frameFingers;
            GestureType before = _machine.Current;
            if (_machine.Update(LeftStatus, LeftPose, RightStatus, RightPose, Time.unscaledDeltaTime))
            {
                Debug.Log($"[FILTER CMR] gesture {Label(before)} -> {Label(_machine.Current)}");
                GestureChanged?.Invoke(before, _machine.Current);
            }
        }

        public void ResetGesture()
        {
            GestureType before = _machine.Current;
            _machine.Reset();
            if (before != GestureType.None) GestureChanged?.Invoke(before, GestureType.None);
        }

        public static string Label(GestureType g)
        {
            switch (g)
            {
                case GestureType.TwoFingerPoint: return "TWO FINGER POINT";
                case GestureType.TwoFingerFrame: return "TWO FINGER FRAME";
                default: return "NONE";
            }
        }

        /// <summary>Python's pose name (as sent in the packet).</summary>
        public static string PoseName(HandPose p)
        {
            switch (p)
            {
                case HandPose.Point: return "point";
                case HandPose.Peace: return "peace";
                case HandPose.Frame: return "frame";
                case HandPose.Open: return "open";
                case HandPose.Fist: return "fist";
                case HandPose.Other: return "other";
                default: return "—";
            }
        }
    }
}
