using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase R (sixth blind playtest: who is who, who won): the pointer tags' timing, the confirm race's countdown, the contest cards' wording and
    /// timing, the strings that name session 017 (and keep session 209 out of the demo), the order of events in the Night 1 instructions, the Night 1
    /// end card's honest outcome, and the end card's CAM 03 row counting only the player's own looks.
    /// </summary>
    public class PhaseRTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));
        static readonly char[] LongDashes = { (char)0x2014, (char)0x2013 };

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

        static ContentDatabase Demo() => Pack("").Build();

        static ContentDatabase Full() => Pack("").Overlay(Pack("full")).Overlay(Pack("night2")).Overlay(Pack("night3")).Build();

        // Every string Phase R added to the base (Night 1 and the demo) and what each is for.
        static readonly string[] NewBaseKeys =
        {
            "pointer.tag.you", "pointer.tag.entity", "title.legend.you", "title.legend.entity", "login.autofill", "window.opened.by", "wait.line",
            "race.rule", "race.progress", "contest.you", "contest.vs", "contest.strip.tug", "contest.strip.race", "contest.strip.cancel",
            "contest.card.won", "contest.card.lost", "contest.card.tug.lost", "contest.card.tug.bin", "contest.card.tug.kept", "contest.card.tug.tear",
            "contest.card.race.won", "contest.card.race.no", "contest.card.race.cancel", "end.n1.detail.kept", "end.n1.detail.shredded", "end.n1.shift",
            "end.n1.carry", "record.camopen",
        };

        // ------------------------------------------------------------------ the pointer tags

        [Fact]
        public void TheYouTagShowsThirtySecondsThenFadesAndAReminderNeverShortensIt()
        {
            Assert.Equal(30f, PointerTagRules.YouFirstSeconds);
            Assert.Equal(1f, PointerTagRules.YouAlpha(30f));
            Assert.Equal(1f, PointerTagRules.YouAlpha(PointerTagRules.FadeSeconds));
            Assert.InRange(PointerTagRules.YouAlpha(PointerTagRules.FadeSeconds / 2f), 0.49f, 0.51f);
            Assert.Equal(0f, PointerTagRules.YouAlpha(0f));
            Assert.Equal(0f, PointerTagRules.YouAlpha(-3f));
            // A 6 s reminder while 20 s are left changes nothing; with 2 s left it extends the tag.
            Assert.Equal(20f, PointerTagRules.Extend(20f, PointerTagRules.YouAgainSeconds));
            Assert.Equal(PointerTagRules.YouAgainSeconds, PointerTagRules.Extend(2f, PointerTagRules.YouAgainSeconds));
        }

        [Fact]
        public void ASecondPointersFirstAppearanceIsNoticedOncePerNight()
        {
            bool seen = false;
            Assert.False(PointerTagRules.IsFirstAppearance(false, ref seen));
            Assert.False(seen);
            Assert.True(PointerTagRules.IsFirstAppearance(true, ref seen));
            Assert.True(seen);
            Assert.False(PointerTagRules.IsFirstAppearance(true, ref seen));
            Assert.False(PointerTagRules.IsFirstAppearance(false, ref seen));
        }

        [Fact]
        public void TheSessionTagsAre017InTheDemoAnd209OnlyInTheFullGame()
        {
            if (!Present) return;
            var demo = Demo();
            Assert.Equal("YOU", demo.Text("pointer.tag.you"));
            Assert.Equal("SESSION " + PointerTagRules.EntityNumber, demo.Text("pointer.tag.entity"));
            Assert.False(demo.HasText("pointer.tag.gary"), "session 209 is Night 2 and 3 text: the demo must not carry it");
            Assert.Equal("SESSION " + PointerTagRules.GaryNumber, Full().Text("pointer.tag.gary"));
            // The title legend names the white arrow as yours and the dark one as session 017's.
            Assert.Contains("WHITE", demo.Text("title.legend.you"));
            Assert.Contains("DARK", demo.Text("title.legend.entity"));
            Assert.Contains("017", demo.Text("title.legend.entity"));
        }

        // ------------------------------------------------------------------ the confirm race's countdown

        [Fact]
        public void TheRaceCountdownIsTheDelayLeftPlusTheTripAcross()
        {
            Assert.Equal(2.5f, RaceCountdown.Eta(2.5f, 0f), 3);
            Assert.Equal(0.07f, RaceCountdown.Eta(0f, 20f), 3);                  // a short hop takes the least a move takes
            Assert.Equal(0.25f + 480f / RaceCountdown.TravelSpeed, RaceCountdown.Eta(0.25f, 480f), 3);
            Assert.Equal(0f, RaceCountdown.Eta(-1f, 0f), 3);                      // a delay that has run out leaves only the trip
            Assert.True(RaceCountdown.Eta(0.5f, 900f) > RaceCountdown.Eta(0.5f, 300f));
            Assert.Equal("2.5S", RaceCountdown.Format(2.5f));
            Assert.Equal("0.0S", RaceCountdown.Format(-0.4f));
            Assert.Equal("0.8S", RaceCountdown.Format(0.81f));
        }

        [Fact]
        public void TheFirstRaceRuleSaysWhatToDoAndWhoIsAgainstYou()
        {
            if (!Present) return;
            var demo = Demo();
            string rule = demo.Text("race.rule");
            Assert.Contains("Click Yes", rule);
            Assert.Contains("bar", rule);
            Assert.Contains("Session 017 will try to click No", rule);
            Assert.Contains("Cancel", demo.Text("race.progress"));
        }

        // ------------------------------------------------------------------ the strip and the cards

        [Theory]
        [InlineData(false, false, false, "contest.card.tug.lost")]
        [InlineData(false, true, true, "contest.card.tug.lost")]
        [InlineData(true, true, false, "contest.card.tug.bin")]
        [InlineData(true, false, true, "contest.card.tug.kept")]
        [InlineData(true, false, false, "contest.card.tug.tear")]
        public void ATugsCardIsChosenFromWhatHappened(bool won, bool intoBin, bool kept, string expected)
        {
            Assert.Equal(expected, ContestCopy.TugKey(won, intoBin, kept));
        }

        [Fact]
        public void ARacedShredsCardSaysHowItWentAndTheCardStaysAboutTwoAndAHalfSeconds()
        {
            Assert.Equal("contest.card.race.won", ContestCopy.ShredKey(true, false));
            Assert.Equal("contest.card.race.no", ContestCopy.ShredKey(false, true));
            Assert.Equal("contest.card.race.cancel", ContestCopy.ShredKey(false, false));
            Assert.Equal(2.5f, ContestCopy.CardSeconds(10));
            Assert.Equal(2.5f, ContestCopy.CardSeconds(70));   // the usual sentence is about 70 characters
            Assert.True(ContestCopy.CardSeconds(160) > 5f && ContestCopy.CardSeconds(160) < 6f);
            Assert.Equal(2.5f, ContestCopy.CardSeconds(-4));
        }

        [Fact]
        public void EveryContestWordExistsInTheDemoAndReadsLikeTheOperatingSystem()
        {
            if (!Present) return;
            var demo = Demo();
            foreach (var key in new[] { ContestCopy.StripTug, ContestCopy.StripRace, ContestCopy.StripCancel })
            {
                string strip = demo.Text(key);
                Assert.EndsWith(":", strip);
                Assert.Equal(strip.ToUpperInvariant(), strip);   // the strip is caps, like the tug panel
            }
            Assert.Equal("YOU WON", demo.Text("contest.card.won"));
            Assert.Equal("YOU LOST", demo.Text("contest.card.lost"));
            foreach (var key in NewBaseKeys.Where(k => k.StartsWith("contest.card.", StringComparison.Ordinal) && k.Contains(".tug.") || k.StartsWith("contest.card.race.", StringComparison.Ordinal)))
            {
                string text = demo.Text(key);
                Assert.True(char.IsUpper(text[0]), key + " starts a sentence");
                Assert.EndsWith(".", text);
                Assert.Contains("{0}", text);   // the file's name
                Assert.DoesNotContain("!", text);
                // One sentence about what happened and one about what it means or what to do, nothing longer (about 100 characters).
                Assert.InRange(text.Length, 30, 110);
            }
            Assert.Contains("Session 017", demo.Text("contest.card.tug.lost"));
            Assert.Contains("Click Yes", demo.Text("contest.card.tug.bin"));
        }

        // ------------------------------------------------------------------ every string: voice, dashes, the demo's walls

        [Fact]
        public void TheNewStringsHaveNoLongDashesAndNeverNameSessionTwoHundredNineInTheDemo()
        {
            if (!Present) return;
            var demo = Demo();
            foreach (var key in NewBaseKeys)
            {
                Assert.True(demo.HasText(key), "missing base string " + key);
                string text = demo.Text(key);
                Assert.True(text.IndexOfAny(LongDashes) < 0, key + " has a long dash");
                Assert.DoesNotContain("209", text);
                Assert.DoesNotContain("Gary", text);
            }
            foreach (var file in Directory.GetFiles(Dir, "*.json", SearchOption.AllDirectories))
                Assert.True(File.ReadAllText(file).IndexOfAny(LongDashes) < 0, Path.GetFileName(file) + " has a long dash");
            // The demo's whole content has no Night 2 and 3 name.
            foreach (var s in demo.Story.anomalyNotes) Assert.DoesNotContain("209", s);
        }

        [Fact]
        public void EveryAutomaticActionNamesItsActor()
        {
            if (!Present) return;
            var demo = Demo();
            var notes = demo.Story.anomalyNotes;
            Assert.Equal(10, notes.Length);
            foreach (var n in notes.Where(n => !n.StartsWith("Unrecognized", StringComparison.Ordinal)))
            {
                Assert.Contains("session 017", n, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("another user", n);
                Assert.DoesNotContain("remote session", n);
            }
            Assert.Equal("Selection changed by session 017.", notes[2]);
            Assert.StartsWith("Session 017 connected pointing device 2", notes[0]);
            Assert.Equal("{0} opened {1}.", demo.Text("window.opened.by"));
            Assert.Equal("Session 017 opened Camera Viewer.", string.Format(demo.Text("window.opened.by"), "Session 017", demo.Text("app.camera")));
            Assert.Contains("session 017", demo.Text("login.autofill"));
            Assert.Contains("previous session", demo.Text("login.autofill"));
            // Night 2 and 3: the third pointer is named in the full game only.
            var full = Full();
            Assert.Contains("session 209", full.Text("notify.pointer3"));
            Assert.Contains("session 209", full.Text("notify.pointer3.lost"));
            Assert.Contains("session 017", full.Text("notify.pointer2.back"));
        }

        [Fact]
        public void TheQuickStartSaysAPreviousSessionIsOpenAndTheWaitLineIsTheOwnersWords()
        {
            if (!Present) return;
            var demo = Demo();
            foreach (var variant in new[] { "", "deck" })
            {
                demo.Variant = variant;
                string body = demo.Text("quickstart.body");
                Assert.Contains("previous session", body);
                Assert.Contains("NEXUS opened your Work Queue", body);
            }
            Assert.Equal("Something is happening. You cannot act yet.", demo.Text("wait.line"));
        }

        // ------------------------------------------------------------------ the order of events (fight at first, then accept it)

        [Fact]
        public void TheInstructionsTellOneStoryFightAtFirstThenAcceptIt()
        {
            if (!Present) return;
            var demo = Demo();
            // The Work Queue once the order is blocked.
            string hint = demo.Text("task.blocked.017.hint");
            Assert.Contains("Session 017 has taken over", hint);
            Assert.Contains("Stop dragging", hint);
            Assert.Contains("Watch", hint);
            Assert.Contains("reply in Jotter", hint);
            // Ruth's mail: try a few times, stop when it will not go, and answer in Jotter.
            var ruth = demo.Email("mail_supervisor_check").body;
            Assert.DoesNotContain("Keep dragging", ruth);
            Assert.DoesNotContain("tell them I checked in", ruth);
            Assert.Contains("a few times", ruth);
            Assert.Contains("stop", ruth);
            Assert.Contains("Jotter", ruth);
            // IT's advice no longer contradicts the order, and the briefing still says what happens when WS-04 goes down.
            Assert.Contains("unless a task in your Work Queue tells you to", demo.Email("mail_it_maintenance").body);
            var briefing = demo.Email("mail_welcome").body;
            Assert.Contains("Shift ends at 7:00 AM", briefing);
            Assert.Contains("If WS-04 goes down before then, go home", briefing);
            // The first fight's label and the task's hint name the dark arrow, and say to fight.
            Assert.Contains("SESSION 017 (THE DARK ARROW)", demo.Text("tug.label.first"));
            Assert.Contains("SESSION 017 IS THE DARK ARROW", demo.Text("haul.label.first"));
            Assert.Contains("HOLD", demo.Text("tug.label.first"));
            Assert.Contains("session 017 (the dark arrow)", demo.Task("t_shred_017").hint);
            Assert.Contains("yank it into the bin", demo.Task("t_shred_017").hint);
        }

        // ------------------------------------------------------------------ the Night 1 end card

        [Fact]
        public void TheNightOneCardSaysWhatIsTrueWhatItMeansAndWhatCarriesOver()
        {
            if (!Present) return;
            var demo = Demo();
            string kept = demo.Text("end.n1.outcome.kept"), shredded = demo.Text("end.n1.outcome.shredded");
            Assert.Equal("THE FILE SURVIVES. SESSION 017 HELD IT.", kept);
            Assert.Equal("YOU SHREDDED IT. SESSION 017 BROUGHT IT BACK.", shredded);
            Assert.Contains("could not be done", demo.Text("end.n1.detail.kept"));
            Assert.Contains("could not be done", demo.Text("end.n1.detail.shredded"));
            string shift = demo.Format("end.n1.shift", "2:44 AM");
            Assert.Contains("2:44 AM", shift);
            Assert.Contains("before your shift ended at 7:00", shift);
            Assert.Contains("go home", shift);   // the briefing's own line: the early end is not a fault
            Assert.Contains("Night 2", demo.Text("end.n1.carry"));
            // Every line fits a 960 px card at 6 px a character with room to spare.
            foreach (var key in new[] { "end.n1.outcome.kept", "end.n1.outcome.shredded", "end.n1.detail.kept", "end.n1.detail.shredded", "end.n1.carry" })
                Assert.True(demo.Text(key).Length < 120, key);
            Assert.True(shift.Length < 125);
        }

        [Fact]
        public void TheCaptureRowIsExplainedInItsOwnLabel()
        {
            if (!Present) return;
            var demo = Demo();
            Assert.Equal("COPY OF YOU (CAPTURE 214)", demo.Text("record.capture"));
            Assert.Equal("88% MADE", demo.Format("record.pct", 88));
        }

        [Fact]
        public void TheCardCountsOnlyTheLooksThePlayerBroughtAbout()
        {
            if (!Present) return;
            var demo = Demo();
            string Fmt(string k, object[] a) => a == null ? demo.Text(k) : demo.Format(k, a);
            // The story opened CAM 03 twice and the player never did: the card says NEVER, not "2 TIMES".
            var d = new SaveData { capture = new[] { new CaptureStats { recorded = true, camLooks = 2, camOpened = 0 }, new CaptureStats(), new CaptureStats() } };
            var rows = RetentionRecord.Card(1, d, Fmt);
            Assert.Equal("CAM 03 OPENED BY YOU", rows[2].Label);
            Assert.Equal("NEVER", rows[2].Value);
            // The player opened it once on their own: ONCE, whatever the story did.
            d.capture[0].camOpened = 1;
            Assert.Equal("ONCE", RetentionRecord.Card(1, d, Fmt)[2].Value);
            // The full page still counts what was on screen.
            Assert.Equal("LOOKED AT CAM 03", demo.Text("record.cam"));
            Assert.Equal(1, new CaptureStats { camOpened = 1 }.Copy().camOpened);
        }
    }
}
