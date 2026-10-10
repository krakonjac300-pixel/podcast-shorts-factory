using System.Collections.Generic;
using System.Linq;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>The 19 achievement rules of expansion spec Section 9: one case that unlocks and one that must not.</summary>
    public class AchievementRulesTests
    {
        [Fact]
        public void ThereAreNineteenUniqueAchievements()
        {
            Assert.Equal(19, AchievementIds.All.Length);
            Assert.Equal(19, AchievementIds.All.Select(a => a.Id).Distinct().Count());
            Assert.All(AchievementIds.All, a => Assert.StartsWith("ACH_", a.Id));
            Assert.True(AchievementIds.IsHidden(AchievementIds.Half));
            Assert.False(AchievementIds.IsHidden(AchievementIds.Night1));
            Assert.False(AchievementIds.IsHidden(AchievementIds.RemainSeated));
            Assert.Equal(12, AchievementIds.All.Count(a => a.Hidden));
            Assert.Equal(2, AchievementIds.CountUnlocked(new[] { "ACH_NIGHT_1", "ACH_FINISHED", "SC_OLD" }));
        }

        [Fact]
        public void NightsUnlockTheirOwnAchievement()
        {
            Assert.Equal(new[] { AchievementIds.Night1 }, AchievementRules.OnNightComplete(1, "n1_blackout", new[] { "n1_blackout" }));
            Assert.Equal(new[] { AchievementIds.Night2 }, AchievementRules.OnNightComplete(2, "n2_kept", new[] { "n2_kept" }));
            Assert.DoesNotContain(AchievementIds.Night2, AchievementRules.OnNightComplete(1, "n1_blackout", new[] { "n1_blackout" }));
        }

        [Fact]
        public void Night3EndingsUnlockTheirAchievements()
        {
            var shred = AchievementRules.OnNightComplete(3, "n3_shred", new[] { "n3_shred" });
            Assert.Contains(AchievementIds.Night3, shred);
            Assert.Contains(AchievementIds.EndShred, shred);
            Assert.DoesNotContain(AchievementIds.EndKeep, shred);
            Assert.Contains(AchievementIds.EndKeep, AchievementRules.OnNightComplete(3, "n3_keep", new[] { "n3_keep" }));
            Assert.Contains(AchievementIds.EndLogOff, AchievementRules.OnNightComplete(3, "n3_logoff", new[] { "n3_logoff" }));
            Assert.DoesNotContain(AchievementIds.AllEndings, shred);
        }

        [Fact]
        public void EveryWayOutNeedsAllThreeEndings()
        {
            var all = AchievementRules.OnNightComplete(3, "n3_keep", new[] { "n1_blackout", "n3_shred", "n3_logoff", "n3_keep" });
            Assert.Contains(AchievementIds.AllEndings, all);
            var two = AchievementRules.OnNightComplete(3, "n3_keep", new[] { "n3_shred", "n3_keep" });
            Assert.DoesNotContain(AchievementIds.AllEndings, two);
        }

        [Fact]
        public void FirmGripNeedsARealWin()
        {
            Assert.Equal(AchievementIds.FirmGrip, AchievementRules.OnTugWon(false));
            Assert.Null(AchievementRules.OnTugWon(true));
        }

        [Fact]
        public void WhiteKnucklesNeedsTenWins()
        {
            Assert.Equal(AchievementIds.WhiteKnuckles, AchievementRules.OnTugTotal(10));
            Assert.Equal(AchievementIds.WhiteKnuckles, AchievementRules.OnTugTotal(11));
            Assert.Null(AchievementRules.OnTugTotal(9));
        }

        [Fact]
        public void DoNotReadCountsOnlyThePlayersOpen()
        {
            Assert.Equal(AchievementIds.DoNotRead, AchievementRules.OnCounter("opened_by_player:employee_017", 1));
            Assert.Null(AchievementRules.OnCounter("opened:employee_017", 1));
            Assert.Null(AchievementRules.OnCounter("opened_by_player:employee_209", 1));
        }

        [Fact]
        public void RemoteSessionNeedsAllThreeAsks()
        {
            Assert.Equal(AchievementIds.RemoteSession, AchievementRules.OnCounter(MemoryFlags.N2Obeyed, 3));
            Assert.Null(AchievementRules.OnCounter(MemoryFlags.N2Obeyed, 2));
        }

        [Fact]
        public void GaryOutcomesUnlockFinishedOrHalf()
        {
            Assert.Equal(AchievementIds.Finished, AchievementRules.OnFlag(MemoryFlags.N2FinishedGary));
            Assert.Equal(AchievementIds.Half, AchievementRules.OnFlag(MemoryFlags.N2KeptGary));
            Assert.Null(AchievementRules.OnFlag(MemoryFlags.N2ArchivedGary));
        }

        [Fact]
        public void HisGlassesIsAskedOfGary()
        {
            Assert.Equal(AchievementIds.HisGlasses, AchievementRules.OnReply("gary", "glasses"));
            Assert.Null(AchievementRules.OnReply("", "glasses"));
        }

        [Fact]
        public void HerNameIsSaidToEllen()
        {
            Assert.Equal(AchievementIds.HerName, AchievementRules.OnReply("", "name"));
            Assert.Equal(AchievementIds.HerName, AchievementRules.OnReply(null, "name"));
            Assert.Null(AchievementRules.OnReply("gary", "name"));
            Assert.Null(AchievementRules.OnReply("", "stay"));
        }

        [Fact]
        public void AuthorizedFollowsTheCodePromptFlag()
        {
            Assert.Equal(AchievementIds.Authorized, AchievementRules.OnFlag(MemoryFlags.N3RestrictedOpen));
            Assert.Null(AchievementRules.OnFlag(MemoryFlags.N3GaryEnabledLogoff));
        }

        [Fact]
        public void RemainSeatedNeedsStageOneOrLess()
        {
            Assert.Equal(AchievementIds.RemainSeated, AchievementRules.OnRoundsSafe(3, 1));
            Assert.Equal(AchievementIds.RemainSeated, AchievementRules.OnRoundsSafe(3, 0));
            Assert.Null(AchievementRules.OnRoundsSafe(3, 2));
            Assert.Null(AchievementRules.OnRoundsSafe(2, 0));
        }

        [Fact]
        public void NotOnMyShelfIsThePlayersReject()
        {
            Assert.Equal(AchievementIds.NotOnMyShelf, AchievementRules.OnOrderDecided("wo_3342", "reject", true));
            Assert.Null(AchievementRules.OnOrderDecided("wo_3342", "approve", true));
            Assert.Null(AchievementRules.OnOrderDecided("wo_3342", "reject", false));
            Assert.Null(AchievementRules.OnOrderDecided("wo_3341", "reject", true));
        }

        [Fact]
        public void WatchersIsThePlayerSelectingCam00()
        {
            Assert.Equal(AchievementIds.Watchers, AchievementRules.OnCameraSelected("cam00", true));
            Assert.Null(AchievementRules.OnCameraSelected("cam00", false));
            Assert.Null(AchievementRules.OnCameraSelected("cam03", true));
        }

        [Fact]
        public void RecordsRebuildOnlyWhatTheyProve()
        {
            var d = new SaveData { endingsSeen = new[] { "n1_blackout", "n2_kept", "n3_shred", "n3_keep", "n3_logoff" }, tugWinsTotal = 10 };
            var r = AchievementRules.FromRecords(d);
            foreach (var id in new[] { AchievementIds.Night1, AchievementIds.Night2, AchievementIds.Night3, AchievementIds.EndShred, AchievementIds.EndKeep,
                         AchievementIds.EndLogOff, AchievementIds.AllEndings, AchievementIds.FirmGrip, AchievementIds.WhiteKnuckles })
                Assert.Contains(id, r);
            Assert.Equal(r.Count, r.Distinct().Count());
            Assert.DoesNotContain(AchievementIds.Half, r);

            var fresh = AchievementRules.FromRecords(new SaveData { endingsSeen = new[] { "n1_blackout" } });
            Assert.Equal(new List<string> { AchievementIds.Night1 }, fresh);
            Assert.Empty(AchievementRules.FromRecords(new SaveData()));
            Assert.Empty(AchievementRules.FromRecords(null));
        }
    }
}
