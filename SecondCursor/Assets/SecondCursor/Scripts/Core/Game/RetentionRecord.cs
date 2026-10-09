using System;
using System.Collections.Generic;
using SecondCursor.Core.Story;

namespace SecondCursor.Core.Game
{
    /// <summary>One row of a record: a label and its value (the page draws the dotted leader between them).</summary>
    public sealed class RecordRow
    {
        public readonly string Label;
        public readonly string Value;

        public RecordRow(string label, string value)
        {
            Label = label ?? "";
            Value = value ?? "";
        }

        /// <summary>"LABEL\tVALUE", the form the save keeps the last record in.</summary>
        public string Pack() => Label + "\t" + Value;

        public static RecordRow Unpack(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return new RecordRow("", "");
            int tab = packed.IndexOf('\t');
            return tab < 0 ? new RecordRow(packed, "") : new RecordRow(packed.Substring(0, tab), packed.Substring(tab + 1));
        }
    }

    /// <summary>
    /// Phase Q2 (board A T2, C V5): the Retention Record, a spoiler-free page built only from what this run measured and kept
    /// (<see cref="CaptureStats"/>, the decisions in memory, the name given). A four-row version sits on the Night 1 card (the
    /// demo's too) and the Night 2 card; the full page follows a Night 3 ending and stays in Records. No ending name and no story
    /// noun beyond Night 1's appears; the player's own words pass <see cref="EchoFilter.ForFile"/>.
    /// </summary>
    public static class RetentionRecord
    {
        /// <summary>The four rows under a night's card (Night 1: words, CAM 03, first words; Night 2: words, CAM 03, hesitation).</summary>
        public static List<RecordRow> Card(int night, SaveData d, Func<string, object[], string> fmt)
        {
            var rows = new List<RecordRow>();
            if (d == null || fmt == null || night < 1 || night > 2) return rows;
            var s = Night(d, night);
            if (s == null) return rows;
            bool hid = d.memory != null && HasFlag(d.memory, MemoryFlags.N2Hid214);
            // Phase S (two cards read 88% and nothing said why): Night 2's card says whether the copy grew, and why not.
            int pct = CaptureProfile.Percent(night, hid, "");
            string pctKey = night == 2 ? (hid ? "record.pct.held" : "record.pct.grew") : "record.pct";
            rows.Add(new RecordRow(fmt("record.capture", null), fmt(pctKey, new object[] { pct })));
            rows.Add(new RecordRow(fmt("record.words", null), s.words017.ToString()));
            // Phase R: the card counts the player's own openings of CAM 03 (the story opens the viewer too, and the page said "looked at"); the full page keeps the looks.
            rows.Add(new RecordRow(fmt("record.camopen", null), Times(s.camOpened, fmt)));
            if (night == 1) rows.Add(new RecordRow(fmt("record.first", null), Quote(s.firstLine, fmt)));
            else rows.Add(new RecordRow(fmt("record.yes", null), Yes(s.yesSeconds, s.yesCount, fmt)));
            return rows;
        }

        /// <summary>
        /// The whole page after Night 3: shifts and minutes, words to session 017, the first words, the hesitation before Yes, CAM 03,
        /// tugs, orders decided against their rule (<paramref name="correctByOrder"/>: order id to its rule's answer), the name given,
        /// whether her name was said, the status and capture for <paramref name="endingId"/>, and one sealed hint per ending not yet seen.
        /// </summary>
        public static List<RecordRow> Full(SaveData d, string endingId, IDictionary<string, string> correctByOrder, Func<string, object[], string> fmt)
        {
            var rows = new List<RecordRow>();
            if (d == null || fmt == null) return rows;
            int shifts = 0, words = 0, looks = 0, wins = 0, losses = 0, yesCount = 0;
            float seconds = 0f, yesSeconds = 0f;
            var nights = new List<CaptureStats>();
            for (int n = 1; n <= SaveData.Nights; n++)
            {
                var s = Night(d, n);
                nights.Add(s);
                if (s == null) continue;
                shifts++;
                seconds += s.seconds;
                words += s.words017;
                looks += s.camLooks;
                wins += s.tugWins;
                losses += s.tugLosses;
                yesSeconds += s.yesSeconds;
                yesCount += s.yesCount;
            }
            var against = AgainstTheRule(d.memory, correctByOrder);
            bool hid = HasFlag(d.memory, MemoryFlags.N2Hid214);
            var memory = new NarrativeFlags();
            memory.Restore(d.memory ?? new FlagSnapshot());

            rows.Add(new RecordRow(fmt("record.shifts", null), fmt("record.shifts.value", new object[] { shifts, (int)Math.Round(seconds / 60f) })));
            rows.Add(new RecordRow(fmt("record.words", null), words.ToString()));
            rows.Add(new RecordRow(fmt("record.first", null), Quote(CaptureProfile.FirstLine(nights), fmt)));
            rows.Add(new RecordRow(fmt("record.yes", null), Yes(yesSeconds, yesCount, fmt)));
            rows.Add(new RecordRow(fmt("record.cam", null), Times(looks, fmt)));
            rows.Add(new RecordRow(fmt("record.tugs", null), wins + losses > 0 ? fmt("record.tugs.value", new object[] { wins, losses }) : fmt("record.none", null)));
            rows.Add(new RecordRow(fmt("record.orders", null), against.Count == 0 ? "0"
                : fmt("record.orders.value", new object[] { against.Count, string.Join(", ", against.GetRange(0, Math.Min(2, against.Count))) })));
            rows.Add(new RecordRow(fmt("record.name", null), NameCapture.IsValid(d.playerName) ? Quote(d.playerName, fmt) : fmt("record.none", null)));
            rows.Add(new RecordRow(fmt("record.saidname", null), fmt(MemoryFlags.SaidNameAny(memory) ? "record.yesword" : "record.noword", null)));
            rows.Add(new RecordRow("", ""));
            rows.Add(new RecordRow(fmt("record.status", null), fmt("record.status." + endingId, null)));
            rows.Add(new RecordRow(fmt("record.capture", null), fmt("record.pct", new object[] { CaptureProfile.Percent(3, hid, endingId) })));
            var sealedEndings = new List<string>();
            foreach (var id in AchievementIds.Night3Endings)
                if (id != endingId && Array.IndexOf(d.endingsSeen ?? Array.Empty<string>(), id) < 0) sealedEndings.Add(id);
            rows.Add(new RecordRow(fmt("record.sealed", null), sealedEndings.Count == 0 ? fmt("record.none", null) : fmt("record.sealed.value", new object[] { sealedEndings.Count })));
            foreach (var id in sealedEndings) rows.Add(new RecordRow("", fmt("record.hint." + id, null)));
            return rows;
        }

        /// <summary>A night's measurements, or null if that night was not recorded in this run.</summary>
        public static CaptureStats Night(SaveData d, int night)
        {
            if (d?.capture == null || night < 1 || night > d.capture.Length) return null;
            var s = d.capture[night - 1];
            return s != null && s.recorded ? s : null;
        }

        /// <summary>"WO-3320" for every remembered order decision that went against its rule, in key order.</summary>
        public static List<string> AgainstTheRule(FlagSnapshot memory, IDictionary<string, string> correctByOrder)
        {
            var list = new List<string>();
            if (memory?.flags == null || correctByOrder == null) return list;
            foreach (var key in memory.flags)
            {
                // m.n2.wo3320.reject
                if (key == null || !key.StartsWith(MemoryFlags.Prefix + "n", StringComparison.Ordinal)) continue;
                var parts = key.Split('.');
                if (parts.Length != 4 || !parts[2].StartsWith("wo", StringComparison.Ordinal)) continue;
                string orderId = "wo_" + parts[2].Substring(2), decision = parts[3];
                if (decision != "approve" && decision != "reject") continue;
                if (correctByOrder.TryGetValue(orderId, out var correct) && correct != decision && !list.Contains("WO-" + parts[2].Substring(2)))
                    list.Add("WO-" + parts[2].Substring(2));
            }
            return list;
        }

        static bool HasFlag(FlagSnapshot memory, string flag) => memory?.flags != null && Array.IndexOf(memory.flags, flag) >= 0;

        static string Times(int n, Func<string, object[], string> fmt) =>
            n <= 0 ? fmt("record.never", null) : n == 1 ? fmt("record.once", null) : fmt("record.times", new object[] { n });

        static string Quote(string line, Func<string, object[], string> fmt)
        {
            string shown = EchoFilter.ForFile(line);
            return shown.Length == 0 ? fmt("record.none", null) : "\"" + shown + "\"";
        }

        static string Yes(float total, int count, Func<string, object[], string> fmt) =>
            count <= 0 ? fmt("record.never", null) : fmt("record.yes.value", new object[] { CaptureProfile.Seconds(total / count), count });
    }
}
