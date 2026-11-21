// FILTER CMR — draws the joint connections (bones) of both hands as thin quads.

using FilterCMR.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.Tracking
{
    [AddComponentMenu("FILTER CMR/Hand Connection Visualizer")]
    public sealed class HandConnectionVisualizer : HandGraphicBase
    {
        protected override void DrawHand(VertexHelper vh, HandView hand, Rect r, Color32 color)
        {
            float w = Settings.connectionWidth;
            Color32 c = color;
            c.a = (byte)(c.a * 0.85f);
            int[] pairs = HandSkeleton.Connections;
            for (int k = 0; k < pairs.Length; k += 2)
            {
                Vector2 a = ToLocal(hand.Smoothed[pairs[k]], r);
                Vector2 b = ToLocal(hand.Smoothed[pairs[k + 1]], r);
                AddQuadLine(vh, a, b, w, c);
            }
        }
    }
}
