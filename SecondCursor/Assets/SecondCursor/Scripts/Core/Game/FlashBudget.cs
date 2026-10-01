namespace SecondCursor.Core.Game
{
    /// <summary>What a flash event may do right now: play in full, play softened, or not play at all (Reduce flashing, over its own budget).</summary>
    public enum FlashVerdict { Full, Soft, Dropped }

    /// <summary>
    /// The photosensitivity budget: bright flash events (glitch bursts, static cuts, hit flashes) may start at most
    /// <see cref="MaxPerSecond"/> times in any second and <see cref="MinGap"/> seconds apart, and with Reduce flashing at most once a second.
    /// An event over the budget is softened (Full effects) or dropped (Reduce flashing), so the moment still reads. Engine-free: the caller
    /// passes its unscaled clock.
    /// </summary>
    public sealed class FlashBudget
    {
        public const int MaxPerSecond = 3;
        public const float MinGap = 0.34f;
        public const int ReducedMaxPerSecond = 1;
        public const float ReducedMinGap = 1f;
        const float Window = 1f;

        readonly float[] _starts = new float[MaxPerSecond];
        int _count;

        /// <summary>An event starts at <paramref name="now"/>: true (and recorded) when the budget has room for it.</summary>
        public bool TryBegin(float now, bool reduced)
        {
            int max = reduced ? ReducedMaxPerSecond : MaxPerSecond;
            float gap = reduced ? ReducedMinGap : MinGap;
            int inWindow = 0;
            float last = float.NegativeInfinity;
            for (int i = 0; i < _count; i++)
            {
                if (now - _starts[i] < Window) inWindow++;
                if (_starts[i] > last) last = _starts[i];
            }
            if (inWindow >= max || now - last < gap) return false;
            Record(now);
            return true;
        }

        /// <summary>A scripted flash that always plays (a climax's own sequence) still counts: the events after it see the budget it used.</summary>
        public void Record(float now)
        {
            // Keeps the newest MaxPerSecond starts (the oldest slot is overwritten).
            int slot = _count < _starts.Length ? _count++ : IndexOfOldest();
            _starts[slot] = now;
        }

        int IndexOfOldest()
        {
            int oldest = 0;
            for (int i = 1; i < _count; i++) if (_starts[i] < _starts[oldest]) oldest = i;
            return oldest;
        }

        /// <summary>The verdict for an event that asked at <paramref name="now"/>.</summary>
        public FlashVerdict Decide(float now, bool reduced)
        {
            bool granted = TryBegin(now, reduced);
            if (reduced) return granted ? FlashVerdict.Soft : FlashVerdict.Dropped;
            return granted ? FlashVerdict.Full : FlashVerdict.Soft;
        }
    }
}
