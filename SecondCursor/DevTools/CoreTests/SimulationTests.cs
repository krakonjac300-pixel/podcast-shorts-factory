using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using Xunit;

namespace SecondCursor.Tests
{
    public class VirtualFileSystemTests
    {
        static VirtualFileSystem Make()
        {
            var db = new ContentDatabase(null, null, null, null, null, null, null, null); // all placeholders
            return new VirtualFileSystem(db.FileSystem);
        }

        [Fact]
        public void PlaceholdersProvideRequiredFolders()
        {
            var fs = Make();
            Assert.NotNull(fs.GetFolder(ContentIds.FolderIntake));
            Assert.NotNull(fs.GetFolder(ContentIds.FolderDisposal));
            Assert.Equal(ContentIds.FolderIntake, fs.FolderOf(ContentIds.File017));
        }

        [Fact]
        public void MoveShredRestore()
        {
            var fs = Make();
            var moved = new List<string>();
            fs.FileMoved += (f, from, to, a) => moved.Add(f.Id + ":" + from + ">" + to + ":" + a);
            Assert.True(fs.Move(ContentIds.FileLedger, ContentIds.FolderArchive, Actor.Player));
            Assert.Equal(ContentIds.FolderArchive, fs.FolderOf(ContentIds.FileLedger));
            Assert.Single(moved);
            Assert.False(fs.Move(ContentIds.FileLedger, ContentIds.FolderArchive, Actor.Player)); // same folder
            Assert.False(fs.Move(ContentIds.FileLedger, ContentIds.FolderRestricted, Actor.Player)); // locked
            Assert.False(fs.Move(ContentIds.FileLedger, ContentIds.FolderDisposal, Actor.Player)); // must shred

            Assert.True(fs.Shred(ContentIds.File017, Actor.Player));
            Assert.Null(fs.FolderOf(ContentIds.File017));
            Assert.DoesNotContain(fs.FilesIn(ContentIds.FolderIntake), f => f.Id == ContentIds.File017);
            Assert.True(fs.Restore(ContentIds.File017, ContentIds.FolderDesktop, Actor.Entity));
            Assert.Equal(ContentIds.FolderDesktop, fs.FolderOf(ContentIds.File017));
        }

        [Fact]
        public void ListingIsSortedAndPathsResolve()
        {
            var fs = Make();
            var files = fs.FilesIn(ContentIds.FolderIntake);
            for (int i = 1; i < files.Count; i++)
                Assert.True(string.Compare(files[i - 1].Name, files[i].Name, StringComparison.OrdinalIgnoreCase) <= 0);
            Assert.EndsWith("\\Intake\\employee_017.dat", fs.PathOf(fs.GetFile(ContentIds.File017)));
            Assert.True(fs.IsInsideLocked(ContentIds.FolderRestricted));
        }
    }

    public class TaskTests
    {
        sealed class World : ITaskWorld
        {
            public readonly HashSet<string> Read = new HashSet<string>();
            public VirtualFileSystem Fs;
            public readonly Dictionary<string, string> Decisions = new Dictionary<string, string>();
            public bool IsEmailRead(string id) => Read.Contains(id);
            public string FolderOf(string id) => Fs.FolderOf(id);
            public bool IsShredded(string id) => Fs.GetFile(id)?.Shredded ?? false;
            public string DecisionFor(string id) => Decisions.TryGetValue(id, out var d) ? d : null;
            public bool IsFileOpenedByPlayer(string id) => false;
            public bool IsEmployeeViewedByPlayer(string id) => false;
        }

        [Fact]
        public void TasksCompleteFromWorldStateIncludingEarlyCompletion()
        {
            var db = new ContentDatabase(null, null, null, null, null, null, null, null);
            var world = new World { Fs = new VirtualFileSystem(db.FileSystem) };
            var tm = new WorkTaskManager(db.Tasks.tasks, world);
            var done = new List<string>();
            tm.TaskCompleted += t => done.Add(t.Id);

            tm.Activate(ContentIds.TaskReadBriefing);
            Assert.Equal(ContentIds.TaskReadBriefing, tm.Current.Id);
            world.Read.Add(ContentIds.MailWelcome);
            tm.Evaluate();
            Assert.Contains(ContentIds.TaskReadBriefing, done);

            // Player archives the batch BEFORE the task is given: activating completes it immediately.
            world.Fs.Move(ContentIds.FileBatchA, ContentIds.FolderArchive, Actor.Player);
            world.Fs.Move(ContentIds.FileBatchB, ContentIds.FolderArchive, Actor.Player);
            tm.Activate(ContentIds.TaskArchiveBatch);
            Assert.Equal(2, tm.Get(ContentIds.TaskArchiveBatch).Progress);
            Assert.False(tm.IsCompleted(ContentIds.TaskArchiveBatch));
            world.Fs.Move(ContentIds.FileBatchC, ContentIds.FolderArchive, Actor.Player);
            tm.Evaluate();
            Assert.True(tm.IsCompleted(ContentIds.TaskArchiveBatch));

            tm.Activate(ContentIds.TaskVerify3317);
            world.Decisions[ContentIds.Order3317] = "reject";
            tm.Evaluate();
            Assert.True(tm.IsCompleted(ContentIds.TaskVerify3317));

            tm.Activate(ContentIds.TaskShred017);
            Assert.False(tm.IsCompleted(ContentIds.TaskShred017));
        }
    }

    public class DialogueTests
    {
        [Theory]
        [InlineData("Who ARE you?!", "who", true)]
        [InlineData("this is weird", "hi", false)]
        [InlineData("hi", "hi", true)]
        [InlineData("what the fucking hell", "fuck", true)]
        [InlineData("what are you", "what are you", true)]
        [InlineData("who's there", "who", true)]
        [InlineData("whole thing", "who", false)]
        [InlineData("employee 017", "017", true)]
        public void KeywordMatching(string input, string keyword, bool expected)
        {
            Assert.Equal(expected, DialogueEngine.Matches(DialogueEngine.Normalize(input), keyword));
        }

        [Fact]
        public void RespondUsesFirstMatchElseFallback()
        {
            var dialogue = new DialogueData
            {
                exchanges = new[]
                {
                    new ExchangeData
                    {
                        id = ContentIds.ExchangeStop,
                        entityLines = new[] { "STOP" },
                        responses = new[]
                        {
                            new ResponseData { keywords = new[] { "who", "what are you" }, reply = new[] { "NOT WHO" } },
                            new ResponseData { keywords = new[] { "why" }, reply = new[] { "BECAUSE" } },
                        },
                        fallback = new[] { "DONT" },
                        silence = new[] { "..." },
                    }
                }
            };
            var db = new ContentDatabase(null, null, null, null, null, null, null, dialogue);
            var engine = new DialogueEngine(db);
            var ex = engine.Get(ContentIds.ExchangeStop);
            Assert.Equal("NOT WHO", engine.Respond(ex, "who are you")?.Lines[0]);
            Assert.Equal("BECAUSE", engine.Respond(ex, "but WHY")?.Lines[0]);
            var fb = engine.Respond(ex, "banana");
            Assert.True(fb.IsFallback);
            Assert.Equal("DONT", fb.Lines[0]);
            Assert.Equal("swear", DialogueEngine.Categorize(DialogueEngine.Normalize("fuck off")));
        }
    }

    public class TugOfWarTests
    {
        const float Dt = 1f / 60f;
        const float EntityDrift = 100f; // the entity drags its end away at ~100 px/s during a fight

        static (TugOutcome outcome, float time) Simulate(Func<float, Vec2> player, Func<float, Vec2> entity, float pull, Func<float, bool> holding = null, float maxTime = 20f)
        {
            var tug = new TugOfWar();
            float t = 0f;
            while (t < maxTime)
            {
                var o = tug.Step(Dt, player(t), holding == null || holding(t), entity(t), pull);
                t += Dt;
                if (o != TugOutcome.None) return (o, t);
            }
            return (TugOutcome.None, t);
        }

        static Vec2 EntityAway(float t) => new Vec2(440f + EntityDrift * t, 300f);

        [Fact]
        public void IdlePlayerLosesInAboutASecond()
        {
            var r = Simulate(t => new Vec2(400, 300), EntityAway, 0.62f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            Assert.InRange(r.time, 0.6f, 2.0f);
        }

        [Fact]
        public void LettingGoLosesImmediately()
        {
            var r = Simulate(t => new Vec2(400, 300), EntityAway, 0.62f, t => t < 0.2f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            Assert.InRange(r.time, 0.2f, 0.4f);
        }

        [Fact]
        public void CommittedYankWins()
        {
            var r = Simulate(t => new Vec2(400f - 450f * t, 300f), EntityAway, 0.62f);
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
            Assert.InRange(r.time, 0.2f, 1.0f);
        }

        [Fact]
        public void WeakDragLoses()
        {
            var r = Simulate(t => new Vec2(400f - 110f * t, 300f), EntityAway, 0.62f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
        }

        [Fact]
        public void JigglingInPlaceIsNotAStrategy()
        {
            // +/-40 px at 4 Hz: fast, frantic, but no net pull.
            var r = Simulate(t => new Vec2(400f + 40f * (float)Math.Sin(t * Math.PI * 8), 300f), EntityAway, 0.62f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
        }

        [Fact]
        public void StrongerGripNeedsAHarderYank()
        {
            var normal = Simulate(t => new Vec2(400f - 450f * t, 300f), EntityAway, 1.3f);
            Assert.Equal(TugOutcome.EntityWins, normal.outcome);
            var hard = Simulate(t => new Vec2(400f - 1000f * t, 300f), EntityAway, 1.3f);
            Assert.Equal(TugOutcome.PlayerWins, hard.outcome);
        }

        [Fact]
        public void ObjectSitsBetweenCursors()
        {
            var tug = new TugOfWar();
            tug.Step(Dt, new Vec2(0, 0), true, new Vec2(100, 0), 0.62f);
            Assert.InRange(tug.ObjectPosition.x, 1f, 99f);
            Assert.InRange(tug.Strain, 0f, 1f);
        }
    }

    public class MovementTests
    {
        [Fact]
        public void EveryProfileLandsExactlyOnTarget()
        {
            string[] names = { "HumanLike", "Hesitant", "Aggressive", "Panicked", "Mechanical", "Lurking", "ImitatingPlayer" };
            var rng = new Rng(42);
            foreach (var n in names)
            {
                for (int i = 0; i < 200; i++)
                {
                    var from = new Vec2(rng.Range(0f, 960f), rng.Range(0f, 540f));
                    var to = new Vec2(rng.Range(0f, 960f), rng.Range(0f, 540f));
                    var plan = MovementPlanner.Plan(from, to, MovementProfiles.Get(n), rng, rng.Range(6f, 60f));
                    Assert.True(plan.Duration > 0f, n);
                    Assert.True(plan.Duration < 12f, n + " duration " + plan.Duration);
                    Assert.Equal(to, plan.Evaluate(plan.Duration));
                    Assert.Equal(to, plan.Evaluate(plan.Duration + 5f));
                    for (float t = 0; t < plan.Duration; t += 0.013f)
                    {
                        var p = plan.Evaluate(t);
                        Assert.True(p.IsFinite, n);
                        Assert.InRange(p.x, -400f, 1400f);
                        Assert.InRange(p.y, -400f, 1000f);
                    }
                }
            }
        }

        [Fact]
        public void PathIsContinuous()
        {
            var rng = new Rng(7);
            var plan = MovementPlanner.Plan(new Vec2(10, 10), new Vec2(900, 500), MovementProfiles.Panicked, rng);
            Vec2 prev = plan.Evaluate(0f);
            for (float t = 0.001f; t <= plan.Duration; t += 0.001f)
            {
                var p = plan.Evaluate(t);
                Assert.True(Vec2.Distance(prev, p) < 25f, "jump at " + t);
                prev = p;
            }
        }

        [Fact]
        public void FasterProfilesAreFaster()
        {
            float Avg(MovementProfileData p)
            {
                var rng = new Rng(3);
                float sum = 0f;
                for (int i = 0; i < 50; i++) sum += MovementPlanner.Plan(new Vec2(100, 100), new Vec2(700, 400), p, rng).Duration;
                return sum / 50f;
            }
            Assert.True(Avg(MovementProfiles.Aggressive) < Avg(MovementProfiles.HumanLike));
            Assert.True(Avg(MovementProfiles.HumanLike) < Avg(MovementProfiles.Hesitant));
            Assert.True(Avg(MovementProfiles.Hesitant) < Avg(MovementProfiles.Lurking));
        }

        [Fact]
        public void HomingFollowsAMovedTarget()
        {
            var plan = MovementPlanner.Plan(new Vec2(0, 0), new Vec2(300, 0), MovementProfiles.HumanLike, new Rng(9));
            var moved = new Vec2(300, 120);
            Assert.True(Vec2.Distance(plan.EvaluateHoming(plan.Duration, moved), moved) < 0.01f);
        }
    }

    public class RecordingTests
    {
        [Fact]
        public void RecordsAndInterpolates()
        {
            var rec = new CursorRecorder(60f, 1f / 30f);
            for (int i = 0; i <= 60; i++) rec.Add(i / 60f, new Vec2(i * 10f, 0f), i > 30);
            var r = rec.Last(1f, 1f);
            Assert.False(r.IsEmpty);
            Assert.InRange(r.Duration, 0.9f, 1.01f);
            var mid = r.Sample(0.5f);
            Assert.InRange(mid.x, 280f, 320f);
            Assert.True(r.Sample(r.Duration).down);
            var mirrored = r.Transformed((p, t) => new Vec2(960f - p.x, p.y));
            Assert.InRange(mirrored.Sample(0f).x, 950f, 960f);
        }
    }

    public class MiscTests
    {
        [Fact]
        public void ClockFormats()
        {
            var c = new GameClock(2, 5);
            Assert.Equal("2:05 AM", c.Format12());
            c.Rate = 1f;
            c.Tick(60f);
            Assert.Equal("3:05 AM", c.Format12());
            // Phase I: Set only moves the clock forward (an earlier time is refused); Reset starts a fresh clock anywhere.
            c.Set(0, 0);
            Assert.Equal("3:05 AM", c.Format12());
            c.Reset(0, 0);
            Assert.Equal("12:00 AM", c.Format12());
        }

        [Fact]
        public void StringsFallBackAndFormatSafely()
        {
            var db = new ContentDatabase(new StringTableData { entries = new[] { new StringEntry { key = "x", value = "Hello {0}" }, new StringEntry { key = "bad", value = "oops {1}" } } },
                null, null, null, null, null, null, null);
            Assert.Equal("Hello Bob", db.Format("x", "Bob"));
            Assert.Equal("oops {1}", db.Format("bad", "a"));
            Assert.Equal("NEXUS OS", db.Text("os.name"));
            Assert.Equal("fallback", db.Text("missing.key", "fallback"));
            Assert.NotEmpty(db.Problems);
        }

        [Fact]
        public void FlagsSnapshotRoundTrip()
        {
            var f = new NarrativeFlags();
            f.Set(Flags.EntitySeen);
            f.Increment(Flags.CounterShredAttempts);
            f.Increment(Flags.CounterShredAttempts);
            f.SetChoice("trusted", "yes");
            var g = new NarrativeFlags();
            g.Restore(f.Snapshot());
            Assert.True(g.Has(Flags.EntitySeen));
            Assert.Equal(2, g.Get(Flags.CounterShredAttempts));
            Assert.Equal("yes", g.GetChoice("trusted"));
        }

        [Fact]
        public void MemoryCountsAndTrust()
        {
            var m = new EntityMemory();
            m.Record(MemoryKind.OpenedApp, "mail", 1f);
            m.Record(MemoryKind.OpenedApp, "files", 2f);
            m.Record(MemoryKind.OpenedApp, "files", 3f);
            m.Record(MemoryKind.ResistedEntity, "017", 4f);
            Assert.Equal(3, m.Count(MemoryKind.OpenedApp));
            Assert.Equal(2, m.Count(MemoryKind.OpenedApp, "files"));
            Assert.Equal("files", m.MostFrequent(MemoryKind.OpenedApp));
            Assert.True(m.Trust < 0f);
            Assert.Equal(1, m.Resistance);
        }
    }
}
