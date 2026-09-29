using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Game;

namespace SecondCursor.Game
{
    /// <summary>
    /// Achievements: stored in the save file, and mirrored to Steam when <see cref="SteamBridge"/> is live. The game
    /// never depends on Steam: without it everything still unlocks locally. Every unlock goes through one gate: the
    /// run must count (<see cref="GameServices.RecordsArmed"/>) and the director must not be preparing a jump, so a
    /// checkpoint restore, a debug jump or a forced tug can never unlock anything. There is no in-game toast (it would
    /// break the fiction); the Steam overlay shows its own, and Records lists them.
    /// </summary>
    public static class Achievements
    {
        /// <summary>Raised once per achievement, the first time it unlocks.</summary>
        public static event Action<string> Unlocked;

        public static bool Has(string id) => Array.IndexOf(SaveSystem.Load().achievements, id) >= 0;

        /// <summary>Unlock for the running game (gated like every other unlock).</summary>
        public static void Unlock(string id) => Unlock(GameRoot.Instance != null ? GameRoot.Instance.G : null, id);

        /// <summary>Unlocks <paramref name="id"/> if this run counts; returns true if it was new.</summary>
        public static bool Unlock(GameServices g, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string held = HeldReason(g);
            if (held != null)
            {
                GameLog.Info(LogChannel.System, "Achievement held (" + held + "): " + id);
                return false;
            }
            return Grant(id, "");
        }

        static string HeldReason(GameServices g)
        {
            if (g == null) return "no game running";
            if (g.Director != null && g.Director.IsPreparing) return "preparing a jump";
            return g.RecordsArmed ? null : g.RecordsHeldReason;
        }

        static bool Grant(string id, string how)
        {
            var data = SaveSystem.Load();
            if (Array.IndexOf(data.achievements, id) >= 0)
            {
                // Already saved (Steam got it then, or at the next boot's resync): nothing to push.
                GameLog.Info(LogChannel.System, "Achievement already unlocked: " + id);
                return false;
            }
            var list = new List<string>(data.achievements) { id };
            data.achievements = list.ToArray();
            SaveSystem.Save(data);
            GameLog.Info(LogChannel.System, "Achievement unlocked: " + id + how);
            SteamBridge.Unlock(id);
            Unlocked?.Invoke(id);
            return true;
        }

        /// <summary>
        /// Boot: rebuild the achievements the saved records prove (endings seen, lifetime tug wins). Those records
        /// only grow in runs that count, so this is not gated.
        /// </summary>
        public static void Reconcile(GameServices g)
        {
            var data = SaveSystem.Load();
            foreach (var id in AchievementRules.FromRecords(data))
                if (Array.IndexOf(data.achievements, id) < 0) Grant(id, " (from records)");
        }
    }
}
