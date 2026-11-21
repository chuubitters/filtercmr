// FILTER CMR — gesture enums. Pure C#.

namespace FilterCMR.Gestures
{
    /// <summary>Two-hand interaction derived from Python's per-hand poses.</summary>
    public enum GestureType
    {
        None = 0,
        /// <summary>Both hands report pose "point" (index finger only).</summary>
        TwoFingerPoint,
        /// <summary>Both hands report pose "peace" (index + middle).</summary>
        TwoFingerFrame,
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
