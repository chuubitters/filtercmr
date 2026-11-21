// FILTER CMR — helpers to build uGUI elements in code (idempotent: reuses children by name).
// Uses only uGUI + Unity's built-in font, so no TextMesh Pro import step is needed.

using UnityEngine;
using UnityEngine.UI;

namespace FilterCMR.UI
{
    public static class UIFactory
    {
        static Font _font;

        /// <summary>Unity's built-in font ("LegacyRuntime.ttf" since Unity 2022.2).</summary>
        public static Font DefaultFont
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static RectTransform Child(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return (RectTransform)t;
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        public static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        public static Text CreateText(Transform parent, string name, int size, Color color, TextAnchor align, FontStyle style = FontStyle.Normal)
        {
            RectTransform rt = Child(parent, name);
            var t = GetOrAdd<Text>(rt.gameObject);
            t.font = DefaultFont;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = align;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.1f;
            return t;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            RectTransform rt = Child(parent, name);
            var img = GetOrAdd<Image>(rt.gameObject);
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Flat button: Image + Button + centred label. Reuses an existing child of the same name.</summary>
        public static Button CreateButton(Transform parent, string name, string label, int fontSize, out Text text)
        {
            RectTransform rt = Child(parent, name);
            var img = GetOrAdd<Image>(rt.gameObject);
            img.color = new Color(0.08f, 0.16f, 0.32f, 0.95f);
            img.raycastTarget = true;
            var btn = GetOrAdd<Button>(rt.gameObject);
            btn.targetGraphic = img;
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.8f, 1f, 1f, 1f);
            cb.pressedColor = new Color(0.6f, 0.8f, 1f, 1f);
            cb.selectedColor = Color.white;
            btn.colors = cb;
            text = CreateText(rt, "Label", fontSize, new Color(0.65f, 0.95f, 0.99f, 1f), TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.text = label;
            return btn;
        }

        /// <summary>Thin flat slider built from uGUI's DefaultControls, restyled. Reuses an existing child of the same name.</summary>
        public static Slider CreateSlider(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            Slider s = existing != null ? existing.GetComponent<Slider>() : null;
            if (s == null)
            {
                GameObject go = DefaultControls.CreateSlider(new DefaultControls.Resources());
                go.name = name;
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                s = go.GetComponent<Slider>();
            }
            var bg = s.transform.Find("Background");
            if (bg != null) bg.GetComponent<Image>().color = new Color(0.1f, 0.2f, 0.38f, 1f);
            if (s.fillRect != null) s.fillRect.GetComponent<Image>().color = new Color(0.08f, 0.37f, 0.94f, 1f);
            if (s.handleRect != null)
            {
                s.handleRect.GetComponent<Image>().color = new Color(0.21f, 0.87f, 0.95f, 1f);
                s.handleRect.sizeDelta = new Vector2(8f, 0f);
            }
            return s;
        }

        /// <summary>Hex colour tag helper for rich text.</summary>
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
