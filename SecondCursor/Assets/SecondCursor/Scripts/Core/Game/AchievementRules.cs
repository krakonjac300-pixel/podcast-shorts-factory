using System;
using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Core.Story;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// When each of the 19 achievements unlocks (expansion spec Section 9), as pure functions of what just happened.
    /// Each returns the achievement id(s) earned, or null / nothing. Whether the run counts at all (armed, not a
    /// Prepare, not a forced outcome) is decided by the caller: these rules only read the event.
    /// </summary>
    public static class AchievementRules
    {
        /// <summary>The player-only open counter of employee_017.dat (Do Not Read).</summary>
        public const string Opened017Counter = Flags.OpenedByPlayerPrefix + ContentIds.File017;
        /// <summary>Remote Session: every one of Ellen's three Night 2 asks done.</summary>
        public const int RemoteSessionAsks = 3;
        /// <summary>Remain Seated: the rounds' highest stage (the start stage included) may be at most this.</summary>
        public const int RemainSeatedMaxStage = 1;

        /// <summary>A night was completed with this ending; <paramref name="endingsSeen"/> already includes it.</summary>
        public static List<string> OnNightComplete(int night, string endingId, string[] endingsSeen)
        {
            var r = new List<string>();
            if (night == 1) r.Add(AchievementIds.Night1);
            else if (night == 2) r.Add(AchievementIds.Night2);
            else if (night == 3) r.Add(AchievementIds.Night3);
            string ending = ForEnding(endingId);
            if (ending != null) r.Add(ending);
            if (night == 3 && HasAllNight3Endings(endingsSeen)) r.Add(AchievementIds.AllEndings);
            return r;
        }

        /// <summary>The Night 3 ending achievement for an ending id, or null.</summary>
        public static string ForEnding(string endingId)
        {
            switch (endingId)
            {
                case ContentIds.EndingN3Shred: return AchievementIds.EndShred;
                case ContentIds.EndingN3Keep: return AchievementIds.EndKeep;
                case ContentIds.EndingN3LogOff: return AchievementIds.EndLogOff;
                default: return null;
            }
        }

        public static bool HasAllNight3Endings(string[] endingsSeen)
        {
            if (endingsSeen == null) return false;
            foreach (var id in AchievementIds.Night3Endings)
                if (Array.IndexOf(endingsSeen, id) < 0) return false;
            return true;
        }

        /// <summary>The player won a tug-of-war. A debug-forced outcome never counts (a mercy win does).</summary>
        public static string OnTugWon(bool forced) => forced ? null : AchievementIds.FirmGrip;

        /// <summary>Lifetime tug wins after the latest one.</summary>
        public static string OnTugTotal(int wins) => wins >= AchievementIds.TugWinsGoal ? AchievementIds.WhiteKnuckles : null;

        /// <summary>A narrative counter changed.</summary>
        public static string OnCounter(string key, int value)
        {
            if (key == Opened017Counter && value >= 1) return AchievementIds.DoNotRead;
            if (key == MemoryFlags.N2Obeyed && value >= RemoteSessionAsks) return AchievementIds.RemoteSession;
            return null;
        }

        /// <summary>A narrative flag was set live (restores and memory merges raise no events).</summary>
        public static string OnFlag(string flag)
        {
            switch (flag)
            {
                case MemoryFlags.N2FinishedGary: return AchievementIds.Finished;
                case MemoryFlags.N2KeptGary: return AchievementIds.Half;
                // Set only by the code prompt's success path for the player (never by Gary's 6:48 unlock).
                case MemoryFlags.N3RestrictedOpen: return AchievementIds.Authorized;
                default: return null;
            }
        }

        /// <summary>The player's typed reply in an exchange: <paramref name="exchangeVoice"/> is "" for Ellen, "gary" for Gary.</summary>
        public static string OnReply(string exchangeVoice, string tag)
        {
            string voice = exchangeVoice ?? "";
            if (tag == "glasses" && voice == "gary") return AchievementIds.HisGlasses;
            if (tag == "name" && voice.Length == 0) return AchievementIds.HerName;
            return null;
        }

        /// <summary>A work order was decided. Only the player refusing to confirm their own shelf counts.</summary>
        public static string OnOrderDecided(string orderId, string decision, bool byPlayer) =>
            byPlayer && orderId == ContentIds.Order3342 && decision == "reject" ? AchievementIds.NotOnMyShelf : null;

        /// <summary>A camera was selected in the viewer (forced opens and cursors other than the player never count).</summary>
        public static string OnCameraSelected(string cameraId, bool byPlayer) =>
            byPlayer && cameraId == ContentIds.Cam00 ? AchievementIds.Watchers : null;

        /// <summary>Night 3's Custodial round ended safe (the seat was never cleared) with this highest stage.</summary>
        public static string OnRoundsSafe(int night, int maxStage) =>
            night == 3 && maxStage <= RemainSeatedMaxStage ? AchievementIds.RemainSeated : null;

        /// <summary>
        /// What the saved records prove (boot reconcile): endings seen and lifetime tug wins. Both only grow in runs
        /// that count, so rebuilding these achievements from them is safe.
        /// </summary>
        public static List<string> FromRecords(SaveData d)
        {
            var r = new List<string>();
            if (d == null) return r;
            void Add(string id)
            {
                if (id != null && !r.Contains(id)) r.Add(id);
            }
            foreach (var e in d.endingsSeen ?? Array.Empty<string>())
            {
                if (e == null) continue;
                if (e.StartsWith("n1_", StringComparison.Ordinal)) Add(AchievementIds.Night1);
                else if (e.StartsWith("n2_", StringComparison.Ordinal)) Add(AchievementIds.Night2);
                else if (e.StartsWith("n3_", StringComparison.Ordinal)) Add(AchievementIds.Night3);
                Add(ForEnding(e));
            }
            if (HasAllNight3Endings(d.endingsSeen)) Add(AchievementIds.AllEndings);
            if (d.tugWinsTotal >= 1) Add(AchievementIds.FirmGrip);
            Add(OnTugTotal(d.tugWinsTotal));
            return r;
        }
    }
}
