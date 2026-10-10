using System;
using SecondCursor.Core.Game;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Frame rate and reading text size (per machine, in settings.json). VSync is the default; a cap turns VSync off
    /// and sets Unity's target frame rate. Large reading text doubles Jotter and Mail text (16 px letters on a Steam
    /// Deck's 1280x800 screen).
    /// </summary>
    public static class DisplaySettings
    {
        /// <summary>0 = VSync (default), then caps. There is no unlimited option: effects and the tug are tuned in time, not frames.</summary>
        public static readonly int[] FrameRates = { 0, 30, 60, 120, 144, 240 };

        /// <summary>The cap an old save's "unlimited" (-1) becomes.</summary>
        const int MigratedUnlimited = 240;

        public static int FrameRate { get; private set; }
        /// <summary>Phase Q1: Normal, Medium or Large reading text (settings.json "textSize"; older files: the Large switch).</summary>
        public static ReadingSize Size { get; private set; }
        public static bool LargeText => Size == ReadingSize.Large;
        /// <summary>The reading size was picked (by the player, or on the Deck's first launch).</summary>
        public static bool LargeTextChosen { get; private set; }

        /// <summary>Whole-number scale for things sized in whole steps (notices, tips, window layouts): 2 for Large, else 1.</summary>
        public static int ReadingScale => LargeText ? 2 : 1;

        /// <summary>Phase Q1: the text scale of the reading panes (Mail, documents, Jotter documents, Help, the Work Queue's instructions): 1, 1.5 or 2.</summary>
        public static float ReadingFactor => DisplayOptions.Factor(Size);

        public static void Apply(SettingsData s)
        {
            if (s == null) s = new SettingsData();
            Size = DisplayOptions.SizeFrom(s.textSize, s.largeText);
            LargeTextChosen = s.largeTextChosen;
            ApplyFrameRate(s.frameRate);
        }

        public static void ApplyFrameRate(int value)
        {
            if (value < 0) value = MigratedUnlimited;
            FrameRate = Array.IndexOf(FrameRates, value) >= 0 ? value : 0;
            if (FrameRate == 0)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = FrameRate;
            }
        }

        /// <summary>The next option after <paramref name="value"/> (wraps).</summary>
        public static int NextFrameRate(int value)
        {
            int i = Array.IndexOf(FrameRates, value);
            return FrameRates[(i + 1) % FrameRates.Length];
        }

        public static string FrameRateLabel(int value, Core.Content.ContentDatabase c)
        {
            if (value == 0) return c != null ? c.Text("pause.framerate.vsync", "VSync") : "VSync";
            return value.ToString();
        }

        public static void SetLargeText(bool on) => SetSize(on ? ReadingSize.Large : ReadingSize.Normal);

        public static void SetSize(ReadingSize size)
        {
            Size = size;
            LargeTextChosen = true;
        }
    }
}
