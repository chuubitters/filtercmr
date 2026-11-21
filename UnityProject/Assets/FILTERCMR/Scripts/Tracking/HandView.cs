// FILTER CMR — Unity-side view of one hand: latest landmarks from Python plus
// a display-smoothed copy for rendering. Coordinates are normalised image
// coordinates exactly as sent by Python (0–1, origin top-left, y down, already
// mirrored if the tracker mirrors).

using FilterCMR.Gestures;
using FilterCMR.Networking;
using UnityEngine;

namespace FilterCMR.Tracking
{
    public sealed class HandView
    {
        public readonly Handedness handedness;

        public bool Present { get; private set; }
        public HandState State { get; private set; }
        public HandPose Pose { get; private set; } = HandPose.Unknown;
        /// <summary>Handedness confidence from Python (0–1).</summary>
        public float Score { get; private set; }
        public float AgeMs { get; private set; }
        public readonly bool[] Extended = new bool[5];

        /// <summary>Latest landmarks from the tracker (normalised, y down).</summary>
        public readonly Vector2[] Target = new Vector2[HandLandmark.Count];
        /// <summary>Landmarks smoothed toward Target every render frame. Use these for drawing.</summary>
        public readonly Vector2[] Smoothed = new Vector2[HandLandmark.Count];

        /// <summary>Time.unscaledTime when the hand (re)appeared.</summary>
        public float AppearedAt { get; private set; }

        public HandStatus Status => !Present ? HandStatus.Lost : State == HandState.Held ? HandStatus.Held : HandStatus.Detected;

        public HandView(Handedness h) { handedness = h; }

        public Vector2 Landmark(int index) => Smoothed[index];

        internal void Apply(HandData d, float now)
        {
            bool appeared = !Present;
            Present = true;
            State = d.state;
            Pose = d.pose;
            Score = d.score;
            AgeMs = d.ageMs;
            for (int i = 0; i < Extended.Length; i++) Extended[i] = d.extended[i];
            for (int i = 0; i < HandLandmark.Count; i++)
                Target[i] = new Vector2(d.landmarks[i * 3], d.landmarks[i * 3 + 1]);
            if (appeared)
            {
                // Snap on (re)appearance so the hand does not slide in from its old position.
                System.Array.Copy(Target, Smoothed, Target.Length);
                AppearedAt = now;
            }
        }

        internal void Smooth(float dt, float rate)
        {
            if (!Present) return;
            if (rate <= 0f || dt <= 0f)
            {
                System.Array.Copy(Target, Smoothed, Target.Length);
                return;
            }
            float a = 1f - Mathf.Exp(-rate * dt); // frame-rate independent exponential smoothing
            for (int i = 0; i < HandLandmark.Count; i++)
                Smoothed[i] = Vector2.LerpUnclamped(Smoothed[i], Target[i], a);
        }

        internal void Clear()
        {
            Present = false;
            Pose = HandPose.Unknown;
            State = HandState.Tracked;
            Score = 0f;
            AgeMs = 0f;
            for (int i = 0; i < Extended.Length; i++) Extended[i] = false;
        }
    }
}
