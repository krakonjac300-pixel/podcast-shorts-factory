// -----------------------------------------------------------------------------
// SECOND CURSOR - NEXUS OS pixel art: icons, cursors, UI glyphs, logos.
//
// Pure C# (no UnityEngine reference) so it lives in SecondCursor.Core and can be
// validated outside Unity (see DevTools/ArtPreview). Every sprite is stored as
// rows of palette characters, Rows[0] being the TOP row, one char per pixel,
// '.' = transparent. The runtime layer converts them to textures/cursors.
//
// Art direction - keep new art consistent with these rules:
//   * 16x16 icons read at 1x (lists, title bars) and 2x (desktop). 1px 'K'
//     outline, light from the top-left (highlight on top/left inner edges,
//     shade on bottom/right inner edges), few colours per icon, chunky shapes.
//     Paper documents share one page silhouette (x 2..13, dog-ear top-right).
//   * NEXUS teal ('T'/'t', glow 'C') is the brand colour; red means alert or
//     danger; 'V' (glitch magenta) is reserved for corruption.
//   * Cursors use ONLY 'K' (outline), 'W' (fill) and '.', so the game can
//     recolour them (e.g. the second cursor). Hotspots are set per cursor.
//   * UI glyphs are 'K' on transparent (tint at runtime for disabled/pressed
//     states); glyph_resize_grip uses 'W'/'D' ridges.
//   * Original designs only: nothing here copies another OS's icons or logos.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Art
{
    /// <summary>
    /// A palette-indexed pixel sprite. Rows[0] is the top row; every row has
    /// length <see cref="Width"/>; each char is a key of <see cref="PixelPalette.Colors"/>.
    /// </summary>
    public sealed class PixelSprite
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;

        /// <summary>Rows[0] is the TOP row; every row has length Width; one char per pixel.</summary>
        public readonly string[] Rows;

        /// <summary>Hotspot in pixels from the left edge (cursors; 0 for everything else).</summary>
        public readonly int HotspotX;

        /// <summary>Hotspot in pixels from the top edge (cursors; 0 for everything else).</summary>
        public readonly int HotspotY;

        /// <exception cref="ArgumentException">
        /// The rows are missing, have unequal lengths, or use a char that is not in
        /// <see cref="PixelPalette"/>; or the hotspot lies outside the sprite.
        /// The message names the sprite and the offending row.
        /// </exception>
        public PixelSprite(string name, int hotspotX, int hotspotY, params string[] rows)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("PixelSprite: a sprite name is required.", nameof(name));
            if (rows == null || rows.Length == 0)
                throw new ArgumentException($"PixelSprite '{name}': at least one row is required.", nameof(rows));

            string firstRow = rows[0];
            if (string.IsNullOrEmpty(firstRow))
                throw new ArgumentException($"PixelSprite '{name}': row 0 is null or empty.", nameof(rows));

            int width = firstRow.Length;
            for (int y = 0; y < rows.Length; y++)
            {
                string row = rows[y];
                if (row == null)
                    throw new ArgumentException($"PixelSprite '{name}': row {y} is null.", nameof(rows));
                if (row.Length != width)
                    throw new ArgumentException(
                        $"PixelSprite '{name}': row {y} has length {row.Length}, expected {width} (the length of row 0).",
                        nameof(rows));
                for (int x = 0; x < row.Length; x++)
                {
                    if (!PixelPalette.TryGet(row[x], out _))
                        throw new ArgumentException(
                            $"PixelSprite '{name}': row {y}, column {x} uses '{row[x]}', which is not a PixelPalette colour.",
                            nameof(rows));
                }
            }

            if (hotspotX < 0 || hotspotX >= width || hotspotY < 0 || hotspotY >= rows.Length)
                throw new ArgumentException(
                    $"PixelSprite '{name}': hotspot ({hotspotX},{hotspotY}) lies outside the {width}x{rows.Length} sprite.",
                    nameof(hotspotX));

            Name = name;
            Width = width;
            Height = rows.Length;
            Rows = (string[])rows.Clone();
            HotspotX = hotspotX;
            HotspotY = hotspotY;
        }

        /// <summary>Palette char at column <paramref name="x"/> and row <paramref name="y"/> (y from the top).</summary>
        public char this[int x, int y]
        {
            get
            {
                if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
                    throw new ArgumentOutOfRangeException(
                        (uint)x >= (uint)Width ? nameof(x) : nameof(y),
                        $"PixelSprite '{Name}': pixel ({x},{y}) is outside the {Width}x{Height} sprite.");
                return Rows[y][x];
            }
        }

        public override string ToString() => $"PixelSprite '{Name}' {Width}x{Height} (hotspot {HotspotX},{HotspotY})";
    }

    /// <summary>The NEXUS OS palette: sprite chars mapped to 0xRRGGBBAA colours.</summary>
    public static class PixelPalette
    {
        public static readonly IReadOnlyDictionary<char, uint> Colors;

        static PixelPalette()
        {
            var colors = new Dictionary<char, uint>
            {
                ['.'] = 0x00000000u, // transparent
                ['K'] = 0x141311FFu, // outline, near-black
                ['W'] = 0xFFFFFFFFu, // white (highlights, cursor fill)
                ['L'] = 0xDDDAD0FFu, // light UI grey (bevel light)
                ['G'] = 0xC2BFB2FFu, // UI face, beige-grey
                ['D'] = 0x86837AFFu, // UI shadow
                ['S'] = 0x4A4843FFu, // deep shadow
                ['P'] = 0xF6F4EAFFu, // paper
                ['Y'] = 0xE8C45AFFu, // folder yellow
                ['y'] = 0xA8812AFFu, // dark yellow
                ['H'] = 0xF7E3A0FFu, // pale yellow highlight (added)
                ['B'] = 0x3F6FA8FFu, // blue
                ['b'] = 0x1F3E6BFFu, // dark blue
                ['A'] = 0x8DB2DCFFu, // pale blue highlight (added)
                ['T'] = 0x3E7A6EFFu, // NEXUS teal
                ['t'] = 0x183F3AFFu, // dark teal
                ['C'] = 0x7FD6D0FFu, // screen-glow cyan
                ['R'] = 0xC0392BFFu, // red
                ['r'] = 0x7A1F17FFu, // dark red
                ['E'] = 0xE8806FFFu, // pale red highlight (added)
                ['O'] = 0xD9822BFFu, // orange
                ['o'] = 0x8A5226FFu, // brown / dark orange (added)
                ['N'] = 0x4E9A4EFFu, // green
                ['n'] = 0x2A5C2AFFu, // dark green
                ['M'] = 0x9AA3A8FFu, // metal
                ['m'] = 0x5E666BFFu, // dark metal
                ['V'] = 0xB0469BFFu, // glitch magenta (added; corruption only)
                ['Q'] = 0x2B3D3AFFu, // desktop dark
                ['q'] = 0x243330FFu, // desktop darker
            };
            Colors = new System.Collections.ObjectModel.ReadOnlyDictionary<char, uint>(colors);
        }

        /// <summary>Looks up a palette char; returns false for chars that are not in the palette.</summary>
        public static bool TryGet(char c, out uint rgba) => Colors.TryGetValue(c, out rgba);
    }

    /// <summary>All NEXUS OS sprites by name (see the section methods below for the catalogue).</summary>
    public static class PixelArtData
    {
        public static readonly IReadOnlyDictionary<string, PixelSprite> Sprites;

        static PixelArtData()
        {
            // Sprites validate their chars against the palette, so the palette must be
            // built first. Touching it here runs PixelPalette's static constructor now.
            if (PixelPalette.Colors.Count == 0)
                throw new InvalidOperationException("PixelArtData: PixelPalette is empty.");

            var sprites = new Dictionary<string, PixelSprite>(StringComparer.Ordinal);
            AddIcons(sprites);
            AddCursors(sprites);
            AddGlyphs(sprites);
            AddLogos(sprites);
            AddMisc(sprites);
            Sprites = new System.Collections.ObjectModel.ReadOnlyDictionary<string, PixelSprite>(sprites);
        }

        /// <exception cref="KeyNotFoundException">No sprite has that name.</exception>
        public static PixelSprite Get(string name)
        {
            if (name != null && Sprites.TryGetValue(name, out PixelSprite sprite))
                return sprite;
            throw new KeyNotFoundException($"PixelArtData: unknown sprite '{name}'");
        }

        public static bool Has(string name) => name != null && Sprites.ContainsKey(name);

        private static void Add(Dictionary<string, PixelSprite> sprites, string name, int hotspotX, int hotspotY,
            params string[] rows)
        {
            if (sprites.ContainsKey(name))
                throw new InvalidOperationException($"PixelArtData: duplicate sprite '{name}'.");
            sprites.Add(name, new PixelSprite(name, hotspotX, hotspotY, rows));
        }

        // 16x16 icons: shown at 1x in lists/title bars and at 2x on the desktop.
        private static void AddIcons(Dictionary<string, PixelSprite> s)
        {
            // Beige desktop case with a CRT showing the NEXUS teal desktop.
            Add(s, "icon_workstation", 0, 0,
                ".KKKKKKKKKKKKKK.",
                ".KWWWWWWWWWWWLK.",
                ".KWKKKKKKKKKKGK.",
                ".KWKCCTTTTTTKGK.",
                ".KWKCTTTTTTTKGK.",
                ".KWKTTTTTTTTKGK.",
                ".KWKTTTTTTTTKGK.",
                ".KWKLGGGGGGGKGK.",
                ".KWKKKKKKKKKKGK.",
                ".KLGGGGGGGGNGDK.",
                ".KKKKKKKKKKKKKK.",
                "....KDDDDDDK....",
                "KKKKKKKKKKKKKKKK",
                "KWWWWWWWWWWWWWLK",
                "KLGGSSSSSGGGOGDK",
                "KKKKKKKKKKKKKKKK");

            // Manila folder, tab on the left.
            Add(s, "icon_folder", 0, 0,
                "................",
                "................",
                "..KKKKK.........",
                ".KyyyyyK........",
                "KyyyyyyyKKKKKKK.",
                "KyyyyyyyyyyyyyyK",
                "KKKKKKKKKKKKKKKK",
                "KHHHHHHHHHHHHHHK",
                "KHYYYYYYYYYYYYyK",
                "KHYPPPPPYYYYYYyK",
                "KHYPDDDPYYYYYYyK",
                "KHYLLLLLYYYYYYyK",
                "KHYYYYYYYYYYYYyK",
                "KyyyyyyyyyyyyyyK",
                "KKKKKKKKKKKKKKKK",
                "................");

            // Open folder: a sheet inside, front flap swung forward.
            Add(s, "icon_folder_open", 0, 0,
                "................",
                ".KKKK...........",
                "KyyyyK..........",
                "KyyyyyKKKKKKKK..",
                "KyyKKKKKKKKKyK..",
                "KyyKWWWWWWWKyK..",
                "KyyKWLLLLLWKyK..",
                "KyyKWWWWWWWKyK..",
                "KyyKKKKKKKKKKKKK",
                "KyyKHHHHHHHHHHHK",
                "KyKHYPPPPPYYYyK.",
                "KyKHYPDDDPYYYyK.",
                "KKHYLLLLLYYYyK..",
                "KKHYYYYYYYYYyK..",
                "KKKKKKKKKKKKK...",
                "................");

            // Folder with a steel padlock.
            Add(s, "icon_folder_locked", 0, 0,
                "................",
                "................",
                "..KKKKK.........",
                ".KyyyyyK........",
                "KyyyyyyyKKKKKKK.",
                "KyyyyyyyyyyyyyyK",
                "KKKKKKKKKKKKKKKK",
                "KHHHHHHHHHHKKKHK",
                "KHYYYYYYYYKYYYKK",
                "KHYPPPPPYYKYYYKK",
                "KHYPDDDPYKKKKKKK",
                "KHYLLLLLYKWMMMmK",
                "KHYYYYYYYKMMKMmK",
                "KyyyyyyyyKMMKMmK",
                "KKKKKKKKKKmmmmmK",
                ".........KKKKKKK");

            // Text document.
            Add(s, "icon_file_txt", 0, 0,
                "..KKKKKKKK......",
                "..KWWWWWWKK.....",
                "..KWPPPPPKLK....",
                "..KWPDDDPKLLK...",
                "..KWPPPPPKKKKK..",
                "..KWPDDDDDDPLK..",
                "..KWPPPPPPPPLK..",
                "..KWPDDDDPPPLK..",
                "..KWPPPPPPPPLK..",
                "..KWPDDDDDDPLK..",
                "..KWPPPPPPPPLK..",
                "..KWPDDDDDPPLK..",
                "..KWPPPPPPPPLK..",
                "..KWPDDDPPPPLK..",
                "..KLLLLLLLLLLK..",
                "..KKKKKKKKKKKK..");

            // Data file: a ruled table with a blue header.
            Add(s, "icon_file_dat", 0, 0,
                "..KKKKKKKK......",
                "..KWWWWWWKK.....",
                "..KWPPPPPKLK....",
                "..KWPPPPPKLLK...",
                "..KWPPPPPKKKKK..",
                "..KWBBBBBBBBLK..",
                "..KWAPPAPPPALK..",
                "..KWAAAAAAAALK..",
                "..KWAPPAPPPALK..",
                "..KWAAAAAAAALK..",
                "..KWAPPAPPPALK..",
                "..KWAAAAAAAALK..",
                "..KWAPPAPPPALK..",
                "..KWAAAAAAAALK..",
                "..KLLLLLLLLLLK..",
                "..KKKKKKKKKKKK..");

            // Log file: levelled entries (green/orange/red) and a clock.
            Add(s, "icon_file_log", 0, 0,
                "..KKKKKKKK......",
                "..KWWWWWWKK.....",
                "..KWPPPPPKLK....",
                "..KWNPDDPKLLK...",
                "..KWPPPPPKKKKK..",
                "..KWNPDDDDDPLK..",
                "..KWPPPPPPPPLK..",
                "..KWOPDDDDPPLK..",
                "..KWPPPPPPPPLK..",
                "..KWNPDDDPPKKK..",
                "..KWPPPPPPKWWWK.",
                "..KWRPDDPKWWKWWK",
                "..KWPPPPPKWWKKWK",
                "..KWNPDDPKWWWWWK",
                "..KLLLLLLLKWWWK.",
                "..KKKKKKKKKKKK..");

            // Temporary file: faded page with a dashed outline.
            Add(s, "icon_file_tmp", 0, 0,
                "..D.D.D.DD......",
                "...LLLLLLDD.....",
                "..DLLLLLLDGD....",
                "...LGGGLLDGGD...",
                "..DLLLLLLDDDDD..",
                "...LGGGGGGLLL...",
                "..DLLLLLLLLLLD..",
                "...LGGGGLLLLL...",
                "..DLLLLLLLLLLD..",
                "...LGGGGGGLLL...",
                "..DLLLLLLLLLLD..",
                "...LGGGGGLLLL...",
                "..DLLLLLLLLLLD..",
                "...LLLLLLLLLL...",
                "..DLLLLLLLLLLD..",
                "..D.D.D.D.D.D...");

            // Executable: a small NEXUS application window.
            Add(s, "icon_file_exe", 0, 0,
                "................",
                "KKKKKKKKKKKKKKKK",
                "KCCCCTTTTTTTTTTK",
                "KTTTTTTTTTTTTTTK",
                "KKKKKKKKKKKKKKKK",
                "KWWWWWWWWWWWWWLK",
                "KWPPPPPPPPPPPPLK",
                "KWPBBPDDDDDDPPLK",
                "KWPBBPPPPPPPPPLK",
                "KWPPPPDDDDPPPPLK",
                "KWPPPPPPPPPPPPLK",
                "KWPDDDDDDPPPPPLK",
                "KWPPPPPPPPPPPPLK",
                "KLLLLLLLLLLLLLLK",
                "KKKKKKKKKKKKKKKK",
                "................");

            // Configuration file: page with a gear.
            Add(s, "icon_file_cfg", 0, 0,
                "..KKKKKKKK......",
                "..KWWWWWWKK.....",
                "..KWPPPPPKLK....",
                "..KWPDDDPKLLK...",
                "..KWPPPPPKKKKK..",
                "..KWPDDDDDDPLK..",
                "..KWPPPPPPPPLK..",
                "..KWPDDDDPKKKK..",
                "..KWPPPPKPKWKKK.",
                "..KWPDDPPKWLMK..",
                "..KWPPPKKWLMMmKK",
                "..KWPDDKWLMKMmmK",
                "..KWPPPKKMMMmmKK",
                "..KWPPPPPKMmmK..",
                "..KLLLLLKLKmKKK.",
                "..KKKKKKKKKKKK..");

            // Corrupted file: glitch-shifted rows, torn corner, stray pixels.
            Add(s, "icon_file_corrupt", 0, 0,
                "..KKKKKKKK......",
                "..KWWWWWWKK.....",
                "..KWPPPPPKLK....",
                "..KWPDDDPKLLK...",
                "..KWPPPPPKKKKK..",
                "..KWPDDVDDDPLK..",
                "....KWPPPPPPPPLK",
                "..KWPDDDDCPPLK..",
                ".KWPPPPPPPPLK...",
                "..KWPDDDDDVPLK..",
                "..KWPPPPPPKK.V..",
                "..KWPVDDDK..C...",
                "..KWPPPPK.V.....",
                "..KWPDDK........",
                "..KLLLK..V......",
                "..KKKK..........");

            // Interoffice envelope sealed with a NEXUS teal sticker.
            Add(s, "icon_mail", 0, 0,
                "................",
                "................",
                "................",
                ".KKKKKKKKKKKKKK.",
                ".KSWWWWWWWWWWSK.",
                ".KPSWWWWWWWWSPK.",
                ".KPPSWWWWWWSPPK.",
                ".KPPPSWWWWSPPPK.",
                ".KPPPPSTTSPPPPK.",
                ".KPPPPTTTTPPPPK.",
                ".KPPPPPttPPPPPK.",
                ".KPPPPPPPPPPPPK.",
                ".KLLLLLLLLLLLLK.",
                ".KKKKKKKKKKKKKK.",
                "................",
                "................");

            // Envelope with a red unread badge.
            Add(s, "icon_mail_unread", 0, 0,
                "............KKK.",
                "...........KERRK",
                "...........KRRRK",
                ".KKKKKKKKKKKRRrK",
                ".KSWWWWWWWWWKKK.",
                ".KPSWWWWWWWWSPK.",
                ".KPPSWWWWWWSPPK.",
                ".KPPPSWWWWSPPPK.",
                ".KPPPPSTTSPPPPK.",
                ".KPPPPTTTTPPPPK.",
                ".KPPPPPttPPPPPK.",
                ".KPPPPPPPPPPPPK.",
                ".KLLLLLLLLLLLLK.",
                ".KKKKKKKKKKKKKK.",
                "................",
                "................");

            // Spiral notepad with a pencil.
            Add(s, "icon_notepad", 0, 0,
                "..K.K.K.K.K.....",
                ".KMKMKMKMKMKKK..",
                ".KSWSWSWSWSWLK..",
                ".KWWWWWWWWWWLK..",
                ".KWEPPPPPPPPLK..",
                ".KWEAAAAAAAAKKK.",
                ".KWEPPPPPPPKERK.",
                ".KWEAAAAAAKERK..",
                ".KWEPPPPPKLMKK..",
                ".KWEAAAAKYyKLK..",
                ".KWEPPPKYyKPLK..",
                ".KWEAAKYyKAALK..",
                ".KWEPKYyKPPPLK..",
                ".KWEKPLKAAAALK..",
                ".KLKSKLLLLLLLK..",
                ".KKKKKKKKKKKKK..");

            // Staff ID badge on a clip.
            Add(s, "icon_staff", 0, 0,
                ".......KK.......",
                "......KMMK......",
                "..KKKKKMMKKKKK..",
                "..KTTTTmmTTTTK..",
                "..KTTTTTTTTTTK..",
                "..KPPPPPPPPPLK..",
                "..KPPKKKKKKPLK..",
                "..KPPKASSAKPLK..",
                "..KPPKASSAKPLK..",
                "..KPPKSSSSKPLK..",
                "..KPPKSSSSKPLK..",
                "..KPPKKKKKKPLK..",
                "..KPPPPPPPPPLK..",
                "..KPDDDDDDDPLK..",
                "..KLLLLLLLLLLK..",
                "..KKKKKKKKKKKK..");

            // Security camera on a wall bracket.
            Add(s, "icon_camera", 0, 0,
                "................",
                "................",
                "..........KKKKKK",
                "..........KLMMmK",
                "..........KKKmKK",
                "............KmK.",
                "...........KKKKK",
                ".......KKKKWWWWK",
                "...KKKKWWWWLLLLK",
                "KKKWWWWLLLLLLRLK",
                "CSKLLLLLLLLGGGGK",
                "SSKLLLLGGGGKKKKK",
                "KKKGGGGKKKK.....",
                "..KKKKK.........",
                "................",
                "................");

            // Clipboard with a checklist.
            Add(s, "icon_workorders", 0, 0,
                "......KKKK......",
                "KKKKKKWMMmKKKKKK",
                "KOOOKLMMMMmKoooK",
                "KOKKKKKKKKKKKKoK",
                "KOKWKKKWWWWWWKoK",
                "KOKWKNKWDDDDWKoK",
                "KOKWKKKWWWWWWKoK",
                "KOKWWWWWWWWWWKoK",
                "KOKWKKKWWWWWWKoK",
                "KOKWKNKWDDDWWKoK",
                "KOKWKKKWWWWWWKoK",
                "KOKWWWWWWWWWWKoK",
                "KOKWKKKWWWWWWKoK",
                "KOKWKWKWDDDDWKoK",
                "KOKWKKKWWWWWWKoK",
                "KKKKKKKKKKKKKKKK");

            // Industrial shredder bin (empty): feed slot, hazard band, dark window.
            Add(s, "icon_disposal_empty", 0, 0,
                "................",
                ".KKKKKKKKKKKKKK.",
                ".KMMMMMMMMMMMmK.",
                ".KmKKKKKKKKKKmK.",
                ".KmKWKWKWKWKKmK.",
                ".KYYKKYYKKYYKKK.",
                ".KYKKYYKKYYKKYK.",
                ".KKKKKKKKKKKKKK.",
                "..KWLLLLLLLLGK..",
                "..KWLKKKKKKLGK..",
                "..KWLKSSSSKLGK..",
                "..KWLKSSSSKLGK..",
                "..KWLKKKKKKLGK..",
                "..KLGGGGGGGGDK..",
                "..KKKKKKKKKKKK..",
                "..KSK......KSK..");

            // Shredder bin (full): strips in the slot and the window.
            Add(s, "icon_disposal_full", 0, 0,
                "....W.P.W.P.....",
                ".KKKWKPKWKPKKKK.",
                ".KMMWMPMWMPMMmK.",
                ".KmKWKPKWKPKKmK.",
                ".KmKWKWKWKWKKmK.",
                ".KYYKKYYKKYYKKK.",
                ".KYKKYYKKYYKKYK.",
                ".KKKKKKKKKKKKKK.",
                "..KWLLLLLLLLGK..",
                "..KWLKKKKKKLGK..",
                "..KWLKWLWPKLGK..",
                "..KWLKPWLWKLGK..",
                "..KWLKKKKKKLGK..",
                "..KLGGGGGGGGDK..",
                "..KKKKKKKKKKKK..",
                "..KSK......KSK..");

            // Monitor with a settings gear.
            Add(s, "icon_system", 0, 0,
                "KKKKKKKKKKKKK...",
                "KWWWWWWWWWWLK...",
                "KWKKKKKKKKKGK...",
                "KWKCTTTTTTKGK...",
                "KWKTTTTTTTKGK...",
                "KWKTTTTTTTKGK...",
                "KWKTTTTTTtKGK...",
                "KWKKKKKKKKKKK...",
                "KLGGGGGGKNKWK.K.",
                "KKKKKKKKKKWLMK..",
                "....KDDKKWLMMmKK",
                "..KKKKKKWLMKMmmK",
                "..KLGGGKKMMMmmKK",
                "..KKKKKKKKMmmK..",
                "........K.KmK.K.",
                "..........KKK...");

            // Help book with a question mark.
            Add(s, "icon_help", 0, 0,
                "................",
                ".KKKKKKKKKKKKK..",
                ".KbKAAAAAAAAAK..",
                ".KbKABWWWWBBbK..",
                ".KbKAWWBBWWBbK..",
                ".KbKABBBBWWBbK..",
                ".KbKABBBWWBBbK..",
                ".KbKABBWWBBBbK..",
                ".KbKABBBBBBBbK..",
                ".KbKABBWWBBBbK..",
                ".KbKABBBBBBBbK..",
                ".KbKbbbbbbbbbK..",
                ".KbKKKKKKKKKKK..",
                ".KbKPPPPPPPPLK..",
                ".KKKKKKKKKKKKK..",
                "................");

            // Information: blue disc with an i.
            Add(s, "icon_info", 0, 0,
                ".....KKKKKK.....",
                "...KKAAAAAAKK...",
                "..KAABBBBBBBBK..",
                ".KABBBBWWBBBBBK.",
                ".KABBBBWWBBBBBK.",
                "KABBBBBBBBBBBBBK",
                "KABBBBWWWBBBBBbK",
                "KABBBBBWWBBBBBbK",
                "KABBBBBWWBBBBBbK",
                "KBBBBBBWWBBBBBbK",
                "KBBBBBBWWBBBBBbK",
                ".KBBBBBWWBBBBbK.",
                ".KBBBBWWWWBBBbK.",
                "..KBBBBBBBBbbK..",
                "...KKbbbbbbKK...",
                ".....KKKKKK.....");

            // Warning: yellow triangle with an exclamation mark.
            Add(s, "icon_warning", 0, 0,
                ".......KK.......",
                "......KHYK......",
                "......KHYK......",
                ".....KHYYyK.....",
                ".....KHKKyK.....",
                "....KHYKKYyK....",
                "....KHYKKYyK....",
                "...KHYYKKYYyK...",
                "...KHYYKKYYyK...",
                "..KHYYYKKYYYyK..",
                "..KHYYYYYYYYyK..",
                ".KHYYYYKKYYYYyK.",
                ".KHYYYYKKYYYYyK.",
                "KHyyyyyyyyyyyyyK",
                "KKKKKKKKKKKKKKKK",
                "................");

            // Error: red disc with a white X.
            Add(s, "icon_error", 0, 0,
                ".....KKKKKK.....",
                "...KKEEEEEEKK...",
                "..KEERRRRRRRRK..",
                ".KERRRRRRRRRRRK.",
                ".KERWWRRRRWWRRK.",
                "KERRRWWRRWWRRRrK",
                "KERRRRWWWWRRRRrK",
                "KERRRRRWWRRRRRrK",
                "KERRRRRWWRRRRRrK",
                "KRRRRRWWWWRRRRrK",
                "KRRRRWWRRWWRRRrK",
                ".KRRWWRRRRWWRrK.",
                ".KRRRRRRRRRRRrK.",
                "..KRRRRRRRRrrK..",
                "...KKrrrrrrKK...",
                ".....KKKKKK.....");

            // Question: speech bubble with a teal question mark.
            Add(s, "icon_question", 0, 0,
                "....KKKKKKKK....",
                "..KKWWWWWWWWKK..",
                ".KWWWWTTTTWWWWK.",
                "KWWWWTTWWTTWWWLK",
                "KWWWWWWWWTTWWWLK",
                "KWWWWWWWTTWWWWLK",
                "KWWWWWWTTWWWWWLK",
                "KWWWWWWWWWWWWWLK",
                "KWWWWWWTTWWWWWLK",
                "KWWWWWWTTWWWWWLK",
                ".KWWWWWWWWWWWLK.",
                "..KKWLLLLLLLKK..",
                "...KWWKKKKKK....",
                "...KWK..........",
                "..KWK...........",
                "..KK............");

            // Padlock.
            Add(s, "icon_lock", 0, 0,
                "................",
                "......KKKKK.....",
                ".....KWMMMmK....",
                "....KWMKKKMmK...",
                "....KWK...KmK...",
                "....KWK...KmK...",
                "....KWK...KmK...",
                "..KKKKKKKKKKKKK.",
                "..KWWWWWWWWWWLK.",
                "..KWMMMKKKMMMmK.",
                "..KWMMMKKKMMMmK.",
                "..KWMMMMKMMMMmK.",
                "..KWMMMMKMMMMmK.",
                "..KLmmmmmmmmmmK.",
                "..KKKKKKKKKKKKK.",
                "................");

            // NEXUS OS start emblem: node-N on a teal disc (matches logo_nexus).
            Add(s, "icon_nexus", 0, 0,
                ".....KKKKKK.....",
                "...KKCCCCCCKK...",
                "..KCCTTTTTTTTK..",
                ".KCTTTTTTTTTTTK.",
                ".KCTWWTTTTWWTTK.",
                "KCTTWWWTTTWWTTtK",
                "KCTTWWWWTTWWTTtK",
                "KCTTWWTWWTWWTTtK",
                "KCTTWWTTWWWWTTtK",
                "KTTTWWTTTWWWTTtK",
                "KTTTWWTTTTWWTTtK",
                ".KTTWWTTTTWWTtK.",
                ".KTTTTTTTTTTTtK.",
                "..KTTTTTTTTttK..",
                "...KKttttttKK...",
                ".....KKKKKK.....");

            // Shut down: round beige power button with a red power symbol.
            Add(s, "icon_shutdown", 0, 0,
                ".....KKKKKK.....",
                "...KKWWWWWWKK...",
                "..KWWLLRRLLLLK..",
                ".KWLLLLRRLLLLGK.",
                ".KWLLLLRRLLLLGK.",
                "KWLLRLLRRLLRLLGK",
                "KWLLRRLRRLRRLLGK",
                "KWLRRRLRRLRRRLGK",
                "KWLRRLLRRLLRRLGK",
                "KWLRRLLLLLLRRLDK",
                "KLLRRRLLLLRRRLDK",
                ".KLLRRRLLRRRLDK.",
                ".KLLRRRRRRRRLDK.",
                "..KLLLRRRRLLDK..",
                "...KKDDDDDDKK...",
                ".....KKKKKK.....");

            // Task done: check box with a green tick.
            Add(s, "icon_task_done", 0, 0,
                "................",
                ".KKKKKKKKKKKKKK.",
                ".KLLLLLLLLLLLLK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWNNK.",
                ".KLWWWWWWWWNNWK.",
                ".KLWWWWWWWNNWWK.",
                ".KLNNWWWWNNWWWK.",
                ".KLWNNWWNNWWWWK.",
                ".KLWWNNNNWWWWWK.",
                ".KLWWWNNWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KKKKKKKKKKKKKK.",
                "................");

            // Task pending: empty check box.
            Add(s, "icon_task_pending", 0, 0,
                "................",
                ".KKKKKKKKKKKKKK.",
                ".KLLLLLLLLLLLLK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KKKKKKKKKKKKKK.",
                "................");

            // Task in progress: check box with a teal play marker.
            Add(s, "icon_task_active", 0, 0,
                "................",
                ".KKKKKKKKKKKKKK.",
                ".KLLLLLLLLLLLLK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWTTWWWWWWK.",
                ".KLWWWTTTWWWWWK.",
                ".KLWWWTTTTWWWWK.",
                ".KLWWWTTTTTWWWK.",
                ".KLWWWTTTTtWWWK.",
                ".KLWWWTTTtWWWWK.",
                ".KLWWWTTtWWWWWK.",
                ".KLWWWttWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KLWWWWWWWWWWWK.",
                ".KKKKKKKKKKKKKK.",
                "................");

            // Hard drive.
            Add(s, "icon_drive", 0, 0,
                "................",
                "................",
                "................",
                "................",
                "...KKKKKKKKKKKKK",
                "..KWWWWWWWWWWWKK",
                ".KLLLLLLLLLLLKDK",
                "KKKKKKKKKKKKKDDK",
                "KWGGGGGGGGGGKDDK",
                "KWGSSSSSGGNGKDDK",
                "KWGGGGGGGGGGKDK.",
                "KDDDDDDDDDDDKK..",
                "KKKKKKKKKKKKK...",
                "................",
                "................",
                "................");
        }

        // Cursors: ONLY 'K', 'W' and '.', so the game can recolour them.
        private static void AddCursors(Dictionary<string, PixelSprite> s)
        {
            // Standard pointer; hotspot at the tip.
            Add(s, "cursor_arrow", 0, 0,
                "K..........",
                "KK.........",
                "KWK........",
                "KWWK.......",
                "KWWWK......",
                "KWWWWK.....",
                "KWWWWWK....",
                "KWWWWWWK...",
                "KWWWWWWWK..",
                "KWWWWWWWWK.",
                "KWWWWWKKKKK",
                "KWWKWWK....",
                "KWK.KWWK...",
                "KK..KWWK...",
                "K....KWWK..",
                ".....KWWK..",
                "......KK...");

            // Pointing hand (links, clickable items); hotspot at the fingertip.
            Add(s, "cursor_hand", 5, 0,
                ".....KK........",
                "....KWWK.......",
                "....KWWK.......",
                "....KWWK.......",
                "....KWWK.......",
                "....KWWKKK.....",
                "....KWWKWWKK...",
                "....KWWKWWKWKK.",
                ".KK.KWWKWWKWKWK",
                "KWWKKWWWWWWWKWK",
                "KWWWKWWWWWWWWWK",
                ".KWWKWWWWWWWWWK",
                "..KWWWWWWWWWWWK",
                "..KWWWWWWWWWWK.",
                "...KWWWWWWWWWK.",
                "...KWWWWWWWWK..",
                "....KWWWWWWWK..",
                "....KKKKKKKKK..");

            // Text I-beam; hotspot at the centre.
            Add(s, "cursor_ibeam", 3, 8,
                "KKK.KKK",
                "KWWKWWK",
                ".KKWKK.",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                "..KWK..",
                ".KKWKK.",
                "KWWKWWK",
                "KKK.KKK");

            // Busy, frame 0: pointer + spinning quartered disk; hotspot at the tip.
            Add(s, "cursor_busy_0", 0, 0,
                "K...............",
                "KK..............",
                "KWK.............",
                "KWWK............",
                "KWWWK...........",
                "KWWWWK..........",
                "KWWWWWK.........",
                "KWWWWWWK........",
                "KWWWWWWWK.......",
                "KWWWWWWWWK......",
                "KWWWWWKKKKK.....",
                "KWWKWWK....KKK..",
                "KWK.KWWK..KWWKK.",
                "KK..KWWK.KWWWKKK",
                "K....KWWKKKKKKKK",
                ".....KWWKKKKWWWK",
                "......KK..KKWWK.",
                "...........KKK..");

            // Busy, frame 1: the disk turned a quarter.
            Add(s, "cursor_busy_1", 0, 0,
                "K...............",
                "KK..............",
                "KWK.............",
                "KWWK............",
                "KWWWK...........",
                "KWWWWK..........",
                "KWWWWWK.........",
                "KWWWWWWK........",
                "KWWWWWWWK.......",
                "KWWWWWWWWK......",
                "KWWWWWKKKKK.....",
                "KWWKWWK....KKK..",
                "KWK.KWWK..KKKWK.",
                "KK..KWWK.KKKKWWK",
                "K....KWWKKWWKWWK",
                ".....KWWKKWWKKKK",
                "......KK..KWKKK.",
                "...........KKK..");

            // Move: four-way arrows; hotspot at the centre.
            Add(s, "cursor_move", 7, 7,
                ".......K.......",
                "......KWK......",
                ".....KWWWK.....",
                "....KKKWKKK....",
                "...K..KWK..K...",
                "..KK..KWK..KK..",
                ".KWKKKKWKKKKWK.",
                "KWWWWWWWWWWWWWK",
                ".KWKKKKWKKKKWK.",
                "..KK..KWK..KK..",
                "...K..KWK..K...",
                "....KKKWKKK....",
                ".....KWWWK.....",
                "......KWK......",
                ".......K.......");

            // Dragging files: pointer carrying a page; hotspot at the tip.
            Add(s, "cursor_drag", 0, 0,
                "K..............",
                "KK.............",
                "KWK............",
                "KWWK...........",
                "KWWWK..........",
                "KWWWWK.........",
                "KWWWWWK........",
                "KWWWWWWK.......",
                "KWWWWWWWK......",
                "KWWWWWWWWK.....",
                "KWWWWWKKKKK....",
                "KWWKWWK.KKKKK..",
                "KWK.KWWKKWWWKK.",
                "KK..KWWKKWWWKWK",
                "K....KWWKWWWKKK",
                ".....KWWKWWWWWK",
                "......KKKWWWWWK",
                "........KWWWWWK",
                "........KWWWWWK",
                "........KKKKKKK");

            // Not allowed: ring with a slash; hotspot at the centre.
            Add(s, "cursor_no", 7, 7,
                ".....KKKKK.....",
                "...KKWWWWWKK...",
                "..KWWWWWWWWWK..",
                ".KWWWKKKKKWWWK.",
                ".KWWWWK...KWWK.",
                "KWWKWWWK...KWWK",
                "KWWKKWWWK..KWWK",
                "KWWK.KWWWK.KWWK",
                "KWWK..KWWWKKWWK",
                "KWWK...KWWWKWWK",
                ".KWWK...KWWWWK.",
                ".KWWWKKKKKWWWK.",
                "..KWWWWWWWWWK..",
                "...KKWWWWWKK...",
                ".....KKKKK.....");

            // Grabbing: closed hand; hotspot at the centre.
            Add(s, "cursor_grab", 7, 6,
                "....KK.KK.KK...",
                "...KWWKWWKWWKK.",
                "...KWWKWWKWWKWK",
                ".KKKWWKWWKWWKWK",
                "KWWKWWWWWWWWWWK",
                "KWWWWWWWWWWWWWK",
                ".KWWWWWWWWWWWWK",
                "..KWWWWWWWWWWWK",
                "..KWWWWWWWWWWK.",
                "...KWWWWWWWWWK.",
                "...KWWWWWWWWK..",
                "....KWWWWWWWK..",
                "....KKKKKKKKK..");
        }

        // UI glyphs for bevelled buttons: 'K' on transparent (grip uses 'W'/'D').
        private static void AddGlyphs(Dictionary<string, PixelSprite> s)
        {
            Add(s, "glyph_close", 0, 0,
                "K.....K",
                "KK...KK",
                ".KK.KK.",
                "..KKK..",
                ".KK.KK.",
                "KK...KK",
                "K.....K");

            Add(s, "glyph_minimize", 0, 0,
                ".......",
                ".......",
                ".......",
                ".......",
                ".......",
                "KKKKKK.",
                "KKKKKK.");

            Add(s, "glyph_maximize", 0, 0,
                "KKKKKKK",
                "KKKKKKK",
                "K.....K",
                "K.....K",
                "K.....K",
                "K.....K",
                "KKKKKKK");

            Add(s, "glyph_restore", 0, 0,
                "..KKKKK",
                "..KKKKK",
                "KKKKK.K",
                "KKKKK.K",
                "K...KKK",
                "K...K..",
                "KKKKK..");

            Add(s, "glyph_arrow_up", 0, 0,
                "...K...",
                "..KKK..",
                ".KKKKK.",
                "KKKKKKK");

            Add(s, "glyph_arrow_down", 0, 0,
                "KKKKKKK",
                ".KKKKK.",
                "..KKK..",
                "...K...");

            Add(s, "glyph_arrow_right", 0, 0,
                "K...",
                "KK..",
                "KKK.",
                "KKKK",
                "KKK.",
                "KK..",
                "K...");

            Add(s, "glyph_arrow_left", 0, 0,
                "...K",
                "..KK",
                ".KKK",
                "KKKK",
                ".KKK",
                "..KK",
                "...K");

            Add(s, "glyph_check", 0, 0,
                ".......",
                "......K",
                ".....KK",
                "K...KK.",
                "KK.KK..",
                ".KKK...",
                "..K....");

            Add(s, "glyph_radio_dot", 0, 0,
                ".KK.",
                "KKKK",
                "KKKK",
                ".KK.");

            Add(s, "glyph_resize_grip", 0, 0,
                "..........W",
                ".........WD",
                "........WD.",
                ".......WD..",
                "......WD..W",
                ".....WD..WD",
                "....WD..WD.",
                "...WD..WD..",
                "..WD..WD..W",
                ".WD..WD..WD",
                "WD..WD..WD.");

            Add(s, "glyph_dropdown", 0, 0,
                ".KKKKK.",
                "..KKK..",
                "...K...",
                "KKKKKKK");
        }

        // Logos.
        private static void AddLogos(Dictionary<string, PixelSprite> s)
        {
            // NEXUS OS boot emblem.
            Add(s, "logo_nexus", 0, 0,
                "...................KKKKKKKKKK...................",
                "................KKKLLLLLLLLLLKKK................",
                ".............KKKLLLLLLLLLLLLLLLMKKK.............",
                "............KKWLLLLLLLKKKKLLLLLMMMKK............",
                "..........KKWWWWLKKKKKttttKKKKKMMMMMKK..........",
                ".........KWWWWWKKKttttttttttttKKKMMMMMK.........",
                "........KWWWWKKtttTTTTTTTTTTTTtttKKMMMMK........",
                ".......KWWWWKtttTTTTTTTTTTTTTTTTtttKMMMMK.......",
                "......KWWWKKtttttttttTTTTTTttttttttTKKMMMK......",
                ".....KWWWKKtttWWWWWLttTTTTttWWWWWLttTKKMMMK.....",
                "....KWWWKKtTtWWWWWWWLtTTTTtWWWWWWWLtTTKKMMMK....",
                "....KWWWKtTTtWWWWWWWLtTTTTtWWWWWWWLtTTTKMMMK....",
                "...KWWWKttTTtWWWCCWWLtTTTTtWWWCCWWLtTTTTKMMMK...",
                "..KKWWKttTTTtWWWCCWWLtTTTTtWWWCCWWLtTTTTTKMMKK..",
                "..KWWWKtTTTTtWWWWWWWLttTTTtWWWWWWWLtTTTTTKMMMK..",
                "..KLWKttTTTTtLWWWWWWWLttTTtLWWWWWWLtTTTTTTKMMK..",
                ".KLLLKtTTTTTttWWWWWWWWLtTTttWWWWWLttTTTTTCKMMMK.",
                ".KLLKKtTTTTTTtWWWWWWWWLttTTtWWWWWLtTTTTTTCKKMmK.",
                ".KLLKtTTTTTTTtWWWWWWWWWLtTTtWWWWWLtTTTTTTTCKMmK.",
                "KLLLKtTTTTTTTtWWWWWWWWWLttTtWWWWWLtTTTTTTTCKMmmK",
                "KLLLKtTTTTTTTtWWWWWWWWWWLtttWWWWWLtTTTTTTTCKMmmK",
                "KLLLKtTTTTTTTtWWWWWWWWWWWLttWWWWWLtTTTTTTTCKMmmK",
                "KLLKttTTTTTTTtWWWWWWWWWWWLttWWWWWLtTTTTTTTCCKMmK",
                "KLLKttTTTTTTTtWWWWWWLWWWWWLtWWWWWLtTTTTTTTCCKMmK",
                "KLLKttTTTTTTTtWWWWWLtLWWWWWWWWWWWLtTTTTTTTCCKMmK",
                "KLLKttTTTTTTTtWWWWWLttWWWWWWWWWWWLtTTTTTTTCCKMmK",
                "KLLLKtTTTTTTTtWWWWWLttLWWWWWWWWWWLtTTTTTTTCKMmmK",
                "KLLLKtTTTTTTTtWWWWWLtttLWWWWWWWWWLtTTTTTTTCKMmmK",
                "KLLLKtTTTTTTTtWWWWWLtTttWWWWWWWWWLtTTTTTTTCKMmmK",
                ".KLLKtTTTTTTTtWWWWWLtTTtLWWWWWWWWLtTTTTTTTCKMmK.",
                ".KLLKKtTTTTTTtWWWWWLtTTttWWWWWWWWLtTTTTTTCKKMmK.",
                ".KMMMKtTTTTTttWWWWWLttTTtLWWWWWWWLttTTTTTCKMmmK.",
                "..KMMKttTTTTtWWWWWWWLtTTttLWWWWWWWLtTTTTCCKMmK..",
                "..KMMMKtTTTTtWWWWWWWLtTTTttWWWWWWWLtTTTTCKMmmK..",
                "..KKMMKtTTTTtWWWCCWWLtTTTTtWWWCCWWLtTTTCCKMmKK..",
                "...KMMMKTTTTtWWWCCWWLtTTTTtWWWCCWWLtTTCCKMmmK...",
                "....KMMMKTTTtWWWWWWWLtTTTTtWWWWWWWLtTTCKMMmK....",
                "....KMMMKKTTtLWWWWWWLtTTTTtLWWWWWWLtTCKKMmmK....",
                ".....KMMMKKTttLLLLLLttTTTTttLLLLLLttCKKMmmK.....",
                "......KMMMKKTttttttttTTTTTTttttttttCKKMmmK......",
                ".......KMMMMKTTTTTTTTTTTTTTTTTTTCCCKMMmmK.......",
                "........KMMMMKKTCCTTTTTTTTTTTTCCCKKMMmmK........",
                ".........KMMMMMKKKCCCCCCCCCCCCKKKMMmmmK.........",
                "..........KKMMMMMKKKKKCCCCKKKKKMMmmmKK..........",
                "............KKMMMMMMMMKKKKMMMMMmmmKK............",
                ".............KKKMmmmmmMMMMmmmmmmKKK.............",
                "................KKKmmmmmmmmmmKKK................",
                "...................KKKKKKKKKK...................");

            // Data-reclamation company emblem.
            Add(s, "logo_company", 0, 0,
                "..............KKKK..............",
                "..........KKKKLLLLKKKK..........",
                "........KKLLLLMMMMLLMMKK........",
                ".......KLLLmmmmmmmmmmMMMK.......",
                ".....KKLLmmMMMMMMMMMMmmMMKK.....",
                "....KKWLMLMMMmmmmmmMMMMmMMKK....",
                "....KWWLLLMmmMMMMMMmmMMMmMMK....",
                "...KLLLWWMMMMMMMMMMMMmKKKKMMK...",
                "..KLLMLWLLLMMMMMMMMMMMKDDKmMMK..",
                "..KLmLLMLWWLMMMMMMMMMMKDDKMmMK..",
                ".KLLmMMMLWWLKKKKKKKKMMKDDKMmMMK.",
                ".KLmMMmMMLKKKWWWWWWKKKKKDKKMmMK.",
                ".KLmMMmMKKKWWWRRRRWWWKKKDDKMmmK.",
                ".KLmMmMKKWWWWRWKKRRWWWWKDDKMDmK.",
                "KLMmMmKKWWWWRRRKKRRRWWWKDDKMDmmK",
                "KLMmMmKWWWWWRRRKKRRRWWWKDDKMDmmK",
                "KLMmMmKLLLWWrRRKKRRrWWLKDDKKDmmK",
                "KLMmMmKKLLWWrRRKKRRrWWLKDDDKDmmK",
                ".KLmMmMKKLLLLrRKKRrLLLLKKDDKDmK.",
                ".KLmMMmMKKKLLLrrrrLLLKKKKDDKDmK.",
                ".KMmMMmMMMKKKLLLLLLKKKMMKDDKDmK.",
                ".KMMmMMmMMMMKKKKKKKKLLLMKDDKKmK.",
                "..KMmMMmmMMMMMMMMMMMMLLMKDDDKK..",
                "..KMMmMMmmMMMMMMMMMMMMMMKDDDKK..",
                "...KMMmMMmmMMMMMMMMMMmMLKDMMMKK.",
                "....KMMmMMMmmMMMMMMmmMMKKMMMDDK.",
                "....KKMMmMMMMmmmmmmMMMmKMMKKDDK.",
                ".....KKMMmmMMMMMMMMMmDDKMMKKDDK.",
                ".......KMMMmmDDDDDDDDmmKMDDDDDK.",
                "........KKMMmmmmmmmmmmKKKDDDDKK.",
                "..........KKKKmmmmKKKK..KKKKKK..",
                "..............KKKK..............");
        }

        // Desktop pattern and small overlays.
        private static void AddMisc(Dictionary<string, PixelSprite> s)
        {
            // Seamless 8x8 desktop tile (only 'Q'/'q').
            Add(s, "pattern_desktop", 0, 0,
                "qQQQqQQQ",
                "QQQQQQQQ",
                "QQqQQQqQ",
                "QQQQQQQQ",
                "qQQQqQQQ",
                "QQQQQQQQ",
                "QQqQQQqQ",
                "QQQQQQQQ");

            // Camera-feed recording dot.
            Add(s, "rec_dot", 0, 0,
                ".RRR.",
                "RERRR",
                "RRRRR",
                "RRRRr",
                ".Rrr.");

            // Notification dot.
            Add(s, "badge_dot", 0, 0,
                ".RR.",
                "RERR",
                "RRRr",
                ".rr.");

            // Text caret.
            Add(s, "caret_block", 0, 0,
                "K",
                "K",
                "K",
                "K",
                "K",
                "K",
                "K",
                "K",
                "K",
                "K");

            // Tileable dotted selection edge (shift 1px per frame for marching ants).
            Add(s, "selection_marquee_h", 0, 0,
                "KK..");
        }
    }
}
