using System;
using System.Collections.Generic;
using System.Text;
using SecondCursor.Core.Story;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase Q2 (board C V6, "the demo keeps a copy"): the few things the free demo hands to the full game. The demo writes this
    /// small text file into its own save folder when Night 1 ends; the full game reads that one fixed file once, from the same
    /// company folder, and silently does nothing if it is missing, too big, from another version or unreadable. Plain lines, so
    /// it is engine-free and unit-tested:
    /// <code>
    /// SECOND CURSOR DEMO HANDOFF 1
    /// name=kosta
    /// line=who are you
    /// shredded=1
    /// </code>
    /// </summary>
    public sealed class DemoHandoff
    {
        public const string Header = "SECOND CURSOR DEMO HANDOFF 1";
        public const string FileName = "demo_handoff.txt";
        /// <summary>The demo's product folder (its own save folder, beside the full game's in the company folder).</summary>
        public const string DemoFolder = "SECOND CURSOR Demo";
        /// <summary>A larger file is not read at all.</summary>
        public const int MaxBytes = 4096;

        /// <summary>A name the player gave (valid by <see cref="NameCapture.IsValid"/>), or "".</summary>
        public string Name = "";
        /// <summary>Up to three Night 1 lines, sanitized (the echo filter applies when they are shown).</summary>
        public string[] Lines = Array.Empty<string>();
        /// <summary>The player won employee_017.dat into the bin once.</summary>
        public bool Shredded017;

        /// <summary>The file's text for what the demo's save holds now.</summary>
        public static string Compose(SaveData d, bool shredded017)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            if (d != null && NameCapture.IsValid(d.playerName)) sb.Append("name=").Append(d.playerName).Append('\n');
            int n = 0;
            foreach (var line in d?.playerLines ?? Array.Empty<string>())
            {
                string clean = SaveData.SanitizePlayerLine(line);
                if (clean.Length == 0) continue;
                sb.Append("line=").Append(clean).Append('\n');
                if (++n >= SaveData.MaxPlayerLines) break;
            }
            sb.Append("shredded=").Append(shredded017 ? "1" : "0").Append('\n');
            return sb.ToString();
        }

        /// <summary>The handoff in a file's text, or null if the text is not one (wrong header, too long, nothing in it).</summary>
        public static DemoHandoff Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaxBytes) return null;
            var rows = text.Replace("\r", "").Split('\n');
            if (rows.Length == 0 || rows[0].Trim() != Header) return null;
            var h = new DemoHandoff();
            var lines = new List<string>();
            for (int i = 1; i < rows.Length; i++)
            {
                string row = rows[i];
                int eq = row.IndexOf('=');
                if (eq <= 0) continue;
                string key = row.Substring(0, eq).Trim(), value = row.Substring(eq + 1);
                switch (key)
                {
                    case "name":
                        string name = value.Trim().ToLowerInvariant();
                        if (NameCapture.IsValid(name)) h.Name = name;
                        break;
                    case "line":
                        string clean = SaveData.SanitizePlayerLine(value);
                        if (clean.Length > 0 && lines.Count < SaveData.MaxPlayerLines) lines.Add(clean);
                        break;
                    case "shredded":
                        h.Shredded017 = value.Trim() == "1";
                        break;
                }
            }
            h.Lines = lines.ToArray();
            return h.Name.Length > 0 || h.Lines.Length > 0 || h.Shredded017 ? h : null;
        }

        /// <summary>
        /// Into the full game's save: the demo's lines become the run the game remembers (only where it remembers none of its own),
        /// the name only if none was given, and the save notes the import so it happens once. Returns false if nothing was taken.
        /// </summary>
        public bool ApplyTo(SaveData d)
        {
            if (d == null || d.demoImported) return false;
            d.demoImported = true;
            d.demoLines = (string[])Lines.Clone();
            if (string.IsNullOrEmpty(d.playerName) && Name.Length > 0) d.playerName = Name;
            if ((d.previousLines == null || d.previousLines.Length == 0) && Lines.Length > 0) d.previousLines = (string[])Lines.Clone();
            return true;
        }
    }
}
