using System;

namespace SecondCursor.Core.Game
{
    /// <summary>Phase Q4 (A10): how hard the screen shakes.</summary>
    public enum ShakeLevel { Full = 0, Reduced = 1, Off = 2 }

    /// <summary>Phase Q4 (A7): how the double-click behaves. Single makes a click open what a double-click would.</summary>
    public enum ClickSpeed { Normal = 0, Slow = 1, Single = 2 }

    /// <summary>
    /// Phase Q4 (review board A7, A9, A10): the access options that used to be bundled with Reduce flashing or fixed in code. Each stored value
    /// of -1 means "follow Reduce flashing" (the behaviour before Phase Q4), so an older settings.json plays as it did. Engine-free.
    /// </summary>
    public static class AccessOptions
    {
        /// <summary>The shake level: the stored 0..2, or (-1) Reduced when Reduce flashing is on and Full when it is off.</summary>
        public static ShakeLevel ShakeFrom(int stored, bool reduceFlashing)
        {
            if (stored >= (int)ShakeLevel.Full && stored <= (int)ShakeLevel.Off) return (ShakeLevel)stored;
            return reduceFlashing ? ShakeLevel.Reduced : ShakeLevel.Full;
        }

        /// <summary>How much of a shake's size plays: all, 0.3 (what Reduce flashing always did), or none.</summary>
        public static float ShakeFactor(ShakeLevel level) => level == ShakeLevel.Off ? 0f : level == ShakeLevel.Reduced ? 0.3f : 1f;

        public static ShakeLevel Next(ShakeLevel level) => (ShakeLevel)(((int)level + 1) % 3);

        /// <summary>Sudden sounds softened: the stored 0 (loud) or 1 (soft), or (-1) as Reduce flashing says.</summary>
        public static bool SoftSoundsFrom(int stored, bool reduceFlashing) => stored == 0 ? false : stored == 1 ? true : reduceFlashing;

        /// <summary>Seconds within which a second click counts as a double-click.</summary>
        public static float DoubleClickSeconds(ClickSpeed speed) => speed == ClickSpeed.Slow ? 0.9f : 0.45f;

        /// <summary>Virtual pixels the pointer may move between the two clicks of a double-click.</summary>
        public static float DoubleClickDistance(ClickSpeed speed) => speed == ClickSpeed.Slow ? 12f : 5f;

        /// <summary>A single click opens icons and rows (and folders) as a double-click would.</summary>
        public static bool OpensOnSingleClick(ClickSpeed speed) => speed == ClickSpeed.Single;

        public static ClickSpeed Next(ClickSpeed speed) => (ClickSpeed)(((int)speed + 1) % 3);

        public static ClickSpeed ClickFrom(int stored) => stored >= (int)ClickSpeed.Normal && stored <= (int)ClickSpeed.Single ? (ClickSpeed)stored : ClickSpeed.Normal;

        /// <summary>The drawn size of the pointers: 1, or twice that with the large cursor (the three silhouettes stay distinct).</summary>
        public static int CursorScale(bool large) => large ? 2 : 1;
    }

    /// <summary>
    /// Phase Q4 (R6): the CRT scanlines in real pixels. The old overlay repeated its two rows once per virtual row, so on a Steam Deck (1.333x)
    /// the dark rows landed 2 on, 2 off per three virtual rows and beat against the 10 px glyphs (a moire). Here the period is a whole number of
    /// real pixels at every scale, so the pattern is perfectly regular. At 2x and 4x it is what it always was (one dark row per two).
    /// </summary>
    public static class ScanlinePlan
    {
        /// <summary>Real pixels from one dark row to the next (never under 2).</summary>
        public static int Period(float scale) => Math.Max(2, (int)Math.Round(scale, MidpointRounding.AwayFromZero));

        /// <summary>Dark rows in a period (the rest are clear).</summary>
        public static int DarkRows(int period) => Math.Max(1, period / 2);

        /// <summary>How many periods fit the display's real height (the texture's v repeat).</summary>
        public static float Repeats(float displayPixelHeight, int period) => displayPixelHeight / period;
    }
}
