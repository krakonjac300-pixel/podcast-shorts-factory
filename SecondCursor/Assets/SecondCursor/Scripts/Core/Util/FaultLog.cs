using System;
using System.Collections.Generic;

namespace SecondCursor.Core
{
    /// <summary>
    /// Where a caught exception is reported. The first time an exception (the same stage, type and message) is seen its full text goes to
    /// the log; after that only a count, at most every <see cref="SummarySeconds"/>. A step that fails every frame therefore costs a few
    /// lines in Player.log instead of a stack trace per frame. Engine-free (the log clock is <see cref="GameLog.Clock"/>).
    /// </summary>
    public static class FaultLog
    {
        public const float SummarySeconds = 30f;
        /// <summary>Distinct exceptions remembered; past it, repeats of one stage and type share an entry (a message that changes every frame).</summary>
        const int MaxSites = 64;

        sealed class Site
        {
            public int Repeats;
            public float NextSummary;
        }

        static readonly Dictionary<string, Site> Sites = new Dictionary<string, Site>();

        /// <summary>Logs <paramref name="e"/> from <paramref name="stage"/>; true when this was its first sighting (the full text was logged).</summary>
        public static bool Report(string stage, Exception e)
        {
            float now = GameLog.Clock != null ? GameLog.Clock() : 0f;
            string type = e.GetType().Name;
            var thrownAt = e.TargetSite;
            string key = stage + "|" + type + "|" + e.Message + "|" + (thrownAt != null ? thrownAt.DeclaringType + "." + thrownAt.Name : "");
            if (Sites.Count >= MaxSites && !Sites.ContainsKey(key)) key = stage + "|" + type;   // a message that changes every frame
            if (!Sites.TryGetValue(key, out var site))
            {
                Sites[key] = new Site { NextSummary = now + SummarySeconds };
                GameLog.Error(LogChannel.System, "Fault in " + stage + ": " + e);
                return true;
            }
            site.Repeats++;
            if (now >= site.NextSummary)
            {
                GameLog.Error(LogChannel.System, "Fault repeated " + site.Repeats + " times in " + stage + " (" + type + ": " + e.Message + ")");
                site.Repeats = 0;
                site.NextSummary = now + SummarySeconds;
            }
            return false;
        }

        /// <summary>A new launch forgets what it has seen (statics survive Play sessions when the domain does not reload).</summary>
        public static void Reset() => Sites.Clear();
    }
}
