using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Story
{
    /// <summary>What the Log Off item says when it is used (expansion spec 5.3, exit 2).</summary>
    public enum LogOffCheck
    {
        /// <summary>Before 7:00: logoff.early.</summary>
        Early,
        /// <summary>session.cfg still says ALLOW_LOGOFF=0: logoff.disabled.</summary>
        Disabled,
        /// <summary>The confirm dialog opens.</summary>
        Allowed,
    }

    /// <summary>The three ways Night 3 ends (spec 6.1).</summary>
    public enum Night3Exit { None, Shred, LogOff, Keep }

    /// <summary>Phase P (T1): the finale's fight over 017 at the bin: she holds on and lets it go (LetGo), or she fights it (Fight).</summary>
    public enum FinaleBinMode { LetGo, Fight }

    /// <summary>
    /// Night 3's rules as pure functions (expansion spec 5 and 6), so they are unit-tested: when Log Off works,
    /// what the edited config files say, how trust changes the last fight, the CAM 04 shelf rule, the KEEP
    /// lines, and when time alone ends the night.
    /// </summary>
    public static class Night3Rules
    {
        /// <summary>Clock minutes since midnight.</summary>
        public const int RuthCall = 2 * 60 + 17, RoundsStart = 3 * 60, RoundsEnd = 3 * 60 + 30;
        public const int FinaleStart = 6 * 60 + 41, GaryLogOff = 6 * 60 + 48, GarySeated = 6 * 60 + 52;
        public const int LogOffTime = 7 * 60, KeepTime = 7 * 60 + 5, HardCap = 7 * 60 + 8;

        /// <summary>Seconds a shred or log off already running at 7:05 gets to finish before KEEP.</summary>
        public const float ExitGrace = 20f;

        /// <summary>Trust at or above this: she asks to be let go (and grips 10% softer).</summary>
        public const float TrustLetGo = 0.2f;
        /// <summary>Trust at or below this: she refuses and grips 10% harder.</summary>
        public const float TrustHard = -0.4f;

        public static LogOffCheck CheckLogOff(int clockMinutes, bool allowLogoff)
        {
            if (clockMinutes < LogOffTime) return LogOffCheck.Early;
            return allowLogoff ? LogOffCheck.Allowed : LogOffCheck.Disabled;
        }

        /// <summary>
        /// The value of the last "KEY=value" line of a config file (keys compare case-insensitively), trimmed;
        /// null when the key is missing. Comment lines (;) are ignored.
        /// </summary>
        public static string ConfigValue(string content, string key)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(key)) return null;
            string found = null;
            foreach (var raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) found = line.Substring(eq + 1).Trim();
            }
            return found;
        }

        /// <summary>session.cfg lets WS-04 log off only when it says exactly ALLOW_LOGOFF=1.</summary>
        public static bool AllowsLogoff(string sessionCfg) => ConfigValue(sessionCfg, "ALLOW_LOGOFF") == "1";

        /// <summary>camview.cfg lists CAM 00 only when it says exactly OPERATOR_OVERRIDE=1.</summary>
        public static bool OperatorOverride(string camviewCfg) => ConfigValue(camviewCfg, "OPERATOR_OVERRIDE") == "1";

        /// <summary>The finale's grip multiplier from trust (spec 5.3): 0.9 when she asks to be let go, 1.1 when trust is very low.</summary>
        public static float GripMultForTrust(float trust)
        {
            if (trust >= TrustLetGo) return 0.9f;
            if (trust <= TrustHard) return 1.1f;
            return 1f;
        }

        /// <summary>Phase P (T1): Fight-mode finale contests she wins before her hand gives up and the mode turns to LetGo.</summary>
        public const int FightContestsBeforeLetGo = 2;
        /// <summary>Phase P (T1): the LetGo hold brings 017 to the finish this long after GET READY; after an early release, the shorter one.</summary>
        public const float LetGoHoldSeconds = 3f, LetGoRetryHoldSeconds = 1.5f;
        /// <summary>Phase P (T1): her paced Jotter lines start at these shares of the hold's progress.</summary>
        public static readonly float[] LetGoLineAt = { 0f, 0.4f, 0.73f };

        /// <summary>
        /// Phase P (T1): she lets 017 go unless trust is very low; a reply asking her to let go, her name, or
        /// <see cref="FightContestsBeforeLetGo"/> refused contests turn even that into a hold.
        /// </summary>
        public static FinaleBinMode BinMode(float trust, bool saidLetGo, bool saidName, int refusedContests) =>
            trust > TrustHard || saidLetGo || saidName || refusedContests >= FightContestsBeforeLetGo ? FinaleBinMode.LetGo : FinaleBinMode.Fight;

        /// <summary>Phase P (T1): her line when the first finale contest over 017 starts.</summary>
        public static string TugLineSet(FinaleBinMode mode) => mode == FinaleBinMode.LetGo ? "n3_tug_letgo" : "n3_tug_refuse";

        /// <summary>Her last words while 017 is shredded.</summary>
        public static string ShredLastWordsSet(float trust) => trust >= TrustLetGo ? "n3_shred_last_a" : "n3_shred_last_b";

        /// <summary>What she says when the log off confirm opens.</summary>
        public static string LogOffLineSet(float trust) => trust >= TrustLetGo ? "n3_logoff_trust" : "n3_logoff_low";

        /// <summary>
        /// The CAM 04 shelf rule: approve only if the owner's number is on the listed shelf. The listed location
        /// ends in "SHELF NN"; the shelf captions read "SHELF NN: 142 LUNDY T.". A shelf missing from the
        /// captions, or an unreadable location, is a reject.
        /// </summary>
        public static string ShelfDecision(string listedLocation, string ownerNumber, IList<string> shelfCaptions)
        {
            int shelf = ShelfNumber(listedLocation);
            if (shelf < 0 || string.IsNullOrEmpty(ownerNumber) || shelfCaptions == null) return "reject";
            foreach (var caption in shelfCaptions)
            {
                if (ShelfNumber(caption) != shelf) continue;
                int colon = caption.IndexOf(':');
                if (colon < 0) continue;
                foreach (var word in caption.Substring(colon + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (word == ownerNumber) return "approve";
            }
            return "reject";
        }

        /// <summary>
        /// Phase K: the CAM 04 label of the shelf an order lists ("SHELF 18: 214 ROURKE C. (RESERVED)"), or null; the result line
        /// after each shelf decision quotes it, so the player sees what the rule was checked against.
        /// </summary>
        public static string ShelfCaptionFor(string listedLocation, IList<string> shelfCaptions)
        {
            int shelf = ShelfNumber(listedLocation);
            if (shelf < 0 || shelfCaptions == null) return null;
            foreach (var caption in shelfCaptions)
                if (ShelfNumber(caption) == shelf) return caption;
            return null;
        }

        /// <summary>The number after the word SHELF ("Sublevel C, SHELF 14" gives 14), or -1.</summary>
        public static int ShelfNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            int at = text.IndexOf("SHELF", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;
            int i = at + 5;
            while (i < text.Length && text[i] == ' ') i++;
            int n = 0, digits = 0;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9')
            {
                n = n * 10 + (text[i] - '0');
                i++;
                digits++;
            }
            return digits > 0 ? n : -1;
        }

        /// <summary>The KEEP ending's line set: the one where you chose to stay, or the one where she kept you.</summary>
        public static string KeepLineSet(bool saidStay) => saidStay ? "n3_end_keep_stay" : "n3_end_keep_default";

        /// <summary>
        /// KEEP's lines with their speakers; if the player said her name, the name line (and its speaker) goes
        /// in before the last line (spec 6.2).
        /// </summary>
        public static void KeepLines(string[] lines, string[] speakers, string[] nameLines, string[] nameSpeakers, bool saidName,
            out string[] outLines, out string[] outSpeakers)
        {
            var l = new List<string>(lines ?? Array.Empty<string>());
            var s = new List<string>();
            for (int i = 0; i < l.Count; i++) s.Add(speakers != null && i < speakers.Length ? speakers[i] : "entity");
            if (saidName && nameLines != null && nameLines.Length > 0)
            {
                int at = Math.Max(0, l.Count - 1);
                for (int i = 0; i < nameLines.Length; i++)
                {
                    l.Insert(at + i, nameLines[i]);
                    s.Insert(at + i, nameSpeakers != null && i < nameSpeakers.Length ? nameSpeakers[i] : "entity");
                }
            }
            outLines = l.ToArray();
            outSpeakers = s.ToArray();
        }

        /// <summary>
        /// Phase J: the end card's cause line (a string key) for an exit; for KEEP, what brought it (<paramref name="keepCause"/>: "confirm"
        /// when she was told to stay, "seat" when Custodial reached the chair, anything else is the clock), and whether a log off was
        /// under way when it came.
        /// </summary>
        public static string EndingCauseKey(Night3Exit exit, string keepCause, bool logOffCut)
        {
            if (exit == Night3Exit.Shred) return "end.shred.cause";
            if (exit == Night3Exit.LogOff) return "end.logoff.cause";
            switch (keepCause)
            {
                case "confirm": return "end.keep.cause.stay";
                case "seat": return logOffCut ? "end.keep.cause.seat.logoff" : "end.keep.cause.seat";
                // Phase N (fifth blind playtest, finding 3): 7:05 came while the player was trying to leave another way.
                case "letgo": return "end.keep.cause.letgo";
                case "logoffdenied": return "end.keep.cause.logoffdenied";
                case "logoffearly": return "end.keep.cause.logoffearly";
                case "logofftried": return "end.keep.cause.logofftried";
                default: return "end.keep.cause.time";
            }
        }

        /// <summary>Phase N: KEEP's subtitle: "You stayed." only when the player told her to stay; otherwise she kept them.</summary>
        public static string KeepSubtitleKey(string keepCause) => keepCause == "confirm" ? "end.keep.subtitle" : "end.keep.subtitle.kept";

        /// <summary>
        /// Whether time alone ends the night with KEEP: at the hard cap always; at 7:05 unless a shred or log off
        /// is running and has not used up its grace yet.
        /// </summary>
        public static bool KeepByTime(int clockMinutes, bool exitRunning, float secondsSinceKeepTime)
        {
            if (clockMinutes >= HardCap) return true;
            if (clockMinutes < KeepTime) return false;
            return !exitRunning || secondsSinceKeepTime >= ExitGrace;
        }
    }
}
