using System.Collections.Generic;
using SecondCursor.Core.Game;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>Phase G: Steam pushes wait for the user's stats, and anything Steam refuses stays pending.</summary>
    public class AchievementPushQueueTests
    {
        sealed class FakeSteam
        {
            public readonly List<string> Set = new List<string>();
            public readonly HashSet<string> Refuse = new HashSet<string>();
            public int Stat = -1;
            public bool RefuseStat;
            public bool StoreOk = true;
            public int Stores;

            public AchievementPushQueue.FlushResult Flush(AchievementPushQueue q) =>
                q.Flush(id =>
                {
                    if (Refuse.Contains(id)) return false;
                    Set.Add(id);
                    return true;
                }, v =>
                {
                    if (RefuseStat) return false;
                    Stat = v;
                    return true;
                }, () =>
                {
                    Stores++;
                    return StoreOk;
                });
        }

        [Fact]
        public void Nothing_is_pushed_before_the_stats_arrive()
        {
            var q = new AchievementPushQueue();
            var steam = new FakeSteam();
            q.Queue(AchievementIds.Night1);
            q.QueueTugWins(3);
            var r = steam.Flush(q);
            Assert.True(r.Skipped);
            Assert.Empty(steam.Set);
            Assert.Equal(0, steam.Stores);
            Assert.True(q.IsPending(AchievementIds.Night1));
            Assert.Equal(3, q.PendingTugWins);
        }

        [Fact]
        public void Queued_pushes_go_out_once_the_stats_are_ready()
        {
            var q = new AchievementPushQueue();
            var steam = new FakeSteam();
            q.Queue(AchievementIds.Night1);
            q.Queue(AchievementIds.FirmGrip);
            q.QueueTugWins(4);
            q.MarkStatsReady();
            var r = steam.Flush(q);
            Assert.Equal(2, r.Sent);
            Assert.True(r.TugSent);
            Assert.Equal(1, steam.Stores);
            Assert.Equal(4, steam.Stat);
            Assert.Equal(0, q.PendingCount);
            Assert.Equal(-1, q.PendingTugWins);
        }

        [Fact]
        public void A_refused_achievement_stays_pending_and_is_retried()
        {
            var q = new AchievementPushQueue();
            var steam = new FakeSteam();
            q.MarkStatsReady();
            steam.Refuse.Add(AchievementIds.Night2);
            q.Queue(AchievementIds.Night2);
            q.Queue(AchievementIds.Night1);
            var r = steam.Flush(q);
            Assert.Equal(1, r.Refused);
            Assert.True(q.IsPending(AchievementIds.Night2));
            Assert.False(q.IsPending(AchievementIds.Night1));
            steam.Refuse.Clear();
            steam.Flush(q);
            Assert.Contains(AchievementIds.Night2, steam.Set);
            Assert.Equal(0, q.PendingCount);
        }

        [Fact]
        public void A_failed_StoreStats_puts_everything_it_sent_back()
        {
            var q = new AchievementPushQueue();
            var steam = new FakeSteam { StoreOk = false };
            q.MarkStatsReady();
            q.Queue(AchievementIds.Night3);
            q.QueueTugWins(7);
            var r = steam.Flush(q);
            Assert.True(r.StoreFailed);
            Assert.True(q.IsPending(AchievementIds.Night3));
            Assert.Equal(7, q.PendingTugWins);
            steam.StoreOk = true;
            r = steam.Flush(q);
            Assert.False(r.StoreFailed);
            Assert.Equal(0, q.PendingCount);
            Assert.Equal(-1, q.PendingTugWins);
        }

        [Fact]
        public void The_tug_stat_only_grows_and_a_refused_stat_stays_pending()
        {
            var q = new AchievementPushQueue();
            var steam = new FakeSteam { RefuseStat = true };
            q.QueueTugWins(6);
            q.QueueTugWins(2);
            Assert.Equal(6, q.PendingTugWins);
            q.MarkStatsReady();
            var r = steam.Flush(q);
            Assert.False(r.TugSent);
            Assert.Equal(0, steam.Stores); // nothing was set, so nothing is stored
            Assert.Equal(6, q.PendingTugWins);
            steam.RefuseStat = false;
            steam.Flush(q);
            Assert.Equal(6, steam.Stat);
        }

        [Fact]
        public void Empty_and_duplicate_ids_are_ignored()
        {
            var q = new AchievementPushQueue();
            q.Queue(null);
            q.Queue("");
            q.Queue(AchievementIds.Half);
            q.Queue(AchievementIds.Half);
            Assert.Equal(1, q.PendingCount);
        }
    }
}
