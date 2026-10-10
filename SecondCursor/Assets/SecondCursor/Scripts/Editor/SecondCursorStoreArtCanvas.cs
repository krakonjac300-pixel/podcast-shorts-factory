using System;
using System.IO;
using SecondCursor.Core.Art;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// A plain pixel buffer for the store art renders (top-left origin, y down), with the game's own pixel sprites and
    /// bitmap font. Nothing here touches the scene: the renders are built from <see cref="PixelArtData"/> and
    /// <see cref="PixelFontData"/> only.
    /// </summary>
    public sealed class ArtCanvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public ArtCanvas(int w, int h, Color32 fill)
        {
            W = w;
            H = h;
            Px = new Color32[w * h];
            for (int i = 0; i < Px.Length; i++) Px[i] = fill;
        }

        public void Set(int x, int y, Color32 c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            Px[y * W + x] = c;
        }

        /// <summary>Alpha-blends <paramref name="c"/> (its alpha times <paramref name="amount"/>) over the pixel.</summary>
        public void Blend(int x, int y, Color32 c, float amount = 1f)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int i = y * W + x;
            var d = Px[i];
            float a = Mathf.Clamp01(c.a / 255f * amount);
            if (a <= 0f) return;
            float da = d.a / 255f, oa = a + da * (1f - a);
            if (oa <= 0f) return;
            Px[i] = new Color32(
                (byte)Mathf.Clamp((c.r * a + d.r * da * (1f - a)) / oa, 0, 255),
                (byte)Mathf.Clamp((c.g * a + d.g * da * (1f - a)) / oa, 0, 255),
                (byte)Mathf.Clamp((c.b * a + d.b * da * (1f - a)) / oa, 0, 255),
                (byte)Mathf.Clamp(oa * 255f, 0, 255));
        }

        public void FillRect(int x, int y, int w, int h, Color32 c, float amount = 1f)
        {
            for (int yy = Math.Max(0, y); yy < Math.Min(H, y + h); yy++)
                for (int xx = Math.Max(0, x); xx < Math.Min(W, x + w); xx++)
                    Blend(xx, yy, c, amount);
        }

        // ------------------------------------------------------------------ sprites

        /// <summary>Palette colour of a sprite char (null for transparent).</summary>
        public static Color32? PaletteColor(char ch)
        {
            if (ch == '.' || !PixelPalette.TryGet(ch, out uint rgba)) return null;
            return new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        /// <summary>Draws a sprite with its top-left at (left, top), each sprite pixel <paramref name="scale"/> pixels wide.</summary>
        public void Stamp(PixelSprite sprite, int left, int top, int scale, Func<char, Color32?> map = null, float amount = 1f)
        {
            if (sprite == null) return;
            map = map ?? PaletteColor;
            for (int row = 0; row < sprite.Height; row++)
                for (int col = 0; col < sprite.Width; col++)
                {
                    var c = map(sprite.Rows[row][col]);
                    if (c == null) continue;
                    FillRect(left + col * scale, top + row * scale, scale, scale, c.Value, amount);
                }
        }

        /// <summary>A soft shadow under a sprite (every opaque pixel, offset and faint).</summary>
        public void Shadow(PixelSprite sprite, int left, int top, int scale, int offset, float amount)
        {
            Stamp(sprite, left + offset, top + offset, scale, ch => ch == '.' ? (Color32?)null : new Color32(0, 0, 0, 255), amount);
        }

        // ------------------------------------------------------------------ text (the game's bitmap font)

        /// <summary>Width of a line in font pixels (bold adds one column per glyph, like the game's atlas).</summary>
        public static int MeasureText(string text, bool bold)
        {
            int w = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int adv = c == ' ' ? PixelFontData.SpaceAdvance + (bold ? 1 : 0) : PixelFontData.GetWidth(c) + (bold ? 1 : 0) + PixelFontData.LetterSpacing;
                w += adv;
            }
            return Math.Max(0, w - PixelFontData.LetterSpacing);
        }

        /// <summary>Draws one line of text with its top-left at (left, top) in the game's pixel font.</summary>
        public void Text(string text, int left, int top, int scale, Color32 color, bool bold, float amount = 1f)
        {
            int pen = left;
            foreach (char c in text)
            {
                if (c == ' ')
                {
                    pen += (PixelFontData.SpaceAdvance + (bold ? 1 : 0)) * scale;
                    continue;
                }
                var rows = PixelFontData.GetGlyph(c);
                int gw = rows[0].Length;
                for (int row = 0; row < rows.Length; row++)
                    for (int col = 0; col < gw; col++)
                    {
                        if (rows[row][col] != '#') continue;
                        FillRect(pen + col * scale, top + row * scale, scale, scale, color, amount);
                        if (bold) FillRect(pen + (col + 1) * scale, top + row * scale, scale, scale, color, amount);
                    }
                pen += (gw + (bold ? 1 : 0) + PixelFontData.LetterSpacing) * scale;
            }
        }

        /// <summary>Centred text; returns the left edge used.</summary>
        public int TextCentered(string text, int centerX, int top, int scale, Color32 color, bool bold, float amount = 1f)
        {
            int left = centerX - MeasureText(text, bold) * scale / 2;
            Text(text, left, top, scale, color, bold, amount);
            return left;
        }

        // ------------------------------------------------------------------ backgrounds

        /// <summary>
        /// CRT glass: a teal glow at (gx, gy) falling off to dark green-slate, a soft vignette and optional scanlines
        /// (the app icon's palette).
        /// </summary>
        public void CrtGlass(float gx, float gy, float glowRadius, int scanlineEvery, float glowStrength = 0.42f)
        {
            var edge = new Color32(0x1A, 0x27, 0x25, 0xFF);
            var mid = new Color32(0x2B, 0x3D, 0x3A, 0xFF);
            var glow = new Color32(0x3E, 0x7A, 0x6E, 0xFF);
            float diag = Mathf.Sqrt(W * W * 0.25f + H * H * 0.25f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float d = Mathf.Sqrt((x - gx) * (x - gx) + (y - gy) * (y - gy));
                    float g = Mathf.Clamp01(1f - d / glowRadius);
                    g = g * g * (3f - 2f * g);
                    var c = Color32.Lerp(mid, glow, g * glowStrength);
                    // Vignette from the frame centre.
                    float v = Mathf.Sqrt((x - W * 0.5f) * (x - W * 0.5f) + (y - H * 0.5f) * (y - H * 0.5f)) / diag;
                    c = Color32.Lerp(c, edge, Mathf.Clamp01((v - 0.45f) / 0.55f) * 0.85f);
                    if (scanlineEvery > 1 && y % scanlineEvery == 0) c = Color32.Lerp(c, new Color32(0, 0, 0, 255), 0.18f);
                    Px[y * W + x] = c;
                }
        }

        /// <summary>A dotted tug-of-war band between two points: pale at the ends, red toward the middle.</summary>
        public void TugBand(Vector2 a, Vector2 b, int dot, int gap)
        {
            var pale = new Color32(0xE6, 0xEC, 0xEA, 0xFF);
            var red = new Color32(0xB0, 0x33, 0x28, 0xFF);
            float len = Vector2.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.FloorToInt(len / (dot + gap)));
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = Vector2.Lerp(a, b, t);
                float mid = 1f - Mathf.Abs(t - 0.5f) * 2f;
                FillRect(Mathf.RoundToInt(p.x - dot / 2f), Mathf.RoundToInt(p.y - dot / 2f), dot, dot, Color32.Lerp(pale, red, mid));
            }
        }

        // ------------------------------------------------------------------ output

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var flipped = new Color32[Px.Length];
            for (int y = 0; y < H; y++) Array.Copy(Px, y * W, flipped, (H - 1 - y) * W, W);
            tex.SetPixels32(flipped);
            tex.Apply();
            return tex;
        }

        public void SavePng(string path)
        {
            var tex = ToTexture();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        public void SaveJpg(string path, int quality = 92)
        {
            var tex = ToTexture();
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllBytes(path, tex.EncodeToJPG(quality));
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>Greyscale and darker: the locked (unachieved) version of an achievement icon.</summary>
        public ArtCanvas Locked()
        {
            var c = new ArtCanvas(W, H, new Color32(0, 0, 0, 0));
            for (int i = 0; i < Px.Length; i++)
            {
                var p = Px[i];
                float l = (0.299f * p.r + 0.587f * p.g + 0.114f * p.b) * 0.5f + 12f;
                byte v = (byte)Mathf.Clamp(l, 0, 255);
                c.Px[i] = new Color32(v, v, v, p.a);
            }
            return c;
        }
    }
}
