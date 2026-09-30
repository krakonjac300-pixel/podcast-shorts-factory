using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SecondCursor.Core.Content
{
    /// <summary>
    /// Phase L (suggestion 4): the readable reconstruction of a damaged or recovered file. The viewer keeps the file's glitchy look
    /// by default; this is the "Recovered text" view: the fragments the glitch left, in order, without the glitch characters, the
    /// repeats and the cut-off echoes. Lines that carry no glitch (a header, a table, a footer) are kept as they are.
    /// </summary>
    public static class RecoveredText
    {
        static readonly Regex Gaps = new Regex(@"\.{2,}");
        static readonly Regex Glitch = new Regex(@"[%#@&~^$]|:{2,}");
        static readonly Regex Spaces = new Regex(@"\s+");
        static readonly Regex DigitInWord = new Regex(@"(?<=[A-Za-z])0(?=[A-Za-z])");

        /// <summary>A glitchy line: its fragments are set apart by runs of dots (a bare "..." is an ellipsis, not damage).</summary>
        static bool IsDirty(string line) => Gaps.IsMatch(line) && line.Trim('.', ' ').Length > 0;

        /// <summary>True when the text has glitch fragments to reconstruct (an ordinary document is shown as it is).</summary>
        public static bool IsGlitched(string raw)
        {
            if (raw == null) return false;
            foreach (var line in raw.Split('\n')) if (IsDirty(line)) return true;
            return false;
        }

        public static string From(string raw)
        {
            if (!IsGlitched(raw)) return raw ?? "";
            var output = new List<string>();
            var dirty = new StringBuilder();
            foreach (var line in raw.Split('\n'))
            {
                if (IsDirty(line))
                {
                    dirty.Append(line).Append(' ');
                    continue;
                }
                Flush(dirty, output);
                output.Add(line);
            }
            Flush(dirty, output);
            return string.Join("\n", output);
        }

        static void Flush(StringBuilder dirty, List<string> output)
        {
            if (dirty.Length == 0) return;
            string paragraph = Fragments(dirty.ToString());
            if (paragraph.Length > 0) output.Add(paragraph);
            dirty.Length = 0;
        }

        /// <summary>The fragments of a glitchy passage joined as sentences.</summary>
        static string Fragments(string passage)
        {
            var kept = new List<string>();
            foreach (var part in Gaps.Split(passage))
            {
                string f = DigitInWord.Replace(Glitch.Replace(part, ""), "o");
                f = Spaces.Replace(f, " ").Trim(' ', '.');
                if (!Worth(f) || Echoes(kept, f)) continue;
                kept.Add(f);
            }
            return kept.Count == 0 ? "" : string.Join(". ", kept) + ".";
        }

        /// <summary>A fragment worth keeping: a word, a number of three digits or more, or a measure ("0.6s"); not a stray "e" or "07".</summary>
        static bool Worth(string f)
        {
            int letters = 0, digits = 0;
            foreach (char c in f)
            {
                if (char.IsLetter(c)) letters++;
                else if (char.IsDigit(c)) digits++;
            }
            return letters >= 2 || digits >= 3 || (letters >= 1 && digits >= 1);
        }

        /// <summary>The fragment repeats, or is the cut-off start or end of, the one kept before it.</summary>
        static bool Echoes(List<string> kept, string f)
        {
            if (kept.Count == 0) return false;
            string p = kept[kept.Count - 1];
            return p.StartsWith(f, System.StringComparison.OrdinalIgnoreCase) || p.EndsWith(f, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
