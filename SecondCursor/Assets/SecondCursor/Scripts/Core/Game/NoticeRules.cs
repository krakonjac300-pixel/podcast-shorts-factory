using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Game
{
    /// <summary>Phase Q4 (A3): how long a notice stays: by its length (Normal), half as long again (Long), or until it is clicked.</summary>
    public enum NoticeTime { Normal = 0, Long = 1, UntilClicked = 2 }

    /// <summary>
    /// Phase Q4 (review board A3): readable notice durations, the cap on how many show at once, which notices may never be dropped
    /// unseen, and the settings value. Engine-free.
    /// </summary>
    public static class NoticeRules
    {
        /// <summary>A notice shows for at least this long, whatever its length.</summary>
        public const float MinSeconds = 7f;
        /// <summary>...and (Normal) at most this long.</summary>
        public const float MaxSeconds = 20f;
        /// <summary>A reading pace of about 12 characters a second, plus <see cref="BaseSeconds"/> to notice the toast and begin.</summary>
        public const float CharsPerSecond = 12f;
        public const float BaseSeconds = 4f;
        /// <summary>Long is this much longer than Normal.</summary>
        public const float LongFactor = 1.5f;
        /// <summary>The ceiling of Long, in seconds.</summary>
        public const float LongMaxSeconds = 30f;
        /// <summary>Until clicked: the toast stays for as long as a session can last.</summary>
        public const float UntilClickedSeconds = 3600f;
        /// <summary>At most this many notices are on screen at once; the rest wait and a "+N more" chip says so.</summary>
        public const int VisibleCap = 3;
        /// <summary>The Recent notices list keeps this many.</summary>
        public const int HistoryCap = 20;

        /// <summary>Seconds a notice with <paramref name="characters"/> characters of body text stays up.</summary>
        public static float Duration(int characters, NoticeTime mode)
        {
            float normal = Clamp(BaseSeconds + Math.Max(0, characters) / CharsPerSecond, MinSeconds, MaxSeconds);
            switch (mode)
            {
                case NoticeTime.Long: return Math.Min(LongMaxSeconds, normal * LongFactor);
                case NoticeTime.UntilClicked: return UntilClickedSeconds;
                default: return normal;
            }
        }

        /// <summary>
        /// A notice that waited for room longer than it would have shown is dropped unseen, unless it is important: a task, a deadline, a
        /// camera or an order, a sticky notice, or one that goes by its own condition. Those wait as long as it takes.
        /// </summary>
        public static bool IsImportant(string icon, bool sticky, bool hasCondition, NoticeKind kind)
        {
            if (sticky || hasCondition || kind == NoticeKind.Deadline) return true;
            switch (icon)
            {
                case "icon_camera":
                case "icon_task_active":
                case "icon_task_pending":
                case "icon_task_done":
                case "icon_workorders":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The settings.json value of a notice time ("normal", "long", "clicked"); anything else reads as Normal.</summary>
        public static NoticeTime Parse(string stored)
        {
            switch (stored)
            {
                case "long": return NoticeTime.Long;
                case "clicked": return NoticeTime.UntilClicked;
                default: return NoticeTime.Normal;
            }
        }

        public static string Id(NoticeTime mode) => mode == NoticeTime.Long ? "long" : mode == NoticeTime.UntilClicked ? "clicked" : "normal";

        public static NoticeTime Next(NoticeTime mode) => (NoticeTime)(((int)mode + 1) % 3);

        /// <summary>How many notices are waiting beyond the cap (the number on the "+N more" chip), 0 for none.</summary>
        public static int MoreWaiting(int waitingForTurn) => Math.Max(0, waitingForTurn);

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>One line of the Recent notices list.</summary>
    public sealed class NoticeEntry
    {
        public readonly string Stamp, Title, Body;
        public readonly NoticeKind Kind;

        public NoticeEntry(string stamp, string title, string body, NoticeKind kind)
        {
            Stamp = stamp ?? "";
            Title = title ?? "";
            Body = body ?? "";
            Kind = kind;
        }
    }

    /// <summary>
    /// Phase Q4 (A3): the last <see cref="NoticeRules.HistoryCap"/> notices, newest first, with the shift clock's time. A notice that
    /// repeats the one just before it word for word (a fight over the viewer can close it every few seconds) is kept once.
    /// </summary>
    public sealed class NoticeHistory
    {
        readonly List<NoticeEntry> _entries = new List<NoticeEntry>();

        public int Count => _entries.Count;
        /// <summary>Raised whenever an entry is added (an open list refreshes).</summary>
        public int Revision { get; private set; }

        /// <summary>The entries, newest first.</summary>
        public IReadOnlyList<NoticeEntry> Entries => _entries;

        public void Add(string stamp, string title, string body, NoticeKind kind)
        {
            if (_entries.Count > 0 && _entries[0].Title == (title ?? "") && _entries[0].Body == (body ?? "")) return;
            _entries.Insert(0, new NoticeEntry(stamp, title, body, kind));
            if (_entries.Count > NoticeRules.HistoryCap) _entries.RemoveRange(NoticeRules.HistoryCap, _entries.Count - NoticeRules.HistoryCap);
            Revision++;
        }

        public void Clear()
        {
            _entries.Clear();
            Revision++;
        }
    }
}
