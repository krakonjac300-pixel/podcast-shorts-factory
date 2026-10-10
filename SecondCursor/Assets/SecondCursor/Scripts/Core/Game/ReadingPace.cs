using System;

namespace SecondCursor.Core.Game
{
    /// <summary>Reading and decision windows measured from the moment a request is ready for the player.</summary>
    public static class ReadingPace
    {
        public const float MinimumReplySeconds = 25f;
        public const float RuthDecisionSeconds = 150f;

        public static float ReplySeconds(float authoredSeconds, float scale) =>
            RelaxedTiming.Seconds(Math.Max(MinimumReplySeconds, authoredSeconds), scale);

        public static float TaskSeconds(float authoredSeconds, float scale) =>
            RelaxedTiming.Seconds(authoredSeconds, scale);

        public static float Remaining(float now, float startedAt, float seconds) =>
            Math.Max(0f, seconds - Math.Max(0f, now - startedAt));
    }
}
