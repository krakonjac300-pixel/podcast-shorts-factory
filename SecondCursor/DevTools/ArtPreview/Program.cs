// ArtPreview: validates SecondCursor.Core.Art.PixelArtData outside Unity and dumps
// every sprite to out/sprites.json so render_preview.py can draw contact sheets.
// Exit code 0 = all checks passed.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SecondCursor.Core.Art;

internal static class Program
{
    // Every sprite the game asks for. Keep in sync with the art brief.
    private static readonly string[] RequiredIcons =
    {
        "icon_workstation", "icon_folder", "icon_folder_open", "icon_folder_locked", "icon_file_txt",
        "icon_file_dat", "icon_file_log", "icon_file_tmp", "icon_file_exe", "icon_file_cfg",
        "icon_file_corrupt", "icon_mail", "icon_mail_unread", "icon_notepad", "icon_staff", "icon_camera",
        "icon_workorders", "icon_disposal_empty", "icon_disposal_full", "icon_system", "icon_help",
        "icon_info", "icon_warning", "icon_error", "icon_question", "icon_lock", "icon_nexus",
        "icon_shutdown", "icon_task_done", "icon_task_pending", "icon_task_active", "icon_drive",
    };

    private static readonly string[] RequiredCursors =
    {
        "cursor_arrow", "cursor_hand", "cursor_ibeam", "cursor_busy_0", "cursor_busy_1", "cursor_move",
        "cursor_drag", "cursor_no", "cursor_grab",
    };

    // name -> exact (width, height)
    private static readonly Dictionary<string, (int W, int H)> ExactSizes = new Dictionary<string, (int, int)>
    {
        ["glyph_close"] = (7, 7), ["glyph_minimize"] = (7, 7), ["glyph_maximize"] = (7, 7),
        ["glyph_restore"] = (7, 7), ["glyph_arrow_up"] = (7, 4), ["glyph_arrow_down"] = (7, 4),
        ["glyph_arrow_right"] = (4, 7), ["glyph_arrow_left"] = (4, 7), ["glyph_check"] = (7, 7),
        ["glyph_radio_dot"] = (4, 4), ["glyph_resize_grip"] = (11, 11), ["glyph_dropdown"] = (7, 4),
        ["logo_nexus"] = (48, 48), ["logo_company"] = (32, 32), ["pattern_desktop"] = (8, 8),
        ["rec_dot"] = (5, 5), ["badge_dot"] = (4, 4), ["caret_block"] = (1, 10),
        ["selection_marquee_h"] = (4, 1),
    };

    private static readonly string[] PointerTipCursors = { "cursor_arrow", "cursor_busy_0", "cursor_busy_1", "cursor_drag" };
    private static readonly string[] CentredCursors = { "cursor_ibeam", "cursor_move", "cursor_no", "cursor_grab" };

    private static readonly Dictionary<char, uint> RequiredPalette = new Dictionary<char, uint>
    {
        ['.'] = 0x00000000u, ['K'] = 0x141311FFu, ['W'] = 0xFFFFFFFFu, ['L'] = 0xDDDAD0FFu, ['G'] = 0xC2BFB2FFu,
        ['D'] = 0x86837AFFu, ['S'] = 0x4A4843FFu, ['P'] = 0xF6F4EAFFu, ['Y'] = 0xE8C45AFFu, ['y'] = 0xA8812AFFu,
        ['B'] = 0x3F6FA8FFu, ['b'] = 0x1F3E6BFFu, ['T'] = 0x3E7A6EFFu, ['t'] = 0x183F3AFFu, ['R'] = 0xC0392BFFu,
        ['r'] = 0x7A1F17FFu, ['O'] = 0xD9822BFFu, ['N'] = 0x4E9A4EFFu, ['n'] = 0x2A5C2AFFu, ['C'] = 0x7FD6D0FFu,
        ['M'] = 0x9AA3A8FFu, ['m'] = 0x5E666BFFu, ['Q'] = 0x2B3D3AFFu, ['q'] = 0x243330FFu,
    };

    private static readonly List<string> Errors = new List<string>();
    private static readonly List<string> Notes = new List<string>();

    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : "out";

        IReadOnlyDictionary<string, PixelSprite> sprites;
        try
        {
            sprites = PixelArtData.Sprites; // runs every PixelSprite constructor (validation)
        }
        catch (TypeInitializationException e)
        {
            Console.Error.WriteLine("FAIL: sprite table did not load: " + (e.InnerException?.Message ?? e.Message));
            return 2;
        }

        CheckPalette();
        CheckApi(sprites);

        foreach (PixelSprite s in sprites.Values)
            CheckPixels(s);

        foreach (string name in RequiredIcons.Concat(RequiredCursors).Concat(ExactSizes.Keys))
        {
            if (!PixelArtData.Has(name))
                Errors.Add($"missing required sprite '{name}'");
        }

        foreach (PixelSprite s in sprites.Values)
            CheckCategoryRules(s);

        Directory.CreateDirectory(outDir);
        string jsonPath = Path.Combine(outDir, "sprites.json");
        File.WriteAllText(jsonPath, BuildJson(sprites), new UTF8Encoding(false));

        PrintSummary(sprites);
        foreach (string n in Notes) Console.WriteLine("note: " + n);

        if (Errors.Count > 0)
        {
            foreach (string e in Errors) Console.Error.WriteLine("FAIL: " + e);
            Console.Error.WriteLine($"{Errors.Count} check(s) failed.");
            return 1;
        }

        int required = RequiredIcons.Length + RequiredCursors.Length + ExactSizes.Count;
        Console.WriteLine($"OK: {sprites.Count} sprites valid, all {required} required names present -> {jsonPath}");
        return 0;
    }

    private static void CheckPalette()
    {
        foreach (var kv in RequiredPalette)
        {
            if (!PixelPalette.TryGet(kv.Key, out uint got))
                Errors.Add($"palette is missing '{kv.Key}'");
            else if (kv.Key == '.' && got != 0u)
                Errors.Add("palette '.' must be 0x00000000");
            else if (got != kv.Value)
                Notes.Add($"palette '{kv.Key}' tuned from {kv.Value:X8} to {got:X8}");
        }

        foreach (var kv in PixelPalette.Colors)
        {
            if (kv.Key != '.' && (kv.Value & 0xFFu) != 0xFFu)
                Errors.Add($"palette '{kv.Key}' should be opaque");
        }

        if (PixelPalette.TryGet('#', out _))
            Errors.Add("'#' should not be a palette char");
    }

    private static void CheckApi(IReadOnlyDictionary<string, PixelSprite> sprites)
    {
        try
        {
            PixelArtData.Get("no_such_sprite");
            Errors.Add("Get(unknown) did not throw");
        }
        catch (KeyNotFoundException e)
        {
            if (e.Message != "PixelArtData: unknown sprite 'no_such_sprite'")
                Errors.Add("Get(unknown) message was: " + e.Message);
        }

        if (PixelArtData.Has("no_such_sprite") || PixelArtData.Has(null) || !PixelArtData.Has("cursor_arrow"))
            Errors.Add("Has() gave a wrong answer");

        if (!ReferenceEquals(PixelArtData.Get("icon_folder"), sprites["icon_folder"]))
            Errors.Add("Get() does not return the table instance");

        ExpectArgumentException("unequal rows", () => new PixelSprite("bad_rows", 0, 0, "KK", "K"), "bad_rows", "row 1");
        ExpectArgumentException("bad char", () => new PixelSprite("bad_char", 0, 0, "KK", "K#"), "bad_char", "row 1");
        ExpectArgumentException("no rows", () => new PixelSprite("no_rows", 0, 0), "no_rows", "row");
        ExpectArgumentException("null row", () => new PixelSprite("null_row", 0, 0, "K", null), "null_row", "row 1");
        ExpectArgumentException("hotspot", () => new PixelSprite("bad_hotspot", 3, 0, "KK"), "bad_hotspot", "hotspot");

        var ok = new PixelSprite("ok", 1, 0, "K.", ".W");
        if (ok.Width != 2 || ok.Height != 2 || ok[1, 1] != 'W' || ok[0, 1] != '.' || ok[0, 0] != 'K')
            Errors.Add("indexer / size mismatch on a hand-made sprite");
        try
        {
            _ = ok[2, 0];
            Errors.Add("indexer did not reject x out of range");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
    }

    private static void ExpectArgumentException(string what, Func<PixelSprite> make, string mustName, string mustMention)
    {
        try
        {
            make();
            Errors.Add($"PixelSprite accepted {what}");
        }
        catch (ArgumentException e)
        {
            if (!e.Message.Contains("'" + mustName + "'") || !e.Message.Contains(mustMention))
                Errors.Add($"PixelSprite {what} message should name the sprite and '{mustMention}': {e.Message}");
        }
    }

    private static void CheckPixels(PixelSprite s)
    {
        if (s.Rows.Length != s.Height)
            Errors.Add($"{s.Name}: Rows.Length != Height");
        bool anyOpaque = false;
        for (int y = 0; y < s.Height; y++)
        {
            if (s.Rows[y].Length != s.Width)
                Errors.Add($"{s.Name}: row {y} length mismatch");
            for (int x = 0; x < s.Width; x++)
            {
                char c = s[x, y];
                if (!PixelPalette.TryGet(c, out uint rgba))
                    Errors.Add($"{s.Name}: ({x},{y}) '{c}' not in palette");
                else if ((rgba & 0xFFu) != 0)
                    anyOpaque = true;
            }
        }

        if (!anyOpaque)
            Errors.Add($"{s.Name}: sprite is fully transparent");
    }

    private static HashSet<char> Chars(PixelSprite s)
    {
        var set = new HashSet<char>();
        foreach (string r in s.Rows)
            foreach (char c in r)
                set.Add(c);
        return set;
    }

    private static void OnlyChars(PixelSprite s, string allowed)
    {
        foreach (char c in Chars(s))
        {
            if (allowed.IndexOf(c) < 0)
                Errors.Add($"{s.Name}: uses '{c}' but only \"{allowed}\" is allowed");
        }
    }

    private static void CheckCategoryRules(PixelSprite s)
    {
        string n = s.Name;
        if (n.StartsWith("icon_", StringComparison.Ordinal))
        {
            if (s.Width != 16 || s.Height != 16)
                Errors.Add($"{n}: icons must be 16x16, got {s.Width}x{s.Height}");
            // icon_file_tmp is deliberately faded (dashed 'D' outline); every other icon has a 'K' outline.
            if (n != "icon_file_tmp" && !Chars(s).Contains('K'))
                Errors.Add($"{n}: icon has no 'K' outline");
            if (s.HotspotX != 0 || s.HotspotY != 0)
                Errors.Add($"{n}: icons keep hotspot 0,0");
        }
        else if (n.StartsWith("cursor_", StringComparison.Ordinal))
        {
            OnlyChars(s, "KW.");
            if (s.Width < 7 || s.Width > 17 || s.Height < 13 || s.Height > 22)
                Errors.Add($"{n}: cursor size {s.Width}x{s.Height} outside 7..17 x 13..22");
            if (s[s.HotspotX, s.HotspotY] == '.')
                Notes.Add($"{n}: hotspot ({s.HotspotX},{s.HotspotY}) is on a transparent pixel");

            if (PointerTipCursors.Contains(n) && (s.HotspotX != 0 || s.HotspotY != 0 || s[0, 0] != 'K'))
                Errors.Add($"{n}: pointer hotspot must be the tip at (0,0)");
            if (CentredCursors.Contains(n) &&
                (Math.Abs(s.HotspotX - (s.Width - 1) / 2.0) > 1.0 || Math.Abs(s.HotspotY - (s.Height - 1) / 2.0) > 1.0))
                Errors.Add($"{n}: hotspot ({s.HotspotX},{s.HotspotY}) is not at the centre of {s.Width}x{s.Height}");
            if (n == "cursor_hand" && (s.HotspotY != 0 || s[s.HotspotX, 0] != 'K'))
                Errors.Add($"{n}: hotspot must sit on the fingertip in the top row");
        }
        else if (n.StartsWith("glyph_", StringComparison.Ordinal))
        {
            OnlyChars(s, n == "glyph_resize_grip" ? "WD." : "K.");
        }
        else if (n == "pattern_desktop")
        {
            OnlyChars(s, "Qq");
        }
        else if (n == "caret_block" || n == "selection_marquee_h")
        {
            OnlyChars(s, "K.");
        }

        if (ExactSizes.TryGetValue(n, out var size) && (s.Width != size.W || s.Height != size.H))
            Errors.Add($"{n}: must be {size.W}x{size.H}, got {s.Width}x{s.Height}");
    }

    private static string BuildJson(IReadOnlyDictionary<string, PixelSprite> sprites)
    {
        var sb = new StringBuilder();
        sb.Append("{\n  \"palette\": {");
        bool first = true;
        foreach (var kv in PixelPalette.Colors.OrderBy(kv => kv.Key))
        {
            sb.Append(first ? "\n" : ",\n");
            first = false;
            sb.Append("    ").Append(Quote(kv.Key.ToString())).Append(": ")
              .Append(Quote(kv.Value.ToString("X8", CultureInfo.InvariantCulture)));
        }

        sb.Append("\n  },\n  \"sprites\": [");
        first = true;
        foreach (PixelSprite s in sprites.Values)
        {
            sb.Append(first ? "\n" : ",\n");
            first = false;
            sb.Append("    {\"name\": ").Append(Quote(s.Name))
              .Append(", \"w\": ").Append(s.Width).Append(", \"h\": ").Append(s.Height)
              .Append(", \"hx\": ").Append(s.HotspotX).Append(", \"hy\": ").Append(s.HotspotY)
              .Append(", \"rows\": [");
            for (int y = 0; y < s.Height; y++)
            {
                if (y > 0) sb.Append(", ");
                sb.Append(Quote(s.Rows[y]));
            }

            sb.Append("]}");
        }

        sb.Append("\n  ]\n}\n");
        return sb.ToString();
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append(c);
        }

        return sb.Append('"').ToString();
    }

    private static void PrintSummary(IReadOnlyDictionary<string, PixelSprite> sprites)
    {
        Console.WriteLine($"{"sprite",-22} {"size",7}  {"hotspot",7}  colours");
        foreach (PixelSprite s in sprites.Values)
        {
            string cols = new string(Chars(s).Where(c => c != '.').OrderBy(c => c).ToArray());
            Console.WriteLine($"{s.Name,-22} {s.Width + "x" + s.Height,7}  {s.HotspotX + "," + s.HotspotY,7}  {cols}");
        }
    }
}
