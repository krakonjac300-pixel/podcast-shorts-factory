using System;
using System.Collections.Generic;
using System.Globalization;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase Q2 (board C V5, A T2): what one night really measured about the player. Saved per night (additive, so older saves
    /// load with nothing recorded); New Game starts a new run. Only measured values are ever shown: anything not measured (a
    /// file finished by Night Operations, a jump past the moment) is shown as not on record, never as a made-up number.
    /// </summary>
    [Serializable]
    public class CaptureStats
    {
        /// <summary>A night that finished wrote these (false = nothing recorded for that night).</summary>
        public bool recorded;
        /// <summary>Seconds the night's first archive drag took, from the pick-up to the drop (-1: none measured).</summary>
        public float dragSeconds = -1f;
        /// <summary>The longest stillness during that drag, in seconds (-1: none measured).</summary>
        public float longestPause = -1f;
        /// <summary>The name of the file that drag carried ("" = none measured).</summary>
        public string dragFile = "";
        /// <summary>Times the player opened the night's briefing mail.</summary>
        public int mailReads;
        /// <summary>Seconds from a shred confirm opening to the player's Yes, added up, and how many there were.</summary>
        public float yesSeconds;
        public int yesCount;
        /// <summary>Times CAM 03 came on screen, and the seconds it was on screen.</summary>
        public int camLooks;
        public float camSeconds;
        /// <summary>Phase R: how many of those looks the player brought about themselves (opened the viewer, restored it or switched to CAM 03); the story opens it too.</summary>
        public int camOpened;
        public int tugWins, tugLosses;
        /// <summary>Lines the player sent to any remote session, and words sent to session 017.</summary>
        public int lines, words017;
        /// <summary>The first line of the night, as typed (sanitized; filtered when shown).</summary>
        public string firstLine = "";
        /// <summary>Seconds the night was played.</summary>
        public float seconds;

        /// <summary>Average seconds before Yes (-1: never asked).</summary>
        public float YesAverage => yesCount > 0 ? yesSeconds / yesCount : -1f;

        public CaptureStats Copy() => (CaptureStats)MemberwiseClone();
    }

    /// <summary>
    /// One drag measured: its length and the longest stretch the hand held still (slower than <see cref="StillSpeed"/>) while
    /// carrying the file. Fed with the pointer every frame between <see cref="Begin"/> and <see cref="End"/>.
    /// </summary>
    public sealed class DragMeter
    {
        /// <summary>Below this many pixels a second the hand counts as still.</summary>
        public const float StillSpeed = 15f;

        float _start = -1f, _lastT, _lastX, _lastY, _still, _longest;

        public bool Running => _start >= 0f;

        public void Begin(float t, float x, float y)
        {
            _start = t;
            _lastT = t;
            _lastX = x;
            _lastY = y;
            _still = 0f;
            _longest = 0f;
        }

        public void Sample(float t, float x, float y)
        {
            if (_start < 0f) return;
            float dt = t - _lastT;
            if (dt <= 0f) return;
            float dx = x - _lastX, dy = y - _lastY;
            float speed = (float)Math.Sqrt(dx * dx + dy * dy) / dt;
            if (speed < StillSpeed)
            {
                _still += dt;
                if (_still > _longest) _longest = _still;
            }
            else
            {
                _still = 0f;
            }
            _lastT = t;
            _lastX = x;
            _lastY = y;
        }

        /// <summary>Stops: the drag's seconds and its longest pause; (-1, -1) if it never began.</summary>
        public (float seconds, float longestPause) End(float t)
        {
            if (_start < 0f) return (-1f, -1f);
            var r = (Math.Max(0f, t - _start), _longest);
            _start = -1f;
            return r;
        }

        public void Cancel() => _start = -1f;
    }

    /// <summary>
    /// Capture Profile 214: the percentage the fiction shows after each night (matching Personnel and nexus.cfg: 88% after
    /// Night 1, 96% after Night 2 or 88% with the source hidden in Archive, 100% only when SHRED completed it), and the rows of
    /// Night 2's employee_214.dat built from Night 1's measurements.
    /// </summary>
    public static class CaptureProfile
    {
        public const int Segments = 4000;

        /// <summary>The profile after <paramref name="nightsDone"/> nights of this run (0 = nothing yet).</summary>
        public static int Percent(int nightsDone, bool hid214, string endingId)
        {
            if (nightsDone <= 0) return 0;
            if (nightsDone == 1) return 88;
            if (nightsDone >= 3 && endingId == "n3_shred") return 100;
            return hid214 ? 88 : 96;
        }

        public static int SegmentsFor(int percent) => Math.Max(0, Math.Min(Segments, percent * Segments / 100));

        public static string Seconds(float s) => s.ToString("0.0", CultureInfo.InvariantCulture);

        static bool Has(CaptureStats s) => s != null && s.recorded;

        public static string DragRow(CaptureStats s) =>
            Has(s) && s.dragSeconds >= 0f
                ? ".." + (string.IsNullOrEmpty(s.dragFile) ? "ledger_1994.dat" : s.dragFile) + "..drag.." + Seconds(s.dragSeconds) + "s..hesitation " + Seconds(Math.Max(0f, s.longestPause)) + "s.."
                : "..ledger_1994.dat..no drag on record..";

        public static string MailRow(CaptureStats s)
        {
            int n = Has(s) ? s.mailReads : 0;
            if (n <= 0) return "..%%..mail_welcome..never opened..";
            return "..%%..read mail_welcome..read it " + Times(n) + "..";
        }

        public static string YesRow(CaptureStats s) =>
            !Has(s) || s.yesCount <= 0 ? "..never reached for Yes.."
            : "..reached for Yes.." + Seconds(s.YesAverage) + "s.." + (s.yesCount >= 2 ? "reached again.." : "");

        public static string CamRow(CaptureStats s)
        {
            int n = Has(s) ? s.camLooks : 0;
            return n <= 0 ? "..camera 03..never looked..#.." : "..camera 03..looked " + Times(n) + "..#..";
        }

        /// <summary>"once", "twice", "3 times".</summary>
        public static string Times(int n) => n == 1 ? "once" : n == 2 ? "twice" : n + " times";

        /// <summary>Words in a line (runs of non-space characters).</summary>
        public static int Words(string line)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int n = 0;
            bool inWord = false;
            foreach (char c in line)
            {
                bool space = char.IsWhiteSpace(c);
                if (!space && !inWord) n++;
                inWord = !space;
            }
            return n;
        }

        /// <summary>The run's first typed line: the earliest night that has one.</summary>
        public static string FirstLine(IList<CaptureStats> nights)
        {
            if (nights == null) return "";
            foreach (var s in nights)
                if (Has(s) && !string.IsNullOrEmpty(s.firstLine)) return s.firstLine;
            return "";
        }
    }
}
