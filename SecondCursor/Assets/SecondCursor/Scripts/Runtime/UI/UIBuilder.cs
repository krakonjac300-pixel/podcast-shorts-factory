using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.UI
{
    /// <summary>
    /// Code-built UI helpers. The whole OS is constructed from code (no prefabs or hand-authored scenes),
    /// so these keep construction terse. Layout convention: x/y are measured from the parent's TOP-LEFT
    /// corner with y growing downward, like a real desktop; everything is whole pixels.
    /// </summary>
    public static class UIBuilder
    {
        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = ScreenRig.UiLayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return rt;
        }

        /// <summary>Place at (x, y) from the parent's top-left with a fixed size.</summary>
        public static RectTransform At(this RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
            rt.sizeDelta = new Vector2(Mathf.Round(w), Mathf.Round(h));
            return rt;
        }

        /// <summary>Fill the parent minus margins.</summary>
        public static RectTransform Stretch(this RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Full-width strip at a fixed distance from the top.</summary>
        public static RectTransform TopStrip(this RectTransform rt, float top, float height, float left = 0, float right = 0)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.offsetMin = new Vector2(left, -top - height);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        /// <summary>Full-width strip at a fixed distance from the bottom.</summary>
        public static RectTransform BottomStrip(this RectTransform rt, float bottom, float height, float left = 0, float right = 0)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, bottom + height);
            return rt;
        }

        /// <summary>Anchored to the parent's top-right corner.</summary>
        public static RectTransform TopRight(this RectTransform rt, float right, float top, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-right, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>Anchored to the parent's bottom-right corner.</summary>
        public static RectTransform BottomRight(this RectTransform rt, float right, float bottom, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-right, bottom);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Solid(Transform parent, Color32 color, string name = "Solid")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static BevelGraphic Bevel(Transform parent, BevelStyle style, string name = "Bevel")
        {
            var rt = Rect(name, parent);
            var g = rt.gameObject.AddComponent<BevelGraphic>();
            g.Style = style;
            g.raycastTarget = false;
            return g;
        }

        public static PixelText Text(Transform parent, string text, Color32 color, bool bold = false, string name = "Text")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<PixelText>();
            t.raycastTarget = false;
            t.color = color;
            t.Bold = bold;
            t.text = text ?? "";
            return t;
        }

        /// <summary>A pixel-art sprite at integer scale, anchored top-left at (0,0) of its parent.</summary>
        public static Image Icon(Transform parent, string spriteName, int scale = 1, string name = null)
        {
            var rt = Rect(name ?? spriteName, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            SetIcon(img, spriteName, scale);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            return img;
        }

        public static void SetIcon(Image img, string spriteName, int scale = 1)
        {
            img.sprite = SpriteLibrary.Get(spriteName);
            var size = SpriteLibrary.Size(spriteName);
            img.rectTransform.sizeDelta = new Vector2(size.x * scale, size.y * scale);
        }

        public static RawImage Raw(Transform parent, Texture texture, string name = "Raw")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = texture;
            img.raycastTarget = false;
            return img;
        }

        public static Interactable Hit(GameObject go, string elementId = "", CursorShape cursor = CursorShape.Arrow)
        {
            var it = go.GetComponent<Interactable>();
            if (it == null) it = go.AddComponent<Interactable>();
            it.elementId = elementId ?? "";
            it.cursor = cursor;
            return it;
        }

        public static void Clip(RectTransform rt)
        {
            if (rt.GetComponent<RectMask2D>() == null) rt.gameObject.AddComponent<RectMask2D>();
        }
    }
}
