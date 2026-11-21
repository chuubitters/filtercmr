// FILTER CMR — debug overlay for the masks (V to toggle, together with the shader's mask tint).
//
// Draws, on the Hand Overlay (same space as the landmarks):
//   ribbon: centre line, both fingertip endpoints (large rings), Bézier control points
//   quad:   hull outline, the four source fingertips coloured by identity,
//           and a white square on hull vertex 0 (perspective grid origin)
// If these markers sit on your real fingertips, the mask is aligned.

using FilterCMR.Tracking;
using UnityEngine;
using UnityEngine.UI;
using NVector2 = System.Numerics.Vector2;

namespace FilterCMR.Mask
{
    [AddComponentMenu("FILTER CMR/Mask Debug Visualizer")]
    public sealed class MaskDebugVisualizer : HandGraphicBase
    {
        [SerializeField] InteractiveMaskManager masks;

        static readonly Color32 RibbonLine = new Color32(0xFF, 0x4F, 0xC8, 0xFF);
        static readonly Color32 Control = new Color32(0xFF, 0xC2, 0x3D, 0xFF);
        static readonly Color32 Endpoint = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        static readonly Color32 QuadLine = new Color32(0x7C, 0xFF, 0x6B, 0xFF);
        static readonly Color32[] SourceColors =
        {
            new Color32(0x35, 0xDD, 0xF2, 0xFF), // left index
            new Color32(0xA5, 0xF3, 0xFC, 0xFF), // left thumb (or middle)
            new Color32(0x15, 0x5E, 0xEF, 0xFF), // right index
            new Color32(0xF7, 0xFB, 0xFF, 0xFF), // right thumb (or middle)
        };

        bool _drew;

        public InteractiveMaskManager Masks { get => masks; set => masks = value; }

        protected override void LateUpdate()
        {
            bool any = masks != null && (masks.RibbonWeight > 0f || masks.QuadWeight > 0f);
            if (any || _drew) SetVerticesDirty();
            _drew = any;
        }

        protected override void DrawHand(VertexHelper vh, HandView hand, Rect r, Color32 color) { }

        Vector2 Local(NVector2 iso, Rect r, float aspect) => ToLocal(new Vector2(iso.X / aspect, iso.Y), r);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (masks == null || !Application.isPlaying) return;
            Rect r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            float aspect = Mathf.Max(masks.Aspect, 1e-3f);

            RibbonShape rb = masks.Ribbon;
            if (rb.Valid && masks.RibbonWeight > 0f)
            {
                for (int i = 0; i + 1 < rb.Count; i++)
                    AddQuadLine(vh, Local(rb.Centers[i], r, aspect), Local(rb.Centers[i + 1], r, aspect), 2f, RibbonLine);
                Vector2 a = Local(rb.Start, r, aspect), b = Local(rb.End, r, aspect);
                Vector2 c1 = Local(rb.Control1, r, aspect), c2 = Local(rb.Control2, r, aspect);
                AddQuadLine(vh, a, c1, 1f, Control);
                AddQuadLine(vh, b, c2, 1f, Control);
                AddDisc(vh, c1, 4f, Control);
                AddDisc(vh, c2, 4f, Control);
                AddRing(vh, a, 16f, 12f, Endpoint);
                AddRing(vh, b, 16f, 12f, Endpoint);
            }

            QuadShape q = masks.Quad;
            if (q.Valid && masks.QuadWeight > 0f)
            {
                int n = q.VertexCount;
                for (int i = 0; i < n; i++)
                    AddQuadLine(vh, Local(q.Vertices[i], r, aspect), Local(q.Vertices[(i + 1) % n], r, aspect), 2f, QuadLine);
                Vector2 v0 = Local(q.Vertices[0], r, aspect);
                AddQuadLine(vh, v0 + new Vector2(-7, -7), v0 + new Vector2(7, 7), 14f, Endpoint);
                for (int s = 0; s < 4; s++)
                    AddRing(vh, Local(q.Sources[s], r, aspect), 14f, 9f, SourceColors[s]);
            }
        }
    }
}
