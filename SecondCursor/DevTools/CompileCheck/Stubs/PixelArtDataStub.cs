// Harness-only placeholder used while the real PixelArtData.cs is being authored. Same public API.
using System;
using System.Collections.Generic;
namespace SecondCursor.Core.Art
{
    public sealed class PixelSprite
    {
        public readonly string Name; public readonly int Width; public readonly int Height;
        public readonly string[] Rows; public readonly int HotspotX; public readonly int HotspotY;
        public PixelSprite(string name, int hotspotX, int hotspotY, params string[] rows) { Name = name; HotspotX = hotspotX; HotspotY = hotspotY; Rows = rows; Height = rows.Length; Width = rows.Length > 0 ? rows[0].Length : 0; }
        public char this[int x, int y] => Rows[y][x];
    }
    public static class PixelPalette
    {
        public static readonly IReadOnlyDictionary<char, uint> Colors = new Dictionary<char, uint> { { '.', 0u } };
        public static bool TryGet(char c, out uint rgba) => Colors.TryGetValue(c, out rgba);
    }
    public static class PixelArtData
    {
        public static readonly IReadOnlyDictionary<string, PixelSprite> Sprites = new Dictionary<string, PixelSprite>();
        public static PixelSprite Get(string name) => throw new KeyNotFoundException(name);
        public static bool Has(string name) => false;
    }
}
