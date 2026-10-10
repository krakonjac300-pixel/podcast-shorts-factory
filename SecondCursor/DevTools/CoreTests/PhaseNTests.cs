using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase N (the fifth blind playtest and the Phase M review): the tug's GET READY beat, one pull direction per file and 300 px of
    /// room, "too slowly" judged after the reaction, the end-of-shift rule at 6:41 and KEEP's cause lines, the race to No in words,
    /// replies that are sent, and the review's audio fixes.
    /// </summary>
    public class PhaseNTests
    {
        const float Dt = 1f / 60f;
        const float Ready = 0.4f, Hitch = 0.3f;
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

        // ------------------------------------------------------------------ GET READY

        /// <summary>A contest as the runtime sets it up: the GET READY beat, then the night's read grace or the grab hitch.</summary>
        static TugOfWar Tug(int night, bool first)
        {
            var s = DifficultyTable.For(night, DifficultyMode.Normal).TugFor(new AdaptiveAssist(0, AdaptiveAssist.MinLevel), false, first);
            s.readGrace = Math.Max(s.readGrace, Hitch);
            s.readySeconds = Ready;
            return new TugOfWar(s);
        }

        static float Grip(int night) => DifficultyTable.For(night, DifficultyMode.Normal).Grip(0, new AdaptiveAssist(0, AdaptiveAssist.MinLevel));

        /// <summary>
        /// The arrow is fixed along -x, her end drifts along +x once the beats are over; the pointer pulls along the arrow at
        /// <paramref name="speed"/> from <paramref name="react"/> seconds after the grab and lets go once the bar is past its line. The coach
        /// is fed from the end of GET READY, as the runtime does.
        /// </summary>
        static (TugOutcome outcome, float time, TugCoach coach) Play(TugOfWar tug, float grip, float speed, float react, float limit = 8f)
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
                if (!tug.InReady) coach.Step(Dt, p, holding);
                var outcome = tug.Step(Dt, p, holding, e, grip);
                if (outcome != TugOutcome.None) return (outcome, t, coach);
                t += Dt;
                if (!tug.InReadGrace) e = e + new Vec2(drift * Dt, 0f);
                if (holding && t >= react) p = p + new Vec2(-speed * Dt, 0f);
            }
            return (TugOutcome.None, t, coach);
        }

        [Fact]
        public void GetReadyScoresNothingButLettingGoStillLoses()
        {
            var tug = Tug(3, false);
            float start = tug.PlayerLead;
            // A hard pull during the beat moves nothing: the bar, her strength and the ramp wait for the fight.
            var p = new Vec2(400f, 300f);
            for (float t = 0f; t < Ready - Dt; t += Dt)
            {
                p = p + new Vec2(-10f, 0f);
                Assert.Equal(TugOutcome.None, tug.Step(Dt, p, true, new Vec2(420f, 300f), 2f));
                Assert.True(tug.InReady && tug.InReadGrace);
            }
            Assert.Equal(start, tug.PlayerLead, 4);
            Assert.Equal(0f, tug.ActiveElapsed);
            // Letting go below the line during GET READY is letting go.
            var early = Tug(1, false);
            early.Step(Dt, p, true, new Vec2(420f, 300f), 0.6f);
            TugOutcome o = TugOutcome.None;
            for (int i = 0; i < 10 && o == TugOutcome.None; i++) o = early.Step(Dt, p, false, new Vec2(420f, 300f), 0.6f);
            Assert.Equal(TugOutcome.EntityWins, o);
        }

        [Fact]
        public void APlayerWhoReactsAfterGetReadyWinsTheFirstAndSecondContestOfEveryNight()
        {
            // 0.25 s from the grab (the Phase K pattern) and 0.3 s after GET READY ends: every first contest from 300 px/s, every second one
            // from 400 px/s on Nights 2 and 3 (their Phase F/J values), as in Phase K.
            foreach (float react in new[] { 0.25f, Ready + 0.3f })
                for (int night = 1; night <= 3; night++)
                    foreach (float speed in new[] { 300f, 400f, 500f, 600f })
                    {
                        var first = Play(Tug(night, true), Grip(night), speed, react);
                        Assert.True(first.outcome == TugOutcome.PlayerWins, "night " + night + " first, " + speed + " px/s, react " + react + ": " + first.outcome);
                        if (night > 1 && speed < 400f) continue;
                        var second = Play(Tug(night, false), Grip(night), speed, react);
                        Assert.True(second.outcome == TugOutcome.PlayerWins, "night " + night + " second, " + speed + " px/s, react " + react + ": " + second.outcome);
                    }
        }

        [Fact]
        public void TooSlowlyIsJudgedOnThePullAfterTheReaction()
        {
            float full = DifficultyTable.For(3, DifficultyMode.Normal).Tug.pullSpeedForFullStrength;
            // A pull that starts a reaction after the fight is on and runs at 60% of full strength is a fair pull, not a slow one.
            var coach = new TugCoach(new Vec2(-1f, 0f));
            var p = new Vec2(400f, 300f);
            for (float t = 0f; t < 1.2f; t += Dt)
            {
                if (t >= TugCoach.ReactionSeconds) p = p + new Vec2(-full * 0.6f * Dt, 0f);
                coach.Step(Dt, p, true);
            }
            Assert.NotEqual(TugLossReason.TooSlow, coach.Classify(false, full));
            // A pull at a third of it still is too slow.
            var slow = new TugCoach(new Vec2(-1f, 0f));
            p = new Vec2(400f, 300f);
            for (float t = 0f; t < 1.2f; t += Dt)
            {
                p = p + new Vec2(-full / 3f * Dt, 0f);
                slow.Step(Dt, p, true);
            }
            Assert.Equal(TugLossReason.TooSlow, slow.Classify(false, full));
        }

        // ------------------------------------------------------------------ the arrow: one way per file, and room to pull

        const float W = 960f, H = 540f, Bottom = 28f;

        [Fact]
        public void TheArrowNeverPointsAtAnEdgeCloserThan300Px()
        {
            Assert.Equal(300f, TugGeometry.MinPlayerRoom);
            var bin = new Vec2(915f, 66f);
            for (float x = 120f; x <= 940f; x += 60f)
                for (float y = 50f; y <= 520f; y += 45f)
                    foreach (var off in new[] { new Vec2(-20f, 15f), new Vec2(20f, 10f), new Vec2(0f, -18f) })
                    {
                        var player = new Vec2(x, y);
                        var d = TugGeometry.EscapeDirection(player, player + off, bin, W, H, Bottom);
                        Assert.True(TugGeometry.RoomAlong(player, d * -1f, W, H, Bottom) >= TugGeometry.MinPlayerRoom, "grab at " + x + "," + y);
                    }
        }

        [Fact]
        public void ARetryOverTheSameFileKeepsItsWayWhenThereIsRoom()
        {
            var first = new Vec2(1f, 0f);   // her way: the player pulls LEFT
            var kept = TugGeometry.WithRoom(first, new Vec2(700f, 250f), W, H, Bottom);
            Assert.Equal(first.x, kept.x, 4);
            Assert.Equal(first.y, kept.y, 4);
            // At a grab with no room that way it turns as little as it must, and has the room.
            var turned = TugGeometry.WithRoom(new Vec2(-1f, 0f), new Vec2(880f, 70f), W, H, Bottom);
            Assert.True(TugGeometry.RoomAlong(new Vec2(880f, 70f), turned * -1f, W, H, Bottom) >= TugGeometry.MinPlayerRoom);
            // A file's first arrow asks for the margin too, so a retry grabbed up to that much further along the arrow keeps it.
            var bin = new Vec2(915f, 66f);
            foreach (var grab in new[] { new Vec2(600f, 200f), new Vec2(700f, 150f), new Vec2(300f, 400f) })
            {
                var way = TugGeometry.EscapeDirection(grab, grab + new Vec2(-15f, 10f), bin, W, H, Bottom, TugGeometry.MinPlayerRoom + TugGeometry.FirstArrowMargin);
                var retryAt = grab + way * -TugGeometry.FirstArrowMargin;
                var again = TugGeometry.WithRoom(way, retryAt, W, H, Bottom);
                Assert.Equal(way.x, again.x, 4);
                Assert.Equal(way.y, again.y, 4);
            }
        }

        // ------------------------------------------------------------------ the end-of-shift rule and KEEP's causes

        [Fact]
        public void KeepSaysWhatThePlayerWasTryingToDo()
        {
            Assert.Equal("end.keep.cause.letgo", Night3Rules.EndingCauseKey(Night3Exit.Keep, "letgo", false));
            Assert.Equal("end.keep.cause.logoffdenied", Night3Rules.EndingCauseKey(Night3Exit.Keep, "logoffdenied", false));
            Assert.Equal("end.keep.cause.logoffearly", Night3Rules.EndingCauseKey(Night3Exit.Keep, "logoffearly", false));
            Assert.Equal("end.keep.cause.logofftried", Night3Rules.EndingCauseKey(Night3Exit.Keep, "logofftried", false));
            Assert.Equal("end.keep.cause.time", Night3Rules.EndingCauseKey(Night3Exit.Keep, "time", false));
            Assert.Equal("end.keep.cause.stay", Night3Rules.EndingCauseKey(Night3Exit.Keep, "confirm", false));
            Assert.Equal("end.keep.subtitle", Night3Rules.KeepSubtitleKey("confirm"));
            foreach (var cause in new[] { "time", "seat", "letgo", "logoffdenied", "logoffearly", "logofftried" })
                Assert.Equal("end.keep.subtitle.kept", Night3Rules.KeepSubtitleKey(cause));
            if (!Present) return;
            var db = Night(3);
            Assert.Contains("You tried to let her go. Session 017 held on until 7:05 AM", db.Text("end.keep.cause.letgo"));
            Assert.Contains("ALLOW_LOGOFF=0", db.Text("end.keep.cause.logoffdenied"));
            Assert.Equal("You stayed.", db.Text("end.keep.subtitle"));
            Assert.Equal("Session 017 kept you.", db.Text("end.keep.subtitle.kept"));
            Assert.False(Demo().HasText("end.keep.cause.letgo"));
        }

        [Fact]
        public void TheEndOfShiftRuleIsWholeAt641AndOnlyInTheFullGame()
        {
            if (!Present) return;
            var db = Night(3);
            var t = db.Task(ContentIds.TaskN3LogOffBy);
            foreach (var part in new[] { "7:00", "7:05", "Log Off CROURKE", "ALLOW_LOGOFF=1", "Custodial", "Camera Viewer", "your chair", "KEEP", "Disposal bin", "stay" })
                Assert.Contains(part, t.description);
            Assert.Contains("7:00 and 7:05", t.title);
            string notice = db.Text("notify.endofshift");
            foreach (var part in new[] { "7:00 and 7:05", "Nexus menu", "Custodial", "your chair", "7:05 AM: session 017 keeps you" })
                Assert.Contains(part, notice);
            Assert.False(Demo().HasText("notify.endofshift"));
            Assert.Contains("look her up, 017", db.Email(ContentIds.MailN3RuthComment).body);
            Assert.Contains("don't watch them come", db.Email(ContentIds.MailN3RuthComment).body);
        }

        // ------------------------------------------------------------------ the race to No, replies, notices

        [Fact]
        public void TheRaceToNoIsSaidInTheDialogAndItsEndInANotice()
        {
            if (!Present) return;
            var db = Demo();
            Assert.Equal("{0} is reaching for No.", db.Text("race.reaching"));
            Assert.StartsWith("{0} is holding No for you.", db.Text("race.holding"));
            Assert.Contains("{0}", db.Text("race.covering"));
            Assert.Contains("reached No before you clicked Yes", db.Format("shred.cancelled.no", "employee_209.dat", "session 017"));
            Assert.Contains("pressed Cancel", db.Format("shred.cancelled.cancel", "employee_209.dat", "session 017"));
            foreach (var key in new[] { "shred.cancelled.no.you", "shred.cancelled.cancel.you", "race.idle.shred" }) Assert.True(db.HasText(key), key);
            Assert.True(Night(3).HasText("race.idle.logoff"));
            Assert.Contains("Try again from the Nexus menu", Night(3).Format("logoff.cancelled.by", "session 209"));
        }

        [Fact]
        public void TheTugAndTheJotterSayTheNewRulesOnScreen()
        {
            if (!Present) return;
            var db = Demo();
            Assert.Contains("GET READY TO DRAG {0}", db.Text("tug.ready"));
            Assert.True(db.HasText("tug.ready.deck"));
            Assert.Contains("sent when it stops", db.Text("notepad.status.typing"));
            Assert.Contains("Press Enter to send", db.Text("notepad.status.held"));
            Assert.Contains("waiting for your reply", db.Text("notify.jotter.waiting"));
            string help = db.Text("help.body");
            Assert.Contains("GET READY", help);
            Assert.Contains("Disposal bin", help);
            Assert.Contains("on the camera you picked last", help);
            Assert.DoesNotContain("(not sent)", help);
            var n3 = Night(3);
            Assert.Contains("now on {1}", n3.Text("camera.closed.custodial"));
            Assert.Contains("comes back on {2}", n3.Text("camera.closed.custodial"));
            Assert.Contains("still counts", n3.Format("notify.damaged", "batch47_b.dat"));
        }

        // ------------------------------------------------------------------ review M: audio

        [Fact]
        public void StoryMomentsWaitOutATugOrADialogForTenSeconds()
        {
            Assert.Equal(10f, ScareRules.StoryEventWindow);
            Assert.True((ScareRules.StoryEventGates & ScareGate.Tug) != 0);
            Assert.True((ScareRules.StoryEventGates & ScareGate.Dialog) != 0);
            Assert.True((ScareRules.IgnoreAllButStory & ScareGate.Typing) != 0);
        }

        [Fact]
        public void TheLimiterSilencesABrokenSampleAndCarriesOn()
        {
            var limiter = new PeakLimiter(ProceduralSoundBank.SampleRate);
            var data = new[] { float.NaN, 0.2f, float.PositiveInfinity, -0.3f, 0.5f, 0.5f };
            limiter.Process(data, 2);
            Assert.Equal(0f, data[0]);
            Assert.Equal(0f, data[1]);
            Assert.Equal(0f, data[2]);
            Assert.Equal(0f, data[3]);
            Assert.Equal(0.5f, data[4], 4);
            Assert.True(data.All(x => !float.IsNaN(x)));
        }
    }
}
