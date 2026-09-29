using System;
using System.Collections.Generic;
using System.Text;
using SecondCursor.Core.Game;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// Template tokens (expansion spec 2.4): files tagged "template" carry {tokens} that are filled at night
    /// start from what the player did before (their Night 1 replies, whether Gary was finished or kept...).
    /// Pure functions so the rules are unit-tested.
    /// </summary>
    public static class NightTemplates
    {
        public const string NoReply = "(no reply)";

        /// <summary>Every token content may use (plus {0}-style format slots in strings).</summary>
        public static readonly string[] Known =
        {
            "line1", "line2", "line3", "log1", "log2", "log3", "p3", "n2_209", "209row", "pct", "214note", "heldrow", "209", "n2door",
        };

        /// <summary>The token values for a night, from the memory flags and the saved Night 1 replies.</summary>
        public static Dictionary<string, string> Tokens(NarrativeFlags flags, IList<string> playerLines) => Tokens(flags, playerLines, null);

        /// <summary>
        /// Like the short form; <paramref name="lineMinutes"/> (the Night 1 clock when each line was typed) makes the
        /// {logN} tokens read like a session log: "02:04 CROURKE&gt; make me" (M3).
        /// </summary>
        public static Dictionary<string, string> Tokens(NarrativeFlags flags, IList<string> playerLines, IList<int> lineMinutes)
        {
            flags = flags ?? new NarrativeFlags();
            var t = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < 3; i++)
            {
                string line = playerLines != null && i < playerLines.Count ? SaveData.SanitizePlayerLine(playerLines[i]) : "";
                t["line" + (i + 1)] = line.Length > 0 ? line : NoReply;
                int minutes = lineMinutes != null && i < lineMinutes.Count ? lineMinutes[i] : -1;
                t["log" + (i + 1)] = LogLine(minutes, line.Length > 0 ? line : NoReply);
            }
#if !SC_DEMO
            // Night 3's tokens (the demo build has no Night 3 content, so their text stays out of it).
            bool finished = flags.Has(MemoryFlags.N2FinishedGary);
            bool hid214 = flags.Has(MemoryFlags.N2Hid214);
            t["p3"] = finished ? "OK (LETHEWORTH)" : "HELD";
            t["n2_209"] = finished
                ? "11/19/98 02:58  RECLAIM 209 ... OK (COMPLETE)\n11/19/98 03:00  209 MOVED TO SUBLEVEL C (SHELF 16)"
                : "11/19/98 03:00  RECLAIM 209 ... FAILED (IN USE)";
            t["209row"] = finished ? "209   PRUITT G.      C/16  RETAINED (COMPLETE)" : "209   PRUITT G.      B-7   HELD (INCOMPLETE)";
            t["pct"] = hid214 ? "88%, SOURCE NOT FOUND" : "96%";
            t["214note"] = hid214 ? "NOTE: 214 source missing from Intake.\nCheck Archive." : "";
            t["heldrow"] = finished ? "HELD:    none (209 COMPLETE, C/16)" : "HELD:    209 PRUITT G.  INCOMPLETE";
            t["209"] = finished ? "209 PRUITT G." : "209 PRUITT G. (RESERVED)";
            t["n2door"] = flags.Has(MemoryFlags.N2WatchedToDoor) ? "n/a     03:04   000" : "n/a     n/a     n/a";
#endif
            return t;
        }

        /// <summary>The login name the session log shows before each typed line.</summary>
        public const string LogUser = "CROURKE";

        /// <summary>"02:04 CROURKE&gt; make me" (24-hour clock); without a known time just "CROURKE&gt; make me".</summary>
        public static string LogLine(int minutes, string line)
        {
            string prefix = LogUser + "> ";
            if (minutes < 0) return prefix + line;
            int h = (minutes / 60) % 24, m = minutes % 60;
            return h.ToString("00") + ":" + m.ToString("00") + " " + prefix + line;
        }

        /// <summary>Replace every {token} that has a value; unknown braces are left as they are.</summary>
        public static string Fill(string text, IDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length + 32);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i && tokens.TryGetValue(text.Substring(i + 1, close - i - 1), out var value))
                    {
                        sb.Append(value);
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        /// <summary>The {token} names used in a text (for content validation).</summary>
        public static List<string> TokensIn(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int i = 0;
            while ((i = text.IndexOf('{', i)) >= 0)
            {
                int close = text.IndexOf('}', i + 1);
                if (close < 0) break;
                list.Add(text.Substring(i + 1, close - i - 1));
                i = close + 1;
            }
            return list;
        }
    }
}
