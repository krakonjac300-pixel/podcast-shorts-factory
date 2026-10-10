using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Art;
using UnityEngine;

namespace SecondCursor.Rendering
{
    /// <summary>
    /// Turns the pixel-art data (PixelArtData) into point-filtered Unity sprites on demand, with cached
    /// recoloured variants (e.g. the second cursor's inverted palette). Missing art never breaks the game:
    /// it renders as a small magenta checker and logs once.
    /// </summary>
    public static class SpriteLibrary
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        static readonly HashSet<string> Warned = new HashSet<string>(StringComparer.Ordinal);
        static readonly Dictionary<string, PixelSprite> Local = new Dictionary<string, PixelSprite>(StringComparer.Ordinal);

        /// <summary>Registers a small code-defined sprite (same palette chars as PixelArtData).</summary>
        public static void Define(string name, int hotspotX, int hotspotY, params string[] rows)
        {
            Local[name] = new PixelSprite(name, hotspotX, hotspotY, rows);
        }

        public static bool Has(string name) => Local.ContainsKey(name) || PixelArtData.Has(name);

        static PixelSprite Find(string name)
        {
            if (Local.TryGetValue(name, out var s)) return s;
            return PixelArtData.Has(name) ? PixelArtData.Get(name) : null;
        }

        public static Sprite Get(string name) => GetVariant(name, null, null);

        /// <summary>
        /// A recoloured copy. <paramref name="remap"/> maps palette chars to replacement colours (return
        /// null to keep the original). <paramref name="variantKey"/> must uniquely identify the mapping.
        /// </summary>
        public static Sprite GetVariant(string name, string variantKey, Func<char, Color32?> remap)
        {
            string key = variantKey == null ? name : name + "#" + variantKey;
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            Texture2D tex;
            var src = Find(name);
            if (src == null)
            {
                if (Warned.Add(name)) GameLog.Warn(LogChannel.System, "Missing sprite '" + name + "'");
                tex = Checker();
            }
            else
            {
                tex = Build(src, remap);
            }
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f), 1f, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Phase Q4 (R5): a recoloured copy with a 1 px halo of <paramref name="halo"/> around every opaque cell, in the empty cells beside it.
        /// The sprite is two pixels wider and taller than the original and its content starts one pixel in (the caller shifts the hotspot by one).
        /// </summary>
        public static Sprite GetHalo(string name, string variantKey, Func<char, Color32?> remap, Color32 halo)
        {
            string key = name + "#" + variantKey + "+halo";
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var src = Find(name);
            if (src == null) return GetVariant(name, variantKey, remap);
            var tex = BuildHalo(src, remap, halo);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f), 1f, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        static Texture2D BuildHalo(PixelSprite sprite, Func<char, Color32?> remap, Color32 halo)
        {
            int w = sprite.Width + 2, h = sprite.Height + 2;
            var px = new Color32[w * h];
            var solid = new bool[w * h];
            for (int y = 0; y < sprite.Height; y++)
            {
                string row = sprite.Rows[y];
                for (int x = 0; x < sprite.Width; x++)
                {
                    char ch = row[x];
                    Color32 c = PaletteColor(ch);
                    var m = remap != null ? remap(ch) : null;
                    if (m.HasValue) c = m.Value;
                    if (c.a == 0) continue;
                    int i = (h - 1 - (y + 1)) * w + (x + 1);   // texture rows start at the bottom
                    px[i] = c;
                    solid[i] = true;
                }
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (solid[i]) continue;
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            if (solid[ny * w + nx]) near = true;
                        }
                    if (near) px[i] = halo;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = sprite.Name + " halo",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        public static Vector2Int Size(string name)
        {
            var s = Find(name);
            return s == null ? new Vector2Int(8, 8) : new Vector2Int(s.Width, s.Height);
        }

        public static Vector2Int Hotspot(string name)
        {
            var s = Find(name);
            return s == null ? Vector2Int.zero : new Vector2Int(s.HotspotX, s.HotspotY);
        }

        /// <summary>Texture of a sprite (shared). Callers may change wrap mode for tiling.</summary>
        public static Texture2D TextureOf(string name) => Get(name).texture;

        public static Color32 PaletteColor(char c)
        {
            if (PixelPalette.TryGet(c, out uint rgba)) return FromRgba(rgba);
            return new Color32(255, 0, 255, 255);
        }

        public static Color32 FromRgba(uint rgba) =>
            new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);

        static Texture2D Build(PixelSprite sprite, Func<char, Color32?> remap)
        {
            int w = sprite.Width, h = sprite.Height;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                string row = sprite.Rows[y];
                for (int x = 0; x < w; x++)
                {
                    char ch = row[x];
                    Color32 c = PaletteColor(ch);
                    if (remap != null)
                    {
                        var m = remap(ch);
                        if (m.HasValue) c = m.Value;
                    }
                    px[(h - 1 - y) * w + x] = c; // texture rows start at the bottom
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = sprite.Name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D Checker()
        {
            var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[64];
            for (int i = 0; i < 64; i++) px[i] = ((i % 8) / 2 + (i / 8) / 2) % 2 == 0 ? new Color32(255, 0, 255, 255) : new Color32(0, 0, 0, 255);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Solid 1x1 white texture helper for procedural overlays.</summary>
        public static Texture2D MakeTexture(int w, int h, Func<int, int, Color32> pixel, FilterMode filter = FilterMode.Point, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = wrap };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = pixel(x, y);
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
