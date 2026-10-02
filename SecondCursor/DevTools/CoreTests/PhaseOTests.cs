using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SecondCursor.Core;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>Phase O: crash safety (a throwing step), frame-rate independence and the flash budget.</summary>
    public class PhaseOTests
    {
        // ------------------------------------------------------------------ Routine faults

        static IEnumerator Child(List<string> log)
        {
            log.Add("child");
            yield return null;
            throw new InvalidOperationException("child boom");
        }

        static IEnumerator Parent(List<string> log)
        {
            log.Add("parent");
            yield return Child(log);
            log.Add("parent after");
        }

        [Fact]
        public void ANestedStepThatThrowsEndsTheRoutineWithItsFault()
        {
            var log = new List<string>();
            var r = new Routine(Parent(log), "fault-nested-test");
            r.Tick(0f);
            Assert.False(r.Done);
            Assert.Null(r.Fault);
            r.Tick(0f);
            Assert.True(r.Done);
            Assert.IsType<InvalidOperationException>(r.Fault);
            Assert.Equal("child boom", r.Fault.Message);
            Assert.Equal(new[] { "parent", "child" }, log);   // the parent never resumes
            r.Tick(0f);
            Assert.Equal(2, log.Count);
        }

        [Fact]
        public void AFinishedRoutineHasNoFault()
        {
            var r = new Routine(Steps(2), "fault-clean-test");
            for (int i = 0; i < 4; i++) r.Tick(0f);
            Assert.True(r.Done);
            Assert.Null(r.Fault);
        }

        [Fact]
        public void AStoppedRoutineHasNoFault()
        {
            var r = new Routine(Steps(5), "fault-stopped-test");
            r.Tick(0f);
            r.Stop();
            Assert.True(r.Done);
            Assert.Null(r.Fault);
        }

        [Fact]
        public void ARoutineThatNeverYieldsEndsWithAFault()
        {
            var r = new Routine(Spin(), "fault-spin-test");
            r.Tick(0f);
            Assert.True(r.Done);
            Assert.NotNull(r.Fault);
        }

        static IEnumerator Steps(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        static IEnumerator Empty()
        {
            yield break;
        }

        static IEnumerator Spin()
        {
            while (true) yield return Empty();
        }

        // ------------------------------------------------------------------ FaultLog

        static float _now;

        static int ErrorLines(string needle) =>
            GameLog.Recent(80).Count(e => e.Level == LogLevel.Error && e.Message.Contains(needle));

        static void WithClock(Action body)
        {
            var before = GameLog.Clock;
            GameLog.Clock = () => _now;
            FaultLog.Reset();
            GameLog.ClearHistory();
            try { body(); }
            finally
            {
                GameLog.Clock = before;
                FaultLog.Reset();
            }
        }

        [Fact]
        public void AnExceptionIsLoggedInFullOnceThenCounted()
        {
            WithClock(() =>
            {
                _now = 100f;
                var e = new InvalidOperationException("same every frame");
                Assert.True(FaultLog.Report("stage-a", e));
                for (int i = 1; i <= 500; i++)
                {
                    _now = 100f + i / 144f;
                    Assert.False(FaultLog.Report("stage-a", e));
                }
                Assert.Equal(1, ErrorLines("stage-a"));   // 3.5 s later: still only the first line
                Assert.Contains(GameLog.Recent(80), x => x.Message.Contains("same every frame") && x.Message.Contains("Fault in stage-a"));
            });
        }

        [Fact]
        public void RepeatsAreSummarisedAtMostEveryThirtySeconds()
        {
            WithClock(() =>
            {
                _now = 0f;
                var e = new InvalidOperationException("x");
                FaultLog.Report("stage-b", e);
                for (int frame = 1; frame <= 144 * 95; frame++)   // 95 s at 144 Hz
                {
                    _now = frame / 144f;
                    FaultLog.Report("stage-b", e);
                }
                Assert.Equal(4, ErrorLines("stage-b"));   // the first sighting, then a count at 30, 60 and 90 s
                Assert.Contains(GameLog.Recent(80), x => x.Message.StartsWith("Fault repeated 4320 times in stage-b"));
            });
        }

        [Fact]
        public void DistinctExceptionsAreEachLoggedOnce()
        {
            WithClock(() =>
            {
                _now = 5f;
                Assert.True(FaultLog.Report("stage-c", new InvalidOperationException("one")));
                Assert.True(FaultLog.Report("stage-c", new InvalidOperationException("two")));
                Assert.True(FaultLog.Report("stage-d", new InvalidOperationException("one")));
                Assert.True(FaultLog.Report("stage-c", new ArgumentException("one")));
                Assert.False(FaultLog.Report("stage-c", new InvalidOperationException("one")));
            });
        }

        [Fact]
        public void AMessageThatChangesEveryFrameStillGetsABoundedLog()
        {
            WithClock(() =>
            {
                _now = 0f;
                int full = 0;
                for (int i = 0; i < 1000; i++)
                    if (FaultLog.Report("stage-e", new InvalidOperationException("index " + i))) full++;
                Assert.InRange(full, 1, 65);
            });
        }

        // ------------------------------------------------------------------ BeatRecovery

        [Fact]
        public void ABeatThatThrowsIsRestartedOnceThenSkipped()
        {
            var r = new BeatRecovery(5);
            Assert.Equal(BeatRecoveryStep.RestartBeat, r.OnFault(2));
            Assert.Equal(BeatRecoveryStep.SkipBeat, r.OnFault(2));
            Assert.Equal(BeatRecoveryStep.RestartBeat, r.OnFault(3));   // each beat has its own count
        }

        [Fact]
        public void TheLastBeatFailingTwiceLeavesForTheTitle()
        {
            var r = new BeatRecovery(3);
            Assert.Equal(BeatRecoveryStep.RestartBeat, r.OnFault(2));
            Assert.Equal(BeatRecoveryStep.ToTitle, r.OnFault(2));
            Assert.Equal(BeatRecoveryStep.ToTitle, r.OnFault(7));       // not a beat of this night
        }

        // ------------------------------------------------------------------ frame-rate independence

        [Fact]
        public void PerFrameValuesAtSixtyHertzAreUnchanged()
        {
            Assert.Equal(0.03f, MathUtil.ChanceAt60(0.03f, 1f / 60f), 5);
            Assert.Equal(0.2f, MathUtil.LerpAt60(0.2f, 1f / 60f), 5);
            Assert.Equal(0f, MathUtil.LerpAt60(0.2f, 0f), 5);
            Assert.Equal(1f, MathUtil.LerpAt60(1f, 1f / 144f), 5);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(120)]
        [InlineData(144)]
        [InlineData(240)]
        public void ALerpCoversTheSameDistancePerSecondAtAnyFrameRate(int fps)
        {
            float at60 = 1f, atFps = 1f;
            for (int i = 0; i < 60; i++) at60 -= at60 * MathUtil.LerpAt60(0.2f, 1f / 60f);
            for (int i = 0; i < fps; i++) atFps -= atFps * MathUtil.LerpAt60(0.2f, 1f / fps);
            Assert.Equal(at60, atFps, 4);
        }

        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void AChanceRolledEveryFrameHasTheSameRatePerSecondAtAnyFrameRate(int fps)
        {
            double perSecond = MathUtil.ChanceAt60(0.03f, 1f / fps) * fps;
            Assert.InRange(perSecond, 1.77, 1.86);   // 0.03 per frame at 60 Hz is 1.8 a second (1.77 at 30 fps: two frames' chances overlap)
        }

        [Theory]
        [InlineData(30, 30)]
        [InlineData(60, 60)]
        [InlineData(120, 60)]
        [InlineData(144, 60)]
        [InlineData(240, 60)]
        public void AStepTimerFiresSixtyTimesASecondAtMost(int fps, int expected)
        {
            var timer = new StepTimer();
            int fired = 0;
            for (int i = 0; i < fps * 10; i++) if (timer.Tick(1f / fps)) fired++;
            Assert.InRange(fired, expected * 10 - 3, expected * 10 + 3);
        }

        [Fact]
        public void AStepTimerFiresAlmostEveryFrameAtSixtyHertzEvenWithJitter()
        {
            var timer = new StepTimer();
            var rng = new Rng(9);
            int fired = 0;
            for (int i = 0; i < 600; i++)
                if (timer.Tick(1f / 60f + rng.Range(-0.0004f, 0.0004f))) fired++;
            Assert.InRange(fired, 590, 600);
        }

        [Fact]
        public void AStepTimerDoesNotCatchUpAfterALongFrame()
        {
            var timer = new StepTimer();
            Assert.True(timer.Tick(0.5f));
            Assert.False(timer.Tick(0.001f));
        }

        [Fact]
        public void APrimedStepTimerFiresAtOnce()
        {
            var timer = new StepTimer();
            timer.Prime(0.17f);
            Assert.True(timer.Tick(0.001f, 0.17f));
            Assert.False(timer.Tick(0.05f, 0.17f));
            Assert.False(timer.Tick(0.05f, 0.17f));
            Assert.True(timer.Tick(0.06f, 0.17f));
        }

        [Theory]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void ARollOnAStepTimerFiresAsOftenAtAnyFrameRate(int fps)
        {
            // The title's glitch: a 3% roll on the 1/60 s step. Counted over 600 s with a fixed seed.
            var rng = new Rng(4);
            var timer = new StepTimer();
            int hits = 0;
            for (int i = 0; i < fps * 600; i++)
                if (timer.Tick(1f / fps) && rng.NextFloat() < 0.03f) hits++;
            Assert.InRange(hits / 600.0, 1.65, 1.95);   // about 1.8 a second
        }

        [Theory]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void TheStrainGlitchCadenceIsTheSameAtAnyFrameRate(int fps)
        {
            // ConflictSystem: a glitch when strain is high, 8% per 1/60 s, at least 0.4 s apart. Counted over 600 s of held strain.
            var rng = new Rng(11);
            float cooldown = 0f, dt = 1f / fps;
            int glitches = 0;
            for (int i = 0; i < fps * 600; i++)
            {
                cooldown -= dt;
                if (cooldown <= 0f && rng.NextFloat() < MathUtil.ChanceAt60(0.08f, dt))
                {
                    glitches++;
                    cooldown = 0.4f;
                }
            }
            Assert.InRange(glitches / 600.0, 1.55, 1.75);   // 0.4 s plus a wait of 12.5 frames: about 1.64 a second at every rate
        }

        // ------------------------------------------------------------------ the sound bank on worker threads

        [Fact]
        public void TheSoundBankMakesTheSameSamplesOnManyThreadsAsOnOne()
        {
            var jobs = new List<KeyValuePair<string, int>>();
            foreach (var id in SecondCursor.Core.Audio.ProceduralSoundBank.Ids)
                foreach (int seed in new[] { 1, 3 }) jobs.Add(new KeyValuePair<string, int>(id, seed));
            var one = jobs.Select(j => SecondCursor.Core.Audio.ProceduralSoundBank.Generate(j.Key, j.Value)).ToArray();
            var many = new float[jobs.Count][];
            System.Threading.Tasks.Parallel.For(0, jobs.Count, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 8 },
                i => many[i] = SecondCursor.Core.Audio.ProceduralSoundBank.Generate(jobs[i].Key, jobs[i].Value));
            for (int i = 0; i < jobs.Count; i++)
                Assert.True(one[i].AsSpan().SequenceEqual(many[i]), jobs[i].Key + "#" + jobs[i].Value + " differs between threads");
        }

        // ------------------------------------------------------------------ FlashBudget

        [Theory]
        [InlineData(30)]
        [InlineData(60)]
        [InlineData(144)]
        [InlineData(240)]
        public void NoMoreThanThreeFlashEventsStartInAnySecondHoweverOftenTheyAreAsked(int fps)
        {
            var budget = new FlashBudget();
            var granted = new List<float>();
            for (int i = 0; i < fps * 30; i++)
            {
                float t = i / (float)fps;
                if (budget.TryBegin(t, false)) granted.Add(t);
            }
            Assert.InRange(granted.Count, 80, 90);                       // 3 a second would be 90: the gap makes it 2.9
            for (int i = 0; i < granted.Count; i++)
            {
                int inWindow = granted.Count(g => g >= granted[i] && g < granted[i] + 1f);
                Assert.True(inWindow <= FlashBudget.MaxPerSecond, "more than 3 events within a second of " + granted[i]);
                if (i > 0) Assert.True(granted[i] - granted[i - 1] >= FlashBudget.MinGap - 1e-4f);
            }
        }

        [Fact]
        public void ReduceFlashingAllowsOneEventASecond()
        {
            var budget = new FlashBudget();
            int granted = 0;
            for (int i = 0; i < 144 * 20; i++) if (budget.TryBegin(i / 144f, true)) granted++;
            Assert.InRange(granted, 19, 20);
        }

        [Fact]
        public void AnEventOverTheBudgetIsSoftenedInFullEffectsAndDroppedWhenReduced()
        {
            var full = new FlashBudget();
            Assert.Equal(FlashVerdict.Full, full.Decide(0f, false));
            Assert.Equal(FlashVerdict.Soft, full.Decide(0.1f, false));    // inside the gap
            Assert.Equal(FlashVerdict.Full, full.Decide(0.4f, false));
            var reduced = new FlashBudget();
            Assert.Equal(FlashVerdict.Soft, reduced.Decide(0f, true));
            Assert.Equal(FlashVerdict.Dropped, reduced.Decide(0.5f, true));
            Assert.Equal(FlashVerdict.Soft, reduced.Decide(1.1f, true));
        }

        [Fact]
        public void ASparseEventStreamIsNeverRefused()
        {
            var budget = new FlashBudget();
            for (int i = 0; i < 100; i++) Assert.True(budget.TryBegin(i * 0.5f, false));
        }

        [Fact]
        public void TheKeepClimaxStaysInsideTheBudget()
        {
            // The hit flash, the lens frame, NO SIGNAL, the tube's collapse and its strips inside 0.7 s, asked in order.
            var budget = new FlashBudget();
            var asks = new[] { 0f, 0.12f, 0.2f, 0.25f, 0.7f };
            int full = asks.Count(t => budget.TryBegin(t, false));
            Assert.InRange(full, 2, 3);
        }
    }
}
