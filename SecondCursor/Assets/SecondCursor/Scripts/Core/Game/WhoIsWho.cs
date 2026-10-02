using System;
using System.Globalization;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase R (sixth blind playtest: "which pointer is mine, who is playing, who won"): the engine-free rules behind the pointer tags,
    /// the confirm race's countdown and the contest cards. The runtime draws them; nothing here changes a fight.
    /// </summary>
    public static class PointerTagRules
    {
        /// <summary>The YOU tag on the player's pointer: the first seconds of a shift, and a short reminder when a second pointer first shows.</summary>
        public const float YouFirstSeconds = 30f, YouAgainSeconds = 6f, FadeSeconds = 1.5f;

        /// <summary>Session numbers worn by the tags (Gary's is a Night 2 and 3 string: the tag text comes from content).</summary>
        public const string EntityNumber = "017", GaryNumber = "209";

        /// <summary>The YOU tag's opacity with <paramref name="left"/> seconds still to show: full, fading over the last <see cref="FadeSeconds"/>.</summary>
        public static float YouAlpha(float left)
        {
            if (left <= 0f) return 0f;
            return left >= FadeSeconds ? 1f : left / FadeSeconds;
        }

        /// <summary>A tag asked for <paramref name="seconds"/> never shortens one already running.</summary>
        public static float Extend(float left, float seconds) => Math.Max(left, seconds);

        /// <summary>Whether a second pointer's first appearance in the night should bring the YOU tag back: once per pointer per night.</summary>
        public static bool IsFirstAppearance(bool visibleNow, ref bool seenBefore)
        {
            if (!visibleNow || seenBefore) return false;
            seenBefore = true;
            return true;
        }
    }

    /// <summary>The numeric countdown beside the confirm race's bar: when the racing pointer is expected to reach No.</summary>
    public static class RaceCountdown
    {
        /// <summary>How fast her pointer crosses the screen in the Aggressive profile (px/s), and the least time a move takes.</summary>
        public const float TravelSpeed = 2400f, MinTravel = 0.07f;

        /// <summary>
        /// Seconds until the click on No: what is left of her reaction delay, plus the travel from where she is
        /// (<paramref name="distance"/> px from the button; 0 once she is on it).
        /// </summary>
        public static float Eta(float delayLeft, float distance)
        {
            float travel = distance <= 1f ? 0f : Math.Max(MinTravel, distance / TravelSpeed);
            return Math.Max(0f, delayLeft) + travel;
        }

        /// <summary>"0.8S": one decimal, never negative, whole seconds under 10 keep their decimal.</summary>
        public static string Format(float seconds) => Math.Max(0f, seconds).ToString("0.0", CultureInfo.InvariantCulture) + "S";
    }

    /// <summary>Which sentence a contest's WON or LOST card says (the strings are content: <c>contest.card.*</c>) and how long it stays.</summary>
    public static class ContestCopy
    {
        /// <summary>A card stays at least this long; a long sentence stays a little longer (about 28 characters a second).</summary>
        public const float CardMinSeconds = 2.5f, CardSecondsPerChar = 0.035f;

        public static float CardSeconds(int sentenceLength) => Math.Max(CardMinSeconds, CardSecondsPerChar * Math.Max(0, sentenceLength));

        /// <summary>The tug's card: lost, won into the bin, won and kept where it was let go, or won and torn loose (drop it somewhere).</summary>
        public static string TugKey(bool won, bool intoBin, bool keptOnRelease)
        {
            if (!won) return "contest.card.tug.lost";
            if (intoBin) return "contest.card.tug.bin";
            return keptOnRelease ? "contest.card.tug.kept" : "contest.card.tug.tear";
        }

        /// <summary>The raced shred's card: the player's shred went through, or another session cancelled it at the confirm (No) or during the shred (Cancel).</summary>
        public static string ShredKey(bool completed, bool atConfirm) =>
            completed ? "contest.card.race.won" : atConfirm ? "contest.card.race.no" : "contest.card.race.cancel";

        /// <summary>The strip's text key for a kind of contest.</summary>
        public const string StripTug = "contest.strip.tug", StripRace = "contest.strip.race", StripCancel = "contest.strip.cancel";
    }
}
