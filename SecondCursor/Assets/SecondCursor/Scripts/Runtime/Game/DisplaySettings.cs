using System;
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
        /// <summary>0 = VSync (default), then caps, -1 = unlimited.</summary>
        public static readonly int[] FrameRates = { 0, 30, 60, 120, 144, -1 };

        public static int FrameRate { get; private set; }
        public static bool LargeText { get; private set; }
        /// <summary>The reading size was picked (by the player, or on the Deck's first launch).</summary>
        public static bool LargeTextChosen { get; private set; }

        /// <summary>Text scale for reading panes (Jotter documents, Mail).</summary>
        public static int ReadingScale => LargeText ? 2 : 1;

        public static void Apply(SettingsData s)
        {
            if (s == null) s = new SettingsData();
            LargeText = s.largeText;
            LargeTextChosen = s.largeTextChosen;
            ApplyFrameRate(s.frameRate);
        }

        public static void ApplyFrameRate(int value)
        {
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
            if (value < 0) return c != null ? c.Text("pause.framerate.unlimited", "Unlimited") : "Unlimited";
            return value.ToString();
        }

        public static void SetLargeText(bool on)
        {
            LargeText = on;
            LargeTextChosen = true;
        }
    }
}
