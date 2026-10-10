using System.Collections.Generic;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Entity
{
    /// <summary>
    /// The tug's pixel-art arrow (Phase J on the file, Phase K at the pointer, Phase P's R1 sizes): a 3 px thick shaft and a filled head drawn
    /// with 4 px squares on a 3 px grid times <see cref="Scale"/>, pale yellow on a 75% black backing so it reads on the teal desktop and on
    /// cream windows. With <c>band</c> a bright band travels along the shaft toward the tip.
    /// </summary>
    public sealed class TugArrow
    {
        /// <summary>The arrow in its own frame, x along the direction, y across (px at scale 1).</summary>
        static readonly Vector2[] Shape = BuildShape();
        public static readonly Color Bright = new Color(1f, 0.93f, 0.55f, 1f);

        readonly List<Image> _backs = new List<Image>(), _dots = new List<Image>();
        public readonly int Scale;
        public bool Shown { get; private set; }
        /// <summary>Where the tip was drawn last (virtual px).</summary>
        public Vector2 Tip { get; private set; }
        public Vector2 From { get; private set; }
        public Vector2 Direction { get; private set; }
        /// <summary>The arrow's length from its start to the tip (px).</summary>
        public float Length => 36f * Scale;

        static Vector2[] BuildShape()
        {
            var pts = new List<Vector2>();
            for (int x = 0; x <= 27; x += 3) pts.Add(new Vector2(x, 0f));
            for (int y = -6; y <= 6; y += 3) pts.Add(new Vector2(30f, y));
            for (int y = -3; y <= 3; y += 3) pts.Add(new Vector2(33f, y));
            pts.Add(new Vector2(36f, 0f));
            return pts.ToArray();
        }

        public TugArrow(RectTransform layer, int scale, string name)
        {
            Scale = scale;
            for (int i = 0; i < Shape.Length; i++)
            {
                _backs.Add(Square(layer, new Color(0f, 0f, 0f, 0.75f), 6f * scale, name + " Back " + i));
                _dots.Add(Square(layer, Bright, 4f * scale, name + " " + i));
            }
        }

        static Image Square(RectTransform layer, Color color, float size, string name)
        {
            var img = UIBuilder.Solid(layer, color, name);
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
            img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.enabled = false;
            return img;
        }

        /// <summary>Draws the arrow from <paramref name="from"/> plus <paramref name="start"/> px along <paramref name="dir"/>.</summary>
        public void Draw(Vector2 from, Vector2 dir, float start, float alpha, bool band)
        {
            if (dir.sqrMagnitude < 0.25f) { Hide(); return; }
            dir.Normalize();
            Shown = true;
            From = from;
            Direction = dir;
            Tip = from + dir * (start + Length);
            Vector2 side = new Vector2(-dir.y, dir.x);
            for (int i = 0; i < Shape.Length; i++)
            {
                Vector2 q = Shape[i] * Scale;
                Vector2 at = from + dir * (start + q.x) + side * q.y;
                Vector2 p = new Vector2(Mathf.Round(at.x), Mathf.Round(at.y));
                _backs[i].rectTransform.anchoredPosition = p;
                _dots[i].rectTransform.anchoredPosition = p;
                float k = 1f;
                if (band && q.x < 30f * Scale)
                {
                    // A bright band runs along the shaft toward the tip so the direction reads even at a glance.
                    float wave = Mathf.Repeat(Time.unscaledTime * 2.4f - q.x * 0.03f / Scale, 1f);
                    k = wave < 0.4f ? 1f : 0.65f;
                }
                _dots[i].color = new Color(Bright.r, Bright.g, Bright.b, k * alpha);
                _backs[i].color = new Color(0f, 0f, 0f, 0.75f * alpha);
                _backs[i].enabled = true;
                _dots[i].enabled = true;
            }
        }

        public void Hide()
        {
            if (!Shown) return;
            Shown = false;
            for (int i = 0; i < _dots.Count; i++) { _dots[i].enabled = false; _backs[i].enabled = false; }
        }

        /// <summary>The arrow (with a margin) covers <paramref name="r"/>: sampled along the shaft.</summary>
        public bool Overlaps(Rect r)
        {
            if (!Shown) return false;
            Vector2 start = Tip - Direction * Length;
            for (int i = 0; i <= 4; i++)
                if (r.Contains(Vector2.Lerp(start, Tip, i / 4f))) return true;
            return false;
        }
    }
}
