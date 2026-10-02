using System;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase Q4 (review board A4): "Relaxed timing". The windows the story runs in real time (Night 2's priority shred, the last five
    /// minutes before 7:05 on Night 3, the grace after a log off or shred has started, the confirm race's head start and the Custodial
    /// reaction) are twice as long. It is on in Story mode and a setting for Normal; content is untouched and the dread stays: before 7:00
    /// every clock rate is as it was. Engine-free multipliers, used at the few places the story reads a real-time window.
    /// </summary>
    public static class RelaxedTiming
    {
        /// <summary>How many times longer a real-time window is when relaxed.</summary>
        public const float RelaxedScale = 2f;
        /// <summary>The confirm race gives the player this much extra head start per unit of scale above 1 (a scale of 2 adds 0.4 s).</summary>
        public const float RaceDelayPerUnit = 0.4f;
        /// <summary>The Custodial's reaction to the viewer showing it is this much slower when relaxed.</summary>
        public const float CloseReactionFactor = 1.5f;

        /// <summary>1 for the normal clock, 2 when relaxed (Story mode, or the setting).</summary>
        public static float Scale(bool story, bool relaxedSetting) => story || relaxedSetting ? RelaxedScale : 1f;

        /// <summary>A real-time deadline in seconds (Night 2's 150 s priority shred, the exit grace).</summary>
        public static float Seconds(float seconds, float scale) => seconds * Math.Max(1f, scale);

        /// <summary>The shift clock's rate after 7:00 (game minutes per real second): a slower clock makes 7:00 to 7:05 last longer.</summary>
        public static float Rate(float rate, float scale) => rate / Math.Max(1f, scale);

        /// <summary>Extra seconds before session 017 or Gary goes for No in a confirm race.</summary>
        public static float RaceDelayAdd(float scale) => RaceDelayPerUnit * (Math.Max(1f, scale) - 1f);

        /// <summary>The Custodial's reaction time, slower when relaxed.</summary>
        public static float CloseReaction(float seconds, float scale) => scale > 1f ? seconds * CloseReactionFactor : seconds;

        /// <summary>The settings.json spelling.</summary>
        public static bool Parse(string stored) => stored == "on";
        public static string Id(bool on) => on ? "on" : "off";
    }
}
