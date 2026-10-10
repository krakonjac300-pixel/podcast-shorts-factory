using System;
using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>Entity-authored tasks, withdrawals and the player-only task types (expansion spec 11.2).</summary>
    public class EntityTaskTests
    {
        sealed class World : ITaskWorld
        {
            public readonly HashSet<string> Opened = new HashSet<string>();
            public readonly HashSet<string> Viewed = new HashSet<string>();
            public VirtualFileSystem Fs = new VirtualFileSystem();
            public bool IsEmailRead(string id) => false;
            public string FolderOf(string id) => Fs.FolderOf(id);
            public bool IsShredded(string id) => Fs.GetFile(id)?.Shredded ?? false;
            public string DecisionFor(string id) => null;
            public bool IsFileOpenedByPlayer(string id) => Opened.Contains(id);
            public bool IsEmployeeViewedByPlayer(string id) => Viewed.Contains(id);
            public string CreditFor(TaskType type, string targetId) => null;
        }

        static TaskData Task(string id, string type, string author = "", params string[] targets) =>
            new TaskData { id = id, title = id, type = type, author = author, targets = targets };

        [Fact]
        public void OpenFileAndViewEmployeeCompleteFromPlayerOnlyQueries()
        {
            var world = new World();
            var tm = new WorkTaskManager(new[] { Task("door", "OpenFile", "entity", "b7_door_log"), Task("look", "ViewEmployee", "entity", "163") }, world);
            tm.Activate("door");
            tm.Activate("look");
            Assert.Equal(TaskType.OpenFile, tm.Get("door").Type);
            Assert.Equal(TaskType.ViewEmployee, tm.Get("look").Type);
            Assert.True(tm.Get("door").IsEntityAuthored);
            Assert.True(WorkTaskManager.IsEntityAuthored(tm.Get("look")));
            tm.Evaluate();
            Assert.False(tm.IsCompleted("door"));
            world.Opened.Add("b7_door_log");
            world.Viewed.Add("163");
            tm.Evaluate();
            Assert.True(tm.IsCompleted("door"));
            Assert.True(tm.IsCompleted("look"));
        }

        [Fact]
        public void WithdrawHidesATaskForGood()
        {
            var world = new World();
            var tm = new WorkTaskManager(new[] { Task("a", "OpenFile", "entity", "x"), Task("b", "OpenFile", "", "y") }, world);
            var withdrawn = new List<string>();
            tm.TaskWithdrawn += t => withdrawn.Add(t.Id);
            tm.Activate("a");
            tm.Activate("b");
            Assert.Equal("a", tm.Current.Id);
            tm.Withdraw("a");
            Assert.True(tm.IsWithdrawn("a"));
            Assert.Equal(new[] { "a" }, withdrawn.ToArray());
            // Current skips it, Activate cannot revive it, nothing completes it.
            Assert.Equal("b", tm.Current.Id);
            tm.Activate("a");
            Assert.True(tm.IsWithdrawn("a"));
            world.Opened.Add("x");
            tm.Evaluate();
            tm.ForceComplete("a");
            Assert.False(tm.IsCompleted("a"));
            // A completed task stays completed.
            world.Opened.Add("y");
            tm.Evaluate();
            tm.Withdraw("b");
            Assert.True(tm.IsCompleted("b"));
            Assert.Null(tm.Current);
        }

        [Fact]
        public void AHiddenTaskCanBeWithdrawnBeforeItIsGiven()
        {
            var tm = new WorkTaskManager(new[] { Task("a", "OpenFile", "entity", "x") }, new World());
            tm.Withdraw("a");
            Assert.True(tm.IsWithdrawn("a"));
            tm.Activate("a");
            Assert.False(tm.IsActive("a"));
        }
    }

    public class WholeWordKeywordTests
    {
        [Theory]
        [InlineData("ellen", "=ellen", true)]
        [InlineData("Ellen?", "=ellen", true)]
        [InlineData("is that you, ELLEN marsh", "=marsh", true)]
        [InlineData("excellent", "=ellen", false)]
        [InlineData("ellens", "=ellen", false)]
        [InlineData("ellens", "=ellens", true)]
        [InlineData("a mug of tea", "=mug", true)]
        [InlineData("smuggle", "=mug", false)]
        [InlineData("2:30", "=2", true)]
        [InlineData("about 12", "=2", false)]
        [InlineData("its 7", "=7", true)]
        [InlineData("17", "=7", false)]
        [InlineData("i read your notes", "=note", false)]
        public void EqualsPrefixMatchesWholeWordsOnly(string input, string keyword, bool expected)
        {
            Assert.Equal(expected, DialogueEngine.Matches(DialogueEngine.Normalize(input), keyword));
        }

        [Fact]
        public void TagsComeBackWithTheReply()
        {
            var dialogue = new DialogueData
            {
                exchanges = new[]
                {
                    new ExchangeData
                    {
                        id = "ex", entityLines = new[] { "casey?" }, fallback = new[] { "sorry" }, silence = new[] { "you there?" },
                        responses = new[]
                        {
                            new ResponseData { keywords = new[] { "=ellen" }, reply = new[] { "shh" }, tag = "name" },
                            new ResponseData { keywords = new[] { "glasses" }, reply = new[] { "my glasses" }, tag = "glasses" },
                            new ResponseData { keywords = new[] { "who" }, reply = new[] { "gary" } },
                        },
                    },
                },
            };
            var engine = new DialogueEngine(new ContentDatabase(null, null, null, null, null, null, null, dialogue));
            var ex = engine.Get("ex");
            Assert.Equal("glasses", engine.Respond(ex, "your glasses").Tag);
            Assert.Equal("name", engine.Respond(ex, "Ellen?").Tag);
            Assert.Equal("", engine.Respond(ex, "excellent, who are you").Tag);
            Assert.Equal("gary", engine.Respond(ex, "excellent, who are you").Lines[0]);
            Assert.Equal("", engine.Respond(ex, "banana").Tag);
        }
    }

    public class CustodialRoundsTests
    {
        static RoundsConfig Night2() => RoundsConfig.Night2(DifficultyMode.Normal, 0f);

        [Fact]
        public void WatchingTheWrongCameraOrNothingNeverAdvances()
        {
            var r = new CustodialRounds(Night2());
            for (int i = 0; i < 600; i++)
            {
                r.Tick(0.05f, "cam03");
                r.Tick(0.05f, null);
                r.Tick(0.05f, "cam01");
            }
            Assert.Equal(0, r.Stage);
            Assert.Equal(0f, r.Meter);
        }

        [Fact]
        public void EachThresholdMovesOneStageAndTheLastEndsTheNight2RoundEarly()
        {
            var r = new CustodialRounds(Night2());
            var stages = new List<int>();
            bool final = false, cleared = false;
            r.StageAdvanced += s => stages.Add(s);
            r.ReachedFinal += () => final = true;
            r.SeatCleared += () => cleared = true;
            Assert.Equal("HallFar", r.FigureStage);
            Assert.Equal("cam02", r.FigureCamera);
            Tick(r, 4.9f, "cam02");
            Assert.Equal(0, r.Stage);
            Tick(r, 0.2f, "cam02");
            Assert.Equal(1, r.Stage);
            Assert.Equal("Corridor", r.FigureStage);
            Tick(r, 5.1f, "cam02");
            Assert.Equal("Doorway", r.FigureStage);
            Assert.Equal("cam03", r.FigureCamera);
            Tick(r, 5f, "cam02"); // it is on CAM 03 now
            Assert.False(final);
            Tick(r, 5.1f, "cam03");
            Assert.True(final);
            Assert.False(cleared);
            Assert.True(r.Finished);
            Assert.Equal(new[] { 1, 2 }, stages.ToArray());
            Assert.Equal(2, r.MaxStage);
            Assert.False(r.Tick(1f, "cam03"));
        }

        [Fact]
        public void ReopeningOntoTheFigureAddsThePenalty()
        {
            var r = new CustodialRounds(Night2());
            Assert.False(r.NotifyReopen("cam03"));
            Assert.Equal(0f, r.Meter);
            r.NotifyReopen("cam02");
            Assert.Equal(0.5f, r.Meter, 3);
            for (int i = 0; i < 9; i++) r.NotifyReopen("cam02");
            Assert.Equal(1, r.Stage);
        }

        [Fact]
        public void ARouteThatClearsTheSeatRaisesSeatCleared()
        {
            var c = new RoundsConfig
            {
                Stages = new[] { "Doorway", "Middle", "BehindChair" }, Cameras = new[] { "cam03", "cam03", "cam03" },
                WatchSeconds = 3f, AtLastStage = RoundsFinalRule.ClearSeat,
            };
            var r = new CustodialRounds(c);
            bool cleared = false;
            r.SeatCleared += () => cleared = true;
            Tick(r, 9.5f, "cam03");
            Assert.True(cleared);
            Assert.Equal("BehindChair", r.FigureStage);
        }

        [Fact]
        public void StoryIsSlowerHasNoPenaltyAndClampsAtMiddle()
        {
            var story = RoundsConfig.Night2(DifficultyMode.Story, 0f);
            Assert.Equal(10f, story.WatchSeconds);
            Assert.Equal(0f, story.ReopenPenalty);
            Assert.Equal(0.6f, story.CloseReactionMin, 3);
            var c = new RoundsConfig
            {
                Stages = new[] { "Corridor", "Doorway", "Middle", "BehindChair" }, Cameras = new[] { "cam02", "cam03", "cam03", "cam03" },
                WatchSeconds = 10f, AtLastStage = RoundsFinalRule.ClearSeat, ClampStage = 2,
            };
            var r = new CustodialRounds(c);
            bool cleared = false;
            r.SeatCleared += () => cleared = true;
            Tick(r, 10.1f, "cam02");
            Tick(r, 200f, "cam03");
            Assert.Equal("Middle", r.FigureStage);
            Assert.False(cleared);
        }

        [Fact]
        public void CloseReactionFollowsTrust()
        {
            Assert.Equal(0.8f, RoundsConfig.Night2(DifficultyMode.Normal, 0.5f).CloseReactionMin, 3);
            Assert.Equal(2.0f, RoundsConfig.Night2(DifficultyMode.Normal, -0.5f).CloseReactionMin, 3);
            var n = RoundsConfig.Night2(DifficultyMode.Normal, 0f);
            Assert.Equal(1.2f, n.CloseReaction(0f), 3);
            Assert.Equal(2.0f, n.CloseReaction(1f), 3);
            // Phase F: 80 s with forced opens at 0, 25 and 55 s (was 90 s with 0 and 45 s: two 43 s silent holes).
            Assert.Equal(new[] { 0f, 25f, 55f }, n.ForcedOpenTimes);
            Assert.Equal(80f, n.Duration);
            // Story keeps only the first forced open and repeats every 45 s.
            var story = RoundsConfig.Night2(DifficultyMode.Story, 0f);
            Assert.Equal(new[] { 0f }, story.ForcedOpenTimes);
            Assert.Equal(45f, story.ForcedOpenRepeatMin);
        }

        static void Tick(CustodialRounds r, float seconds, string cam)
        {
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt) r.Tick(dt, cam);
        }
    }

    public class TemplateTests
    {
        [Fact]
        public void PlayerLinesFillAndFallBack()
        {
            var t = NightTemplates.Tokens(new NarrativeFlags(), new[] { "who are you", "  leave   me  {alone}  ", "café ok" });
            Assert.Equal("who are you", t["line1"]);
            Assert.Equal("leave me alone", t["line2"]);
            Assert.Equal("caf ok", t["line3"]);
            var empty = NightTemplates.Tokens(new NarrativeFlags(), null);
            Assert.Equal(NightTemplates.NoReply, empty["line1"]);
            Assert.Equal(NightTemplates.NoReply, empty["line3"]);
            string longLine = new string('a', 60);
            Assert.Equal(40, NightTemplates.Tokens(new NarrativeFlags(), new[] { longLine })["line1"].Length);
        }

        [Fact]
        public void EveryTokenIsReplacedForBothGaryBranches()
        {
            var finished = new NarrativeFlags();
            finished.Set(MemoryFlags.N2FinishedGary);
            finished.Set(MemoryFlags.N2Hid214);
            var kept = new NarrativeFlags();
            kept.Set(MemoryFlags.N2KeptGary);
            kept.Set(MemoryFlags.N2WatchedToDoor);
            string all = "";
            foreach (var k in NightTemplates.Known) all += "{" + k + "}|";
            foreach (var flags in new[] { finished, kept })
            {
                string filled = NightTemplates.Fill(all, NightTemplates.Tokens(flags, new[] { "a", "b", "c" }));
                Assert.DoesNotContain("{", filled);
            }
            var f = NightTemplates.Tokens(finished, null);
            var k2 = NightTemplates.Tokens(kept, null);
            Assert.Equal("OK (LETHEWORTH)", f["p3"]);
            Assert.Equal("HELD", k2["p3"]);
            Assert.Equal("88%, SOURCE NOT FOUND", f["pct"]);
            Assert.Equal("96%", k2["pct"]);
            Assert.Equal("n/a     03:04   000", k2["n2door"]);
            Assert.Equal("keep {unknown} and {0}", NightTemplates.Fill("keep {unknown} and {0}", f));
        }
    }

    public class NightSaveTests
    {
        [Fact]
        public void FinishingNight2UnlocksNight3AndKeepsItsMemory()
        {
            var d = new SaveData();
            d.RecordNightComplete(new NightResult { Night = 1, EndingId = ContentIds.EndingN1Blackout, PlayerLines = new[] { "no" } });
            Assert.Equal(2, d.nightUnlocked);
            Assert.Equal(2, d.currentNight);
            var mem = new NarrativeFlags();
            mem.Set(MemoryFlags.N2KeptGary);
            mem.SetCounter(MemoryFlags.N2Obeyed, 2);
            d.RecordNightComplete(new NightResult { Night = 2, EndingId = ContentIds.EndingN2Kept, Memory = mem.Snapshot(MemoryFlags.Prefix), Trust = 0.3f });
            Assert.Equal(3, d.nightUnlocked);
            Assert.Equal(3, d.currentNight);
            Assert.Contains(ContentIds.EndingN2Kept, d.endingsSeen);
            Assert.Contains(MemoryFlags.N2KeptGary, d.memory.flags);
            // Night 2 never overwrites Night 1's remembered lines.
            Assert.Equal(new[] { "no" }, d.playerLines);
        }

        [Fact]
        public void ReplayingANightStartsWithoutItsOwnChoices()
        {
            var mem = new NarrativeFlags();
            mem.Set(MemoryFlags.N1Shredded017);
            mem.Set(MemoryFlags.N2KeptGary);
            mem.Set(MemoryFlags.N3SaidStay);
            mem.Set(MemoryFlags.SaidName);
            mem.SetCounter(MemoryFlags.N1TugWins, 3);
            mem.SetCounter(MemoryFlags.N2Obeyed, 2);
            var d = new SaveData { memory = mem.Snapshot(MemoryFlags.Prefix) };
            var n2 = new NarrativeFlags();
            n2.Merge(d.MemoryForNight(2));
            Assert.True(n2.Has(MemoryFlags.N1Shredded017));
            Assert.True(n2.Has(MemoryFlags.SaidName));
            Assert.False(n2.Has(MemoryFlags.N2KeptGary));
            Assert.False(n2.Has(MemoryFlags.N3SaidStay));
            Assert.Equal(3, n2.Get(MemoryFlags.N1TugWins));
            Assert.Equal(0, n2.Get(MemoryFlags.N2Obeyed));
            var n3 = new NarrativeFlags();
            n3.Merge(d.MemoryForNight(3));
            Assert.True(n3.Has(MemoryFlags.N2KeptGary));
            Assert.Equal(2, n3.Get(MemoryFlags.N2Obeyed));
            Assert.False(n3.Has(MemoryFlags.N3SaidStay));
        }
    }
}
