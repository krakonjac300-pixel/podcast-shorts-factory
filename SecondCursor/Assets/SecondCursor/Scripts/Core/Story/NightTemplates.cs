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
            "line1", "line2", "line3", "log1", "log2", "log3", "p3", "n2_209", "209row", "pct", "214note", "heldrow", "209", "n2door", "163fx", "n2patch",
            // Phase Q2: her voice (LINEn, PREV1), the line as typed or nothing (said1), the name (V3), the measured profile rows (V5),
            // the post-game echo (D9), the last Night 3 input (V1, filled by the director), D3's Ruth payoffs.
            "LINE1", "LINE2", "LINE3", "said1", "PREV1", "name", "NAME", "n0", "namerows", "dragrow", "mailrow", "yesrow", "camrow", "segments",
            "p2own", "p2owner", "lastn3", "118n2", "ruthask",
        };

        /// <summary>{lastn3} before the director knows the night's last line.</summary>
        public const string NoLastInput = "NONE";

        /// <summary>The token values for a night, from the memory flags and the saved Night 1 replies.</summary>
        public static Dictionary<string, string> Tokens(NarrativeFlags flags, IList<string> playerLines) => Tokens(flags, playerLines, null);

        /// <summary>
        /// Like the short form; <paramref name="lineMinutes"/> (the Night 1 clock when each line was typed) makes the
        /// {logN} tokens read like a session log: "02:04 CROURKE&gt; make me" (M3).
        /// </summary>
        public static Dictionary<string, string> Tokens(NarrativeFlags flags, IList<string> playerLines, IList<int> lineMinutes)
        {
            var save = new SaveData();
            if (playerLines != null) save.playerLines = new List<string>(playerLines).ToArray();
            if (lineMinutes != null) save.playerLineMinutes = new List<int>(lineMinutes).ToArray();
            return ForSave(flags, save);
        }

        /// <summary>
        /// Every token from the memory flags and the save: the Night 1 replies (every echo passes <see cref="EchoFilter"/>), the name
        /// the player gave, Night 1's measurements, the last run's first line and the endings seen.
        /// </summary>
        public static Dictionary<string, string> ForSave(NarrativeFlags flags, SaveData save)
        {
            flags = flags ?? new NarrativeFlags();
            save = save ?? new SaveData();
            var playerLines = save.playerLines ?? Array.Empty<string>();
            var lineMinutes = save.playerLineMinutes ?? Array.Empty<int>();
            var t = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < 3; i++)
            {
                string raw = i < playerLines.Length ? playerLines[i] : "";
                string line = EchoFilter.ForFile(raw);
                t["line" + (i + 1)] = line.Length > 0 ? line : NoReply;
                t["LINE" + (i + 1)] = EchoFilter.ForVoice(raw);
                int minutes = i < lineMinutes.Length ? lineMinutes[i] : -1;
                t["log" + (i + 1)] = LogLine(minutes, line.Length > 0 ? line : NoReply);
            }
            t["said1"] = EchoFilter.ForFile(playerLines.Length > 0 ? playerLines[0] : "");
            var previous = save.previousLines ?? Array.Empty<string>();
            t["PREV1"] = EchoFilter.ForVoice(previous.Length > 0 ? previous[0] : "");
            AddName(t, save.playerName);
            var n1 = save.capture != null && save.capture.Length > 0 ? save.capture[0] : null;
            t["dragrow"] = CaptureProfile.DragRow(n1);
            t["mailrow"] = CaptureProfile.MailRow(n1);
            t["yesrow"] = CaptureProfile.YesRow(n1);
            t["camrow"] = CaptureProfile.CamRow(n1);
            t["segments"] = CaptureProfile.SegmentsFor(CaptureProfile.Percent(1, false, "")).ToString();
            // D9: after SHRED the second pointing device is 214's (Night 1's BIOS line and nexus.cfg).
            bool shredSeen = save.endingsSeen != null && Array.IndexOf(save.endingsSeen, "n3_shred") >= 0;
            t["p2own"] = shredSeen ? " (214)" : "";
            t["p2owner"] = shredSeen ? "214" : "";
            t["lastn3"] = NoLastInput;
            t["118n2"] = "";
            t["ruthask"] = "";
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
            // Phase L: what Night 2's two decisions did, as Night 3's Personnel says it (163's box, Pell's patch).
            t["163fx"] = flags.Has(MemoryFlags.N2Box163Held) ? " Personal effects released to family at Reception 11/19/98 (WO-3320)."
                : flags.Has(MemoryFlags.N2Box163Released) ? " Personal effects disposed by Custodial 11/19/98 (WO-3320)."
                : " Personal effects disposed by Custodial 11/19/98.";
            t["n2patch"] = flags.Has(MemoryFlags.N2TookHand) ? "attempted 11/19/98 (WO-3322). Device 2 would not detach. Patch withdrawn."
                : flags.Has(MemoryFlags.N2LeftHand) ? "rejected by the WS-04 operator 11/19/98 (WO-3322). Back on the schedule for 11/21/98."
                : "scheduled 11/21/98.";
            // Phase Q2 (D3, WO-3324): what Night 2's audit filed about Ruth's check-in, in her record and in her last request.
            bool covered = flags.Has(MemoryFlags.N2CoveredRuth), reported = flags.Has(MemoryFlags.N2ReportedRuth);
            t["118n2"] = covered ? "Check-in 11/19/98 confirmed by WS-04 (WO-3324). " : reported ? "Under review since 11/19/98 (Security audit 3324). " : "";
            t["ruthask"] = covered
                ? "You covered for me last night. Thank you. You shouldn't have. I'm asking for one more thing, and it's the last."
                : reported
                    ? "Security says you told them the truth about last night. Good. It put me under review, which is the most honest thing that's happened to me in eleven years. Now I'm asking you to break a rule for me."
                    : "I'm asking you to break a rule for me.";
#endif
            return t;
        }

        /// <summary>{name} (as typed, lower case), {NAME}, {n0} (its first letter, or "C" as the record has it) and Night 2's {namerows}.</summary>
        static void AddName(Dictionary<string, string> t, string name)
        {
            name = NameCapture.IsValid(name) ? name : "";
            t["name"] = name;
            t["NAME"] = name.ToUpperInvariant();
            t["n0"] = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "C";
            t["namerows"] = name.Length > 0 ? "..name given: " + name + "..\n..name on file: ROURKE C..\n..mismatch corrected..\n" : "";
        }

        /// <summary>
        /// Lines filled with tokens for her voice or an ending: a line whose tokens all came out empty is dropped (YOU SAID {LINE1}
        /// without a line, GOODNIGHT {NAME} without a name), with its speaker when <paramref name="speakers"/> is given.
        /// </summary>
        public static string[] FillLines(string[] lines, IDictionary<string, string> tokens, string[] speakers, out string[] outSpeakers)
        {
            var l = new List<string>();
            var s = new List<string>();
            for (int i = 0; i < (lines?.Length ?? 0); i++)
            {
                string line = lines[i] ?? "";
                var used = TokensIn(line);
                bool allEmpty = used.Count > 0;
                foreach (var name in used)
                    if (tokens == null || !tokens.TryGetValue(name, out var v) || v.Length > 0) allEmpty = false;
                if (allEmpty) continue;
                string filled = used.Count > 0 ? Fill(line, tokens).Trim() : line;
                if (filled.Length == 0) continue;
                l.Add(filled);
                if (speakers != null) s.Add(i < speakers.Length ? speakers[i] : "entity");
            }
            outSpeakers = speakers != null ? s.ToArray() : null;
            return l.ToArray();
        }

        public static string[] FillLines(string[] lines, IDictionary<string, string> tokens) => FillLines(lines, tokens, null, out _);

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
