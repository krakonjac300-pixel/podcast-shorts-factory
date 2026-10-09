using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>Phase S (eleven blind testers): fair races, true statistics, readable text. Engine-free rules and strings.</summary>
    public class PhaseSTests
    {
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

        static DialogueReply Say(ContentDatabase db, string exchange, string said) => new DialogueEngine(db).Respond(db.Exchange(exchange), said);

        /// <summary>Every conversation with Ellen, by night (the confirm and the audience question are single-question exchanges).</summary>
        static readonly (int night, string id)[] Ellen =
        {
            (1, "ex_stop"), (1, "ex_two"), (1, "ex_three"), (2, "ex2_back"), (3, "ex3_ruth"), (3, "ex3_final"),
        };

        static readonly (int night, string id)[] Gary = { (2, "ex2_gary_one"), (2, "ex2_gary_two") };

        public static IEnumerable<object[]> EllenExchanges() => Ellen.Select(e => new object[] { e.night, e.id });
        public static IEnumerable<object[]> EveryExchange() => Ellen.Concat(Gary).Select(e => new object[] { e.night, e.id });


        static Dictionary<string, string> Strings(string folder)
        {
            var t = Read<StringTableData>(folder, "strings");
            return t.entries.ToDictionary(e => e.key, e => e.value);
        }

        // ------------------------------------------------------------------ fair races

        [Fact]
        public void TheOtherPointerNeverClicksBeforeAHumanCouldGetThere()
        {
            // A pointer 600 px from Yes needs about a second and a quarter; the profile's own 0.5 s is raised to it.
            float reach = RaceRules.ReachSeconds(600f, 1f);
            Assert.InRange(reach, 1.1f, 1.3f);
            Assert.Equal(reach, RaceRules.FairDelay(0.5f, 600f, 1f));
            // A slow profile delay is kept when it is already longer.
            Assert.Equal(2.5f, RaceRules.FairDelay(2.5f, 100f, 1f));
        }

        [Fact]
        public void RelaxedTimingAndStoryStretchTheHeadStartAndShortenTheCancelFight()
        {
            Assert.Equal(RaceRules.ReachSeconds(500f, 1f) * 2f, RaceRules.ReachSeconds(500f, 2f), 3);
            Assert.True(RaceRules.ReachSeconds(5000f, 1f) <= RaceRules.MaxReachSeconds);
            Assert.Equal(0f, RaceRules.CancelDelayAdd(1f));
            Assert.Equal(0.4f, RaceRules.CancelDelayAdd(2f), 3);
            Assert.Equal(6f, RaceRules.CancelPatience(6f, 1f));
            Assert.Equal(3f, RaceRules.CancelPatience(6f, 2f));
            Assert.Equal(3f, RaceRules.CancelPatience(3f, 2f));
            Assert.Equal(3.5f, RaceRules.CancelPatience(7f, 2f));
        }

        [Fact]
        public void NightOneEndsTheShredFightAfterTwoLostRaces()
        {
            Assert.False(RaceRules.Night1FightOver(0));
            Assert.False(RaceRules.Night1FightOver(1));
            Assert.True(RaceRules.Night1FightOver(2));
            Assert.Equal("contest.card.n1.first.no", RaceRules.Night1LossKey(true, 1));
            Assert.Equal("contest.card.n1.first.cancel", RaceRules.Night1LossKey(false, 1));
            Assert.Equal("contest.card.n1.last", RaceRules.Night1LossKey(true, 2));
        }

        [Fact]
        public void EveryRaceAndOptionLineExistsAndNamesWhatToDo()
        {
            if (!Present) return;
            var s = Strings("");
            foreach (var key in new[] { "race.covering", "race.covered.click", "race.dragging", "race.cancel.idle", "race.cancel.reaching", "race.cancel.blocked",
                                        "race.cancel.holding", "contest.card.n1.first.no", "contest.card.n1.first.cancel", "contest.card.n1.last", "contest.card.n1.won",
                                        "notepad.status.skip", "pause.lost.checkpoint", "pause.lost.start", "pause.totitle.q", "pause.quit.q",
                                        "drop.refused", "drop.refused.desktop" })
                Assert.True(s.ContainsKey(key), key);
            // The counterplay is in the line: hold the pointer on the button the other pointer wants.
            Assert.Contains("No", s["race.covering"]);
            Assert.Contains("Cancel", s["race.cancel.idle"]);
            // The first loss on Night 1 says plainly that the file may not be shreddable.
            Assert.Contains("may not let it go", s["contest.card.n1.first.no"]);
            Assert.Contains("{0}", s["contest.card.n1.first.cancel"]);
            Assert.Contains("{0}", s["pause.lost.checkpoint"]);
            Assert.Contains("{1}", s["pause.lost.checkpoint"]);
            Assert.Contains("beginning", s["pause.lost.start"]);
        }

        [Fact]
        public void EveryPauseControlHasADescription()
        {
            if (!Present) return;
            var s = Strings("");
            foreach (var id in new[] { "crt", "flashing", "display", "framerate", "textsize", "voldown", "volup", "difficulty", "tugassist", "clicklock", "access",
                                       "restart", "totitle", "quit", "resume", "back", "accessback", "noticetime", "relaxed", "captions", "sudden", "shake", "mono",
                                       "bigcursor", "clickspeed" })
            {
                Assert.True(s.TryGetValue("pause.desc." + id, out var text), id);
                Assert.InRange(text.Length, 12, 90);
            }
        }

        [Fact]
        public void NoNewTextHasADash()
        {
            if (!Present) return;
            foreach (var folder in new[] { "", "full" })
                foreach (var e in Read<StringTableData>(folder, "strings").entries)
                    Assert.False(e.value.Contains((char)0x2014) || e.value.Contains((char)0x2013), e.key);
        }

        [Fact]
        public void TheLogOffCountdownIsNight3TextOnly()
        {
            if (!Present) return;
            Assert.False(Strings("").ContainsKey("logoff.countdown"));
            Assert.Contains("{0}", Strings("full")["logoff.countdown"]);
            Assert.True(Strings("full").ContainsKey("record.pct.grew") && Strings("full").ContainsKey("record.pct.held"));
            Assert.False(Strings("").ContainsKey("record.pct.grew"));
        }

        [Fact]
        public void TheExitCountdownCountsRealSecondsAtTheClocksOwnRate()
        {
            // 7:00 to 7:05 at 1/18 minute a second is 90 s; relaxed is twice that.
            Assert.Equal(90f, Night3Rules.ExitSecondsLeft(7 * 60, Night3Rules.KeepTime, 1f / 18f), 2);
            Assert.Equal(180f, Night3Rules.ExitSecondsLeft(7 * 60, Night3Rules.KeepTime, 1f / 36f), 2);
            Assert.Equal(0f, Night3Rules.ExitSecondsLeft(7 * 60 + 6, Night3Rules.KeepTime, 1f / 18f));
            Assert.Equal("1:30", Night3Rules.ExitClock(90f));
            Assert.Equal("0:05", Night3Rules.ExitClock(4.2f));
            Assert.Equal("0:00", Night3Rules.ExitClock(-3f));
        }

        // ------------------------------------------------------------------ the record says what happened

        [Theory]
        [InlineData("ellen marsh?")]
        [InlineData("is your name ellen marsh?")]
        [InlineData("are you Ellen")]
        [InlineData("hello marsh")]
        public void HerNameAnywhereInALineCounts(string said)
        {
            Assert.True(DialogueEngine.MentionsHerName(said));
        }

        [Theory]
        [InlineData("excellent")]
        [InlineData("who are you")]
        [InlineData("")]
        public void OtherWordsDoNot(string said)
        {
            Assert.False(DialogueEngine.MentionsHerName(said));
        }

        [Fact]
        public void HerNameTypedOnNightTwoIsTagged()
        {
            if (!Present) return;
            Assert.Equal("name", Say(Night(2), "ex2_back", "ellen marsh?").Tag);
        }

        static string Fmt(string key, object[] args) => args == null ? key : key + ":" + string.Join(",", args);

        static SaveData Saved(bool hid, bool said)
        {
            var d = new SaveData();
            var flags = new NarrativeFlags();
            if (hid) flags.Set(MemoryFlags.N2Hid214);
            if (said) flags.Set(MemoryFlags.N2SaidName);
            d.memory = flags.Snapshot();
            for (int n = 0; n < SaveData.Nights; n++)
                d.capture[n] = new CaptureStats { recorded = true, seconds = 600f, words017 = 5, camOpened = 1, camLooks = 2, tugWins = 2, tugLosses = n == 1 ? 1 : 0, yesCount = 1, yesSeconds = 2f };
            return d;
        }

        [Fact]
        public void TheSecondCardSaysWhetherTheCopyGrew()
        {
            var grew = RetentionRecord.Card(2, Saved(false, true), Fmt);
            var held = RetentionRecord.Card(2, Saved(true, true), Fmt);
            Assert.StartsWith("record.pct.grew", grew[0].Value);
            Assert.StartsWith("record.pct.held", held[0].Value);
            Assert.Contains("96", grew[0].Value);
            Assert.Contains("88", held[0].Value);
            // Night 1's card is as it was.
            Assert.StartsWith("record.pct:", RetentionRecord.Card(1, Saved(false, true), Fmt)[0].Value);
        }

        [Fact]
        public void TheFullRecordCountsEveryShiftAndEveryLostTug()
        {
            var rows = RetentionRecord.Full(Saved(false, true), "n3_logoff", new Dictionary<string, string>(), Fmt);
            string Value(string label) => rows.First(r => r.Label == label).Value;
            Assert.StartsWith("record.shifts.value:3,", Value("record.shifts"));
            Assert.Equal("record.tugs.value:6,1", Value("record.tugs"));
            Assert.Equal("record.yesword", Value("record.saidname"));
            Assert.Equal("record.noword", RetentionRecord.Full(Saved(false, false), "n3_logoff", new Dictionary<string, string>(), Fmt).First(r => r.Label == "record.saidname").Value);
        }

        [Fact]
        public void ACheckpointCarriesTheNightsMeasurementsSoContinueIsStillOneRecordedNight()
        {
            var cp = new Checkpoint { captureComplete = true, capture = new CaptureStats { recorded = false, camLooks = 3, words017 = 11, firstLine = "who are you" } };
            Assert.Equal(11, cp.capture.Copy().words017);
            Assert.True(cp.captureComplete);
            Assert.False(new Checkpoint().captureComplete);
        }

        // ------------------------------------------------------------------ a task that was done is never withdrawn as expired

        sealed class World : SecondCursor.Core.Tasks.ITaskWorld
        {
            public readonly HashSet<string> Read = new HashSet<string>();
            public bool IsEmailRead(string emailId) => Read.Contains(emailId);
            public string FolderOf(string fileId) => null;
            public bool IsShredded(string fileId) => false;
            public string DecisionFor(string orderId) => null;
            public bool IsFileOpenedByPlayer(string fileId) => false;
            public bool IsEmployeeViewedByPlayer(string employeeId) => false;
            public string CreditFor(SecondCursor.Core.Tasks.TaskType type, string targetId) => null;
        }

        [Fact]
        public void WithdrawingATaskTheWorldAlreadyShowsDoneCompletesIt()
        {
            var w = new World();
            var tasks = new SecondCursor.Core.Tasks.WorkTaskManager(new[] { new TaskData { id = "t", title = "t", type = "ReadEmail", targets = new[] { "m" } } }, w);
            tasks.Activate("t");
            w.Read.Add("m");   // done, but nothing has evaluated yet
            tasks.Withdraw("t", "expired");
            Assert.Equal(SecondCursor.Core.Tasks.TaskState.Completed, tasks.Get("t").State);
            // One that is not done is withdrawn as before.
            var open = new SecondCursor.Core.Tasks.WorkTaskManager(new[] { new TaskData { id = "u", title = "u", type = "ReadEmail", targets = new[] { "x" } } }, new World());
            open.Activate("u");
            open.Withdraw("u", "expired");
            Assert.Equal(SecondCursor.Core.Tasks.TaskState.Withdrawn, open.Get("u").State);
        }
    }
}
