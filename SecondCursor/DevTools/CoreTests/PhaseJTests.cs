using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core;
using SecondCursor.Core.Art;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase J (third blind playtest): letting go with the bar past its line keeps the file, the tug and every ending say what
    /// happened, the finale answers a goodbye with the action, and the clarity text (remote tasks, the code prompt, the rounds
    /// line, the clock's zero).
    /// </summary>
    public class PhaseJTests
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

        // ------------------------------------------------------------------ the release rule

        static TugOfWar Night1Tug(bool grace)
        {
            var p = DifficultyTable.For(1, DifficultyMode.Normal);
            return new TugOfWar(p.TugFor(new AdaptiveAssist(0, AdaptiveAssist.MinLevel), false, grace));
        }

        static float Night1Grip(int tugLosses) => DifficultyTable.For(1, DifficultyMode.Normal).Grip(tugLosses, new AdaptiveAssist(0, AdaptiveAssist.MinLevel));

        /// <summary>
        /// One contest along x: she grabbed 60 px from the player and drifts away (held still in the read grace); the player moves
        /// by <paramref name="player"/>(t, dt) (px along -x, away from her), and lets go when <paramref name="letGo"/> says so.
        /// </summary>
        static (TugOutcome outcome, float time, float leadAtRelease) Play(TugOfWar tug, float grip, Func<float, float, float> player,
            Func<float, TugOfWar, bool> letGo, float limit = 8f)
        {
            float drift = TugOfWar.EntityDriftSpeed(grip), t = 0f, px = 400f, ex = 460f, lead = -1f;
            bool holding = true;
            while (t < limit)
            {
                if (holding && letGo(t, tug)) { holding = false; lead = tug.PlayerLead; }
                var outcome = tug.Step(Dt, new Vec2(px, 300f), holding, new Vec2(ex, 300f), grip);
                if (outcome != TugOutcome.None) return (outcome, t, lead);
                t += Dt;
                if (!tug.InReadGrace) ex += drift * Dt;
                if (holding) px -= player(t, Dt);
            }
            return (TugOutcome.None, t, lead);
        }

        [Fact]
        public void TheKeepLineIsSixtyPercentAndEveryNightStartsBelowIt()
        {
            Assert.Equal(0.6f, TugOfWar.ReleaseKeepLead);
            for (int night = 1; night <= 3; night++)
            {
                var tug = new TugOfWar(DifficultyTable.For(night, DifficultyMode.Normal).Tug);
                Assert.True(tug.PlayerLead < TugOfWar.ReleaseKeepLead, "night " + night + " starts at " + tug.PlayerLead);
                Assert.False(tug.KeepsOnRelease);
            }
        }

        [Fact]
        public void NightsTwoAndThreeLetTheCursorsComeFurtherApartAndNightOneIsUntouched()
        {
            // Phase J balance: the release rule on top of the read grace made those nights' first tug a near-sure win for an
            // average player; their own tension limits bring it back to about 90%. Night 1 (the demo) keeps its values.
            Assert.Equal(315f, DifficultyTable.For(2, DifficultyMode.Normal).Tug.maxTension);
            Assert.Equal(330f, DifficultyTable.For(3, DifficultyMode.Normal).Tug.maxTension);
            Assert.Equal(280f, DifficultyTable.For(1, DifficultyMode.Normal).Tug.maxTension);
        }

        [Fact]
        public void LettingGoPastTheLineKeepsTheFileAtOnce()
        {
            var tug = Night1Tug(false);
            var r = Play(tug, Night1Grip(0), (t, dt) => 450f * dt, (t, x) => x.KeepsOnRelease);
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
            Assert.True(r.leadAtRelease >= TugOfWar.ReleaseKeepLead);
            Assert.False(tug.KeepsOnRelease);   // over: nothing more to keep
        }

        [Fact]
        public void LettingGoBeforeTheLineStillHandsHerTheFile()
        {
            var tug = Night1Tug(false);
            var r = Play(tug, Night1Grip(0), (t, dt) => 450f * dt, (t, x) => t >= 0.1f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            Assert.True(r.leadAtRelease < TugOfWar.ReleaseKeepLead);
            Assert.True(r.time < 0.2f, "she takes it within the release grace, not later: " + r.time);
        }

        [Fact]
        public void HoldingStillThroughTheGraceAndLettingGoLoses()
        {
            var r = Play(Night1Tug(true), Night1Grip(0), (t, dt) => 0f, (t, x) => t >= 1.1f);
            Assert.Equal(TugOutcome.EntityWins, r.outcome);
            Assert.Equal(0.5f, r.leadAtRelease, 2);
        }

        /// <summary>The blind testers' bridge input: 30 px jumps every 0.1 s for 0.8 s, 0.3 s still, then the button goes up.</summary>
        static float TesterJumps(float t, float dt)
        {
            int before = (int)Math.Floor((t - dt) / 0.1f + 1e-4f), now = (int)Math.Floor(t / 0.1f + 1e-4f);
            return now > before && now <= 8 ? 30f : 0f;
        }

        [Fact]
        public void TheTestersPatternFromTheGrabKeepsTheFirstContest()
        {
            var r = Play(Night1Tug(true), Night1Grip(0), TesterJumps, (t, x) => t >= 1.1f);
            Assert.Equal(TugOutcome.PlayerWins, r.outcome);
        }

        [Fact]
        public void APlayerWhoReadsTheBarWinsEveryFirstContest()
        {
            foreach (float speed in new[] { 300f, 400f, 500f, 600f })
            {
                var r = Play(Night1Tug(true), Night1Grip(0), (t, dt) => t >= 0.25f ? speed * dt : 0f, (t, x) => x.KeepsOnRelease);
                Assert.True(r.outcome == TugOutcome.PlayerWins, speed + " px/s: " + r.outcome);
                Assert.True(r.time < 1.2f, speed + " px/s took " + r.time.ToString("0.00") + " s");
            }
        }

        [Fact]
        public void TheSecondContestStillWantsARealPull()
        {
            // No grace after the first contest: holding still and letting go at once loses; a firm pull that waits for the line wins.
            var still = Play(Night1Tug(false), Night1Grip(1), (t, dt) => 0f, (t, x) => t >= 0.3f);
            Assert.Equal(TugOutcome.EntityWins, still.outcome);
            var firm = Play(Night1Tug(false), Night1Grip(1), (t, dt) => t >= 0.25f ? 500f * dt : 0f, (t, x) => x.KeepsOnRelease);
            Assert.Equal(TugOutcome.PlayerWins, firm.outcome);
        }

        // ------------------------------------------------------------------ what the fight says

        [Fact]
        public void TheTugSaysWhatWinningLooksLike()
        {
            if (!Present) return;
            var db = Demo();
            Assert.Contains("UNTIL THE BAR IS YOURS", db.Text("tug.label"));
            Assert.StartsWith("THE BAR IS YOURS", db.Text("tug.ahead"));
            Assert.Contains("BIN OR A FOLDER", db.Text("tug.ahead"));
            Assert.StartsWith("YOU LET GO TOO EARLY", db.Text("tug.lost.release"));
            Assert.False(db.HasText("tug.lost"), "tug.lost is no longer read (Review J10)");
            foreach (var key in new[] { "notify.conflict", "notify.conflict.release", "notify.conflict.won", "drop.missed.desktop", "drop.missed", "tug.refused" })
                Assert.Contains("{0}", db.Text(key));
            Assert.Contains("{1}", db.Text("drop.missed"));
            Assert.Equal("It is holding the file. Pull harder, or leave it: the file is not going anywhere.", db.Text("task.017.lost.hint"));
            Assert.Contains("until the bar is yours", db.Task(ContentIds.TaskShred017).hint);
            Assert.Contains("passes the line", db.Text("help.body"));
            db.Variant = "deck";
            foreach (var key in new[] { "tug.label", "tug.ahead", "tug.lost.release", "notify.conflict", "notify.conflict.release", "tug.refused" })
                Assert.Contains("R2", db.Text(key));
            Assert.Contains("until the bar is yours", db.Task(ContentIds.TaskShred017).hint);
        }

        // ------------------------------------------------------------------ endings say why

        [Fact]
        public void EveryNightThreeEndingNamesItsCause()
        {
            Assert.Equal("end.shred.cause", Night3Rules.EndingCauseKey(Night3Exit.Shred, "time", false));
            Assert.Equal("end.logoff.cause", Night3Rules.EndingCauseKey(Night3Exit.LogOff, "time", false));
            Assert.Equal("end.keep.cause.stay", Night3Rules.EndingCauseKey(Night3Exit.Keep, "confirm", false));
            Assert.Equal("end.keep.cause.seat", Night3Rules.EndingCauseKey(Night3Exit.Keep, "seat", false));
            Assert.Equal("end.keep.cause.seat.logoff", Night3Rules.EndingCauseKey(Night3Exit.Keep, "seat", true));
            Assert.Equal("end.keep.cause.time", Night3Rules.EndingCauseKey(Night3Exit.Keep, "time", false));
            Assert.Equal("end.keep.cause.time", Night3Rules.EndingCauseKey(Night3Exit.Keep, "time", true));
            if (!Present) return;
            var night3 = Night(3);
            var demo = Demo();
            foreach (var key in new[] { "end.shred.cause", "end.logoff.cause", "end.keep.cause.stay", "end.keep.cause.seat", "end.keep.cause.seat.logoff",
                                        "end.keep.cause.time", "end.n2.outcome.finished", "end.n2.outcome.archived", "end.n2.outcome.missed",
                                        "logoff.confirm.watched", "logoff.cancelled.seat", "logoff.cancelled.time" })
            {
                Assert.True(night3.HasText(key), "missing " + key);
                Assert.Equal("", demo.Text(key, ""));
            }
            Assert.Equal("You logged off with session 017 still open.", night3.Text("end.logoff.cause"));
            Assert.Contains("before your log off finished", night3.Text("end.keep.cause.seat.logoff"));
            Assert.Contains("017", night3.Text("logoff.confirm"));
        }

        // ------------------------------------------------------------------ the finale's words

        [Fact]
        public void AGoodbyeIsAnsweredWithTheAction()
        {
            if (!Present) return;
            var db = Night(3);
            var ex = db.Exchange(ContentIds.ExchangeN3Final);
            var engine = new DialogueEngine(db);
            foreach (var said in new[] { "goodbye ellen. i will let you go", "goodbye", "bye", "go", "leave", "i will let you go", "let go" })
            {
                var r = engine.Respond(ex, said);
                Assert.True(r.Tag == "letgo", "'" + said + "' was answered as " + r.Tag);
                Assert.Contains("THEN PUT ME IN THE BIN", r.Lines);
            }
            foreach (var said in new[] { "stay", "stay with me ellen", "i wont let you go", "i will never let you go", "please don't go", "don't leave me" })
                Assert.True(engine.Respond(ex, said).Tag == "stay", "'" + said + "' should be stay");
            foreach (var said in new[] { "let me go", "log off", "go home" })
                Assert.Equal("go", engine.Respond(ex, said).Tag);
            Assert.Equal("name", engine.Respond(ex, "ellen").Tag);
            Assert.Equal(new[] { "SAY STAY", "OR PUT ME IN THE BIN" }, db.LineSet("n3_final_third").lines);
            foreach (var resp in ex.responses)
                foreach (var line in resp.reply)
                    Assert.Matches("^[A-Z ]+$", line);
        }

        // ------------------------------------------------------------------ clarity

        [Fact]
        public void RemoteTasksAreOptionalAndTheRoundsHaveAQueueLine()
        {
            if (!Present) return;
            var n2 = Night(2);
            foreach (var id in new[] { "e2_door_log", "e2_lookup_163", "e2_hide_214", "e2_archive_209" })
            {
                Assert.StartsWith("Optional.", n2.Task(id).hint);
                Assert.StartsWith("Optional.", n2.Task(id).hintDeck);
            }
            Assert.Contains("you don't have to do it", n2.Email(ContentIds.MailN2Briefing).body);
            var n3 = Night(3);
            Assert.StartsWith("Optional", n3.Task(ContentIds.TaskE3LetGo).hint);
            var until = n3.Task(ContentIds.TaskN3RoundsUntil);
            Assert.NotNull(until);
            Assert.Equal("Wait", until.type);
            Assert.Equal("Rounds until 3:30. Nothing to do. Stay seated.", until.title);
            Assert.Null(Demo().Task(ContentIds.TaskN3RoundsUntil));
            Assert.Contains("(4 digits)", n3.Text("auth.body"));
            Assert.Contains("It's the minute she stopped. Personnel still has the time.", n3.Email(ContentIds.MailN3RuthComment).body);
        }

        [Fact]
        public void TheClocksZeroCannotPassForAnEight()
        {
            var zero = PixelFontData.Glyphs['0'];
            var eight = PixelFontData.Glyphs['8'];
            Assert.Equal(eight[0].Length, zero[0].Length);
            // The zero's sides run unbroken from row 1 to row 6 (the eight is pinched at its waist, row 3)...
            for (int row = 1; row <= 6; row++)
                Assert.True(zero[row][0] == '#' && zero[row][zero[row].Length - 1] == '#', "row " + row + " of the zero is open");
            Assert.Equal('.', eight[3][0]);
            // ...and its slash is not a centre bar: the rows above and below the middle differ.
            Assert.NotEqual(zero[2], zero[5]);
            int differ = 0;
            for (int row = 0; row < zero.Length; row++)
                for (int col = 0; col < zero[row].Length; col++)
                    if (zero[row][col] != eight[row][col]) differ++;
            Assert.True(differ >= 6, "only " + differ + " pixels tell 0 from 8");
        }
    }
}
