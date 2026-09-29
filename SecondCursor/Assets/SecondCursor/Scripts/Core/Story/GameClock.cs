using System;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// In-game time shown in the taskbar. The night shift runs faster than real time so the clock visibly
    /// moves; the story can freeze it, jump it, or make it glitch.
    /// </summary>
    public sealed class GameClock
    {
        float _minutes;

        /// <summary>Game minutes that pass per real second.</summary>
        public float Rate = 1f / 12f;
        public bool Frozen;

        public GameClock(int startHour, int startMinute)
        {
            Set(startHour, startMinute);
        }

        public int TotalMinutes => (int)Math.Floor(_minutes);
        public int Hour24 => (TotalMinutes / 60) % 24;
        public int Minute => TotalMinutes % 60;

        public void Set(int hour24, int minute) => _minutes = ((hour24 % 24) * 60 + minute) % (24 * 60);

        public void Tick(float realDeltaSeconds)
        {
            if (Frozen || realDeltaSeconds <= 0f) return;
            _minutes = (_minutes + realDeltaSeconds * Rate) % (24 * 60);
        }

        /// <summary>"2:47 AM" style.</summary>
        public string Format12() => Format12(TotalMinutes);

        /// <summary>"2:47 AM" for any minutes since midnight (the title's Continue shows a checkpoint's time).</summary>
        public static string Format12(int totalMinutes)
        {
            int t = ((totalMinutes % (24 * 60)) + 24 * 60) % (24 * 60);
            int hour24 = t / 60, minute = t % 60;
            int h = hour24 % 12;
            if (h == 0) h = 12;
            return h + ":" + minute.ToString("00") + (hour24 < 12 ? " AM" : " PM");
        }

        /// <summary>"02:47:13" style for camera overlays (seconds derived from the fractional minute).</summary>
        /// <summary>Minutes since midnight, with the fraction (for clocks that run separately, like CCTV time).</summary>
        public double ExactMinutes => _minutes;

        public string FormatCamera() => FormatCamera(_minutes);

        /// <summary>"HH:MM:SS" for any minutes-since-midnight value.</summary>
        public static string FormatCamera(double minutes)
        {
            minutes = ((minutes % (24 * 60)) + 24 * 60) % (24 * 60);
            int total = (int)Math.Floor(minutes);
            int sec = (int)((minutes - total) * 60.0);
            return ((total / 60) % 24).ToString("00") + ":" + (total % 60).ToString("00") + ":" + sec.ToString("00");
        }
    }
}
