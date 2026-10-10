using System;

namespace SecondCursor.Core.Game
{
    /// <summary>Phase Q1 (owner 7, F A10): the reading text size.</summary>
    public enum ReadingSize { Normal = 0, Medium = 1, Large = 2 }

    /// <summary>Phase Q1: how strong the CRT layer is (scanlines, vignette, grain, mains flicker).</summary>
    public enum CrtLevel { Off = 0, Low = 1, Full = 2 }

    /// <summary>
    /// Phase Q1: the reading text size and the CRT intensity as settings.json keeps them. A file from before Phase Q1 has only the old
    /// switches (largeText, crtEffects); their values are read back as Large or Normal and Full or Off. Engine-free.
    /// </summary>
    public static class DisplayOptions
    {
        /// <summary>A stored size (-1 = not stored yet) or the old Large switch.</summary>
        public static ReadingSize SizeFrom(int stored, bool legacyLarge)
        {
            if (stored >= (int)ReadingSize.Normal && stored <= (int)ReadingSize.Large) return (ReadingSize)stored;
            return legacyLarge ? ReadingSize.Large : ReadingSize.Normal;
        }

        /// <summary>A stored level (-1 = not stored yet) or the old CRT switch.</summary>
        public static CrtLevel CrtFrom(int stored, bool legacyOn)
        {
            if (stored >= (int)CrtLevel.Off && stored <= (int)CrtLevel.Full) return (CrtLevel)stored;
            return legacyOn ? CrtLevel.Full : CrtLevel.Off;
        }

        /// <summary>The text scale of the reading panes: 1, 1.5 or 2 (Medium is drawn on a 2x screen texture, so its pixels stay square).</summary>
        public static float Factor(ReadingSize size) => size == ReadingSize.Large ? 2f : size == ReadingSize.Medium ? 1.5f : 1f;

        /// <summary>How much of the CRT layer shows: 0, about half, or all of it.</summary>
        public static float Strength(CrtLevel level) => level == CrtLevel.Full ? 1f : level == CrtLevel.Low ? 0.45f : 0f;

        public static ReadingSize Next(ReadingSize size) => (ReadingSize)(((int)size + 1) % 3);
        public static CrtLevel Next(CrtLevel level) => (CrtLevel)(((int)level + 1) % 3);

        /// <summary>
        /// The screen texture's scale for a display at <paramref name="displayScale"/> virtual pixels per real one: Medium text needs at
        /// least 2 (a 1.5x glyph is then exactly 3 texture pixels per font pixel) once the display is big enough to show it.
        /// </summary>
        public static int TextureScale(float displayScale, bool integerDisplay, ReadingSize size)
        {
            int scale = integerDisplay ? 1 : Math.Max(1, Math.Min(4, (int)Math.Ceiling(displayScale)));
            if (size == ReadingSize.Medium && displayScale >= 2f) scale = Math.Max(scale, 2);
            return scale;
        }
    }
}
