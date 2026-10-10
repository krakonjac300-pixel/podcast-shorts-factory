// FontPreview: validates SecondCursor.Core.Art.PixelFontData and exports it for render_preview.py.
//
// Outputs (in ./out):
//   font.json        metrics, every glyph, the fallback glyph, and MeasureWidth() of each sample line
//   samples_cs.pgm   reference 1x render of samples.txt made with the C# API (black ink on white);
//                    render_preview.py re-renders the same page and asserts it is pixel-identical.
// Exit code 0 = no errors (warnings are printed but do not fail).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SecondCursor.Core.Art;

namespace SecondCursor.DevTools.FontPreview
{
    internal static class Program
    {
        private const int PageMargin = 2;

        // Glyphs allowed to contain ink pixels with no 8-connected neighbour (dots, marks).
        private static readonly Dictionary<char, int> AllowedIsolatedPixels = new Dictionary<char, int>
        {
            ['!'] = 1, ['.'] = 1, [':'] = 2, [';'] = 1, ['?'] = 1, ['i'] = 1, ['j'] = 1,
        };

        private static int Main(string[] args)
        {
            string toolDir = FindToolDir();
            string outDir = Path.Combine(toolDir, "out");
            Directory.CreateDirectory(outDir);

            var errors = new List<string>();
            var warnings = new List<string>();

            ValidateMetrics(errors);
            ValidateGlyphs(errors, warnings);
            ValidateFallback(errors);
            ValidateMeasure(errors);
            ValidateDuplicates(errors);

            string[] samples = File.ReadAllLines(Path.Combine(toolDir, "samples.txt"))
                .Where(l => l.Length > 0)
                .ToArray();

            WriteJson(Path.Combine(outDir, "font.json"), samples);
            WritePgm(Path.Combine(outDir, "samples_cs.pgm"), samples);

            PrintSummary();

            foreach (string w in warnings)
            {
                Console.WriteLine("WARN  " + w);
            }

            foreach (string e in errors)
            {
                Console.WriteLine("ERROR " + e);
            }

            Console.WriteLine(errors.Count == 0
                ? $"OK: {PixelFontData.Glyphs.Count} glyphs valid, {warnings.Count} warning(s)."
                : $"FAILED: {errors.Count} error(s), {warnings.Count} warning(s).");
            return errors.Count == 0 ? 0 : 1;
        }

        private static string FindToolDir()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "FontPreview.csproj")))
            {
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }

            return dir ?? Directory.GetCurrentDirectory();
        }

        // ------------------------------------------------------------------ validation

        private static void ValidateMetrics(List<string> errors)
        {
            Check(errors, PixelFontData.GlyphHeight == 10, "GlyphHeight must be 10");
            Check(errors, PixelFontData.Ascent == 8, "Ascent must be 8");
            Check(errors, PixelFontData.XHeight == 6, "XHeight must be 6");
            Check(errors, PixelFontData.LineHeight >= PixelFontData.GlyphHeight, "LineHeight must be >= GlyphHeight");
            Check(errors, PixelFontData.SpaceAdvance == 3 || PixelFontData.SpaceAdvance == 4, "SpaceAdvance must be 3 or 4");
            Check(errors, PixelFontData.LetterSpacing == 1, "LetterSpacing must be 1");
        }

        private static void ValidateGlyphs(List<string> errors, List<string> warnings)
        {
            Check(errors, PixelFontData.Glyphs != null, "Glyphs is null");
            for (int code = 32; code <= 126; code++)
            {
                char c = (char)code;
                if (!PixelFontData.Glyphs.TryGetValue(c, out string[] rows))
                {
                    errors.Add($"missing glyph {Describe(c)}");
                    continue;
                }

                if (!CheckShape(errors, Describe(c), rows))
                {
                    continue;
                }

                int width = rows[0].Length;
                Check(errors, ReferenceEquals(PixelFontData.GetGlyph(c), rows), $"GetGlyph({Describe(c)}) does not return Glyphs entry");
                Check(errors, PixelFontData.GetWidth(c) == width, $"GetWidth({Describe(c)}) = {PixelFontData.GetWidth(c)} but glyph is {width} wide");

                if (c == ' ')
                {
                    Check(errors, width == PixelFontData.SpaceAdvance - PixelFontData.LetterSpacing, "space width must be SpaceAdvance - LetterSpacing");
                    Check(errors, rows.All(r => r.IndexOf('#') < 0), "space must be blank");
                    continue;
                }

                var ink = InkRows(rows);
                if (ink.Count == 0)
                {
                    errors.Add($"{Describe(c)} has no ink");
                    continue;
                }

                // No blank padding columns: the first and last column must carry ink
                // (tabular digits may keep an intentional blank column to stay 5 wide).
                bool tabular = c >= '0' && c <= '9';
                Check(errors, tabular || rows.Any(r => r[0] == '#'), $"{Describe(c)} has a blank left column");
                Check(errors, tabular || rows.Any(r => r[width - 1] == '#'), $"{Describe(c)} has a blank right column");
                Check(errors, width <= 7, $"{Describe(c)} is {width} wide (max 7)");

                int top = ink.Min();
                int bottom = ink.Max();

                if (c >= 'A' && c <= 'Z')
                {
                    Check(errors, top == 0 && bottom == 7, $"capital {Describe(c)} spans rows {top}-{bottom}, expected 0-7");
                }
                else if (c >= '0' && c <= '9')
                {
                    Check(errors, width == 5, $"digit {Describe(c)} is {width} wide, expected 5 (tabular)");
                    Check(errors, top == 0 && bottom == 7, $"digit {Describe(c)} spans rows {top}-{bottom}, expected 0-7");
                }
                else if ("acemnorsuvwxz".IndexOf(c) >= 0)
                {
                    Check(errors, top == 2 && bottom == 7, $"x-height letter {Describe(c)} spans rows {top}-{bottom}, expected 2-7");
                }
                else if ("bdfhkl".IndexOf(c) >= 0)
                {
                    Check(errors, top == 0 && bottom == 7, $"ascender {Describe(c)} spans rows {top}-{bottom}, expected 0-7");
                }
                else if (c == 't')
                {
                    Check(errors, (top == 0 || top == 1) && bottom == 7, $"'t' spans rows {top}-{bottom}, expected 0/1-7");
                }
                else if (c == 'i')
                {
                    Check(errors, top == 0 && bottom == 7, $"'i' spans rows {top}-{bottom}, expected 0-7");
                }
                else if (c == 'j')
                {
                    Check(errors, top == 0 && bottom >= 8, $"'j' spans rows {top}-{bottom}, expected 0-8/9");
                }
                else if ("gpqy".IndexOf(c) >= 0)
                {
                    Check(errors, top == 2 && bottom >= 8, $"descender {Describe(c)} spans rows {top}-{bottom}, expected 2-8/9");
                }
                else if (c == '.')
                {
                    Check(errors, top == 7 && bottom == 7, "'.' must sit on row 7");
                }
                else if (c == ':')
                {
                    Check(errors, ink.SetEquals(new[] { 3, 7 }), "':' dots must be on rows 3 and 7");
                }
                else if (c == '_')
                {
                    Check(errors, ink.SetEquals(new[] { 9 }), "'_' must be on row 9 only");
                }
                else if (c == '-')
                {
                    Check(errors, ink.Count == 1 && (top == 4 || top == 5), "'-' must be a single row on row 4 or 5");
                }

                int isolated = CountIsolated(rows);
                AllowedIsolatedPixels.TryGetValue(c, out int allowed);
                if (isolated > allowed)
                {
                    warnings.Add($"{Describe(c)} has {isolated} isolated pixel(s) (allowed {allowed})");
                }
            }

            foreach (char c in PixelFontData.Glyphs.Keys)
            {
                if (c < 32 || c > 126)
                {
                    warnings.Add($"extra glyph outside printable ASCII: {Describe(c)}");
                }
            }
        }

        private static void ValidateFallback(List<string> errors)
        {
            string[] fb = PixelFontData.GetGlyph('\u0001');
            if (!CheckShape(errors, "fallback", fb))
            {
                return;
            }

            Check(errors, InkRows(fb).Count > 0, "fallback glyph has no ink");
            Check(errors, ReferenceEquals(fb, PixelFontData.GetGlyph('\u263A')), "fallback must be shared by all unsupported chars");
            Check(errors, PixelFontData.GetWidth('\u0001') == fb[0].Length, "GetWidth(unsupported) must equal fallback width");
            foreach (var kv in PixelFontData.Glyphs)
            {
                Check(errors, !kv.Value.SequenceEqual(fb), $"fallback glyph is identical to {Describe(kv.Key)}");
            }
        }

        private static void ValidateMeasure(List<string> errors)
        {
            int ls = PixelFontData.LetterSpacing;
            int w(char c) => PixelFontData.GetWidth(c);

            Check(errors, PixelFontData.MeasureWidth(null) == 0, "MeasureWidth(null) != 0");
            Check(errors, PixelFontData.MeasureWidth("") == 0, "MeasureWidth(\"\") != 0");
            Check(errors, PixelFontData.MeasureWidth("A") == w('A'), "MeasureWidth(\"A\") != GetWidth('A')");
            Check(errors, PixelFontData.MeasureWidth("Ab") == w('A') + ls + w('b'), "MeasureWidth(\"Ab\") wrong");
            Check(errors, PixelFontData.MeasureWidth("A b") == w('A') + ls + PixelFontData.SpaceAdvance + w('b'), "space does not advance by SpaceAdvance");
            Check(errors, PixelFontData.MeasureWidth("A\nb") == PixelFontData.MeasureWidth("Ab"), "MeasureWidth must ignore '\\n'");
            Check(errors, PixelFontData.MeasureWidth("\n") == 0, "MeasureWidth(\"\\n\") != 0");
            Check(errors, PixelFontData.MeasureWidth(" ") == PixelFontData.SpaceAdvance - ls, "MeasureWidth(\" \") wrong");
            Check(errors, PixelFontData.MeasureWidth("\u0001") == PixelFontData.GetGlyph('\u0001')[0].Length, "MeasureWidth(unsupported) wrong");

            // Tabular digits: every digit string of equal length measures the same.
            int reference = PixelFontData.MeasureWidth("0000");
            for (char d = '0'; d <= '9'; d++)
            {
                Check(errors, PixelFontData.MeasureWidth(new string(d, 4)) == reference, $"digits are not tabular ('{d}')");
            }
        }

        private static void ValidateDuplicates(List<string> errors)
        {
            var seen = new Dictionary<string, char>();
            foreach (var kv in PixelFontData.Glyphs)
            {
                if (kv.Key == ' ')
                {
                    continue;
                }

                string key = string.Join("|", kv.Value);
                if (seen.TryGetValue(key, out char other))
                {
                    errors.Add($"{Describe(kv.Key)} and {Describe(other)} have identical bitmaps");
                }
                else
                {
                    seen[key] = kv.Key;
                }
            }
        }

        private static bool CheckShape(List<string> errors, string name, string[] rows)
        {
            if (rows == null)
            {
                errors.Add($"{name}: null glyph");
                return false;
            }

            if (rows.Length != PixelFontData.GlyphHeight)
            {
                errors.Add($"{name}: {rows.Length} rows, expected {PixelFontData.GlyphHeight}");
                return false;
            }

            if (rows.Any(r => r == null || r.Length == 0))
            {
                errors.Add($"{name}: null or empty row");
                return false;
            }

            int width = rows[0].Length;
            bool ok = true;
            for (int r = 0; r < rows.Length; r++)
            {
                if (rows[r].Length != width)
                {
                    errors.Add($"{name}: row {r} is {rows[r].Length} wide, row 0 is {width}");
                    ok = false;
                }

                if (rows[r].Any(ch => ch != '#' && ch != '.'))
                {
                    errors.Add($"{name}: row {r} contains characters other than '#' and '.'");
                    ok = false;
                }
            }

            return ok;
        }

        private static HashSet<int> InkRows(string[] rows)
        {
            var set = new HashSet<int>();
            for (int r = 0; r < rows.Length; r++)
            {
                if (rows[r].IndexOf('#') >= 0)
                {
                    set.Add(r);
                }
            }

            return set;
        }

        private static int CountIsolated(string[] rows)
        {
            int count = 0;
            int h = rows.Length;
            int w = rows[0].Length;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (rows[y][x] != '#')
                    {
                        continue;
                    }

                    bool neighbour = false;
                    for (int dy = -1; dy <= 1 && !neighbour; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < w && ny < h && rows[ny][nx] == '#')
                            {
                                neighbour = true;
                                break;
                            }
                        }
                    }

                    if (!neighbour)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void Check(List<string> errors, bool condition, string message)
        {
            if (!condition)
            {
                errors.Add(message);
            }
        }

        private static string Describe(char c)
        {
            return c >= 32 && c <= 126 ? $"'{c}' (U+{(int)c:X4})" : $"U+{(int)c:X4}";
        }

        // ------------------------------------------------------------------ export

        private static void WriteJson(string path, string[] samples)
        {
            var glyphs = new SortedDictionary<int, string[]>();
            foreach (var kv in PixelFontData.Glyphs)
            {
                glyphs[kv.Key] = kv.Value;
            }

            var doc = new Dictionary<string, object>
            {
                ["metrics"] = new Dictionary<string, int>
                {
                    ["GlyphHeight"] = PixelFontData.GlyphHeight,
                    ["Ascent"] = PixelFontData.Ascent,
                    ["XHeight"] = PixelFontData.XHeight,
                    ["LineHeight"] = PixelFontData.LineHeight,
                    ["SpaceAdvance"] = PixelFontData.SpaceAdvance,
                    ["LetterSpacing"] = PixelFontData.LetterSpacing,
                    ["PageMargin"] = PageMargin,
                },
                ["glyphs"] = glyphs.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                ["fallback"] = PixelFontData.GetGlyph('\u0001'),
                ["measure"] = samples.Select(s => new Dictionary<string, object> { ["text"] = s, ["width"] = PixelFontData.MeasureWidth(s) }).ToArray(),
            };

            File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        // Reference renderer: exactly how a runtime renderer is expected to lay out one line per sample.
        private static void WritePgm(string path, string[] samples)
        {
            int width = samples.Max(PixelFontData.MeasureWidth) + (2 * PageMargin);
            int height = (samples.Length * PixelFontData.LineHeight) + (2 * PageMargin);
            var pixels = new byte[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = 255;
            }

            for (int line = 0; line < samples.Length; line++)
            {
                int penX = PageMargin;
                int top = PageMargin + (line * PixelFontData.LineHeight);
                bool first = true;
                foreach (char c in samples[line])
                {
                    if (c == '\n' || c == '\r')
                    {
                        continue;
                    }

                    if (!first)
                    {
                        penX += PixelFontData.LetterSpacing;
                    }

                    first = false;
                    string[] glyph = PixelFontData.GetGlyph(c);
                    for (int y = 0; y < glyph.Length; y++)
                    {
                        for (int x = 0; x < glyph[y].Length; x++)
                        {
                            if (glyph[y][x] == '#')
                            {
                                pixels[((top + y) * width) + penX + x] = 0;
                            }
                        }
                    }

                    penX += PixelFontData.GetWidth(c);
                }
            }

            using var stream = File.Create(path);
            byte[] header = Encoding.ASCII.GetBytes($"P5\n{width} {height}\n255\n");
            stream.Write(header, 0, header.Length);
            stream.Write(pixels, 0, pixels.Length);
        }

        private static void PrintSummary()
        {
            var byWidth = PixelFontData.Glyphs
                .Where(kv => kv.Key != ' ')
                .GroupBy(kv => kv.Value[0].Length)
                .OrderBy(g => g.Key);
            foreach (var group in byWidth)
            {
                Console.WriteLine($"width {group.Key}: {new string(group.Select(kv => kv.Key).OrderBy(c => c).ToArray())}");
            }
        }
    }
}
