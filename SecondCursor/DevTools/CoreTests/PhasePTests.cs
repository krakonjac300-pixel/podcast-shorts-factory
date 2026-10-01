using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase P (engine-free parts): the finale's bin mode by trust (T1), KEEP waiting for a running hold, click lock (A2), which tug ends
    /// count for achievements, the per-file contest cap (T6), the reel's per-night tables (plan section 1.5) and the reel's words.
    /// </summary>
    public class PhasePTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        /// <summary>The long dashes the game's text never uses (en and em).</summary>
        static readonly char[] LongDashes = { (char)0x2013, (char)0x2014 };

        static Dictionary<string, string> BaseStrings()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "strings.json")));
            return doc.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("key").GetString(), e => e.GetProperty("value").GetString());
        }

        [Fact]
        public void HaulTextExistsWithDeckWordingAndNoLongDashes()
        {
            var s = BaseStrings();
            var haul = s.Keys.Where(k => (k.StartsWith("haul.") || k.StartsWith("notify.haul")) && !k.EndsWith(".deck")).ToList();
            // Every word the reel's panel and notices use.
            foreach (var k in new[] { "haul.ready", "haul.label", "haul.label.first", "haul.ahead", "haul.regrip", "haul.won", "haul.won.tear", "haul.kept",
                         "haul.lost.release", "haul.lost.still", "haul.lost.wrong", "haul.lost.stopped", "haul.lost.slow", "haul.lost.pulled", "haul.refused",
                         "notify.haul", "notify.haul.won", "notify.haul.won.tear", "notify.haul.kept", "notify.haul.release", "notify.haul.still",
                         "notify.haul.wrong", "notify.haul.stopped", "notify.haul.slow" })
                Assert.Contains(k, haul);
            // A line that names an input has a Steam Deck twin that names R2 or a swipe.
            string[] inputs = { "BUTTON", "YANK", "PRESS", "LET GO", "DROP" };
            foreach (var k in haul)
            {
                string v = s[k].ToUpperInvariant();
                if (!inputs.Any(v.Contains)) continue;
                Assert.True(s.TryGetValue(k + ".deck", out var deck), k + " has no deck variant");
                Assert.True(deck.Contains("R2") || deck.ToUpperInvariant().Contains("SWIP"), k + ".deck: " + deck);
            }
            foreach (var k in s.Keys.Where(k => k.StartsWith("haul.") || k.StartsWith("notify.haul")))
                Assert.True(s[k].IndexOfAny(LongDashes) < 0, k);
        }

        [Fact]
        public void FinaleBinModeTable()
        {
            Assert.Equal(FinaleBinMode.Fight, Night3Rules.BinMode(-0.5f, false, false, 0));
            Assert.Equal(FinaleBinMode.Fight, Night3Rules.BinMode(-0.4f, false, false, 0));
            Assert.Equal(FinaleBinMode.LetGo, Night3Rules.BinMode(-0.39f, false, false, 0));
            Assert.Equal(FinaleBinMode.LetGo, Night3Rules.BinMode(0f, false, false, 0));
            Assert.Equal(FinaleBinMode.LetGo, Night3Rules.BinMode(0.3f, false, false, 0));
            // Even at the lowest trust: a reply asking her to let go, her name, or two refused contests.
            Assert.Equal(FinaleBinMode.LetGo, Night3Rules.BinMode(-0.9f, true, false, 0));
            Assert.Equal(FinaleBinMode.LetGo, Night3Rules.BinMode(-0.9f, false, true, 0));
            Assert.Equal(FinaleBinMode.Fight, Night3Rules.BinMode(-0.9f, false, false, 1));
            Assert.Equal(FinaleBinMode.LetGo, Night3Rules.BinMode(-0.9f, false, false, Night3Rules.FightContestsBeforeLetGo));
            Assert.Equal("n3_tug_letgo", Night3Rules.TugLineSet(FinaleBinMode.LetGo));
            Assert.Equal("n3_tug_refuse", Night3Rules.TugLineSet(FinaleBinMode.Fight));
        }

        [Fact]
        public void KeepWaitsForALetGoHold()
        {
            // A running hold is an exit under way: 7:05 waits for it, up to the exit grace.
            Assert.False(Night3Rules.KeepByTime(Night3Rules.KeepTime, true, 5f));
            Assert.True(Night3Rules.KeepByTime(Night3Rules.KeepTime, true, Night3Rules.ExitGrace));
            Assert.True(Night3Rules.KeepByTime(Night3Rules.KeepTime, false, 0f));
        }

        [Fact]
        public void ClickLockFilter()
        {
            const float dt = 1f / 60f;
            var lockOn = new ClickLock { Enabled = true };
            // A short click passes through unchanged.
            Assert.Equal((true, true, false), lockOn.Filter(true, true, false, false, dt));
            Assert.Equal((false, false, true), lockOn.Filter(false, false, true, false, dt));
            Assert.False(lockOn.Locked);
            // A drag held for 0.4 s: letting go keeps it held.
            lockOn.Filter(true, true, false, false, dt);
            for (int i = 0; i < 25; i++) lockOn.Filter(true, false, false, true, dt);
            Assert.Equal((true, false, false), lockOn.Filter(false, false, true, true, dt));
            Assert.True(lockOn.Locked);
            Assert.Equal((true, false, false), lockOn.Filter(false, false, false, true, dt));
            // The next press is the drop; its own release is not another click.
            Assert.Equal((false, false, true), lockOn.Filter(true, true, false, true, dt));
            Assert.False(lockOn.Locked);
            Assert.Equal((false, false, false), lockOn.Filter(true, false, false, false, dt));
            Assert.Equal((false, false, false), lockOn.Filter(false, false, true, false, dt));
            Assert.Equal((true, true, false), lockOn.Filter(true, true, false, false, dt));
            Assert.Equal((false, false, true), lockOn.Filter(false, false, true, false, dt));
            // A drag shorter than 0.4 s is an ordinary release.
            lockOn.Filter(true, true, false, false, dt);
            for (int i = 0; i < 10; i++) lockOn.Filter(true, false, false, true, dt);
            Assert.Equal((false, false, true), lockOn.Filter(false, false, true, true, dt));
            // Clear (pause, Esc) lets go of a locked drag on the next frame.
            lockOn.Filter(true, true, false, false, dt);
            for (int i = 0; i < 25; i++) lockOn.Filter(true, false, false, true, dt);
            lockOn.Filter(false, false, true, true, dt);
            lockOn.Clear();
            Assert.False(lockOn.Locked);
            Assert.Equal((false, false, true), lockOn.Filter(false, false, false, true, dt));
            Assert.Equal((false, false, false), lockOn.Filter(false, false, false, false, dt));
            // Off: nothing changes.
            var off = new ClickLock();
            off.Filter(true, true, false, true, dt);
            for (int i = 0; i < 40; i++) off.Filter(true, false, false, true, dt);
            Assert.Equal((false, false, true), off.Filter(false, false, true, true, dt));
        }

        [Fact]
        public void AssistedAndMercyWinsCount_ReleasedNeverDoes()
        {
            Assert.True(AchievementRules.CountsTug(TugOutcome.PlayerWins, false));
            Assert.True(AchievementRules.CountsTug(TugOutcome.EntityWins, false));
            Assert.False(AchievementRules.CountsTug(TugOutcome.Released, false));
            Assert.False(AchievementRules.CountsTug(TugOutcome.None, false));
            Assert.False(AchievementRules.CountsTug(TugOutcome.PlayerWins, true));
        }

        [Fact]
        public void TheContestCapMakesTheFourthAMercyContest()
        {
            var n2 = DifficultyTable.For(2, DifficultyMode.Normal);
            Assert.Equal(3, n2.TugContestCap);
            Assert.False(n2.MercyByCap(2));
            Assert.True(n2.MercyByCap(3));
            Assert.Equal(3, DifficultyTable.For(3, DifficultyMode.Normal).TugContestCap);
            var n1 = DifficultyTable.For(1, DifficultyMode.Normal);
            Assert.Equal(0, n1.TugContestCap);
            Assert.False(n1.MercyByCap(10));
        }

        [Fact]
        public void ReelTablesMatchSection15()
        {
            // herPull, GripBase, surge, rampDelay, ramp, finishMax per column: N1, N2, N3, Story.
            var rows = new[]
            {
                (DifficultyTable.For(1, DifficultyMode.Normal), 25f, 0.62f, 60f, 3.0f, 15f, 140f),
                (DifficultyTable.For(2, DifficultyMode.Normal), 40f, 0.68f, 90f, 2.5f, 25f, 150f),
                (DifficultyTable.For(3, DifficultyMode.Normal), 55f, 0.74f, 120f, 2.0f, 35f, 160f),
                (DifficultyTable.For(2, DifficultyMode.Story), -8f, 0.41f, 0f, 99f, 0f, 140f),
            };
            foreach (var (p, her, gripBase, surge, rampDelay, ramp, finishMax) in rows)
            {
                var r = p.Tug.reel;
                Assert.Equal(TugModel.Speed, p.Tug.model);
                Assert.Equal(her, r.herPull);
                Assert.Equal(gripBase, p.GripBase);
                Assert.Equal(surge, r.surge);
                Assert.Equal(rampDelay, p.Tug.rampDelay);
                Assert.Equal(ramp, r.reelRampPerSecond);
                Assert.Equal(finishMax, r.finishMax);
                Assert.Equal(110f, r.finishMin);
                Assert.Equal(90f, r.herLine);
                bool story = p.Mode == DifficultyMode.Story;
                Assert.Equal(story ? 150f : 300f, r.reelCap);
                Assert.Equal(story ? 1f : 1.5f, r.reelGain);
                Assert.Equal(1.2f, r.fadeFirst);
                Assert.Equal(0.5f, r.fadeLater);
                Assert.Equal(0.5f, r.regrip);
                Assert.Equal(0.5f, r.keepFraction);
                Assert.Equal(0.25f, r.surgeSeconds);
                Assert.Equal(0.15f, r.telegraphSeconds);
                Assert.Equal(0f, r.holdSeconds);
            }
            // Mercy: grip 0.15, no ramp, no surges.
            var mercy = DifficultyTable.For(3, DifficultyMode.Normal).TugFor(new AdaptiveAssist(), true);
            Assert.Equal(0f, mercy.reel.surge);
            Assert.Equal(0f, mercy.reel.reelRampPerSecond);
            Assert.Equal(11.1f, 55f * AdaptiveAssist.MercyGrip / 0.74f, 1);
            // A contest's settings are a deep copy: changing them never touches the night's table.
            var n1 = DifficultyTable.For(1, DifficultyMode.Normal);
            n1.TugFor(new AdaptiveAssist()).reel.herPull = 99f;
            Assert.Equal(25f, n1.Tug.reel.herPull);
        }
    }
}
