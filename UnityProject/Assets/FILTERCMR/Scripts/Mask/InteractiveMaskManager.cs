// FILTER CMR — drives the interactive masks from real fingertip positions.
//
//   gesture (GestureManager) ─┐
//   mode (keys 0/1/2)        ─┼─► MaskTransitions ─► ribbon / quad faders (cross-fade)
//                             │
//   fingertips (HandView.Smoothed, IMAGE space)
//      ─► ISO space (× image aspect) ─► PointSmoother (smoothing + jump limit)
//      ─► RibbonMaskGenerator (index tips 8 + finger directions 6→8)
//      ─► QuadrilateralMaskGenerator (index 8 + thumb 4 of both hands — or index 8 +
//         middle 12 when Frame Fingers = Index And Middle — convex hull)
//
// WebcamCompositeController uploads the shapes + weights to the shader.
// Nothing is allocated per frame; all shapes and smoothers are reused.

using FilterCMR.CameraInput;
using FilterCMR.Gestures;
using FilterCMR.Networking;
using FilterCMR.Rendering;
using FilterCMR.Tracking;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;

namespace FilterCMR.Mask
{
    [DefaultExecutionOrder(-40)]   // after HandTrackingManager (-150) and GestureManager (-100)
    [DisallowMultipleComponent]
    public sealed class InteractiveMaskManager : MonoBehaviour
    {
        [SerializeField] HandTrackingManager tracking;
        [SerializeField] GestureManager gestures;
        [SerializeField] WebcamDisplay display;
        [SerializeField] VisualEffectController effects;
        [Tooltip("Auto = gestures decide (point → ribbon, L shapes (thumb + index) → quad). Ribbon/Quad = that mask whenever both hands are visible. Off = normal webcam.")]
        [SerializeField] MaskMode mode = MaskMode.Auto;

        readonly RibbonShape _ribbon = new RibbonShape();
        readonly QuadShape _quad = new QuadShape();
        MaskFader _ribbonFade, _quadFade;
        PointSmoother _lIndex, _rIndex, _lSecond, _rSecond, _lDir, _rDir;
        bool _pointsStale = true;
        float _lAppeared = -1f, _rAppeared = -1f;
        float _aspect = 16f / 9f;
        EffectValues _values = EffectValues.Default;

        public MaskMode Mode => mode;
        public RibbonShape Ribbon => _ribbon;
        public QuadShape Quad => _quad;
        /// <summary>Eased 0..1 visibility of the ribbon (0 when no valid shape).</summary>
        public float RibbonWeight => _ribbon.Valid ? _ribbonFade.Eased : 0f;
        public float QuadWeight => _quad.Valid ? _quadFade.Eased : 0f;
        public float Aspect => _aspect;
        public MaskShapeType Dominant => RibbonWeight <= 0f && QuadWeight <= 0f ? MaskShapeType.None
                                       : RibbonWeight >= QuadWeight ? MaskShapeType.Ribbon : MaskShapeType.Quad;
        public HandTrackingManager Tracking => tracking;

        public event System.Action<MaskMode> ModeChanged;

        public void SetMode(MaskMode m)
        {
            if (m == mode) return;
            mode = m;
            Debug.Log("[FILTER CMR] mask mode: " + MaskTransitions.Label(m));
            ModeChanged?.Invoke(m);
        }

        /// <summary>Keys 1/2/0: select a mode; pressing the active one again returns to Auto.</summary>
        public void SelectOrToggle(MaskMode m) => SetMode(mode == m ? MaskMode.Auto : m);

        public void ResetMasks()
        {
            _ribbonFade.Reset();
            _quadFade.Reset();
            _ribbon.Clear();
            _quad.Clear();
            _pointsStale = true;
        }

        void Update()
        {
            if (tracking == null) return;
            float dt = Time.unscaledDeltaTime;
            if (effects != null) effects.Fill(ref _values);
            if (display != null && display.Aspect > 0f) _aspect = display.Aspect;

            HandView left = tracking.Left, right = tracking.Right;
            bool both = left.Present && right.Present;
            GestureType g = gestures != null ? gestures.CurrentGesture : GestureType.None;
            MaskTransitions.Targets(mode, g, both, out float ribbonTarget, out float quadTarget);

            _ribbonFade.Update(ribbonTarget, dt, _values.fadeInSeconds, _values.fadeOutSeconds);
            _quadFade.Update(quadTarget, dt, _values.fadeInSeconds, _values.fadeOutSeconds);

            if (!_ribbonFade.Visible && !_quadFade.Visible)
            {
                _pointsStale = true;   // next appearance snaps to the hands instead of gliding from old positions
                _ribbon.Clear();
                _quad.Clear();
                return;
            }

            UpdatePoints(left, right, dt);

            if (_ribbonFade.Visible && _lIndex.Initialised && _rIndex.Initialised)
                RibbonMaskGenerator.Generate(_lIndex.Value, _rIndex.Value, Dir(_lDir), Dir(_rDir), _values.ribbon, Time.unscaledTime, _ribbon);
            else if (!_ribbonFade.Visible) _ribbon.Clear();

            if (_quadFade.Visible && _lIndex.Initialised && _rIndex.Initialised && _lSecond.Initialised && _rSecond.Initialised)
                QuadrilateralMaskGenerator.Generate(_lIndex.Value, _lSecond.Value, _rIndex.Value, _rSecond.Value, _quad);
            else if (!_quadFade.Visible) _quad.Clear();
        }

        static NVector2 Dir(in PointSmoother s) => s.Initialised ? MaskMath.SafeNormalize(s.Value, NVector2.Zero) : NVector2.Zero;

        NVector2 Iso(HandView h, int landmark)
        {
            Vector2 q = h.Smoothed[landmark];
            return new NVector2(q.x * _aspect, q.y);
        }

        void UpdatePoints(HandView left, HandView right, float dt)
        {
            float rate = _values.pointSmoothing, speed = _values.maxPointSpeed;
            // Second frame corner per hand: thumb tip (default, "L" shape) or middle tip (peace sign).
            int second = gestures != null ? gestures.FrameSecondLandmark : HandLandmark.ThumbTip;
            bool snapL = _pointsStale || (left.Present && left.AppearedAt != _lAppeared);
            bool snapR = _pointsStale || (right.Present && right.AppearedAt != _rAppeared);
            _pointsStale = false;

            // A missing hand keeps its last points (the mask is fading out at that point anyway).
            if (left.Present)
            {
                _lAppeared = left.AppearedAt;
                Track(ref _lIndex, Iso(left, HandLandmark.IndexTip), snapL, dt, rate, speed);
                Track(ref _lSecond, Iso(left, second), snapL, dt, rate, speed);
                NVector2 d = MaskMath.SafeNormalize(Iso(left, HandLandmark.IndexTip) - Iso(left, HandLandmark.IndexPip), NVector2.Zero);
                Track(ref _lDir, d, snapL, dt, rate, 0f);
            }
            if (right.Present)
            {
                _rAppeared = right.AppearedAt;
                Track(ref _rIndex, Iso(right, HandLandmark.IndexTip), snapR, dt, rate, speed);
                Track(ref _rSecond, Iso(right, second), snapR, dt, rate, speed);
                NVector2 d = MaskMath.SafeNormalize(Iso(right, HandLandmark.IndexTip) - Iso(right, HandLandmark.IndexPip), NVector2.Zero);
                Track(ref _rDir, d, snapR, dt, rate, 0f);
            }
        }

        static void Track(ref PointSmoother s, NVector2 target, bool snap, float dt, float rate, float speed)
        {
            if (snap || !s.Initialised) s.Snap(target);
            else s.Update(target, dt, rate, speed);
        }
    }
}
