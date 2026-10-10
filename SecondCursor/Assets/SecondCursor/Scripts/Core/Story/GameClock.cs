using System;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// In-game time shown in the taskbar. The night shift runs faster than real time so the clock visibly
    /// moves; the story can freeze it, speed it up or jump it forward, but never back (Phase I: a player who reads
    /// "10 min left" next to a clock that has gone backwards trusts neither). <see cref="Set"/> only ever moves it
    /// forward; <see cref="Reset"/> is for a fresh shift, a restored checkpoint and tests.
    /// </summary>
    public sealed class GameClock
    {
        double _minutes;
        float _rate = 1f / 12f;
        double _highWater;

        /// <summary>Game minutes that pass per real second (never below 0: time does not run backwards).</summary>
        public float Rate
        {
            get => _rate;
            set => _rate = value < 0f || float.IsNaN(value) ? 0f : value;
        }

        public bool Frozen;

        /// <summary>Times <see cref="Set"/> was asked for an earlier time and refused (a story bug, counted so tests and the bridge can see it).</summary>
        public int RefusedBackSets { get; private set; }

        /// <summary>The latest time reached since the last <see cref="Reset"/> (minutes since midnight, with the fraction).</summary>
        public double HighWater => _highWater;

        public GameClock(int startHour, int startMinute)
        {
            Reset(startHour, startMinute);
        }

        public int TotalMinutes => (int)Math.Floor(_minutes);
        public int Hour24 => (TotalMinutes / 60) % 24;
        public int Minute => TotalMinutes % 60;

        /// <summary>
        /// Jumps the clock forward to h:mm. A time that is not later than the shown one changes nothing (the same minute keeps
        /// its fraction), so "make sure it shows at least 3:00" can never move the clock back.
        /// </summary>
        public void Set(int hour24, int minute)
        {
            double target = (((hour24 % 24) * 60 + minute) % (24 * 60) + (24 * 60)) % (24 * 60);
            if (target <= _minutes)
            {
                if (target < Math.Floor(_minutes))
                {
                    RefusedBackSets++;
                    GameLog.Warn(LogChannel.Story, "Clock: refused to go back to " + Format12((int)target) + " from " + Format12());
                }
                return;
            }
            _minutes = target;
            NoteReached();
        }

        /// <summary>Starts the clock at h:mm whatever it showed (a fresh shift, a restored checkpoint, a test). Not for the story.</summary>
        public void Reset(int hour24, int minute)
        {
            _minutes = (((hour24 % 24) * 60 + minute) % (24 * 60) + (24 * 60)) % (24 * 60);
            _highWater = _minutes;
            RefusedBackSets = 0;
        }

        public void Tick(float realDeltaSeconds)
        {
            if (Frozen || realDeltaSeconds <= 0f) return;
            _minutes = (_minutes + realDeltaSeconds * (double)_rate) % (24 * 60);
            NoteReached();
        }

        void NoteReached()
        {
            // The clock wraps at midnight (no night reaches it); a value far below the mark is that wrap, not a step back.
            if (_minutes > _highWater || _highWater - _minutes >= 12 * 60) _highWater = _minutes;
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
