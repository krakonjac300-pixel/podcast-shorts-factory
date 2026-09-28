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

        public static bool Has(string name) => PixelArtData.Has(name);

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
            if (!PixelArtData.Has(name))
            {
                if (Warned.Add(name)) GameLog.Warn(LogChannel.System, "Missing sprite '" + name + "'");
                tex = Checker();
            }
            else
            {
                tex = Build(PixelArtData.Get(name), remap);
            }
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0f, 1f), 1f, 0, SpriteMeshType.FullRect);
            sprite.name = key;
            Cache[key] = sprite;
            return sprite;
        }

        public static Vector2Int Size(string name)
        {
            if (!PixelArtData.Has(name)) return new Vector2Int(8, 8);
            var s = PixelArtData.Get(name);
            return new Vector2Int(s.Width, s.Height);
        }

        public static Vector2Int Hotspot(string name)
        {
            if (!PixelArtData.Has(name)) return Vector2Int.zero;
            var s = PixelArtData.Get(name);
            return new Vector2Int(s.HotspotX, s.HotspotY);
        }

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
