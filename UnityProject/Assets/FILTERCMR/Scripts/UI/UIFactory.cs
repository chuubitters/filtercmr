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

        /// <summary>Hex colour tag helper for rich text.</summary>
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
