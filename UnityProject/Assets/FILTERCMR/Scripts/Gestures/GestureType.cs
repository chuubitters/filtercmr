// FILTER CMR — gesture enums. Pure C#.

namespace FilterCMR.Gestures
{
    /// <summary>Two-hand interaction derived from Python's per-hand poses.</summary>
    public enum GestureType
    {
        None = 0,
        /// <summary>Both hands report pose "point" (index finger only).</summary>
        TwoFingerPoint,
        /// <summary>Both hands make the frame pose: "frame" (thumb + index L shape, default)
        /// or "peace" (index + middle), see <see cref="FrameFingers"/>.</summary>
        TwoFingerFrame,
    }

    /// <summary>Which two fingers of each hand form the frame (and its four corners).</summary>
    public enum FrameFingers
    {
        /// <summary>"L" shape: thumb tip (4) + index tip (8) of each hand. Python pose "frame".</summary>
        IndexAndThumb = 0,
        /// <summary>Peace sign: index tip (8) + middle tip (12) of each hand. Python pose "peace".</summary>
        IndexAndMiddle = 1,
    }

    public enum GestureState
    {
        /// <summary>No gesture.</summary>
        Idle = 0,
        /// <summary>Candidate seen; waiting for it to be held for the confirm time.</summary>
        Confirming,
        /// <summary>Gesture confirmed and currently held.</summary>
        Active,
        /// <summary>Gesture no longer seen; kept for a grace period so brief dropouts don't break it.</summary>
        Releasing,
    }

    /// <summary>Per-hand presence as seen by Unity.</summary>
    public enum HandStatus
    {
        /// <summary>Not in the latest packet (or tracker offline).</summary>
        Lost = 0,
        /// <summary>Present with state "tracked".</summary>
        Detected,
        /// <summary>Present with state "held" (briefly lost by MediaPipe, last pose repeated).</summary>
        Held,
    }
}
