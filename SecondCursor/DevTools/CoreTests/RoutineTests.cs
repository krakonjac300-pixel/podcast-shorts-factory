using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Core;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>The coroutine runner every story beat and entity action runs on.</summary>
    public class RoutineTests
    {
        static IEnumerator Steps(List<string> log, string name, int count)
        {
            for (int i = 0; i < count; i++)
            {
                log.Add(name + i);
                yield return null;
            }
        }

        [Fact]
        public void NullYieldAdvancesOneStepPerTick()
        {
            var log = new List<string>();
            var r = new Routine(Steps(log, "s", 3), "steps");
            r.Tick(0f);
            Assert.Equal(new[] { "s0" }, log);
            r.Tick(0f);
            r.Tick(0f);
            Assert.Equal(new[] { "s0", "s1", "s2" }, log);
            Assert.False(r.Done);
            r.Tick(0f);
            Assert.True(r.Done);
        }

        static IEnumerator WaitThen(List<string> log, float seconds)
        {
            log.Add("before");
            yield return Waits.Seconds(seconds);
            log.Add("after");
        }

        [Fact]
        public void WaitSecondsBlocksUntilGameTimePasses()
        {
            var log = new List<string>();
            var r = new Routine(WaitThen(log, 1f));
            r.Tick(10f);
            r.Tick(10.5f);
            r.Tick(10.99f);
            Assert.Equal(new[] { "before" }, log);
            r.Tick(11f);
            Assert.Equal(new[] { "before", "after" }, log);
            Assert.True(r.Done);
        }

        [Fact]
        public void ZeroSecondWaitStillYieldsOneTick()
        {
            var log = new List<string>();
            var r = new Routine(WaitThen(log, 0f));
            r.Tick(5f);
            Assert.Equal(new[] { "before" }, log);
            r.Tick(5f);
            Assert.Equal(new[] { "before", "after" }, log);
        }

        static IEnumerator Outer(List<string> log)
        {
            log.Add("outer start");
            yield return Inner(log);
            log.Add("outer end");
        }

        static IEnumerator Inner(List<string> log)
        {
            log.Add("inner start");
            yield return Waits.Seconds(2f);
            yield return Steps(log, "deep", 2);
            log.Add("inner end");
        }

        [Fact]
        public void NestedEnumeratorsRunInlineIncludingTheirWaits()
        {
            var log = new List<string>();
            var r = new Routine(Outer(log));
            r.Tick(0f);
            Assert.Equal(new[] { "outer start", "inner start" }, log);
            r.Tick(1f);
            Assert.Equal(2, log.Count);
            r.Tick(2f);   // wait over: first deep step
            r.Tick(2f);   // second deep step
            r.Tick(2f);   // deep ends, inner ends, outer ends
            Assert.Equal(new[] { "outer start", "inner start", "deep0", "deep1", "inner end", "outer end" }, log);
            Assert.True(r.Done);
        }

        static IEnumerator Forever(int[] counter)
        {
            while (true)
            {
                counter[0]++;
                yield return null;
            }
        }

        static IEnumerator Wrapper(int[] counter)
        {
            yield return Forever(counter);
        }

        [Fact]
        public void StopHaltsNestedChildrenToo()
        {
            var counter = new int[1];
            var r = new Routine(Wrapper(counter), "entity action");
            for (int i = 0; i < 5; i++) r.Tick(i);
            Assert.Equal(5, counter[0]);
            r.Stop();
            for (int i = 0; i < 5; i++) r.Tick(10 + i);
            Assert.Equal(5, counter[0]);
            Assert.True(r.Done);
        }

        static IEnumerator WaitFor(Routine other, List<string> log)
        {
            yield return other;
            log.Add("other finished");
        }

        [Fact]
        public void YieldingARoutineWaitsForItToFinish()
        {
            var log = new List<string>();
            var first = new Routine(WaitThen(log, 1f));
            var second = new Routine(WaitFor(first, log));
            first.Tick(0f);
            second.Tick(0f);
            second.Tick(0.5f);
            Assert.DoesNotContain("other finished", log);
            first.Tick(1f);
            Assert.True(first.Done);
            second.Tick(1f);
            Assert.Contains("other finished", log);
            Assert.True(second.Done);
        }

        static IEnumerator Throws()
        {
            yield return null;
            throw new InvalidOperationException("boom");
        }

        [Fact]
        public void ExceptionStopsTheRoutineAndIsLogged()
        {
            var r = new Routine(Throws(), "throwing-routine-test");
            r.Tick(0f);
            Assert.False(r.Done);
            r.Tick(0f);
            Assert.True(r.Done);
            Assert.Contains(GameLog.Recent(80), e => e.Level == LogLevel.Error && e.Message.Contains("throwing-routine-test") && e.Message.Contains("boom"));
        }

        static IEnumerator Empty()
        {
            yield break;
        }

        static IEnumerator NeverYields()
        {
            while (true) yield return Empty();
        }

        [Fact]
        public void ARoutineThatNeverYieldsIsStoppedInsteadOfHanging()
        {
            var r = new Routine(NeverYields(), "spin-routine-test");
            r.Tick(0f);
            Assert.True(r.Done);
            Assert.Contains(GameLog.Recent(80), e => e.Level == LogLevel.Error && e.Message.Contains("spin-routine-test"));
        }

        static IEnumerator UnknownYield(List<string> log)
        {
            yield return "some unity yield instruction";
            log.Add("next");
        }

        [Fact]
        public void UnsupportedYieldCountsAsOneTick()
        {
            var log = new List<string>();
            var r = new Routine(UnknownYield(log));
            r.Tick(0f);
            Assert.Empty(log);
            r.Tick(0f);
            Assert.Equal(new[] { "next" }, log);
        }

        [Fact]
        public void NullRootIsDoneImmediately()
        {
            var r = new Routine(null);
            Assert.True(r.Done);
            r.Tick(0f);
            Assert.True(r.Done);
        }
    }
}
