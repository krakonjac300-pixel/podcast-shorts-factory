using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Game
{
    /// <summary>
    /// What still has to reach Steam: achievement ids and the TUG_WINS value. Steam refuses SetAchievement and SetStat
    /// until the user's stats have arrived (UserStatsReceived_t), so pushes wait here until <see cref="MarkStatsReady"/>.
    /// An id Steam refuses stays pending, and a failed StoreStats puts back everything that call sent; the next unlock or
    /// the next stats callback flushes again. Engine-free so the rules are unit-tested; SteamBridge supplies the calls.
    /// </summary>
    public sealed class AchievementPushQueue
    {
        readonly HashSet<string> _pending = new HashSet<string>();
        int _pendingTugWins = -1;

        /// <summary>Steam has delivered the stats: pushes may go out.</summary>
        public bool StatsReady { get; private set; }

        public int PendingCount => _pending.Count;
        public bool IsPending(string id) => _pending.Contains(id);

        /// <summary>The TUG_WINS value waiting to be pushed, or -1.</summary>
        public int PendingTugWins => _pendingTugWins;

        public void Queue(string id)
        {
            if (!string.IsNullOrEmpty(id)) _pending.Add(id);
        }

        /// <summary>The stat only grows: the larger of the pending and the new value is kept.</summary>
        public void QueueTugWins(int total)
        {
            if (total >= 0) _pendingTugWins = Math.Max(_pendingTugWins, total);
        }

        public void MarkStatsReady() => StatsReady = true;

        /// <summary>The result of one <see cref="Flush"/>.</summary>
        public struct FlushResult
        {
            public int Sent;
            public int Refused;
            public bool TugSent;
            public bool StoreFailed;
            public bool Skipped;
        }

        /// <summary>
        /// Pushes everything pending when the stats are ready. <paramref name="setAchievement"/> and
        /// <paramref name="setStat"/> return false when Steam refuses; <paramref name="storeStats"/> commits.
        /// </summary>
        public FlushResult Flush(Func<string, bool> setAchievement, Func<int, bool> setStat, Func<bool> storeStats)
        {
            var result = new FlushResult();
            if (!StatsReady || setAchievement == null || setStat == null || storeStats == null)
            {
                result.Skipped = true;
                return result;
            }
            var sent = new List<string>();
            foreach (var id in new List<string>(_pending))
            {
                if (setAchievement(id)) sent.Add(id);
                else result.Refused++;
            }
            foreach (var id in sent) _pending.Remove(id);
            int tug = _pendingTugWins;
            if (tug >= 0 && setStat(tug))
            {
                result.TugSent = true;
                _pendingTugWins = -1;
            }
            result.Sent = sent.Count;
            if (sent.Count == 0 && !result.TugSent) return result;
            if (storeStats()) return result;
            result.StoreFailed = true;
            foreach (var id in sent) _pending.Add(id);
            if (result.TugSent) QueueTugWins(tug);
            return result;
        }
    }
}
