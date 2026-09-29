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
    /// constant speed, for every night and assist level. Phase F values (BalanceReport section 3.1).
    /// </summary>
    public class DifficultyCurveTests
    {
        const float Dt = 1f / 60f;

        /// <summary>
        /// Minimum sustained yank (px/s) to win a night's first contest (no lost tugs yet) at assist level -1..3.
        /// Phase F: the L1..L3 rows of the assist table were made clearly easier (was N1 170/140/100/70).
        /// </summary>
        static readonly Dictionary<int, float[]> Thresholds = new Dictionary<int, float[]>
        {
            { 1, new[] { 234f, 184f, 115f, 72f, 41f } },
            { 2, new[] { 316f, 248f, 157f, 99f, 59f } },
            { 3, new[] { 415f, 323f, 202f, 130f, 79f } },
        };

        /// <summary>
        /// Growth-aware (BalanceReport 3.1): contest k of a night is played at the level the assist reaches after
        /// k-1 straight tug losses (L0, L0, L1, L1, L2, L2, L3, L3), with k-1 lost tugs of grip growth.
        /// </summary>
        static readonly Dictionary<int, float[]> Path = new Dictionary<int, float[]>
        {
            { 1, new[] { 184f, 202f, 127f, 134f, 81f, 83f, 41f, 41f } },
            { 2, new[] { 248f, 268f, 170f, 176f, 109f, 111f, 59f, 59f } },
            { 3, new[] { 323f, 347f, 218f, 227f, 141f, 144f, 79f, 79f } },
        };

        static TugOutcome Simulate(DifficultyProfile p, int level, float yank, out float time, int tugLosses = 0)
        {
            var assist = new AdaptiveAssist(level, AdaptiveAssist.MinLevel);
            return SimulateGrip(p.TugFor(assist), p.Grip(tugLosses, assist), t => yank, out time);
        }

        /// <summary>The player's x speed away from the entity is <paramref name="speedAt"/>(t) (negative = toward it).</summary>
        static TugOutcome SimulateGrip(TugOfWarSettings s, float grip, Func<float, float> speedAt, out float time, float limit = 20f)
        {
            var tug = new TugOfWar(s);
            float drift = TugOfWar.EntityDriftSpeed(grip);
            float t = 0f, px = 400f;
            while (t < limit)
            {
                var player = new Vec2(px, 300f);
                var entity = new Vec2(400f + drift * t, 300f);
                var o = tug.Step(Dt, player, true, entity, grip);
                px -= speedAt(t) * Dt;
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

        static float Threshold(DifficultyProfile p, int level, int tugLosses = 0)
        {
            float lo = 0f, hi = 2000f;
            for (int i = 0; i < 30; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Simulate(p, level, mid, out _, tugLosses) == TugOutcome.PlayerWins) hi = mid;
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
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void GrowthAwarePathMatchesTheReport(int night)
        {
            var p = DifficultyTable.For(night, DifficultyMode.Normal);
            int[] levels = { 0, 0, 1, 1, 2, 2, 3, 3 };
            for (int k = 0; k < levels.Length; k++)
            {
                float t = Threshold(p, levels[k], k);
                Assert.InRange(t, Path[night][k] * 0.95f, Path[night][k] * 1.05f);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EachLostTugAtTheSameLevelIsOnlyALittleHarder(int night)
        {
            // Phase F: growth only from lost tugs, and small (tug 2 at most 12% over tug 1; was 20% on Night 1).
            var p = DifficultyTable.For(night, DifficultyMode.Normal);
            float first = Threshold(p, 0, 0), second = Threshold(p, 0, 1);
            Assert.True(second > first && second / first < 1.12f, first + " -> " + second);
        }

        [Fact]
        public void TheAssistSoftensTheThirdTugWellBelowTheFirst()
        {
            // "One spike per night that the assist softens after two losses": tug 3 needs at most 75% of tug 1.
            for (int night = 1; night <= 3; night++)
            {
                var p = DifficultyTable.For(night, DifficultyMode.Normal);
                Assert.True(Threshold(p, 1, 2) < 0.75f * Threshold(p, 0, 0), "night " + night);
            }
        }

        [Theory]
        [InlineData(1, 1.07f)]
        [InlineData(2, 0.88f)]
        [InlineData(3, 0.77f)]
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
            // Spec 7.1, growth-aware (Phase F): Night N's third tug (L1 after two lost tugs) needs about what
            // Night N-1's first tug needed, within 15%. The first-grip version ignored growth.
            for (int night = 2; night <= 3; night++)
            {
                float now = Threshold(DifficultyTable.For(night, DifficultyMode.Normal), 1, 2);
                float before = Threshold(DifficultyTable.For(night - 1, DifficultyMode.Normal), 0, 0);
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

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void StoryHoldingStillDoesNotWinBefore20Seconds(int night)
        {
            // Story's tug cannot be lost by holding on, but it is not won by holding on either.
            var p = DifficultyTable.For(night, DifficultyMode.Story);
            var assist = AdaptiveAssist.ForNight(p, 0);
            var outcome = SimulateGrip(p.TugFor(assist), p.Grip(0, assist), t => 0f, out float time, 20f);
            Assert.Equal(TugOutcome.None, outcome);
            Assert.True(time >= 20f - Dt);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void StoryRatchetAt150WinsInFourToNineSeconds(int night)
        {
            // A real struggle: 1 s strokes at 150 px/s, each followed by 0.5 s sliding back toward her at 100 px/s.
            var p = DifficultyTable.For(night, DifficultyMode.Story);
            var assist = AdaptiveAssist.ForNight(p, 0);
            var outcome = SimulateGrip(p.TugFor(assist), p.Grip(0, assist), t => (t % 1.5f) < 1f ? 150f : -100f, out float time, 30f);
            Assert.Equal(TugOutcome.PlayerWins, outcome);
            Assert.InRange(time, 4f, 9f);
        }

        [Fact]
        public void StoryNeverSnapsAndKeepsItsStrain()
        {
            var s = DifficultyTable.For(1, DifficultyMode.Story).Tug;
            Assert.Equal(DifficultyTable.StoryNoSnap, s.maxTension);
            Assert.Equal(300f, s.strainTension);
            // Strain still rises with distance although the snap is off.
            var tug = new TugOfWar(s);
            tug.Step(Dt, new Vec2(100f, 300f), true, new Vec2(400f, 300f), 0.28f);
            Assert.True(tug.Strain > 0.6f, "strain " + tug.Strain);
        }
    }

    public class DifficultyProfileTests
    {
        [Fact]
        public void NightOneNormalKeepsTheSliceTugWithThePhaseFBalance()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var today = new TugOfWarSettings();
            foreach (var f in typeof(TugOfWarSettings).GetFields())
                Assert.Equal(f.GetValue(today), f.GetValue(p.Tug));
            Assert.Equal(0.62f, p.GripBase);
            Assert.Equal(0.06f, p.GripGrowth);
            Assert.Equal(1f, p.ReactionScale);
            // Phase F: she reacts later in the confirm race and at Cancel, and lunges after a human beat.
            Assert.Equal(0.40f, p.RaceToNoDelayMin);
            Assert.Equal(0.60f, p.RaceToNoDelayMax);
            Assert.Equal(0.20f, p.InterceptDelay);
            Assert.Equal(0.35f, p.CancelDelayMin);
            Assert.Equal(0.55f, p.CancelDelayMax);
            Assert.Equal(70f, p.DragDialogRadius);
            Assert.Equal(0.35f, p.CancelCrawl);
            Assert.Equal(6f, p.CancelPatience);
            Assert.Equal(30f, p.TaskHintFirst);
            Assert.Equal(25f, p.BriefingHintFirst);
            Assert.Equal(40f, p.TaskHintRepeat);
            // Phase F: a stuck chore is finished for the player 150 s after its first hint (was 240 s).
            Assert.Equal(150f, p.TaskForceAfterHint);
            Assert.True(p.ConflictToastOnFirstLoss);
        }

        [Fact]
        public void NightTablesMatchPhaseF()
        {
            var n2 = DifficultyTable.For(2, DifficultyMode.Normal);
            Assert.Equal(0.68f, n2.GripBase);
            Assert.Equal(0.05f, n2.GripGrowth);
            Assert.Equal(0.20f, n2.RaceToNoDelayMin);
            Assert.Equal(0.40f, n2.RaceToNoDelayMax);
            Assert.Equal(0.15f, n2.InterceptDelay);
            var n3 = DifficultyTable.For(3, DifficultyMode.Normal);
            Assert.Equal(460f, n3.Tug.pullSpeedForFullStrength);
            Assert.Equal(0.74f, n3.GripBase);
            Assert.Equal(0.05f, n3.GripGrowth);
            Assert.Equal(0.18f, n3.RaceToNoDelayMin);
            Assert.Equal(0.35f, n3.RaceToNoDelayMax);
            Assert.Equal(0.12f, n3.InterceptDelay);
            Assert.Equal(0.25f, n3.CancelDelayMin);
            Assert.Equal(0.45f, n3.CancelDelayMax);
            // She always reacts later in Story, and every night reacts a little faster than the one before.
            var story = DifficultyTable.For(3, DifficultyMode.Story);
            Assert.True(story.InterceptDelay > DifficultyTable.For(1, DifficultyMode.Normal).InterceptDelay);
            Assert.True(story.CancelDelay(0f) > DifficultyTable.For(1, DifficultyMode.Normal).CancelDelay(1f));
            Assert.True(n2.InterceptDelay < DifficultyTable.For(1, DifficultyMode.Normal).InterceptDelay && n3.InterceptDelay < n2.InterceptDelay);
        }

        [Fact]
        public void GripGrowsOnlyWithLostTugs()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var assist = new AdaptiveAssist();
            for (int d = 0; d < 8; d++)
                Assert.Equal(Math.Min(1.35f, 0.62f * (1f + d * 0.06f)), p.Grip(d, assist), 4);
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
            Assert.Equal(0.40f, p.RaceToNoDelay(0f, assist), 4);
            Assert.Equal(0.60f, p.RaceToNoDelay(1f, assist), 4);
            Assert.True(p.DragsDialog(assist));
        }

        [Fact]
        public void AssistLevelsScaleTheContest()
        {
            var p = DifficultyTable.For(2, DifficultyMode.Normal);
            var s = p.TugFor(new AdaptiveAssist(1));
            Assert.Equal(440f * 0.88f, s.pullSpeedForFullStrength, 3);
            Assert.Equal(0.18f * 0.50f, s.rampPerSecond, 4);
            Assert.Equal(0.08f + 0.04f, s.releaseGrace, 4);
            Assert.Equal(0.68f * 0.82f * (1f + 2 * 0.05f * 0.50f), p.Grip(2, new AdaptiveAssist(1)), 4);
            // The confirm race opens up with the level: +0.40 s at L1, +0.80 at L2, +1.00 at L3.
            Assert.Equal(0.20f + 0.40f, p.RaceToNoDelay(0f, new AdaptiveAssist(1)), 4);
            Assert.Equal(0.20f + 0.80f, p.RaceToNoDelay(0f, new AdaptiveAssist(2)), 4);
            Assert.Equal(0.20f + 1.00f, p.RaceToNoDelay(0f, new AdaptiveAssist(3)), 4);
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
            Assert.Equal(0.41f, p.Grip(10, new AdaptiveAssist(0)), 4);
            Assert.False(p.GuardsYes(10, new AdaptiveAssist(0)));
            Assert.False(p.KeepsAway(10));
            Assert.False(p.DragsDialog(new AdaptiveAssist(0)));
            Assert.Equal(15f, p.TaskHintFirst);
            Assert.Equal(90f, p.TaskForceAfterHint);
            Assert.Equal(2, p.AssistFloor);
            Assert.Equal(1, p.MercyAfterLosses);
            Assert.Equal(0.45f, p.Tug.releaseGrace);
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
            // Snatched icons and closed windows weigh the same.
            var b = new AdaptiveAssist();
            for (int i = 0; i < 3; i++) b.ReportDefense(i % 2 == 0 ? "keepaway" : "close");
            Assert.Equal(0, b.Level);
        }

        [Theory]
        [InlineData("no")]
        [InlineData("dialog")]
        [InlineData("guard")]
        [InlineData("cancel")]
        public void TwoLostAttemptsRaiseTheLevel(string how)
        {
            // Phase F: a lost confirm race or Cancel fight ends the attempt as surely as a lost tug.
            var a = new AdaptiveAssist();
            a.ReportDefense(how);
            Assert.Equal(0, a.Level);
            a.ReportDefense(how);
            Assert.Equal(1, a.Level);
        }

        [Fact]
        public void TugWonDialogLostTwiceRaisesTheLevel()
        {
            // BalanceReport finding 7: an attempt that wins the tug and loses the race must still bring help.
            var a = new AdaptiveAssist();
            a.ReportTug(true, 1.2f, 0.9f);
            a.ReportDefense("no");
            Assert.Equal(0, a.Level);
            a.ReportTug(true, 1.2f, 0.9f);
            a.ReportDefense("no");
            Assert.Equal(1, a.Level);
        }

        [Fact]
        public void AWinDoesNotClearTheLossStreak()
        {
            // Phase F (was: a win broke the streak). Loss, win, loss is two losses since the last level change.
            var a = new AdaptiveAssist();
            a.ReportTug(false, 1f, 0.2f);
            a.ReportTug(true, 1f, 0.8f);
            Assert.Equal(1f, a.LossStreak);
            a.ReportTug(false, 1f, 0.2f);
            Assert.Equal(1, a.Level);
        }

        [Fact]
        public void ALowerLevelStartsAFreshLossStreak()
        {
            // "Since the last level change": two wins that lower the level also clear what was building up.
            var a = new AdaptiveAssist();
            for (int i = 0; i < 3; i++) a.ReportDefense("keepaway");
            a.ReportTug(true, 1f, 0.8f);
            a.ReportTug(true, 1f, 0.8f);
            Assert.Equal(-1, a.Level);
            Assert.Equal(0f, a.LossStreak);
            a.ReportDefense("keepaway");
            Assert.Equal(-1, a.Level);
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
        public void StoryMercyComesAfterALossAtAnyLevel()
        {
            // Phase F: in Story the contest after any lost tug is a mercy contest, also at the floor (+2).
            var a = AdaptiveAssist.ForNight(DifficultyTable.For(1, DifficultyMode.Story), 0);
            Assert.Equal(2, a.Level);
            a.ReportTug(false, 1f, 0.2f);
            Assert.True(a.MercyArmed);
            Assert.True(a.BeginContest());
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

    public class TugGeometryTests
    {
        static readonly Vec2 Bin = new Vec2(915f, 66f);
        const float W = 960f, H = 540f, Bottom = 28f;

        [Fact]
        public void MidScreenSheEscapesAwayFromThePlayerAndTheBin()
        {
            var player = new Vec2(480f, 270f);
            var entity = new Vec2(470f, 282f);
            var d = TugGeometry.EscapeDirection(player, entity, Bin, W, H, Bottom);
            var expected = ((entity - player).Normalized * 0.7f + (entity - Bin).Normalized * 0.3f).Normalized;
            Assert.Equal(expected.x, d.x, 4);
            Assert.Equal(expected.y, d.y, 4);
        }

        [Theory]
        [InlineData(815f, 113f, 808f, 133f)]   // grabbed from above-left, next to the bin (the Night 2 corner trap)
        [InlineData(900f, 80f, 890f, 95f)]
        [InlineData(940f, 300f, 925f, 305f)]   // against the right edge
        public void AGrabByTheBinLeavesThePlayerRoomToPull(float px, float py, float ex, float ey)
        {
            var player = new Vec2(px, py);
            var d = TugGeometry.EscapeDirection(player, new Vec2(ex, ey), Bin, W, H, Bottom);
            Assert.InRange(d.Length, 0.99f, 1.01f);
            Assert.True(TugGeometry.RoomAlong(player, -d, W, H, Bottom) >= TugGeometry.MinPlayerRoom);
        }

        [Fact]
        public void RoomIsMeasuredToTheTaskbarAndTheEdges()
        {
            Assert.Equal(72f, TugGeometry.RoomAlong(new Vec2(500f, 100f), new Vec2(0f, -1f), W, H, Bottom), 3);
            Assert.Equal(460f, TugGeometry.RoomAlong(new Vec2(500f, 100f), new Vec2(1f, 0f), W, H, Bottom), 3);
        }
    }

    public class MercyReleaseTests
    {
        const float Dt = 1f / 60f;

        static float StepsUntilRelease(float effort, float limit = 5f)
        {
            var m = new MercyRelease();
            float t = 0f;
            while (t < limit)
            {
                t += Dt;
                if (m.Step(Dt, true, effort)) return t;
            }
            return float.PositiveInfinity;
        }

        [Fact]
        public void HoldingTheButtonForTwoAndAHalfSecondsReleases()
        {
            Assert.InRange(StepsUntilRelease(0f), 2.5f - Dt, 2.5f + 2 * Dt);
        }

        [Fact]
        public void AGentlePullReleasesAfterOneSecond()
        {
            Assert.InRange(StepsUntilRelease(AdaptiveAssist.MercyEffort), 1f - Dt, 1f + 2 * Dt);
        }

        [Fact]
        public void LettingGoNeverReleases()
        {
            var m = new MercyRelease();
            for (int i = 0; i < 600; i++) Assert.False(m.Step(Dt, false, 1f));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void AMercyContestIsNotLostByHoldingOn(int night)
        {
            // Mercy grip is below every night's base strength, so holding still never loses before the release.
            var p = DifficultyTable.For(night, DifficultyMode.Normal);
            var assist = new AdaptiveAssist(3);
            var tug = new TugOfWar(p.TugFor(assist, true));
            var mercy = new MercyRelease();
            float drift = TugOfWar.EntityDriftSpeed(AdaptiveAssist.MercyGrip), t = 0f;
            bool released = false;
            while (t < 3f && !released)
            {
                var o = tug.Step(Dt, new Vec2(400f, 300f), true, new Vec2(400f + drift * (t + Dt), 300f), AdaptiveAssist.MercyGrip);
                Assert.NotEqual(TugOutcome.EntityWins, o);
                if (o != TugOutcome.None) break;
                released = mercy.Step(Dt, true, tug.Effort);
                t += Dt;
            }
            Assert.True(released || tug.Outcome == TugOutcome.PlayerWins);
        }
    }
}
