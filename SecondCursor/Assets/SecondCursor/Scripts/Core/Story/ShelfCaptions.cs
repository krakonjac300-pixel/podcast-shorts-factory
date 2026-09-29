using System;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// CAM 04's caption loop (M10): the shelf labels one after another, each for <see cref="StepSeconds"/>, except the
    /// held one (the player's own shelf) which stays for <see cref="HoldSeconds"/>. Every time the feed is switched to
    /// CAM 04 the loop starts at <see cref="StartShelf"/>, so the player's shelf comes up after one step instead of most
    /// of a cycle. The loop still passes every shelf the shelf-check orders need.
    /// </summary>
    public static class ShelfCaptions
    {
        public const float StepSeconds = 3.5f, HoldSeconds = 5.0f;
#if SC_DEMO
        // Night 3's shelves are not in the free demo (only the loop's timing is).
        public const string StartShelf = "", HoldShelf = "";
#else
        public const string StartShelf = "SHELF 17", HoldShelf = "SHELF 18";
#endif

        /// <summary>The index of the first label that starts with <paramref name="prefix"/> (then a colon), or -1.</summary>
        public static int Find(string[] labels, string prefix)
        {
            if (labels == null) return -1;
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != null && labels[i].StartsWith(prefix + ":", StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Seconds of one full pass through <paramref name="count"/> labels.</summary>
        public static float CycleSeconds(int count, int hold) => count <= 0 ? 0f : count * StepSeconds + (hold >= 0 && hold < count ? HoldSeconds - StepSeconds : 0f);

        /// <summary>The label shown <paramref name="elapsed"/> seconds after the feed switched to CAM 04.</summary>
        public static int Index(float elapsed, int count, int start, int hold)
        {
            if (count <= 0) return 0;
            if (start < 0 || start >= count) start = 0;
            float cycle = CycleSeconds(count, hold);
            float t = elapsed < 0f ? 0f : elapsed % cycle;
            int i = start;
            for (int n = 0; n < count; n++)
            {
                float len = i == hold ? HoldSeconds : StepSeconds;
                if (t < len) return i;
                t -= len;
                i = (i + 1) % count;
            }
            return start;
        }
    }
}
