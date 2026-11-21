// FILTER CMR — draws the 21 landmarks of each hand as discs; fingertips as
// larger rings. The index (and, for the frame gesture, middle) fingertips
// get a white centre while a gesture is active.

using FilterCMR.Gestures;
using FilterCMR.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.Tracking
{
    [AddComponentMenu("FILTER CMR/Hand Landmark Visualizer")]
    public sealed class HandLandmarkVisualizer : HandGraphicBase
    {
        [Tooltip("Optional: highlights the fingertips used by the active gesture.")]
        [SerializeField] GestureManager gestures;

        static readonly Color32 TipCore = new Color32(0xF2, 0xFA, 0xFF, 0xFF);
        static readonly Color32 Shadow = new Color32(0x05, 0x07, 0x0D, 0xB0);

        public GestureManager Gestures { get => gestures; set => gestures = value; }

        protected override void DrawHand(VertexHelper vh, HandView hand, Rect r, Color32 color)
        {
            var s = Settings;
            GestureType g = gestures != null ? gestures.CurrentGesture : GestureType.None;
            Color32 dark = Shadow;
            dark.a = (byte)(dark.a * color.a / 255f);

            for (int i = 0; i < HandLandmark.Count; i++)
            {
                Vector2 p = ToLocal(hand.Smoothed[i], r);
                if (HandLandmark.IsFingertip(i))
                {
                    float outer = s.fingertipRadius;
                    AddDisc(vh, p, outer + 1.5f, dark);                       // contrast halo on bright video
                    AddRing(vh, p, outer, outer * 0.55f, color);
                    bool used = (g == GestureType.TwoFingerPoint && i == HandLandmark.IndexTip)
                             || (g == GestureType.TwoFingerFrame && (i == HandLandmark.IndexTip || i == HandLandmark.MiddleTip));
                    if (used) AddDisc(vh, p, outer * 0.45f, TipCore);
                }
                else
                {
                    AddDisc(vh, p, s.landmarkRadius + 1f, dark);
                    AddDisc(vh, p, s.landmarkRadius, color);
                }
            }
        }
    }
}
