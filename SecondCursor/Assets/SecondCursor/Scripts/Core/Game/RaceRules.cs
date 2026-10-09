using System;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase S (eleven blind testers, the shred race again): the rules that make a raced confirm and a raced Cancel fair to a normal
    /// human. Engine-free numbers used by the brain, Gary and the shred service: the least time a pointer needs to cross to a button, the
    /// slack a covered button gives back, how Relaxed timing and Story stretch the Cancel phase, and how many races Night 1 lets the
    /// player lose to a file the story will not let go.
    /// </summary>
    public static class RaceRules
    {
        /// <summary>A human's reaction before the pointer starts moving (s).</summary>
        public const float ReactionSeconds = 0.30f;
        /// <summary>How fast a normal pointer crosses to a button (px/s). Relaxed timing halves it (a tremor, a trackpad, a Deck's stick).</summary>
        public const float CrossSpeed = 700f;
        /// <summary>The fair head start never exceeds this (s), so a far corner cannot hold the race forever.</summary>
        public const float MaxReachSeconds = 3.2f;
        /// <summary>When a covered Yes is uncovered, the other pointer waits this long before it goes for No (s).</summary>
        public const float UncoverGraceSeconds = 0.9f;
        /// <summary>After the other pointer drags the dialog away it waits this long before it goes for No (s).</summary>
        public const float DragGraceSeconds = 0.7f;
        /// <summary>Relaxed timing or Story never shortens the Cancel fight below this (s).</summary>
        public const float MinCancelPatience = 3f;
        /// <summary>Extra seconds before Cancel is pressed per unit of scale above 1.</summary>
        public const float CancelDelayPerUnit = 0.4f;
        /// <summary>Night 1: the races for employee_017.dat the player may lose before session 017 takes the night over.</summary>
        public const int Night1ShredLossLimit = 2;

        /// <summary>Seconds a normal human needs to reach a button <paramref name="distancePx"/> away (reaction plus the trip), longer when relaxed.</summary>
        public static float ReachSeconds(float distancePx, float scale)
        {
            float s = Math.Max(1f, scale);
            float t = (ReactionSeconds + Math.Max(0f, distancePx) / CrossSpeed) * s;
            return Math.Min(MaxReachSeconds * s, t);
        }

        /// <summary>The seconds from the dialog opening before the other pointer may click: its own delay, but never before the player could get there.</summary>
        public static float FairDelay(float profileDelay, float distancePx, float scale) =>
            Math.Max(profileDelay, ReachSeconds(distancePx, scale));

        /// <summary>Extra seconds before the other pointer presses Cancel when relaxed (Story is relaxed too).</summary>
        public static float CancelDelayAdd(float scale) => CancelDelayPerUnit * (Math.Max(1f, scale) - 1f);

        /// <summary>How long the other pointer fights over Cancel before it gives up: shorter when relaxed (holding the button down is the counterplay).</summary>
        public static float CancelPatience(float seconds, float scale)
        {
            if (scale <= 1f) return seconds;
            return Math.Max(Math.Min(seconds, MinCancelPatience), seconds / scale);
        }

        /// <summary>Night 1's file is held by the story: after this many lost races the fight is over and the order says so.</summary>
        public static bool Night1FightOver(int racesLost) => racesLost >= Night1ShredLossLimit;

        /// <summary>Which sentence a lost race says on Night 1: the first loss says plainly the file may not be shreddable, the last says to stop.</summary>
        public static string Night1LossKey(bool atConfirm, int racesLost) =>
            Night1FightOver(racesLost) ? "contest.card.n1.last" : atConfirm ? "contest.card.n1.first.no" : "contest.card.n1.first.cancel";
    }
}
