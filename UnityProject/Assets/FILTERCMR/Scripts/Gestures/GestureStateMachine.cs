// FILTER CMR — interprets Python's per-hand poses as a two-hand gesture. Pure C#.
//
// This is NOT a second classifier: finger geometry is classified once, in
// Python (finger_state.classify_pose -> "pose"). Unity only combines the two
// hands' poses and debounces the result over time:
//
//   Idle ──candidate seen, both hands "tracked"──▶ Confirming
//   Confirming ──held for ConfirmSeconds──▶ Active
//   Confirming ──candidate changes/disappears──▶ Confirming(new) / Idle
//   Active ──candidate no longer matches──▶ Releasing (gesture still reported)
//   Releasing ──matches again──▶ Active
//   Releasing ──ReleaseSeconds elapsed──▶ Confirming(new candidate) / Idle
//
// "Held" hands (MediaPipe briefly lost them) can keep an active gesture alive
// but cannot start a new one.

using FilterCMR.Networking;

namespace FilterCMR.Gestures
{
    public sealed class GestureStateMachine
    {
        public float ConfirmSeconds = 0.20f;
        public float ReleaseSeconds = 0.25f;

        public GestureState State { get; private set; } = GestureState.Idle;
        /// <summary>The gesture being confirmed (Confirming) or held (Active/Releasing).</summary>
        public GestureType Tracked { get; private set; } = GestureType.None;
        /// <summary>What the current hands show right now, without debouncing.</summary>
        public GestureType Candidate { get; private set; } = GestureType.None;
        float _timer;

        /// <summary>The confirmed gesture (None unless Active or Releasing).</summary>
        public GestureType Current =>
            State == GestureState.Active || State == GestureState.Releasing ? Tracked : GestureType.None;

        /// <summary>0..1 while confirming, 1 when active, fading 1..0 while releasing.</summary>
        public float Progress
        {
            get
            {
                switch (State)
                {
                    case GestureState.Confirming: return ConfirmSeconds <= 0 ? 1f : Clamp01(_timer / ConfirmSeconds);
                    case GestureState.Active: return 1f;
                    case GestureState.Releasing: return ReleaseSeconds <= 0 ? 0f : 1f - Clamp01(_timer / ReleaseSeconds);
                    default: return 0f;
                }
            }
        }

        public static GestureType Classify(HandStatus left, HandPose leftPose, HandStatus right, HandPose rightPose)
        {
            if (left == HandStatus.Lost || right == HandStatus.Lost) return GestureType.None;
            if (leftPose == HandPose.Point && rightPose == HandPose.Point) return GestureType.TwoFingerPoint;
            if (leftPose == HandPose.Peace && rightPose == HandPose.Peace) return GestureType.TwoFingerFrame;
            return GestureType.None;
        }

        /// <summary>Advance by <paramref name="dt"/> seconds. Returns true if <see cref="Current"/> changed.</summary>
        public bool Update(HandStatus left, HandPose leftPose, HandStatus right, HandPose rightPose, float dt)
        {
            if (dt < 0) dt = 0;
            GestureType before = Current;
            GestureType cand = Classify(left, leftPose, right, rightPose);
            bool canStart = left == HandStatus.Detected && right == HandStatus.Detected;
            Candidate = cand;

            switch (State)
            {
                case GestureState.Idle:
                    if (cand != GestureType.None && canStart) BeginConfirm(cand);
                    break;

                case GestureState.Confirming:
                    if (cand == Tracked && cand != GestureType.None)
                    {
                        _timer += dt;
                        if (_timer >= ConfirmSeconds) { State = GestureState.Active; _timer = 0; }
                    }
                    else if (cand != GestureType.None && canStart) BeginConfirm(cand);
                    else GoIdle();
                    break;

                case GestureState.Active:
                    if (cand != Tracked) { State = GestureState.Releasing; _timer = dt; CheckReleaseDone(cand, canStart); }
                    break;

                case GestureState.Releasing:
                    if (cand == Tracked) { State = GestureState.Active; _timer = 0; }
                    else { _timer += dt; CheckReleaseDone(cand, canStart); }
                    break;
            }
            return Current != before;
        }

        void CheckReleaseDone(GestureType cand, bool canStart)
        {
            if (_timer < ReleaseSeconds) return;
            if (cand != GestureType.None && canStart) BeginConfirm(cand);
            else GoIdle();
        }

        void BeginConfirm(GestureType g)
        {
            State = GestureState.Confirming;
            Tracked = g;
            _timer = 0;
            if (ConfirmSeconds <= 0) State = GestureState.Active;
        }

        void GoIdle()
        {
            State = GestureState.Idle;
            Tracked = GestureType.None;
            _timer = 0;
        }

        public void Reset()
        {
            GoIdle();
            Candidate = GestureType.None;
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
