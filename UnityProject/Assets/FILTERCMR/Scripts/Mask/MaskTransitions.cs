// FILTER CMR — decides which mask should be visible. Pure C#.
//
// Auto mode follows GestureManager: the ribbon is visible while TWO FINGER POINT
// is active (including its release grace period), the quad while TWO FINGER
// FRAME is active. Each mask has its own fader, so switching gestures is a
// cross-fade: the old mask is still fading out (fadeOut > release + confirm
// time) when the new one starts fading in, and the image never flashes back to
// the plain webcam in between.

using FilterCMR.Gestures;

namespace FilterCMR.Mask
{
    public static class MaskTransitions
    {
        public static void Targets(MaskMode mode, GestureType currentGesture, bool bothHandsPresent,
                                   out float ribbon, out float quad)
        {
            ribbon = quad = 0f;
            switch (mode)
            {
                case MaskMode.Auto:
                    if (currentGesture == GestureType.TwoFingerPoint) ribbon = 1f;
                    else if (currentGesture == GestureType.TwoFingerFrame) quad = 1f;
                    break;
                case MaskMode.Ribbon:
                    if (bothHandsPresent) ribbon = 1f;
                    break;
                case MaskMode.Quad:
                    if (bothHandsPresent) quad = 1f;
                    break;
            }
        }

        public static string Label(MaskMode m)
        {
            switch (m)
            {
                case MaskMode.Auto: return "AUTO (GESTURE)";
                case MaskMode.Ribbon: return "RIBBON";
                case MaskMode.Quad: return "QUAD";
                default: return "OFF";
            }
        }
    }
}
