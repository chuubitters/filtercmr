// FILTER CMR — base class for procedural hand overlays.
// A single UI Graphic builds one mesh for both hands every frame (no GameObject
// per landmark, nothing created or destroyed at runtime). It must sit on a
// RectTransform stretched over the webcam image so normalised landmark
// coordinates map to image pixels.

using FilterCMR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.Tracking
{
    [RequireComponent(typeof(CanvasRenderer))]
    public abstract class HandGraphicBase : MaskableGraphic
    {
        [SerializeField] protected ApplicationSettings settings;
        [SerializeField] protected HandTrackingManager tracking;

        bool _drewLastFrame;

        public ApplicationSettings Settings { get => settings != null ? settings : ApplicationSettings.Fallback; set => settings = value; }
        public HandTrackingManager Tracking { get => tracking; set => tracking = value; }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected virtual void LateUpdate()
        {
            if (tracking == null) return;
            bool any = tracking.Left.Present || tracking.Right.Present;
            // Rebuild while hands are visible, plus once more to clear after they disappear.
            if (any || _drewLastFrame) SetVerticesDirty();
            _drewLastFrame = any;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (tracking == null || !Application.isPlaying) return;
            Rect r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            if (tracking.Left.Present) DrawHand(vh, tracking.Left, r, HandColor(tracking.Left));
            if (tracking.Right.Present) DrawHand(vh, tracking.Right, r, HandColor(tracking.Right));
        }

        protected abstract void DrawHand(VertexHelper vh, HandView hand, Rect r, Color32 color);

        protected Color32 HandColor(HandView hand)
        {
            var s = Settings;
            Color c = hand.handedness == Networking.Handedness.Left ? s.leftHandColor : s.rightHandColor;
            if (hand.State == Networking.HandState.Held) c.a *= s.heldHandAlpha;
            return c;
        }

        /// <summary>Normalised image coords (0–1, y down) -> local position in rect.</summary>
        protected static Vector2 ToLocal(Vector2 n, Rect r) => new Vector2(r.xMin + n.x * r.width, r.yMax - n.y * r.height);

        protected static void AddQuadLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color32 c)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            Vector2 n = new Vector2(-d.y, d.x) * (0.5f * width / len);
            int i = vh.currentVertCount;
            vh.AddVert(a - n, c, Vector2.zero);
            vh.AddVert(a + n, c, Vector2.zero);
            vh.AddVert(b + n, c, Vector2.zero);
            vh.AddVert(b - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        const int CircleSegments = 14;
        static readonly Vector2[] Unit = BuildUnitCircle();

        static Vector2[] BuildUnitCircle()
        {
            var u = new Vector2[CircleSegments];
            for (int k = 0; k < CircleSegments; k++)
            {
                float a = k * Mathf.PI * 2f / CircleSegments;
                u[k] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }
            return u;
        }

        protected static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color32 c)
        {
            int start = vh.currentVertCount;
            vh.AddVert(center, c, Vector2.zero);
            for (int k = 0; k < CircleSegments; k++) vh.AddVert(center + Unit[k] * radius, c, Vector2.zero);
            for (int k = 0; k < CircleSegments; k++)
                vh.AddTriangle(start, start + 1 + k, start + 1 + (k + 1) % CircleSegments);
        }

        protected static void AddRing(VertexHelper vh, Vector2 center, float outer, float inner, Color32 c)
        {
            int start = vh.currentVertCount;
            for (int k = 0; k < CircleSegments; k++)
            {
                vh.AddVert(center + Unit[k] * outer, c, Vector2.zero);
                vh.AddVert(center + Unit[k] * inner, c, Vector2.zero);
            }
            for (int k = 0; k < CircleSegments; k++)
            {
                int o0 = start + 2 * k, i0 = o0 + 1;
                int o1 = start + 2 * ((k + 1) % CircleSegments), i1 = o1 + 1;
                vh.AddTriangle(o0, o1, i1);
                vh.AddTriangle(i1, i0, o0);
            }
        }
    }
}
