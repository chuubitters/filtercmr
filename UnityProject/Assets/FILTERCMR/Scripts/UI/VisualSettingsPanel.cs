// FILTER CMR — compact visual settings panel (S to toggle; fully usable with the mouse).
//
// Anchored bottom-right so it stays clear of the main interaction area.
// Controls write straight into VisualEffectController / InteractiveMaskManager;
// the panel re-reads them at 10 Hz, so keyboard changes ([ ], T, G, X, 1/2/0)
// show up here too. Builds its own uGUI children (idempotent; also run by the
// Editor setup so the layout is visible in the Scene view).

using System;
using System.Collections.Generic;
using FilterCMR.Core;
using FilterCMR.Mask;
using FilterCMR.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class VisualSettingsPanel : MonoBehaviour
    {
        [SerializeField] FilterCMRManager manager;

        const float Width = 380f;
        const float Pad = 12f;
        const float RowH = 24f;
        const float LabelW = 118f;
        const float ValueW = 52f;

        sealed class SliderRow
        {
            public Slider slider;
            public Text value;
            public Func<VisualEffectController, float> get;
            public Action<VisualEffectController, float> set;
            public string format;
            public float last = float.NaN;
        }

        readonly List<SliderRow> _sliders = new List<SliderRow>();
        readonly Text[] _modeText = new Text[4];
        readonly Image[] _modeImg = new Image[4];
        readonly Text[] _fxText = new Text[4];
        readonly Image[] _fxImg = new Image[4];
        Text _paletteText, _shapeText, _debugText;
        Image _debugImg;
        bool _bound;
        PalettePreset _lastPalette = (PalettePreset)(-1);
        DotShape _lastShape = (DotShape)(-1);
        float _next;

        static readonly MaskMode[] Modes = { MaskMode.Auto, MaskMode.Ribbon, MaskMode.Quad, MaskMode.Off };
        static readonly string[] ModeLabels = { "AUTO", "1 RIBBON", "2 QUAD", "0 OFF" };
        static readonly string[] FxLabels = { "T HALFTONE", "RGB SPLIT", "X GLITCH", "G GRAIN" };
        static readonly Color On = new Color(0.08f, 0.37f, 0.94f, 1f);
        static readonly Color Off = new Color(0.08f, 0.16f, 0.32f, 0.95f);

        public FilterCMRManager Manager { get => manager; set => manager = value; }

        void Awake()
        {
            Build();
            Bind();
        }

        float _y;
        RectTransform Row(string name, float height)
        {
            RectTransform r = UIFactory.Child(transform, name);
            r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, -_y); r.sizeDelta = new Vector2(-2 * Pad, height);
            _y += height + 6f;
            return r;
        }

        static void Place(RectTransform rt, float x, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 0.5f); rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(w, h);
        }

        Text Label(Transform parent, string name, string text, float x, float w, TextAnchor a = TextAnchor.MiddleLeft)
        {
            Text t = UIFactory.CreateText(parent, name, 12, Palette.WithAlpha(Palette.LightBlue, 0.75f), a);
            Place(t.rectTransform, x, w, RowH);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.text = text;
            return t;
        }

        /// <summary>Create or find all children. Safe to call repeatedly and in Edit Mode.</summary>
        public void Build()
        {
            _sliders.Clear();
            _y = 0f;
            var rt = (RectTransform)transform;
            UIFactory.Anchor(rt, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(Width, 100));
            var bg = UIFactory.GetOrAdd<Image>(gameObject);
            bg.color = Palette.WithAlpha(Palette.Navy, 0.9f);
            bg.raycastTarget = true;   // clicks on the panel don't fall through
            var outline = UIFactory.GetOrAdd<Outline>(gameObject);
            outline.effectColor = Palette.WithAlpha(Palette.ElectricBlue, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            // header
            Image header = UIFactory.CreateImage(transform, "Header", Palette.ElectricBlue);
            var hrt = header.rectTransform;
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.anchoredPosition = Vector2.zero; hrt.sizeDelta = new Vector2(0, 26);
            Text title = UIFactory.CreateText(hrt, "Title", 13, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Stretch(title.rectTransform, 12, 12, 0, 0);
            title.text = "VISUAL SETTINGS";
            Button hide = UIFactory.CreateButton(hrt, "Hide", "S  HIDE", 11, out _);
            var hideRt = (RectTransform)hide.transform;
            hideRt.anchorMin = hideRt.anchorMax = new Vector2(1, 0.5f); hideRt.pivot = new Vector2(1, 0.5f);
            hideRt.anchoredPosition = new Vector2(-4, 0); hideRt.sizeDelta = new Vector2(72, 20);
            _y = 26f + 10f;

            // mask mode buttons
            RectTransform modeRow = Row("Mode Row", RowH);
            Label(modeRow, "Label", "MASK MODE", 0, LabelW);
            float bw = (Width - 2 * Pad - LabelW - 3 * 4f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                Button b = UIFactory.CreateButton(modeRow, "Mode " + i, ModeLabels[i], 11, out _modeText[i]);
                Place((RectTransform)b.transform, LabelW + i * (bw + 4f), bw, RowH - 2);
                _modeImg[i] = b.GetComponent<Image>();
            }

            // effect toggles
            RectTransform fxRow = Row("Effects Row", RowH);
            Label(fxRow, "Label", "EFFECTS", 0, LabelW);
            for (int i = 0; i < 4; i++)
            {
                Button b = UIFactory.CreateButton(fxRow, "Fx " + i, FxLabels[i], 10, out _fxText[i]);
                Place((RectTransform)b.transform, LabelW + i * (bw + 4f), bw, RowH - 2);
                _fxImg[i] = b.GetComponent<Image>();
            }

            // sliders
            AddSlider("Ribbon Width", "RIBBON WIDTH", 0.02f, 0.3f, "0.000", e => e.ribbonWidth, (e, v) => e.ribbonWidth = v);
            AddSlider("Dot Spacing", "DOT SPACING", 3f, 30f, "0 px", e => e.dotSpacing, (e, v) => e.dotSpacing = v);
            AddSlider("Dot Size", "DOT SIZE", 0.2f, 1.6f, "0.00", e => e.dotSize, (e, v) => e.dotSize = v);
            AddSlider("Halftone Intensity", "HALFTONE", 0f, 1f, "0%", e => e.halftoneIntensity, (e, v) => e.halftoneIntensity = v);
            AddSlider("Grain Intensity", "GRAIN", 0f, 1f, "0%", e => e.grainIntensity, (e, v) => e.grainIntensity = v);
            AddSlider("RGB Split", "RGB SPLIT", 0f, 15f, "0.0 px", e => e.rgbSplit, (e, v) => e.rgbSplit = v);
            AddSlider("Glitch Intensity", "GLITCH", 0f, 1f, "0%", e => e.glitchIntensity, (e, v) => e.glitchIntensity = v);
            AddSlider("Edge Feather", "EDGE FEATHER", 0.005f, 0.12f, "0.000", e => e.edgeFeather, (e, v) => e.edgeFeather = v);
            AddSlider("Edge Glow", "EDGE GLOW", 0f, 1.5f, "0.00", e => e.edgeGlow, (e, v) => e.edgeGlow = v);

            // palette / dot shape
            RectTransform styleRow = Row("Style Row", RowH);
            float half = (Width - 2 * Pad - 4f) / 2f;
            Button pal = UIFactory.CreateButton(styleRow, "Palette", "PALETTE", 11, out _paletteText);
            Place((RectTransform)pal.transform, 0, half, RowH - 2);
            Button shp = UIFactory.CreateButton(styleRow, "Shape", "DOTS", 11, out _shapeText);
            Place((RectTransform)shp.transform, half + 4f, half, RowH - 2);

            // utilities
            RectTransform utilRow = Row("Utility Row", RowH);
            float third = (Width - 2 * Pad - 8f) / 3f;
            Button dbg = UIFactory.CreateButton(utilRow, "Mask Debug", "V  MASK DEBUG", 11, out _debugText);
            Place((RectTransform)dbg.transform, 0, third, RowH - 2);
            _debugImg = dbg.GetComponent<Image>();
            Button pres = UIFactory.CreateButton(utilRow, "Presentation", "P  PRESENT", 11, out _);
            Place((RectTransform)pres.transform, third + 4f, third, RowH - 2);
            Button reset = UIFactory.CreateButton(utilRow, "Reset", "RESET LOOK", 11, out _);
            Place((RectTransform)reset.transform, 2 * (third + 4f), third, RowH - 2);

            Text hint = UIFactory.CreateText(transform, "Hint", 10, Palette.WithAlpha(Palette.LightBlue, 0.5f), TextAnchor.UpperLeft);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = new Vector2(0, 1); hintRt.anchorMax = new Vector2(1, 1); hintRt.pivot = new Vector2(0.5f, 1);
            hintRt.anchoredPosition = new Vector2(0, -_y); hintRt.sizeDelta = new Vector2(-2 * Pad, 16);
            hint.text = "[ ] intensity · T G X toggles · 1 2 0 mode · more in the Inspector";
            _y += 22f;

            rt.sizeDelta = new Vector2(Width, _y);
        }

        void AddSlider(string name, string label, float min, float max, string format,
                       Func<VisualEffectController, float> get, Action<VisualEffectController, float> set)
        {
            RectTransform row = Row(name, RowH);
            Label(row, "Label", label, 0, LabelW);
            Slider s = UIFactory.CreateSlider(row, "Slider");
            Place((RectTransform)s.transform, LabelW, Width - 2 * Pad - LabelW - ValueW - 6f, 14f);
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = false;
            Text value = Label(row, "Value", "", Width - 2 * Pad - ValueW, ValueW, TextAnchor.MiddleRight);
            value.color = Palette.Cyan;
            _sliders.Add(new SliderRow { slider = s, value = value, get = get, set = set, format = format });
        }

        /// <summary>Hook up runtime listeners (Play Mode only, once).</summary>
        void Bind()
        {
            if (_bound || !Application.isPlaying) return;
            _bound = true;
            foreach (SliderRow row in _sliders)
            {
                SliderRow r = row;
                r.slider.onValueChanged.AddListener(v =>
                {
                    VisualEffectController fx = manager != null ? manager.Effects : null;
                    if (fx != null) r.set(fx, v);
                });
            }
            for (int i = 0; i < 4; i++)
            {
                MaskMode m = Modes[i];
                transform.Find("Mode Row/Mode " + i).GetComponent<Button>().onClick.AddListener(() =>
                {
                    if (manager != null && manager.Masks != null) manager.Masks.SetMode(m);
                });
            }
            Btn("Effects Row/Fx 0", () => Fx()?.ToggleHalftone());
            Btn("Effects Row/Fx 1", () => Fx()?.ToggleRgbSplit());
            Btn("Effects Row/Fx 2", () => Fx()?.ToggleGlitch());
            Btn("Effects Row/Fx 3", () => Fx()?.ToggleGrain());
            Btn("Style Row/Palette", () => Fx()?.CyclePalette());
            Btn("Style Row/Shape", () => Fx()?.CycleDotShape());
            Btn("Utility Row/Mask Debug", () => manager?.ToggleMaskDebug());
            Btn("Utility Row/Presentation", () => manager?.SetPresentation(true));
            Btn("Utility Row/Reset", () => Fx()?.ResetToDefaults());
            Btn("Header/Hide", () => manager?.ToggleSettingsPanel());
        }

        VisualEffectController Fx() => manager != null ? manager.Effects : null;

        void Btn(string path, UnityEngine.Events.UnityAction action)
        {
            Transform t = transform.Find(path);
            if (t != null) t.GetComponent<Button>().onClick.AddListener(action);
            else Debug.LogWarning("[FILTER CMR] settings panel: missing " + path);
        }

        void Update()
        {
            if (Time.unscaledTime < _next || manager == null) return;
            _next = Time.unscaledTime + 0.1f;
            VisualEffectController fx = manager.Effects;
            if (fx == null) return;

            foreach (SliderRow r in _sliders)
            {
                float v = r.get(fx);
                if (v == r.last) continue;
                r.last = v;
                r.slider.SetValueWithoutNotify(v);
                r.value.text = v.ToString(r.format);
            }

            MaskMode mode = manager.Masks != null ? manager.Masks.Mode : MaskMode.Off;
            for (int i = 0; i < 4; i++) SetState(_modeImg[i], Modes[i] == mode);
            SetState(_fxImg[0], fx.halftoneEnabled);
            SetState(_fxImg[1], fx.rgbSplitEnabled);
            SetState(_fxImg[2], fx.glitchEnabled);
            SetState(_fxImg[3], fx.grainEnabled);
            SetState(_debugImg, fx.debugMask);
            if (fx.palette != _lastPalette || _paletteText.text.Length == 0)
            {
                _lastPalette = fx.palette;
                SetText(_paletteText, "PALETTE  " + EffectValues.Label(fx.palette));
            }
            if (fx.dotShape != _lastShape || _shapeText.text.Length == 0 || _shapeText.text == "DOTS")
            {
                _lastShape = fx.dotShape;
                SetText(_shapeText, "DOTS  " + ShapeLabel(fx.dotShape));
            }
        }

        static string ShapeLabel(DotShape s)
        {
            switch (s)
            {
                case DotShape.Square: return "SQUARE";
                case DotShape.SoftCircle: return "SOFT CIRCLE";
                case DotShape.Irregular: return "IRREGULAR";
                default: return "CIRCLE";
            }
        }

        static void SetState(Image img, bool on)
        {
            if (img == null) return;
            Color c = on ? On : Off;
            if (img.color != c) img.color = c;
        }

        static void SetText(Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }
    }
}
