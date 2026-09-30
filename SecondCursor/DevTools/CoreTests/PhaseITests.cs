using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase I (second blind playtest): the tug-of-war's read grace and its numbers, a clock that never runs backwards, mail
    /// received at the shift clock, tasks that can rewrite themselves, and the new on-screen text (blocked order, expired
    /// requests, the finale's two exits, the rounds hint).
    /// </summary>
    public class PhaseITests
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

        // ------------------------------------------------------------------ read grace

        /// <summary>
        /// Night 1 Normal's first contest as the bridge plays it: she grabs about 60 px from the player and drags her end away at
        /// the runtime's drift speed (held still during the read grace); the player holds the button and, from
        /// <paramref name="startAt"/> seconds after the grab, pulls straight away from her at <paramref name="speed"/> px/s.
        /// </summary>
        static (TugOutcome outcome, float time, TugOfWar tug) FirstContest(float startAt, float speed, bool grace, float limit = 12f, DifficultyMode mode = DifficultyMode.Normal)
        {
            var p = DifficultyTable.For(1, mode);
            var assist = new AdaptiveAssist(mode == DifficultyMode.Story ? 2 : 0, AdaptiveAssist.MinLevel);
            var settings = p.TugFor(assist, false, grace);
            float pull = p.Grip(0, assist);
            var tug = new TugOfWar(settings);
            float drift = TugOfWar.EntityDriftSpeed(pull);
            float t = 0f, px = 400f, ex = 460f;
            while (t < limit)
            {
                var outcome = tug.Step(Dt, new Vec2(px, 300f), true, new Vec2(ex, 300f), pull);
                if (outcome != TugOutcome.None) return (outcome, t, tug);
                t += Dt;
                if (!tug.InReadGrace) ex += drift * Dt;
                if (t >= startAt) px -= speed * Dt;
            }
            return (TugOutcome.None, t, tug);
        }

        static IEnumerable<float> Speeds() => Enumerable.Range(0, 10).Select(i => 300f + 300f * i / 9f);

        [Fact]
        public void TheReadGraceIsOnlyForTheContestThatAskedForIt()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var assist = new AdaptiveAssist(0, AdaptiveAssist.MinLevel);
            Assert.Equal(0f, p.TugFor(assist).readGrace);
            Assert.Equal(0f, p.TugFor(assist, false, false).readGrace);
            Assert.Equal(DifficultyProfile.ReadGraceSeconds, p.TugFor(assist, false, true).readGrace);
            Assert.Equal(1.2f, DifficultyProfile.ReadGraceSeconds);
            // The profile's own settings never carry it (the next contest starts clean).
            Assert.Equal(0f, p.Tug.readGrace);
        }

        [Fact]
        public void HoldingStillCannotLoseInsideTheGraceButLosesRightAfterIt()
        {
            var r = FirstContest(startAt: 99f, speed: 0f, grace: true);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            // She only starts to count when the grace is over: the loss comes after 1.2 s plus the old time to lose (about 1 s).
            Assert.InRange(r.time, 1.2f, 3.2f);
            Assert.InRange(r.tug.ActiveElapsed, 0.3f, 2.2f);
            var without = FirstContest(startAt: 99f, speed: 0f, grace: false);
            Assert.Equal(TugOutcome.EntityWins, without.outcome);
            Assert.InRange(without.time, 0.5f, 1.5f);
        }

        [Fact]
        public void ThePullMeterAndOutcomeStayStillInsideTheGrace()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var assist = new AdaptiveAssist(0, AdaptiveAssist.MinLevel);
            var tug = new TugOfWar(p.TugFor(assist, false, true));
            for (float t = 0f; t < 1.15f; t += Dt)
            {
                var o = tug.Step(Dt, new Vec2(400f, 300f), true, new Vec2(460f, 300f), 0.62f);
                Assert.Equal(TugOutcome.None, o);
                Assert.True(tug.InReadGrace);
                Assert.Equal(0.5f, tug.EntityShare, 3);
                Assert.Equal(0.5f, tug.PlayerLead, 3);
            }
        }

        [Fact]
        public void LettingGoStillLosesAtOnceEvenInsideTheGrace()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var tug = new TugOfWar(p.TugFor(new AdaptiveAssist(0, AdaptiveAssist.MinLevel), false, true));
            TugOutcome o = TugOutcome.None;
            for (float t = 0f; t < 0.5f && o == TugOutcome.None; t += Dt)
                o = tug.Step(Dt, new Vec2(400f, 300f), t < 0.2f, new Vec2(460f, 300f), 0.62f);
            Assert.Equal(TugOutcome.EntityWins, o);
            Assert.True(tug.Elapsed < 0.4f);
        }

        [Fact]
        public void ThePlayersOwnPullCountsFromTheFirstFrameOfTheGrace()
        {
            // A hard early yank wins during the grace (it is never made slower).
            var early = FirstContest(0.1f, 600f, true);
            Assert.Equal(TugOutcome.PlayerWins, early.outcome);
            var old = FirstContest(0.1f, 600f, false);
            Assert.Equal(TugOutcome.PlayerWins, old.outcome);
            Assert.True(early.time <= old.time + 0.5f, "grace " + early.time + " s, old " + old.time + " s");
        }

        [Theory]
        [InlineData(0.25f)]
        [InlineData(0.8f)]
        [InlineData(1.2f)]
        public void AContinuousPullOf300To600WinsTheFirstContestWhenItStartsWithinTheGrace(float startAt)
        {
            foreach (float speed in Speeds())
            {
                var r = FirstContest(startAt, speed, true);
                Assert.True(r.outcome == TugOutcome.PlayerWins, "pull " + speed.ToString("0") + " px/s from " + startAt + " s: " + r.outcome + " after " + r.time.ToString("0.00") + " s");
            }
        }

        [Fact]
        public void WithoutTheGraceAPullThatStartsAfterHalfASecondLosesAsTheBridgeFound()
        {
            // Phase H measured on the bridge: a continuous pull within 0.25 s of her grab wins, a 0.5 s pause before it loses.
            int wins025 = Speeds().Count(s => FirstContest(0.25f, s, false).outcome == TugOutcome.PlayerWins);
            int wins08 = Speeds().Count(s => FirstContest(0.8f, s, false).outcome == TugOutcome.PlayerWins);
            Assert.True(wins025 >= 9, "0.25 s: " + wins025 + " of 10");
            Assert.True(wins08 <= 2, "0.8 s without the grace: " + wins08 + " of 10");
        }

        [Fact]
        public void TheAssistAndTheRampCountTheContestAfterTheGrace()
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            var assist = new AdaptiveAssist(0, AdaptiveAssist.MinLevel);
            var s = p.TugFor(assist, false, true);
            var tug = new TugOfWar(s);
            for (float t = 0f; t < 1.5f; t += Dt) tug.Step(Dt, new Vec2(400f, 300f), true, new Vec2(460f, 300f), 0.62f);
            Assert.InRange(tug.Elapsed, 1.49f, 1.52f);
            Assert.InRange(tug.ActiveElapsed, 0.29f, 0.33f);
            Assert.Equal(0f, new TugOfWar(p.TugFor(assist)).ActiveElapsed - 0f);
        }

        [Fact]
        public void StoryModeHasTheStandoffToo()
        {
            var r = FirstContest(startAt: 1.2f, speed: 250f, grace: true, limit: 20f, mode: DifficultyMode.Story);
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
        }

        // ------------------------------------------------------------------ the clock

        [Fact]
        public void TheClockNeverGoesBackWhateverTheStoryAsks()
        {
            var c = new GameClock(1, 52);
            c.Set(2, 49);
            c.Set(2, 10);        // an earlier time: refused
            Assert.Equal("2:49 AM", c.Format12());
            Assert.Equal(1, c.RefusedBackSets);
            c.Set(3, 0);
            c.Set(3, 0);         // the same minute again is not a step back
            Assert.Equal(1, c.RefusedBackSets);
            c.Tick(30f);         // 3:02 and a half
            double before = c.ExactMinutes;
            c.Set(3, 2);         // the minute it already shows keeps its fraction
            Assert.Equal(before, c.ExactMinutes);
            Assert.Equal(1, c.RefusedBackSets);
            c.Set(3, 0);         // earlier: refused and counted
            Assert.Equal(before, c.ExactMinutes);
            Assert.Equal(2, c.RefusedBackSets);
            Assert.Equal(0, c.Regressions);
        }

        [Fact]
        public void ARateBelowZeroIsHeldAtZero()
        {
            var c = new GameClock(2, 0);
            c.Rate = -3f;
            Assert.Equal(0f, c.Rate);
            c.Tick(100f);
            Assert.Equal("2:00 AM", c.Format12());
            c.Rate = 0.1f;
            c.Tick(60f);
            Assert.Equal("2:06 AM", c.Format12());
        }

        [Fact]
        public void ResetStartsAFreshClockAtAnyTime()
        {
            var c = new GameClock(1, 52);
            c.Set(6, 41);
            c.Reset(1, 52);
            Assert.Equal("1:52 AM", c.Format12());
            Assert.Equal(0, c.RefusedBackSets);
            Assert.Equal(0, c.Regressions);
            Assert.Equal(112.0, c.HighWater, 3);
        }

        /// <summary>The director's EnsureClockAtLeast: speed the clock up until it shows at least the time, then restore the speed.</summary>
        static void EnsureAtLeast(GameClock c, int hour, int minute, float overSeconds, List<double> samples)
        {
            double target = hour * 60 + minute;
            double missing = target - c.ExactMinutes;
            if (missing <= 0) return;
            float rate = c.Rate;
            bool frozen = c.Frozen;
            c.Frozen = false;
            c.Rate = (float)(missing / Math.Max(0.1f, overSeconds));
            for (float t = 0f; t < overSeconds + 2f && c.ExactMinutes < target; t += Dt) { c.Tick(Dt); samples.Add(c.ExactMinutes); }
            c.Set(hour, minute);
            samples.Add(c.ExactMinutes);
            c.Rate = rate;
            c.Frozen = frozen;
        }

        [Fact]
        public void Night2sDeadlineLandingAndNight3sFinaleFastForwardNeverStepBack()
        {
            var samples = new List<double>();
            var c = new GameClock(1, 52);
            void Run(float seconds) { for (float t = 0f; t < seconds; t += Dt) { c.Tick(Dt); samples.Add(c.ExactMinutes); } }

            // Night 2: held at 2:49, then 3:00 lands on the 150 s deadline; the shred fight runs past it.
            c.Rate = 0.06f;
            c.Set(2, 49);
            c.Frozen = true;
            Run(5f);
            c.Frozen = false;
            c.Rate = Math.Max(0.01f, (float)((180.0 - c.ExactMinutes) / 150f));
            Run(150f);
            Assert.True(c.TotalMinutes >= 180);
            Run(20f);                                   // past the deadline while a tug is still going
            EnsureAtLeast(c, 3, 0, 5f, samples);        // the rounds beat: "at least 3:00" must not move it back
            c.Rate = 0.06f;
            Run(30f);

            // Night 3's finale: 6:41, the idle fast-forward starts, then stops half way (the player moved), then runs again.
            c.Reset(6, 41);
            samples.Add(c.ExactMinutes);
            c.Rate = 0.09f;
            Run(20f);
            double target = 7 * 60;
            c.Rate = (float)((target - c.ExactMinutes) / 15f);
            Run(6f);                                     // fast-forward running
            c.Rate = 0.09f;                              // stopped by input: the rate goes back, the time stays
            Run(10f);
            c.Rate = (float)((target - c.ExactMinutes) / 15f);
            Run(16f);
            c.Rate = 1f / 12f;
            Run(30f);
            EnsureAtLeast(c, 7, 0, 25f, samples);        // KEEP confirmed: "it runs to seven" (already there)
            Run(10f);

            for (int i = 1; i < samples.Count; i++)
            {
                // The Reset to 6:41 is a new shift's clock, not a step: skip that one sample pair.
                if (samples[i - 1] > 6 * 60 - 1 && false) continue;
                if (Math.Abs(samples[i] - 6 * 60 - 41) < 1e-9 && samples[i - 1] > 3 * 60) continue;
                Assert.True(samples[i] + 1e-9 >= samples[i - 1], "clock stepped back at sample " + i + ": " + samples[i - 1] + " -> " + samples[i]);
            }
            Assert.Equal(0, c.Regressions);
            Assert.Equal(0, c.RefusedBackSets);
        }

        // ------------------------------------------------------------------ mail dates

        [Fact]
        public void MailReceivedTonightIsNeverDatedAfterTheClock()
        {
            // Night 2's "Re: remote activity" was dated 2:31 AM and arrived at 2:15 AM.
            Assert.Equal("Thu 11/19/98 2:15 AM", MailDates.Received("Thu 11/19/98 2:31 AM", 2, 2 * 60 + 15));
            // Already earlier than the clock: it keeps its own time.
            Assert.Equal("Thu 11/19/98 2:17 AM", MailDates.Received("Thu 11/19/98 2:17 AM", 2, 2 * 60 + 30));
            Assert.Equal("Thu 11/19/98 2:17 AM", MailDates.Received("Thu 11/19/98 2:17 AM", 2, 2 * 60 + 17));
            // Not tonight: briefings of earlier days and the 1987 message keep their dates.
            Assert.Equal("Mon 03/02/87 2:17 AM", MailDates.Received("Mon 03/02/87 2:17 AM", 1, 100));
            Assert.Equal("Wed 11/18/98 2:17 AM", MailDates.Received("Wed 11/18/98 2:17 AM", 3, 60));
            Assert.Equal("nonsense", MailDates.Received("nonsense", 1, 100));
        }

        [Fact]
        public void NightDatesMatchTheContentsCalendar()
        {
            Assert.Equal("Wed 11/18/98 12:00 AM", MailDates.NightDate(1).ToString(MailDates.Format, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("Thu 11/19/98 12:00 AM", MailDates.NightDate(2).ToString(MailDates.Format, System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("Fri 11/20/98 12:00 AM", MailDates.NightDate(3).ToString(MailDates.Format, System.Globalization.CultureInfo.InvariantCulture));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryAuthoredMailDateParses(int night)
        {
            if (!Present) return;
            foreach (var e in Night(night).Emails.emails)
                Assert.True(MailDates.TryParse(e.date, out _), e.id + ": " + e.date);
        }

        // ------------------------------------------------------------------ tasks that rewrite themselves

        sealed class World : ITaskWorld
        {
            public bool IsEmailRead(string emailId) => false;
            public string FolderOf(string fileId) => null;
            public bool IsShredded(string fileId) => false;
            public string DecisionFor(string orderId) => null;
            public bool IsFileOpenedByPlayer(string fileId) => false;
            public bool IsEmployeeViewedByPlayer(string employeeId) => false;
        }

        [Fact]
        public void ATaskCanBeRewrittenWhileItStaysTheSameTask()
        {
            var data = new TaskData { id = "t", title = "PRIORITY: Shred a", description = "d", hint = "h", type = "DeleteFile", targets = new[] { "a" } };
            var tm = new WorkTaskManager(new[] { data }, new World());
            tm.Activate("t");
            var t = tm.Get("t");
            Assert.Equal("PRIORITY: Shred a", t.Title);
            int rev = tm.Revision;
            tm.Rewrite("t", "blocked title", "blocked description", "blocked hint");
            Assert.True(tm.Revision > rev);
            Assert.Equal("blocked title", t.Title);
            Assert.Equal("blocked description", t.Description);
            Assert.Equal("blocked hint", t.Hint);
            Assert.True(tm.IsActive("t"));
            Assert.Equal("PRIORITY: Shred a", t.Data.title);
            // The same words again change nothing.
            rev = tm.Revision;
            tm.Rewrite("t", "blocked title", "blocked description", "blocked hint");
            Assert.Equal(rev, tm.Revision);
            tm.Rewrite("nobody", "x", "y", "z");
        }

        [Fact]
        public void ARemoteRequestThatRanOutStaysListedAsExpired()
        {
            var data = new TaskData { id = "e", title = "PUT HIM IN ARCHIVE", type = "MoveFile", targets = new[] { "x" }, param = "archive", author = "entity" };
            var tm = new WorkTaskManager(new[] { data }, new World());
            tm.Activate("e");
            tm.Withdraw("e", "expired");
            Assert.True(WorkTaskManager.IsListedWithdrawn(tm.Get("e")));
            Assert.Equal("expired", tm.Get("e").WithdrawNote);
        }

        // ------------------------------------------------------------------ the new words

        [Fact]
        public void TheTugLabelsSayWhyYouLostAndWhatSnatchingMeans()
        {
            if (!Present) return;
            var db = Night(1);
            Assert.Equal("YOU LET GO. HOLD THE BUTTON UNTIL YOU KEPT THE FILE.", db.Text("tug.lost.release"));
            Assert.Equal("SESSION 017 PULLED HARDER. DRAG FASTER, AWAY FROM IT.", db.Text("tug.lost.pulled"));
            Assert.Equal("SESSION 017 TOOK THE FILE WHILE YOU WEREN'T HOLDING IT.", db.Text("tug.snatch"));
            Assert.Contains("R2", db.Text("tug.lost.release.deck"));
            Assert.Contains("PULL FASTER", db.Text("tug.lost.pulled.deck"));
            Assert.Contains("while you were not holding it", db.Text("file.moved.by"));
        }

        [Fact]
        public void TheQuickStartResolvesTheLetItGoAdvice()
        {
            if (!Present) return;
            foreach (var variant in new[] { "", "deck" })
            {
                var db = Night(1);
                db.Variant = variant;
                string body = db.Text("quickstart.body");
                Assert.Contains("if a task tells you to shred a file, hold on and fight for it", body);
                Assert.Contains(variant == "deck" ? "when you choose Begin" : "when you click Begin", body);
                Assert.Contains("fight for it", db.Text("help.body"));
            }
        }

        [Fact]
        public void TheBlockedShredOrderAdmitsNobodyCanDoIt()
        {
            if (!Present) return;
            var db = Demo();
            string title = db.Text("task.blocked.017.title");
            Assert.Equal("PRIORITY: Shred employee_017.dat (blocked: held by session 017)", title);
            Assert.Contains("Nobody can", db.Text("task.blocked.017.hint"));
            Assert.Contains("cannot be shredded", db.Text("task.blocked.017.description"));
            // The card no longer reads as a failure.
            string kept = db.Format("end.n1.outcome.kept", "2:48 AM");
            Assert.Contains("Nobody could shred it", kept);
            Assert.DoesNotContain("still on the desktop", kept);
        }

        [Fact]
        public void TheRoundsHintsGiveTheRealRule()
        {
            if (!Present) return;
            foreach (var (night, id) in new[] { (2, "t2_rounds_watch"), (3, "t3_shelf_check") })
            {
                var t = Night(night).Task(id);
                Assert.Contains("keeps closing", t.hint);
                Assert.Contains("custodian", t.hint);
                Assert.Contains("Personnel 000", t.hint);
                Assert.Contains("different camera", t.hint);
            }
        }

        [Fact]
        public void TheNightTwoShredHintSaysWhereTheFileIsAndWhatToDoWhenItIsGrabbed()
        {
            if (!Present) return;
            var t = Night(2).Task("t2_shred_209");
            Assert.Contains("on the desktop", t.hint);
            Assert.Contains("grabs it", t.hint);
            Assert.Contains("Too late", Night(2).Text("shred.confirm.late"));
            Assert.Equal("expired", Night(2).Text("workqueue.withdrawn.expired"));
            Assert.Contains("renamed to", Night(2).Text("notify.renamed"));
            Assert.Contains("still counts", Night(2).Text("notify.renamed"));
        }

        [Fact]
        public void TheFinaleNamesBothExitsAndAsksToBePutInTheBin()
        {
            if (!Present) return;
            var db = Night(3);
            var ex = db.Exchange(ContentIds.ExchangeN3Final);
            Assert.Contains("SAY STAY", ex.entityLines);
            Assert.Contains("PUT ME IN THE BIN", ex.entityLines);
            var engine = new DialogueEngine(db);
            Assert.Equal("stay", engine.Respond(ex, "stay").Tag);
            var letgo = engine.Respond(ex, "how do i let you go?");
            Assert.Equal("letgo", letgo.Tag);
            Assert.Contains("PUT ME IN THE BIN", letgo.Lines);
            var steer = db.LineSet("n3_final_third").lines;
            Assert.Contains("SAY STAY", steer);
            var task = db.Task(ContentIds.TaskE3LetGo);
            Assert.NotNull(task);
            Assert.Equal("entity", task.author);
            Assert.Equal("DeleteFile", task.type);
            Assert.Contains("employee_017", task.targets);
            Assert.Matches("^[A-Z0-9 ?]+$", task.title);
            Assert.InRange(task.title.Split(' ').Length, 1, 6);
            Assert.Contains("Not assigned by Night Operations", task.description);
            Assert.Contains("holds on", task.hint);
            Assert.Equal(0f, task.timeout);
        }

        [Fact]
        public void NoNightTwoOrThreeSpoilerReachesTheDemoInThePhaseIStrings()
        {
            if (!Present) return;
            var demo = Demo();
            foreach (var key in new[] { "workqueue.withdrawn.expired", "shred.confirm.late", "notify.renamed" })
                Assert.Equal("", demo.Text(key, ""));
            foreach (var kv in Read<StringTableData>("", "strings").entries)
                foreach (var banned in new[] { "Personnel 000", "PUT ME IN THE BIN", "e3_letgo" })
                    Assert.False(kv.value.Contains(banned), kv.key + " mentions '" + banned + "' (demo spoiler)");
        }
    }
}
