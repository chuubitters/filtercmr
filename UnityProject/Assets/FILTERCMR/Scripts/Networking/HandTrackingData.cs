// FILTER CMR — data model for the Python tracker's UDP JSON messages (protocol v1).
//
// Mirrors HandTracking/udp_sender.py exactly. See Documentation/UDP_PROTOCOL.md.
// Pure C# (no UnityEngine) so it can be unit-tested outside Unity.
//
// Fields that the Python protocol does NOT provide are intentionally absent:
// there is no per-landmark confidence/visibility and no wall-clock timestamp
// (only the sender's monotonic clock, "t", which is meaningful as a difference).

namespace FilterCMR.Networking
{
    /// <summary>"type" field.</summary>
    public enum MessageType { Unknown = 0, Frame, Heartbeat, Bye }

    /// <summary>"hands[].id" — the USER's hand.</summary>
    public enum Handedness { Left = 0, Right = 1 }

    /// <summary>"hands[].state".</summary>
    public enum HandState { Tracked = 0, Held }

    /// <summary>"hands[].pose" — per-hand classification made by Python (finger_state.classify_pose).</summary>
    public enum HandPose { Unknown = 0, Point, Peace, Open, Fist, Other }

    /// <summary>"cam.status".</summary>
    public enum CameraStatus { Unknown = 0, Starting, Ok, Reconnecting, Error, Stopped }

    /// <summary>"trk.status".</summary>
    public enum TrackerStatus { Unknown = 0, Starting, Ok, Error, Restarting, Stopped }

    /// <summary>MediaPipe hand landmark indices.</summary>
    public static class HandLandmark
    {
        public const int Count = 21;
        public const int Wrist = 0;
        public const int ThumbCmc = 1, ThumbMcp = 2, ThumbIp = 3, ThumbTip = 4;
        public const int IndexMcp = 5, IndexPip = 6, IndexDip = 7, IndexTip = 8;
        public const int MiddleMcp = 9, MiddlePip = 10, MiddleDip = 11, MiddleTip = 12;
        public const int RingMcp = 13, RingPip = 14, RingDip = 15, RingTip = 16;
        public const int PinkyMcp = 17, PinkyPip = 18, PinkyDip = 19, PinkyTip = 20;

        public static bool IsFingertip(int i) => i == ThumbTip || i == IndexTip || i == MiddleTip || i == RingTip || i == PinkyTip;
    }

    /// <summary>One entry of "hands". Pre-allocated and reused; never resized.</summary>
    public sealed class HandData
    {
        public const int FloatsPerLandmark = 3;
        public const int LandmarkFloats = HandLandmark.Count * FloatsPerLandmark; // 63

        /// <summary>True if this hand was present in the message.</summary>
        public bool present;
        public Handedness handedness;
        /// <summary>"score": handedness confidence (0–1). This is the only confidence Python sends.</summary>
        public float score;
        public HandState state;
        /// <summary>"age": ms since the hand was last actually detected (0 when tracked).</summary>
        public float ageMs;
        /// <summary>"ext": thumb, index, middle, ring, pinky.</summary>
        public readonly bool[] extended = new bool[5];
        public HandPose pose;
        /// <summary>"lm": 21 × (x, y, z). x/y normalised 0–1, origin top-left, y down; x pre-mirrored if cam.mirrored.</summary>
        public readonly float[] landmarks = new float[LandmarkFloats];

        public HandData(Handedness h) { handedness = h; }

        public float X(int landmark) => landmarks[landmark * 3];
        public float Y(int landmark) => landmarks[landmark * 3 + 1];
        public float Z(int landmark) => landmarks[landmark * 3 + 2];

        public void Clear()
        {
            present = false;
            score = 0f;
            state = HandState.Tracked;
            ageMs = 0f;
            pose = HandPose.Unknown;
            System.Array.Clear(extended, 0, extended.Length);
            System.Array.Clear(landmarks, 0, landmarks.Length);
        }

        public void CopyFrom(HandData o)
        {
            present = o.present;
            handedness = o.handedness;
            score = o.score;
            state = o.state;
            ageMs = o.ageMs;
            pose = o.pose;
            System.Array.Copy(o.extended, extended, extended.Length);
            System.Array.Copy(o.landmarks, landmarks, landmarks.Length);
        }
    }

    /// <summary>One complete UDP message. Pre-allocated and reused.</summary>
    public sealed class TrackingMessage
    {
        public const int SupportedVersion = 1;

        public MessageType type;
        /// <summary>"v"</summary>
        public int version;
        /// <summary>"seq": increases by one per packet, restarts at 1 when the tracker restarts.</summary>
        public long seq;
        /// <summary>"t": sender monotonic clock in ms (differences only).</summary>
        public double senderTimeMs;

        // "cam"
        public CameraStatus cameraStatus;
        public int frameWidth;
        public int frameHeight;
        public bool mirrored;

        // "trk"
        public TrackerStatus trackerStatus;
        public float trackerFps;
        public float latencyMs;

        // "hands" — at most one of each (Python guarantees this; the parser enforces it).
        public readonly HandData left = new HandData(Handedness.Left);
        public readonly HandData right = new HandData(Handedness.Right);

        public HandData Get(Handedness h) => h == Handedness.Left ? left : right;
        public int HandCount => (left.present ? 1 : 0) + (right.present ? 1 : 0);

        public void Reset()
        {
            type = MessageType.Unknown;
            version = 0;
            seq = 0;
            senderTimeMs = 0;
            cameraStatus = CameraStatus.Unknown;
            frameWidth = frameHeight = 0;
            mirrored = false;
            trackerStatus = TrackerStatus.Unknown;
            trackerFps = latencyMs = 0f;
            left.Clear();
            right.Clear();
        }

        public void CopyFrom(TrackingMessage o)
        {
            type = o.type;
            version = o.version;
            seq = o.seq;
            senderTimeMs = o.senderTimeMs;
            cameraStatus = o.cameraStatus;
            frameWidth = o.frameWidth;
            frameHeight = o.frameHeight;
            mirrored = o.mirrored;
            trackerStatus = o.trackerStatus;
            trackerFps = o.trackerFps;
            latencyMs = o.latencyMs;
            left.CopyFrom(o.left);
            right.CopyFrom(o.right);
        }
    }
}
