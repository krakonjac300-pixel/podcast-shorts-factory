using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Rendering
{
    public enum TextAlign { Left, Center, Right }
    public enum TextVAlign { Top, Middle, Bottom }

    /// <summary>
    /// uGUI graphic that renders text with the NEXUS bitmap font. One quad per glyph, pixel-snapped,
    /// optional word wrap, integer scale, bold, a blinking caret for text entry, and first-line scrolling.
    /// Works inside RectMask2D (it is a MaskableGraphic).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PixelText : MaskableGraphic
    {
        [SerializeField] string _text = "";
        [SerializeField] bool _bold;
        [SerializeField] int _scale = 1;
        [SerializeField] bool _wrap;
        [SerializeField] TextAlign _align = TextAlign.Left;
        [SerializeField] TextVAlign _valign = TextVAlign.Top;
        [SerializeField] int _extraLineSpacing;
        [SerializeField] int _firstLine;
        [SerializeField] bool _shadow;
        [SerializeField] Color32 _shadowColor = new Color32(0, 0, 0, 255);

        bool _caretEnabled;
        bool _caretVisible;
        int _caretIndex = -1;
        readonly List<string> _lines = new List<string>();

        public string text
        {
            get => _text;
            set
            {
                value = value ?? "";
                if (_text == value) return;
                _text = value;
                SetVerticesDirty();
            }
        }

        public bool Bold { get => _bold; set { if (_bold != value) { _bold = value; SetVerticesDirty(); } } }
        public int Scale { get => _scale; set { value = Mathf.Max(1, value); if (_scale != value) { _scale = value; SetVerticesDirty(); } } }
        public bool Wrap { get => _wrap; set { if (_wrap != value) { _wrap = value; SetVerticesDirty(); } } }
        public TextAlign Align { get => _align; set { if (_align != value) { _align = value; SetVerticesDirty(); } } }
        public TextVAlign VAlign { get => _valign; set { if (_valign != value) { _valign = value; SetVerticesDirty(); } } }
        public int ExtraLineSpacing { get => _extraLineSpacing; set { if (_extraLineSpacing != value) { _extraLineSpacing = value; SetVerticesDirty(); } } }

        /// <summary>Index of the first wrapped line to draw (for scrolling text views).</summary>
        public int FirstLine { get => _firstLine; set { value = Mathf.Max(0, value); if (_firstLine != value) { _firstLine = value; SetVerticesDirty(); } } }

        /// <summary>1px drop shadow (desktop icon labels).</summary>
        public bool Shadow { get => _shadow; set { if (_shadow != value) { _shadow = value; SetVerticesDirty(); } } }
        public Color32 ShadowColor { get => _shadowColor; set { _shadowColor = value; SetVerticesDirty(); } }

        public int LineHeightPx => (PixelFont.LineHeight + _extraLineSpacing) * _scale;

        /// <summary>Caret shown after character index (text length = end). Visibility is toggled by the owner.</summary>
        public void SetCaret(bool enabled, int index, bool visible)
        {
            if (_caretEnabled == enabled && _caretIndex == index && _caretVisible == visible) return;
            _caretEnabled = enabled;
            _caretIndex = index;
            _caretVisible = visible;
            SetVerticesDirty();
        }

        public override Texture mainTexture => PixelFont.Atlas;

        /// <summary>Number of wrapped lines at the current width (layout helper for scroll views).</summary>
        public int CountLines()
        {
            int maxW = _wrap ? Mathf.FloorToInt(rectTransform.rect.width) : 0;
            return PixelFont.Wrap(_text, maxW, _bold, _scale, _lines).Count;
        }

        public int VisibleLineCapacity => Mathf.Max(1, Mathf.FloorToInt((rectTransform.rect.height + (LineHeightPx - PixelFont.GlyphHeight * _scale)) / Mathf.Max(1, LineHeightPx)));

        public Vector2Int PreferredSize(int maxWidth = 0) => PixelFont.Measure(_text, maxWidth, _bold, _scale);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            int maxW = _wrap ? Mathf.FloorToInt(rect.width) : 0;
            PixelFont.Wrap(_text, maxW, _bold, _scale, _lines);

            int s = _scale;
            int lineH = LineHeightPx;
            int glyphH = PixelFont.GlyphHeight * s;
            int first = Mathf.Min(_firstLine, Mathf.Max(0, _lines.Count - 1));
            int count = _lines.Count - first;
            int blockH = count <= 0 ? glyphH : (count - 1) * lineH + glyphH;

            // Snap the rect's top-left to whole world pixels so the point-filtered glyphs stay crisp.
            Vector3 worldTopLeft = rectTransform.TransformPoint(new Vector3(rect.xMin, rect.yMax, 0f));
            float snapX = Mathf.Round(worldTopLeft.x) - worldTopLeft.x;
            float snapY = Mathf.Round(worldTopLeft.y) - worldTopLeft.y;

            float top;
            switch (_valign)
            {
                case TextVAlign.Middle: top = rect.yMax - Mathf.Floor((rect.height - blockH) * 0.5f); break;
                case TextVAlign.Bottom: top = rect.yMin + blockH; break;
                default: top = rect.yMax; break;
            }
            top += snapY;

            Color32 col = color;
            int caretLine = -1, caretCol = 0;
            if (_caretEnabled && _caretVisible) LocateCaret(first, out caretLine, out caretCol);

            for (int li = first; li < _lines.Count; li++)
            {
                string line = _lines[li];
                float y = top - (li - first) * lineH;
                if (y < rect.yMin - lineH) break; // fully below the visible rect
                int w = PixelFont.MeasureLine(line, _bold, s);
                float x;
                switch (_align)
                {
                    case TextAlign.Center: x = rect.xMin + Mathf.Floor((rect.width - w) * 0.5f); break;
                    case TextAlign.Right: x = rect.xMax - w; break;
                    default: x = rect.xMin; break;
                }
                x += snapX;

                float cx = x;
                for (int ci = 0; ci < line.Length; ci++)
                {
                    char c = line[ci];
                    if (li == caretLine && ci == caretCol) AddQuad(vh, cx - 1, y - glyphH, cx, y, PixelFont.WhiteUv, col);
                    if (c != ' ' && c != '\t' && c != '\r')
                    {
                        var g = PixelFont.Get(c, _bold);
                        float gx0 = cx, gx1 = cx + g.Width * s;
                        if (_shadow) AddQuad(vh, gx0 + s, y - glyphH - s, gx1 + s, y - s, g.Uv, _shadowColor);
                        AddQuad(vh, gx0, y - glyphH, gx1, y, g.Uv, col);
                    }
                    cx += PixelFont.Advance(c, _bold) * s;
                }
                if (li == caretLine && caretCol >= line.Length)
                {
                    float px = line.Length == 0 ? cx + 1 : cx;
                    AddQuad(vh, px - 1, y - glyphH, px, y, PixelFont.WhiteUv, col);
                }
            }

            if (_caretEnabled && _caretVisible && _lines.Count == 0)
            {
                float x = rect.xMin + snapX + 1;
                AddQuad(vh, x - 1, top - glyphH, x, top, PixelFont.WhiteUv, col);
            }
        }

        void LocateCaret(int first, out int line, out int column)
        {
            // Map a character index in _text to (wrapped line, column). Wrapping drops single spaces at
            // break points, so walk the source string alongside the wrapped lines.
            line = -1;
            column = 0;
            int target = _caretIndex < 0 ? _text.Length : Mathf.Min(_caretIndex, _text.Length);
            int src = 0;
            for (int li = 0; li < _lines.Count; li++)
            {
                string l = _lines[li];
                int end = src + l.Length;
                bool last = li == _lines.Count - 1;
                if (target <= end || last)
                {
                    line = li;
                    column = Mathf.Clamp(target - src, 0, l.Length);
                    break;
                }
                src = end;
                if (src < _text.Length && (_text[src] == '\n' || _text[src] == ' ')) src++;
            }
            if (line < first) line = -1;
        }

        static void AddQuad(VertexHelper vh, float x0, float y0, float x1, float y1, Rect uv, Color32 c)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), c, new Vector2(uv.xMin, uv.yMin));
            vh.AddVert(new Vector3(x0, y1), c, new Vector2(uv.xMin, uv.yMax));
            vh.AddVert(new Vector3(x1, y1), c, new Vector2(uv.xMax, uv.yMax));
            vh.AddVert(new Vector3(x1, y0), c, new Vector2(uv.xMax, uv.yMin));
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }
    }
}
