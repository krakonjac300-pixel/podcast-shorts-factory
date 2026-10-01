using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase K (the owner's suggestions 1, 3, 5 and the fourth blind playtest): counters that say who did the work, a tug that
    /// scores exactly the arrow and says what to change after a loss, deadlines in real time, the shelf check's result lines, the
    /// Night 3 countdown to KEEP, one camera rule, and the Jotter's status lines.
    /// </summary>
    public class PhaseKTests
    {
        const float Dt = 1f / 60f;
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));

        static T Read<T>(string folder, string name) where T : class
        {
            string path = Path.Combine(Dir, folder, name + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }

        static ContentPack Pack(string folder) => new ContentPack
        {
            Strings = Read<StringTableData>(folder, "strings"), Story = Read<StoryData>(folder, "story"),
            FileSystem = Read<FileSystemData>(folder, "filesystem"), Emails = Read<EmailsData>(folder, "emails"),
            Employees = Read<EmployeesData>(folder, "employees"), WorkOrders = Read<WorkOrdersData>(folder, "workorders"),
            Tasks = Read<TasksData>(folder, "tasks"), Dialogue = Read<DialogueData>(folder, "dialogue"),
        };

        static ContentDatabase Night(int night)
        {
            var pack = Pack("").Overlay(Pack("full"));
            for (int n = 2; n <= night; n++) pack = pack.Overlay(Pack("night" + n));
            return pack.Build();
        }

        static ContentDatabase Demo() => Pack("").Build();

        // ------------------------------------------------------------------ trustworthy counters

        sealed class World : ITaskWorld
        {
            public readonly VirtualFileSystem Files;
            public World(VirtualFileSystem files) { Files = files; }
            public bool IsEmailRead(string emailId) => false;
            public string FolderOf(string fileId) => Files.FolderOf(fileId);
            public bool IsShredded(string fileId) => Files.GetFile(fileId)?.Shredded ?? false;
            public string DecisionFor(string orderId) => null;
            public bool IsFileOpenedByPlayer(string fileId) => false;
            public bool IsEmployeeViewedByPlayer(string employeeId) => false;
            public string MovedBy(string fileId) => Files.GetFile(fileId)?.MovedBy;
        }

        static VirtualFileSystem Batch46()
        {
            var fs = new VirtualFileSystem(new FileSystemData
            {
                folders = new[] { new FolderData { id = "intake", name = "Intake" }, new FolderData { id = "archive", name = "Archive" }, new FolderData { id = "desktop", name = "Desktop" } },
                files = new[] { "a", "b", "c", "d" }.Select(x => new FileData { id = "batch46_" + x, name = "batch46_" + x + ".dat", type = "dat", folder = "intake" }).ToArray(),
            });
            return fs;
        }

        static WorkTaskManager Tasks(VirtualFileSystem fs) => new WorkTaskManager(new[]
        {
            new TaskData { id = "t2_archive_batch46", title = "Archive Batch 46 (4 files)", type = "MoveFile", param = "archive",
                           targets = new[] { "batch46_a", "batch46_b", "batch46_c", "batch46_d" } },
        }, new World(fs));

        [Fact]
        public void AMoveRemembersWhichOtherSessionMadeIt()
        {
            var fs = Batch46();
            fs.Move("batch46_a", "archive", Actor.Player);
            fs.Move("batch46_b", "archive", Actor.Entity, "session 017");
            fs.Move("batch46_c", "desktop", Actor.Entity);
            fs.Move("batch46_d", "archive", Actor.System);
            Assert.Null(fs.GetFile("batch46_a").MovedBy);
            Assert.Equal("session 017", fs.GetFile("batch46_b").MovedBy);
            Assert.Equal("another session", fs.GetFile("batch46_c").MovedBy);
            Assert.Null(fs.GetFile("batch46_d").MovedBy);
            // The player moving it again takes it back.
            fs.Move("batch46_b", "intake", Actor.Player);
            Assert.Null(fs.GetFile("batch46_b").MovedBy);
        }

        [Fact]
        public void TheCounterSaysWhoDidTheWork()
        {
            var fs = Batch46();
            var tasks = Tasks(fs);
            tasks.Activate("t2_archive_batch46");
            var t = tasks.Get("t2_archive_batch46");
            Assert.Equal("0/4", t.ProgressText);
            fs.Move("batch46_a", "archive", Actor.Player);
            tasks.Evaluate();
            Assert.Equal("1/4", t.ProgressText);
            fs.Move("batch46_b", "archive", Actor.Entity, "session 017");
            tasks.Evaluate();
            Assert.Equal("2/4, 1 by session 017", t.ProgressText);
            fs.Move("batch46_c", "archive", Actor.Entity, "session 209");
            tasks.Evaluate();
            Assert.Equal("3/4, 1 by session 017, 1 by session 209", t.ProgressText);
            // A file taken back out no longer counts, for anyone.
            fs.Move("batch46_b", "desktop", Actor.Entity, "session 017");
            tasks.Evaluate();
            Assert.Equal("2/4, 1 by session 209", t.ProgressText);
        }

        [Fact]
        public void HelpThatChangesOnlyTheAttributionStillRaisesARevision()
        {
            var fs = Batch46();
            var tasks = Tasks(fs);
            tasks.Activate("t2_archive_batch46");
            fs.Move("batch46_a", "archive", Actor.Player);
            tasks.Evaluate();
            int rev = tasks.Revision;
            // Same count, different doer: the Work Queue must redraw its line.
            fs.Move("batch46_a", "intake", Actor.Player);
            fs.Move("batch46_b", "archive", Actor.Entity, "session 017");
            tasks.Evaluate();
            Assert.True(tasks.Revision > rev);
            Assert.Equal("1/4, 1 by session 017", tasks.Get("t2_archive_batch46").ProgressText);
        }

        [Fact]
        public void TaskProgressFormatsPlainAndHelpedCounts()
        {
            Assert.Equal("0/3", TaskProgress.Format(0, 3, null));
            Assert.Equal("3/4", TaskProgress.Format(3, 4, new List<KeyValuePair<string, int>>()));
            Assert.Equal("3/4, 1 by session 017", TaskProgress.Format(3, 4, new[] { new KeyValuePair<string, int>("session 017", 1) }));
        }

        [Fact]
        public void AStoryNoteCanBeFiledUnderOneTarget()
        {
            var tasks = new WorkTaskManager(new[] { new TaskData { id = "t3_shelf_check", type = "DecideOrder", targets = new[] { "wo_3340", "wo_3342" } } }, new World(Batch46()));
            int rev = tasks.Revision;
            tasks.SetTargetNote("t3_shelf_check", "wo_3342", "WO-3342 rejected");
            Assert.Equal("WO-3342 rejected", tasks.Get("t3_shelf_check").TargetNotes["wo_3342"]);
            Assert.True(tasks.Revision > rev);
        }

        // ------------------------------------------------------------------ the tug: the arrow is what counts

        static TugOfWar Tug(int night, bool grace, float hitch = 0f)
        {
            var s = DifficultyTable.For(night, DifficultyMode.Normal).TugFor(new AdaptiveAssist(0, AdaptiveAssist.MinLevel), false, grace);
            s.readGrace = Math.Max(s.readGrace, hitch);
            return new TugOfWar(s);
        }

        static float Grip(int night, int losses) => DifficultyTable.For(night, DifficultyMode.Normal).Grip(losses, new AdaptiveAssist(0, AdaptiveAssist.MinLevel));

        /// <summary>
        /// A contest with the arrow fixed along -x (her drift along +x, held during the grace): the player's pointer moves by
        /// <paramref name="move"/>(t, dt) and lets go when the bar is theirs.
        /// </summary>
        static (TugOutcome outcome, float time, TugCoach coach) Play(TugOfWar tug, float grip, Func<float, float, Vec2> move, float limit = 8f)
        {
            tug.PullAxis = new Vec2(-1f, 0f);
            var coach = new TugCoach(new Vec2(-1f, 0f));
            float drift = TugOfWar.EntityDriftSpeed(grip), t = 0f;
            var p = new Vec2(400f, 300f);
            var e = new Vec2(420f, 300f);
            bool holding = true;
            while (t < limit)
            {
                if (holding && tug.KeepsOnRelease) holding = false;
                coach.Step(Dt, p, holding);
                var outcome = tug.Step(Dt, p, holding, e, grip);
                if (outcome != TugOutcome.None) return (outcome, t, coach);
                t += Dt;
                if (!tug.InReadGrace) e = e + new Vec2(drift * Dt, 0f);
                if (holding) p = p + move(t, Dt);
            }
            return (TugOutcome.None, t, coach);
        }

        static Func<float, float, Vec2> Along(float speed, float react = 0.25f) => (t, dt) => t >= react ? new Vec2(-speed * dt, 0f) : Vec2.Zero;

        [Fact]
        public void ABarReaderWinsTheFirstAndSecondContestOfEveryNight()
        {
            // 0.25 s to react, a continuous pull along the arrow, letting go once the bar is theirs: every first contest from 300 px/s;
            // the second contest (no read grace, only the grab hitch) from 400 px/s on Nights 2 and 3, which keep their Phase F/J values.
            for (int night = 1; night <= 3; night++)
                foreach (float speed in new[] { 300f, 400f, 500f, 600f })
                {
                    var first = Play(Tug(night, true), Grip(night, 0), Along(speed));
                    Assert.True(first.outcome == TugOutcome.PlayerWins, "night " + night + " first, " + speed + " px/s: " + first.outcome);
                    if (night > 1 && speed < 400f) continue;
                    var second = Play(Tug(night, false, 0.3f), Grip(night, 0), Along(speed));
                    Assert.True(second.outcome == TugOutcome.PlayerWins, "night " + night + " second, " + speed + " px/s: " + second.outcome);
                }
        }

        [Fact]
        public void PullingAcrossTheArrowIsNoPullAtAll()
        {
            // With the arrow fixed, a fast move at right angles to it earns nothing: she keeps the file.
            var r = Play(Tug(1, false, 0.3f), Grip(1, 0), (t, dt) => new Vec2(0f, 500f * dt));
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            Assert.Equal(TugLossReason.WrongWay, r.coach.Classify(false, 400f));
        }

        [Fact]
        public void TheGrabHitchHoldsHerPullButCountsThePlayers()
        {
            var tug = Tug(1, false, 0.3f);
            Assert.True(tug.InReadGrace);
            var r = Play(tug, Grip(1, 2), (t, dt) => Vec2.Zero, 0.25f);
            Assert.Equal(TugOutcome.None, r.outcome);
            Assert.Equal(0.5f, tug.PlayerLead, 3);
        }

        [Fact]
        public void TheFinalLeadIsWhereTheBarWasJustBeforeTheEnd()
        {
            var tug = Tug(1, false, 0.3f);
            var r = Play(tug, Grip(1, 0), Along(450f));
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
            Assert.True(tug.FinalLead >= TugOfWar.ReleaseKeepLead, "lead " + tug.FinalLead);
        }

        // ------------------------------------------------------------------ what to change after a loss

        [Fact]
        public void LossReasonsFollowWhatThePointerDid()
        {
            float full = DifficultyTable.For(1, DifficultyMode.Normal).Tug.pullSpeedForFullStrength;
            var still = Play(Tug(1, false, 0.3f), Grip(1, 0), (t, dt) => Vec2.Zero);
            Assert.Equal(TugLossReason.HeldStill, still.coach.Classify(false, full));
            var wrong = Play(Tug(1, false, 0.3f), Grip(1, 0), (t, dt) => new Vec2(300f * dt, 0f));
            Assert.Equal(TugLossReason.WrongWay, wrong.coach.Classify(false, full));
            var slow = Play(Tug(1, false, 0.3f), Grip(1, 0), Along(100f));
            Assert.Equal(TugOutcome.EntityWins, slow.outcome);
            Assert.Equal(TugLossReason.TooSlow, slow.coach.Classify(false, full));
            var stopped = Play(Tug(1, false, 0.3f), Grip(1, 0), (t, dt) => t < 0.15f ? new Vec2(-450f * dt, 0f) : Vec2.Zero);
            Assert.Equal(TugOutcome.EntityWins, stopped.outcome);
            Assert.Equal(TugLossReason.Stopped, stopped.coach.Classify(false, full));
            Assert.Equal(TugLossReason.LetGo, still.coach.Classify(true, full));
        }

        [Fact]
        public void AFirmPullAlongTheArrowIsNeverBlamedOnThePlayer()
        {
            var coach = new TugCoach(new Vec2(0f, -1f));
            var p = new Vec2(300f, 400f);
            for (int i = 0; i < 40; i++)
            {
                coach.Step(Dt, p, true);
                p = p + new Vec2(0f, -500f * Dt);
            }
            Assert.Equal(TugLossReason.Overpowered, coach.Classify(false, 400f));
            Assert.Equal("DOWN", TugCoach.DirectionName(coach.Net));
        }

        [Fact]
        public void DirectionsHaveEightPlainNames()
        {
            Assert.Equal("RIGHT", TugCoach.DirectionName(new Vec2(1f, 0f)));
            Assert.Equal("UP-RIGHT", TugCoach.DirectionName(new Vec2(1f, 1f)));
            Assert.Equal("UP", TugCoach.DirectionName(new Vec2(0f, 1f)));
            Assert.Equal("UP-LEFT", TugCoach.DirectionName(new Vec2(-1f, 1f)));
            Assert.Equal("LEFT", TugCoach.DirectionName(new Vec2(-1f, 0.1f)));
            Assert.Equal("DOWN-LEFT", TugCoach.DirectionName(new Vec2(-1f, -1f)));
            Assert.Equal("DOWN", TugCoach.DirectionName(new Vec2(0.1f, -1f)));
            Assert.Equal("DOWN-RIGHT", TugCoach.DirectionName(new Vec2(1f, -1f)));
            Assert.Equal("NOWHERE", TugCoach.DirectionName(Vec2.Zero));
        }

        [Fact]
        public void EveryLossReasonHasItsWordsWithTheArrowAndDeckWording()
        {
            if (!Present) return;
            var db = Demo();
            foreach (var key in new[] { "tug.lost.still", "tug.lost.wrong" }) Assert.Contains("{0}", db.Text(key));
            Assert.Contains("{1}", db.Text("tug.lost.wrong"));
            Assert.Contains("{0}", db.Text("tug.label"));
            Assert.StartsWith("YOU LET GO TOO EARLY", db.Text("tug.lost.release"));
            foreach (var key in new[] { "notify.conflict.still", "notify.conflict.wrong", "notify.conflict.stopped", "notify.conflict.slow" })
                Assert.Contains("{0}", db.Text(key));
            Assert.Contains("{2}", db.Text("notify.conflict.wrong"));
            Assert.Equal("SESSION 017 GRABBED IT BACK.\nIT WILL NOT LET GO TONIGHT.", db.Text("tug.grabbed.back"));
            db.Variant = "deck";
            foreach (var key in new[] { "tug.label", "tug.lost.still", "tug.lost.wrong", "tug.lost.stopped", "tug.lost.slow", "tug.lost.pulled" })
                Assert.Contains("PULL", db.Text(key));
            Assert.Contains("R2", db.Text("tug.label"));
        }

        // ------------------------------------------------------------------ deadlines in real terms

        [Fact]
        public void DeadlinesAreAlsoSaidInRealTime()
        {
            // Night 2's order: 29 shift minutes over the 150 s the clock is set to take.
            Assert.Equal(150f, TaskDeadline.RealSeconds("3:00 AM", 151.0, 29f / 150f, false), 1);
            Assert.Equal("about 2 min 30 s", TaskDeadline.Approx(150f));
            // Night 3 after 7:00: five minutes at one per 12 s.
            Assert.Equal(60f, TaskDeadline.RealSeconds("7:05 AM", 420.0, 1f / 12f, false), 1);
            Assert.Equal("about 1 min", TaskDeadline.Approx(60f));
            Assert.Equal("about 40 s", TaskDeadline.Approx(41f));
            Assert.Equal("a few seconds", TaskDeadline.Approx(6f));
            Assert.Equal(-1f, TaskDeadline.RealSeconds("3:00 AM", 169.0, 0.06f, true));
            Assert.Equal(-1f, TaskDeadline.RealSeconds("", 169.0, 0.06f, false));
            Assert.Equal(0f, TaskDeadline.RealSeconds("3:00 AM", 181.0, 0.06f, false));
        }

        // ------------------------------------------------------------------ the shelf check

        static readonly string[] Shelves =
        {
            "SHELF 12: 031 VOSS A.", "SHELF 13: 017 MARSH E. (EMPTY)", "SHELF 14: 142 LUNDY T.", "SHELF 15: 188 ACHTERBERG M.",
            "SHELF 16: 209 PRUITT G. (RESERVED)", "SHELF 17: 163 NAKAMURA J.", "SHELF 18: 214 ROURKE C. (RESERVED)", "SHELF 19: 175 CASTELL D.",
        };

        [Fact]
        public void EachShelfOrderIsCheckedAgainstTheLabelItNames()
        {
            Assert.Equal("SHELF 18: 214 ROURKE C. (RESERVED)", Night3Rules.ShelfCaptionFor("Sublevel C, SHELF 18", Shelves));
            Assert.Equal("approve", Night3Rules.ShelfDecision("Sublevel C, SHELF 18", "214", Shelves));
            Assert.Equal("reject", Night3Rules.ShelfDecision("Sublevel C, SHELF 16", "188", Shelves));
            Assert.Null(Night3Rules.ShelfCaptionFor("Sublevel C, SHELF 40", Shelves));
            if (!Present) return;
            var db = Night(3);
            Assert.Contains("RESERVED", db.Task(ContentIds.TaskN3Shelf).description);
            foreach (var id in new[] { ContentIds.Order3340, ContentIds.Order3341, ContentIds.Order3342 })
                Assert.Contains("RESERVED", db.Order(id).instructions);
            Assert.Contains("{4}", db.Text("shelf.result.against"));
            Assert.Contains("follows the rule", db.Text("shelf.result.match"));
        }

        // ------------------------------------------------------------------ the final choice

        [Fact]
        public void AtSevenTheQueueCountsDownToKeepAndNamesTheWaysOut()
        {
            if (!Present) return;
            var db = Night(3);
            var t = db.Task(ContentIds.TaskN3LogOffBy);
            Assert.NotNull(t);
            Assert.Equal("Wait", t.type);
            Assert.Equal("7:05 AM", t.deadline);
            Assert.Equal(Night3Rules.KeepTime, TaskDeadline.Minutes(t.deadline));
            // Phase N: the task is given at 6:41 and states the whole rule; at 7:00 its title becomes the "keeps you" countdown.
            Assert.Contains("keeps you", t.description);
            Assert.Contains("keeps you", db.Text("task.logoff.now.title"));
            Assert.Contains("Log Off", t.description);
            Assert.Contains("Disposal bin", t.description);
            Assert.Contains("Camera Viewer", t.hint);
            Assert.Contains("7:05", db.Text("logoff.available"));
            Assert.Contains("keeps you", db.Text("finale.onit"));
            Assert.Null(Demo().Task(ContentIds.TaskN3LogOffBy));
        }

        [Fact]
        public void ALateShredIsOnNightTwosCard()
        {
            if (!Present) return;
            var db = Night(2);
            string line = db.Format("end.n2.outcome.late", "3:03 AM", 3);
            Assert.Contains("3:03 AM", line);
            Assert.Contains("3 min", line);
            Assert.Equal("", Demo().Text("end.n2.outcome.late", ""));
        }

        // ------------------------------------------------------------------ one camera rule

        [Fact]
        public void EveryCameraHintAndMailSaysTheSameRule()
        {
            if (!Present) return;
            var db = Night(3);
            var n2 = Night(2);
            foreach (var text in new[] { n2.Task(ContentIds.TaskN2RoundsWatch).hint, db.Task(ContentIds.TaskN3RoundsUntil).hint })
            {
                Assert.Contains("Never watch the camera that shows Custodial", text);
                Assert.Contains("Location now", text);
            }
            Assert.Contains("Location now", db.Task(ContentIds.TaskN3Shelf).hint);
            foreach (var text in new[] { n2.Email(ContentIds.MailN2SecurityRounds).body, db.Email(ContentIds.MailN3SecurityRounds).body })
            {
                Assert.DoesNotContain("keep watching", text);
                Assert.DoesNotContain("camera covering Custodial", text);
                Assert.Contains("Keep it open", text);
            }
            foreach (var t in db.Tasks.tasks.Concat(n2.Tasks.tasks))
                Assert.DoesNotContain("you can let it close", t.hint ?? "");
            Assert.Contains("{0}", db.Text("rounds.reopen"));
            Assert.Contains("Switch", db.Text("rounds.onit"));
            Assert.Contains("Double-click Camera Viewer", db.Text("camera.closed.custodial"));
        }

        // ------------------------------------------------------------------ Jotter and move confirmations

        [Fact]
        public void EveryReplyAndMoveIsConfirmedInWords()
        {
            if (!Present) return;
            var db = Demo();
            // Phase N: a reply typed ahead is sent when the other side stops (no more grey "(not sent)").
            Assert.False(db.HasText("notepad.notsent"));
            Assert.Contains("Your turn", db.Text("notepad.status.turn"));
            foreach (var key in new[] { "notepad.status.typing", "notepad.status.held" }) Assert.Contains("{1}", db.Text(key));
            Assert.Equal("Session {0}: {1}", db.Text("notepad.title.person"));
            Assert.Equal("Moved {0} to {1}.", db.Text("files.moved"));
            foreach (var key in new[] { "files.help.by", "files.unhelp.by" })
            {
                Assert.Contains("{2}", db.Text(key));
                Assert.Contains("{4}", db.Text(key));
            }
            Assert.Contains("{2}", db.Text("task.filed.rest"));
            Assert.Contains("{2}", db.Text("workqueue.deadline.real"));
            Assert.Equal("Quit SECOND CURSOR?", db.Text("end.card.quit.ask"));
        }

        [Fact]
        public void PhaseKTextHasNoLongDashes()
        {
            if (!Present) return;
            char em = (char)0x2014, en = (char)0x2013;
            foreach (var path in Directory.GetFiles(Dir, "*.json", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(path);
                Assert.False(text.IndexOf(em) >= 0 || text.IndexOf(en) >= 0, path);
            }
        }
    }
}
