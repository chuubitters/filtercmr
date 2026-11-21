// FILTER CMR — applies the interactive mask + halftone composite to the webcam image.
//
// Lives on the "Webcam Display" RawImage (next to WebcamDisplay). It gives the
// RawImage a private instance of the FILTERCMR/WebcamComposite material, so the
// whole effect is ONE extra-free draw call: the same quad that already shows the
// webcam now runs the composite shader. Each LateUpdate it uploads:
//   * the RawImage uvRect + displayed size (coordinate mapping, mirroring),
//   * the ribbon / quad shapes and fade weights (InteractiveMaskManager),
//   * every effect parameter (VisualEffectController),
// through CompositeUniformWriter (pure C#, shared with Tools/ShaderPreview).
// Property IDs are cached; nothing is allocated per frame.
//
// If the shader is missing or unsupported, the RawImage keeps its default
// material, so the plain webcam image still works.

using System;
using FilterCMR.CameraInput;
using FilterCMR.Mask;
using UnityEngine;
using UnityEngine.UI;
using NVector4 = System.Numerics.Vector4;

namespace FilterCMR.Rendering
{
    [DefaultExecutionOrder(100)]   // after WebcamDisplay (uvRect/aspect) and InteractiveMaskManager
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class WebcamCompositeController : MonoBehaviour, IUniformSink
    {
        public const string ShaderName = "FILTERCMR/WebcamComposite";

        [Tooltip("FILTERCMR/WebcamComposite. Assigned by FILTER CMR > Setup Visual Effects.")]
        [SerializeField] Shader compositeShader;
        [Tooltip("Optional template material (Assets/FILTERCMR/Materials). A private copy is used at runtime.")]
        [SerializeField] Material compositeMaterial;
        [SerializeField] InteractiveMaskManager masks;
        [SerializeField] VisualEffectController effects;
        [SerializeField] WebcamDisplay display;

        static readonly int[] s_ids = BuildIds();
        static readonly int s_ribbonPtsId = Shader.PropertyToID(CompositeUniformWriter.RibbonPointsName);

        readonly CompositeUniformWriter _writer = new CompositeUniformWriter();
        readonly Vector4[] _unityPts = new Vector4[RibbonShape.MaxPoints];
        EffectValues _values = EffectValues.Default;
        RawImage _raw;
        Canvas _canvas;
        Material _mat;

        public bool IsActive => _mat != null;
        /// <summary>Human-readable shader status for the debug panel.</summary>
        public string Status { get; private set; } = "not started";
        public float ImageHeightPixels { get; private set; }

        static int[] BuildIds()
        {
            var ids = new int[(int)FxUniform.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = Shader.PropertyToID("_" + ((FxUniform)i).ToString());
            return ids;
        }

        void Awake() => _raw = GetComponent<RawImage>();

        void OnEnable()
        {
            if (_raw == null) _raw = GetComponent<RawImage>();
            Shader shader = compositeShader != null ? compositeShader
                          : compositeMaterial != null ? compositeMaterial.shader
                          : Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Status = shader == null ? "shader " + ShaderName + " not found" : "shader not supported on this GPU";
                Debug.LogError("[FILTER CMR] " + Status + ". Run FILTER CMR > Setup Visual Effects. The webcam is shown without effects.", this);
                return;
            }
            _mat = compositeMaterial != null && compositeMaterial.shader == shader ? new Material(compositeMaterial) : new Material(shader);
            _mat.name = "FILTERCMR Composite (instance)";
            _mat.hideFlags = HideFlags.DontSave;
            _raw.material = _mat;
            Status = "ok";
        }

        void OnDisable()
        {
            if (_raw != null && _raw.material == _mat) _raw.material = null;   // back to the default UI material
            if (_mat != null)
            {
                Destroy(_mat);
                _mat = null;
            }
            if (Status == "ok") Status = "disabled";
        }

        void LateUpdate()
        {
            if (_mat == null) return;
            if (_raw.material != _mat) _raw.material = _mat;
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();

            if (effects != null) effects.Fill(ref _values);

            Rect r = _raw.rectTransform.rect;
            float scale = _canvas != null ? _canvas.rootCanvas.scaleFactor : 1f;
            ImageHeightPixels = Mathf.Max(1f, r.height * scale);
            Rect uv = _raw.uvRect;
            float aspect = display != null ? display.Aspect : (r.height > 0 ? r.width / r.height : 16f / 9f);
            float t = Time.unscaledTime;

            var frame = new FrameMapping
            {
                uvRect = new NVector4(uv.x, uv.y, uv.width, uv.height),
                aspect = aspect,
                imageHeightPx = ImageHeightPixels,
                time = t,
                grainSeed = CompositeUniformWriter.GrainSeed(_values, t),
            };
            _writer.WriteFrame(this, frame);
            if (masks != null)
                _writer.WriteMasks(this, masks.Ribbon, masks.RibbonWeight, masks.Quad, masks.QuadWeight, _values, 1f / ImageHeightPixels);
            else
                _writer.WriteMasks(this, null, 0f, null, 0f, _values, 1f / ImageHeightPixels);
            _writer.WriteEffects(this, _values);
        }

        // ---- IUniformSink ----
        void IUniformSink.SetFloat(FxUniform u, float value) => _mat.SetFloat(s_ids[(int)u], value);

        void IUniformSink.SetVector(FxUniform u, NVector4 v) => _mat.SetVector(s_ids[(int)u], new Vector4(v.X, v.Y, v.Z, v.W));

        void IUniformSink.SetRibbonPoints(NVector4[] p)
        {
            int n = Math.Min(p.Length, _unityPts.Length);
            for (int i = 0; i < n; i++) _unityPts[i] = new Vector4(p[i].X, p[i].Y, p[i].Z, p[i].W);
            _mat.SetVectorArray(s_ribbonPtsId, _unityPts);   // always the same length (Unity fixes array size on first set)
        }
    }
}
