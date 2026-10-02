using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Rendering
{
    public enum BevelStyle
    {
        Flat,        // solid fill
        Raised,      // button
        Pressed,     // pushed button
        Window,      // window frame
        Sunken,      // list / text field (white fill)
        StatusField, // shallow status bar well
        Etched,      // group box / separator groove (no fill)
        Outline,     // 1px border (no fill)
        Gradient,    // horizontal gradient (title bars)
    }

    /// <summary>
    /// Draws 90s-style 3D chrome (1-2px bevels) as a single mesh: buttons, frames, wells, grooves and
    /// title-bar gradients. Pixel-snapped; colors come from <see cref="Palette"/> and are tinted by
    /// <see cref="Graphic.color"/> (so whole windows can fade).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BevelGraphic : MaskableGraphic
    {
        [SerializeField] BevelStyle _style = BevelStyle.Raised;
        [SerializeField] Color32 _fill = Palette.Face;
        [SerializeField] Color32 _fill2 = Palette.Face;
        [SerializeField] bool _customFill;

        public BevelStyle Style
        {
            get => _style;
            set { if (_style != value) { _style = value; SetVerticesDirty(); } }
        }

        /// <summary>Overrides the style's default fill (and the gradient's left colour).</summary>
        public Color32 Fill
        {
            get => _fill;
            set { _fill = value; _customFill = true; SetVerticesDirty(); }
        }

        /// <summary>Gradient right colour.</summary>
        public Color32 Fill2
        {
            get => _fill2;
            set { _fill2 = value; SetVerticesDirty(); }
        }

        public void SetGradient(Color32 left, Color32 right)
        {
            _fill = left;
            _fill2 = right;
            _customFill = true;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            Vector3 wtl = rectTransform.TransformPoint(new Vector3(r.xMin, r.yMax, 0f));
            float sx = Mathf.Round(wtl.x) - wtl.x, sy = Mathf.Round(wtl.y) - wtl.y;
            float x0 = r.xMin + sx, x1 = r.xMax + sx, y0 = r.yMin + sy, y1 = r.yMax + sy;
            x1 = x0 + Mathf.Round(x1 - x0);
            y0 = y1 - Mathf.Round(y1 - y0);

            switch (_style)
            {
                case BevelStyle.Flat:
                    Quad(vh, x0, y0, x1, y1, FillOr(Palette.Face));
                    break;
                case BevelStyle.Raised:
                    Quad(vh, x0, y0, x1, y1, FillOr(Palette.Face));
                    Edge(vh, x0, y0, x1, y1, 0, Palette.Highlight, Palette.Dark);
                    Edge(vh, x0, y0, x1, y1, 1, Palette.Midlight, Palette.Shadow);
                    break;
                case BevelStyle.Pressed:
                    Quad(vh, x0, y0, x1, y1, FillOr(Palette.Face));
                    Edge(vh, x0, y0, x1, y1, 0, Palette.Dark, Palette.Highlight);
                    Edge(vh, x0, y0, x1, y1, 1, Palette.Shadow, Palette.Midlight);
                    break;
                case BevelStyle.Window:
                    Quad(vh, x0, y0, x1, y1, FillOr(Palette.Face));
                    Edge(vh, x0, y0, x1, y1, 0, Palette.Midlight, Palette.Dark);
                    Edge(vh, x0, y0, x1, y1, 1, Palette.Highlight, Palette.Shadow);
                    break;
                case BevelStyle.Sunken:
                    Quad(vh, x0, y0, x1, y1, FillOr(Palette.Window));
                    Edge(vh, x0, y0, x1, y1, 0, Palette.Shadow, Palette.Highlight);
                    Edge(vh, x0, y0, x1, y1, 1, Palette.Dark, Palette.Midlight);
                    break;
                case BevelStyle.StatusField:
                    Quad(vh, x0, y0, x1, y1, FillOr(Palette.Face));
                    Edge(vh, x0, y0, x1, y1, 0, Palette.Shadow, Palette.Highlight);
                    break;
                case BevelStyle.Etched:
                    Edge(vh, x0, y0, x1, y1, 0, Palette.Shadow, Palette.Highlight);
                    Edge(vh, x0, y0, x1, y1, 1, Palette.Highlight, Palette.Shadow);
                    break;
                case BevelStyle.Outline:
                    Edge(vh, x0, y0, x1, y1, 0, FillOr(Palette.Dark), FillOr(Palette.Dark));
                    break;
                case BevelStyle.Gradient:
                    GradientQuad(vh, x0, y0, x1, y1, FillOr(Palette.TitleActiveA), _customFill ? _fill2 : Palette.TitleActiveB);
                    break;
            }
        }

        Color32 FillOr(Color32 fallback) => _customFill ? _fill : fallback;

        Color32 Tint(Color32 c)
        {
            Color t = color;
            return new Color32((byte)(c.r * t.r), (byte)(c.g * t.g), (byte)(c.b * t.b), (byte)(c.a * t.a));
        }

        void Edge(VertexHelper vh, float x0, float y0, float x1, float y1, int inset, Color32 topLeft, Color32 bottomRight)
        {
            float i = inset;
            if (x1 - x0 <= i * 2 || y1 - y0 <= i * 2) return;
            Quad(vh, x0 + i, y1 - i - 1, x1 - i - 1, y1 - i, topLeft);       // top
            Quad(vh, x0 + i, y0 + i + 1, x0 + i + 1, y1 - i, topLeft);       // left
            Quad(vh, x0 + i, y0 + i, x1 - i, y0 + i + 1, bottomRight);       // bottom
            Quad(vh, x1 - i - 1, y0 + i, x1 - i, y1 - i, bottomRight);       // right
        }

        void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 c)
        {
            if (x1 <= x0 || y1 <= y0) return;
            c = Tint(c);
            int n = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), c, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), c, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n + 2, n + 3, n);
        }

        void GradientQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 left, Color32 right)
        {
            if (x1 <= x0 || y1 <= y0) return;
            left = Tint(left);
            right = Tint(right);
            int n = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), left, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), left, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), right, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), right, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n + 2, n + 3, n);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }
    }
}
