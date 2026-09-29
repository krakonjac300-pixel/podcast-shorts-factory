using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Expansion spec 7.7: simulates the tug-of-war at 60 Hz with the runtime's drift model (the entity's end
    /// moves away at <see cref="TugOfWar.EntityDriftSpeed"/>) and a player yanking straight away at a
    /// constant speed, for every night and assist level.
    /// </summary>
    public class DifficultyCurveTests
    {
        const float Dt = 1f / 60f;

        /// <summary>
        /// Section 7.7's minimum sustained yank (px/s) to win the first contest of a night, by assist level
        /// -1..3. Night 1 at L = -1 is 230, not the table's 195: the table value was a rough estimate and the
        /// model (which this test pins) needs about 234 px/s there.
        /// </summary>
        static readonly Dictionary<int, float[]> Thresholds = new Dictionary<int, float[]>
        {
            { 1, new[] { 230f, 170f, 140f, 100f, 70f } },
            { 2, new[] { 310f, 275f, 200f, 140f, 95f } },
            { 3, new[] { 450f, 405f, 290f, 200f, 130f } },
        };

        static TugOutcome Simulate(DifficultyProfile p, int level, float yank, out float time)
        {
            var assist = new AdaptiveAssist(level, AdaptiveAssist.MinLevel);
            var tug = new TugOfWar(p.TugFor(assist));
            float grip = p.Grip(0, assist);
            float drift = TugOfWar.EntityDriftSpeed(grip);
            float t = 0f;
            while (t < 20f)
            {
                var player = new Vec2(400f - yank * t, 300f);
                var entity = new Vec2(400f + drift * t, 300f);
                var o = tug.Step(Dt, player, true, entity, grip);
                t += Dt;
                if (o != TugOutcome.None)
                {
                    time = t;
                    return o;
                }
            }
            time = t;
            return TugOutcome.None;
        }

        static float Threshold(DifficultyProfile p, int level)
        {
            float lo = 0f, hi = 2000f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Simulate(p, level, mid, out _) == TugOutcome.PlayerWins) hi = mid;
                else lo = mid;
            }
            return hi;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void YankThresholdsMatchTheTable(int night)
        {
            var p = DifficultyTable.For(night, DifficultyMode.Normal);
            for (int level = AdaptiveAssist.MinLevel; level <= AdaptiveAssist.MaxLevel; level++)
            {
                float target = Thresholds[night][level + 1];
                Assert.True(Simulate(p, level, target * 1.15f, out _) == TugOutcome.PlayerWins,
                    "night " + night + " L" + level + ": " + (target * 1.15f) + " px/s should win (threshold " + Threshold(p, level) + ")");
                Assert.True(Simulate(p, level, target * 0.85f, out _) == TugOutcome.EntityWins,
                    "night " + night + " L" + level + ": " + (target * 0.85f) + " px/s should lose (threshold " + Threshold(p, level) + ")");
            }
        }

        [Theory]
        [InlineData(1, 1.06f)]
        [InlineData(2, 0.85f)]
        [InlineData(3, 0.71f)]
        public void HoldingStillLosesOnTime(int night, float seconds)
        {
            var p = DifficultyTable.For(night, DifficultyMode.Normal);
            var outcome = Simulate(p, 0, 0f, out float time);
            Assert.Equal(TugOutcome.EntityWins, outcome);
            Assert.InRange(time, seconds - 0.1f, seconds + 0.1f);
        }

        [Fact]
        public void EachNightIsHarderAtEveryLevel()
        {
            var n1 = DifficultyTable.For(1, DifficultyMode.Normal);
            var n2 = DifficultyTable.For(2, DifficultyMode.Normal);
            var n3 = DifficultyTable.For(3, DifficultyMode.Normal);
            for (int level = AdaptiveAssist.MinLevel; level <= AdaptiveAssist.MaxLevel; level++)
            {
                float a = Threshold(n1, level), b = Threshold(n2, level), c = Threshold(n3, level);
                Assert.True(a < b && b < c, "L" + level + ": " + a + " / " + b + " / " + c);
            }
        }

        [Fact]
        public void HigherAssistNeedsASofterYank()
        {
            for (int night = 1; night <= 3; night++)
            {
                var p = DifficultyTable.For(night, DifficultyMode.Normal);
                float previous = float.MaxValue;
                for (int level = AdaptiveAssist.MinLevel; level <= AdaptiveAssist.MaxLevel; level++)
                {
                    float t = Threshold(p, level);
                    Assert.True(t < previous, "night " + night + " L" + level);
                    previous = t;
                }
            }
        }

        [Fact]
        public void ThirdTugFeelsLikeLastNightsFirst()
        {
            // Night N at L1 needs about what Night N-1 needed at L0 (within 15%).
            for (int night = 2; night <= 3; night++)
            {
                float now = Threshold(DifficultyTable.For(night, DifficultyMode.Normal), 1);
                float before = Threshold(DifficultyTable.For(night - 1, DifficultyMode.Normal), 0);
                Assert.InRange(now / before, 0.85f, 1.15f);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        public void StoryWinsWithAGentleDrag(int night)
        {
            var p = DifficultyTable.For(night, DifficultyMode.Story);
            var assist = AdaptiveAssist.ForNight(p, 0);
            Assert.Equal(2, assist.Level);
            Assert.Equal(TugOutcome.PlayerWins, Simulate(p, assist.Level, 150f, out _));
        }
    }

    public class DifficultyProfileTests
    {
        [Fact]
        public void NightOneNormalIsTheVerticalSlice()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var today = new TugOfWarSettings();
            foreach (var f in typeof(TugOfWarSettings).GetFields())
                Assert.Equal(f.GetValue(today), f.GetValue(p.Tug));
            Assert.Equal(0.62f, p.GripBase);
            Assert.Equal(1f, p.ReactionScale);
            Assert.Equal(0.15f, p.RaceToNoDelayMin);
            Assert.Equal(0.35f, p.RaceToNoDelayMax);
            Assert.Equal(70f, p.DragDialogRadius);
            Assert.Equal(0.35f, p.CancelCrawl);
            Assert.Equal(6f, p.CancelPatience);
            Assert.Equal(30f, p.TaskHintFirst);
            Assert.Equal(25f, p.BriefingHintFirst);
            Assert.Equal(40f, p.TaskHintRepeat);
            Assert.Equal(240f, p.TaskForceAfterHint);
            Assert.True(p.ConflictToastOnFirstLoss);
        }

        [Fact]
        public void GripAtLevelZeroMatchesTheOldFormula()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var assist = new AdaptiveAssist();
            for (int d = 0; d < 8; d++)
                Assert.Equal(Math.Min(1.35f, 0.62f * (1f + d * 0.12f)), p.Grip(d, assist), 4);
        }

        [Fact]
        public void DefenseRulesAtLevelZeroMatchTheOldBrain()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var assist = new AdaptiveAssist();
            for (int d = 0; d < 8; d++)
            {
                Assert.Equal(d >= 2 && d % 2 == 0, p.GuardsYes(d, assist));
                Assert.Equal(d >= 2, p.KeepsAway(d));
                Assert.Equal(Math.Min(2.2f, 1f + d * 0.15f), p.Urgency(d), 4);
            }
            Assert.Equal(0.15f, p.RaceToNoDelay(0f, assist), 4);
            Assert.Equal(0.35f, p.RaceToNoDelay(1f, assist), 4);
            Assert.True(p.DragsDialog(assist));
        }

        [Fact]
        public void AssistLevelsScaleTheContest()
        {
            var p = DifficultyTable.For(2, DifficultyMode.Normal);
            var s = p.TugFor(new AdaptiveAssist(1));
            Assert.Equal(440f * 0.90f, s.pullSpeedForFullStrength, 3);
            Assert.Equal(0.18f * 0.60f, s.rampPerSecond, 4);
            Assert.Equal(0.08f + 0.03f, s.releaseGrace, 4);
            Assert.Equal(0.70f * 0.88f * (1f + 2 * 0.10f * 0.75f), p.Grip(2, new AdaptiveAssist(1)), 4);
            // L2 stops guarding Yes, L3 stops dragging the dialog, and grip no longer grows.
            Assert.False(p.GuardsYes(2, new AdaptiveAssist(2)));
            Assert.True(p.DragsDialog(new AdaptiveAssist(2)));
            Assert.False(p.DragsDialog(new AdaptiveAssist(3)));
            Assert.Equal(p.Grip(0, new AdaptiveAssist(3)), p.Grip(5, new AdaptiveAssist(3)), 4);
            // Settings are a fresh copy per contest.
            Assert.NotSame(p.Tug, s);
            Assert.Equal(440f, p.Tug.pullSpeedForFullStrength);
        }

        [Fact]
        public void MercyContestsNeverRamp()
        {
            var s = DifficultyTable.For(3, DifficultyMode.Normal).TugFor(new AdaptiveAssist(3), true);
            Assert.Equal(0f, s.rampPerSecond);
        }

        [Fact]
        public void StoryChangesChallengeOnly()
        {
            var p = DifficultyTable.For(2, DifficultyMode.Story);
            Assert.Equal(DifficultyMode.Story, p.Mode);
            Assert.Equal(0.45f, p.Grip(10, new AdaptiveAssist(0)), 4);
            Assert.False(p.GuardsYes(10, new AdaptiveAssist(0)));
            Assert.False(p.KeepsAway(10));
            Assert.False(p.DragsDialog(new AdaptiveAssist(0)));
            Assert.Equal(15f, p.TaskHintFirst);
            Assert.Equal(90f, p.TaskForceAfterHint);
            Assert.Equal(2, p.AssistFloor);
            Assert.Equal(1, p.MercyAfterLosses);
            Assert.Equal(DifficultyMode.Story, DifficultyTable.ParseMode("Story"));
            Assert.Equal(DifficultyMode.Normal, DifficultyTable.ParseMode("anything"));
        }
    }

    public class AdaptiveAssistTests
    {
        [Fact]
        public void TwoLossesRaiseTheLevel()
        {
            var a = new AdaptiveAssist();
            a.ReportTug(false, 1f, 0.2f);
            Assert.Equal(0, a.Level);
            a.ReportTug(false, 1f, 0.2f);
            Assert.Equal(1, a.Level);
            Assert.Equal(0f, a.LossStreak);
        }

        [Fact]
        public void FourOtherDefensesRaiseTheLevel()
        {
            var a = new AdaptiveAssist();
            for (int i = 0; i < 3; i++) a.ReportDefense();
            Assert.Equal(0, a.Level);
            a.ReportDefense();
            Assert.Equal(1, a.Level);
        }

        [Fact]
        public void AWinBreaksALossStreak()
        {
            var a = new AdaptiveAssist();
            a.ReportTug(false, 1f, 0.2f);
            a.ReportTug(true, 1f, 0.8f);
            a.ReportTug(false, 1f, 0.2f);
            Assert.Equal(0, a.Level);
        }

        [Fact]
        public void TwoWinsInARowLowerTheLevel()
        {
            var a = new AdaptiveAssist(2);
            a.ReportTug(true, 0.9f, 0.8f);
            Assert.Equal(2, a.Level);
            a.ReportTug(true, 0.9f, 0.8f);
            Assert.Equal(1, a.Level);
        }

        [Fact]
        public void AnEasyWinLowersTheLevel()
        {
            var a = new AdaptiveAssist(1);
            a.ReportTug(true, 0.3f, 1.9f);
            Assert.Equal(0, a.Level);
            // Fast but weak, or strong but slow, is not easy.
            var b = new AdaptiveAssist(1);
            b.ReportTug(true, 0.3f, 1.2f);
            Assert.Equal(1, b.Level);
        }

        [Fact]
        public void LevelsStopAtTheFloorAndTheTop()
        {
            var normal = new AdaptiveAssist();
            for (int i = 0; i < 10; i++) normal.ReportTug(true, 0.2f, 2f);
            Assert.Equal(-1, normal.Level);
            var story = AdaptiveAssist.ForNight(DifficultyTable.For(1, DifficultyMode.Story), 0);
            for (int i = 0; i < 10; i++) story.ReportTug(true, 0.2f, 2f);
            Assert.Equal(2, story.Level);
            for (int i = 0; i < 20; i++) story.ReportTug(false, 1f, 0f);
            Assert.Equal(3, story.Level);
        }

        [Fact]
        public void MercyArmsAfterTwoMoreLossesAtTheTopAndLastsOneContest()
        {
            var a = new AdaptiveAssist(3);
            a.ReportTug(false, 1f, 0.2f);
            Assert.False(a.MercyArmed);
            a.ReportDefense(); // only tug losses count toward mercy
            Assert.False(a.MercyArmed);
            a.ReportTug(false, 1f, 0.2f);
            Assert.True(a.MercyArmed);
            Assert.True(a.BeginContest());
            Assert.True(a.InMercyContest);
            a.ReportTug(false, 3f, 0.1f); // even a lost mercy contest disarms it
            Assert.False(a.MercyArmed);
            Assert.False(a.BeginContest());
            a.ReportTug(false, 1f, 0.2f);
            Assert.False(a.MercyArmed);
        }

        [Fact]
        public void LosingAtLevelTwoDoesNotCountTowardMercy()
        {
            var a = new AdaptiveAssist(2);
            a.ReportTug(false, 1f, 0.2f);
            a.ReportTug(false, 1f, 0.2f); // raises to 3
            Assert.Equal(3, a.Level);
            Assert.False(a.MercyArmed);
        }

        [Fact]
        public void StoryMercyComesAfterOneLoss()
        {
            var a = AdaptiveAssist.ForNight(DifficultyTable.For(3, DifficultyMode.Story), 0);
            a.SetLevel(3);
            a.ReportTug(false, 1f, 0.2f);
            Assert.True(a.MercyArmed);
        }

        [Fact]
        public void NightStartLevels()
        {
            Assert.Equal(0, AdaptiveAssist.CarryLevel(-1));
            Assert.Equal(0, AdaptiveAssist.CarryLevel(0));
            Assert.Equal(0, AdaptiveAssist.CarryLevel(1));
            Assert.Equal(1, AdaptiveAssist.CarryLevel(2));
            Assert.Equal(1, AdaptiveAssist.CarryLevel(3));
            Assert.Equal(0, AdaptiveAssist.ForNight(DifficultyTable.For(1, DifficultyMode.Normal), 3).Level);
            Assert.Equal(1, AdaptiveAssist.ForNight(DifficultyTable.For(2, DifficultyMode.Normal), 3).Level);
            Assert.Equal(0, AdaptiveAssist.ForNight(DifficultyTable.For(3, DifficultyMode.Normal), 1).Level);
            Assert.Equal(2, AdaptiveAssist.ForNight(DifficultyTable.For(2, DifficultyMode.Story), 0).Level);
        }

        [Fact]
        public void FirstRaiseIsAnnouncedOncePerNight()
        {
            var a = new AdaptiveAssist();
            int raised = 0, changes = 0;
            a.FirstRaise += () => raised++;
            a.LevelChanged += (from, to) => changes++;
            for (int i = 0; i < 6; i++) a.ReportTug(false, 1f, 0.2f);
            Assert.Equal(3, a.Level);
            Assert.Equal(1, raised);
            Assert.Equal(3, changes);
            Assert.True(a.RaisedThisNight);
        }
    }
}
