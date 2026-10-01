using System.Collections.Generic;
using SecondCursor.Core.Art;
using UnityEngine;

namespace SecondCursor.Rendering
{
    /// <summary>
    /// Bakes the bitmap font data (PixelFontData) into a point-filtered atlas texture with regular and
    /// bold variants, and provides measuring / word-wrapping shared by every text element.
    /// </summary>
    public static class PixelFont
    {
        public struct Glyph
        {
            public int Width;
            public Rect Uv;
        }

        const int AtlasWidth = 512;
        static Texture2D _atlas;
        static readonly Dictionary<char, Glyph> Regular = new Dictionary<char, Glyph>();
        static readonly Dictionary<char, Glyph> Bold = new Dictionary<char, Glyph>();
        static Glyph _regularFallback, _boldFallback;
        static Rect _whiteUv;

        public static int GlyphHeight => PixelFontData.GlyphHeight;
        public static int LineHeight => PixelFontData.LineHeight;
        public static int Ascent => PixelFontData.Ascent;

        public static Texture2D Atlas
        {
            get
            {
                if (_atlas == null) Build();
                return _atlas;
            }
        }

        /// <summary>UV of a solid white texel (used for carets and underlines).</summary>
        public static Rect WhiteUv
        {
            get
            {
                if (_atlas == null) Build();
                return _whiteUv;
            }
        }

        public static Glyph Get(char c, bool bold)
        {
            if (_atlas == null) Build();
            var map = bold ? Bold : Regular;
            if (map.TryGetValue(c, out var g)) return g;
            return bold ? _boldFallback : _regularFallback;
        }

        public static int Advance(char c, bool bold)
        {
            if (c == ' ') return PixelFontData.SpaceAdvance + (bold ? 1 : 0);
            if (c == '\t') return (PixelFontData.SpaceAdvance + 1) * 4;
            return Get(c, bold).Width + PixelFontData.LetterSpacing;
        }

        /// <summary>Width in pixels of a single line (no trailing letter spacing).</summary>
        public static int MeasureLine(string line, bool bold, int scale = 1) => MeasureLine(line, bold, (float)Mathf.Max(1, scale));

        /// <summary>Phase Q1: the same at a half-step scale (Medium reading text is 1.5); rounded up to whole pixels.</summary>
        public static int MeasureLine(string line, bool bold, float scale)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int w = 0;
            foreach (char c in line)
            {
                if (c == '\r' || c == '\n') continue;
                w += Advance(c, bold);
            }
            return Mathf.CeilToInt((w - PixelFontData.LetterSpacing) * Mathf.Max(1f, scale) - 0.001f);
        }

        /// <summary>Splits on newlines and word-wraps to maxWidth (pixels, already scaled). maxWidth &lt;= 0 disables wrapping.</summary>
        public static List<string> Wrap(string text, int maxWidth, bool bold, int scale, List<string> into = null) =>
            Wrap(text, maxWidth, bold, (float)scale, into);

        // Phase Q4 (CH7): one cached delegate per weight (no closure per call); the algorithm is Core.Art.TextWrap, one pass.
        static readonly System.Func<char, int> AdvanceRegular = c => Advance(c, false), AdvanceBold = c => Advance(c, true);

        public static List<string> Wrap(string text, int maxWidth, bool bold, float scale, List<string> into = null)
            => Core.Art.TextWrap.Wrap(text, maxWidth, scale, bold ? AdvanceBold : AdvanceRegular, into);

        static readonly List<string> _measureScratch = new List<string>(32);

        public static Vector2Int Measure(string text, int maxWidth, bool bold, int scale) => Measure(text, maxWidth, bold, (float)scale);

        public static Vector2Int Measure(string text, int maxWidth, bool bold, float scale)
        {
            scale = Mathf.Max(1f, scale);
            var lines = Wrap(text, maxWidth, bold, scale, _measureScratch);   // measuring only: no list per call
            int w = 0;
            foreach (var l in lines) w = Mathf.Max(w, MeasureLine(l, bold, scale));
            int h = lines.Count == 0 ? 0 : Mathf.CeilToInt(((lines.Count - 1) * PixelFontData.LineHeight + PixelFontData.GlyphHeight) * scale - 0.001f);
            return new Vector2Int(w, h);
        }

        static void Build()
        {
            var chars = new List<char>();
            for (int c = 32; c <= 126; c++) chars.Add((char)c);
            foreach (var kv in PixelFontData.Glyphs) if (!chars.Contains(kv.Key)) chars.Add(kv.Key);

            int h = PixelFontData.GlyphHeight;
            int rowH = h + 1;

            // First pass: layout.
            var layout = new List<(char c, bool bold, string[] rows, int x, int y, int w)>();
            int cx = 3, cy = 0; // 2x2 white block lives at (0,0)
            void Place(char c, bool bold, string[] rows)
            {
                int w = rows.Length > 0 ? rows[0].Length : 1;
                if (bold) w += 1;
                if (cx + w + 1 > AtlasWidth)
                {
                    cx = 0;
                    cy += rowH;
                }
                layout.Add((c, bold, rows, cx, cy, w));
                cx += w + 1;
            }
            foreach (char c in chars)
            {
                if (c == ' ') continue;
                var rows = PixelFontData.GetGlyph(c);
                Place(c, false, rows);
                Place(c, true, rows);
            }
            var fb = PixelFontData.GetGlyph('\u0001');
            Place('\u0001', false, fb);
            Place('\u0001', true, fb);

            int texH = Mathf.NextPowerOfTwo(cy + rowH + 1);
            var pixels = new Color32[AtlasWidth * texH];
            var clear = new Color32(255, 255, 255, 0);
            var ink = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

            void Set(int x, int yTop, Color32 col)
            {
                int ty = texH - 1 - yTop;
                if (x < 0 || x >= AtlasWidth || ty < 0 || ty >= texH) return;
                pixels[ty * AtlasWidth + x] = col;
            }

            Set(0, 0, ink); Set(1, 0, ink); Set(0, 1, ink); Set(1, 1, ink);
            _whiteUv = new Rect(0.5f / AtlasWidth, (texH - 1.5f) / texH, 0.001f, 0.001f);

            foreach (var g in layout)
            {
                for (int row = 0; row < g.rows.Length && row < h; row++)
                {
                    string line = g.rows[row];
                    for (int col = 0; col < line.Length; col++)
                    {
                        if (line[col] != '#') continue;
                        Set(g.x + col, g.y + row, ink);
                        if (g.bold) Set(g.x + col + 1, g.y + row, ink);
                    }
                }
                var uv = new Rect(g.x / (float)AtlasWidth, (texH - g.y - h) / (float)texH, g.w / (float)AtlasWidth, h / (float)texH);
                var glyph = new Glyph { Width = g.w, Uv = uv };
                if (g.c == '\u0001')
                {
                    if (g.bold) _boldFallback = glyph; else _regularFallback = glyph;
                }
                else
                {
                    (g.bold ? Bold : Regular)[g.c] = glyph;
                }
            }

            _atlas = new Texture2D(AtlasWidth, texH, TextureFormat.RGBA32, false)
            {
                name = "NEXUS Pixel Font",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _atlas.SetPixels32(pixels);
            _atlas.Apply(false, true);
        }
    }
}
