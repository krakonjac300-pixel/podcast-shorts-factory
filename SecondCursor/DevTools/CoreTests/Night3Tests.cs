using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>The Restricted code prompt (expansion spec 5.5).</summary>
    public class AuthCodeTests
    {
        [Theory]
        [InlineData("0217")]
        [InlineData("217")]
        [InlineData("2:17")]
        [InlineData("02:17")]
        [InlineData("2 17 am")]
        [InlineData(" 00217 ")]
        public void TheMinuteSheStoppedIsAcceptedInAnyForm(string input)
        {
            Assert.True(VirtualFileSystem.CodeMatches("0217", input));
        }

        [Theory]
        [InlineData("1234")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("0218")]
        [InlineData("21")]
        [InlineData("abc")]
        [InlineData("2170")]
        public void AnythingElseIsRefused(string input)
        {
            Assert.False(VirtualFileSystem.CodeMatches("0217", input));
        }

        [Fact]
        public void TryUnlockOpensOnlyALockedFolderWithACode()
        {
            var data = new FileSystemData
            {
                folders = new[]
                {
                    new FolderData { id = "root", name = "WS-04", parent = "" },
                    new FolderData { id = "restricted", name = "Restricted", parent = "root", locked = true, code = "0217" },
                    new FolderData { id = "vault", name = "Vault", parent = "root", locked = true },
                },
            };
            var fs = new VirtualFileSystem(data);
            Assert.True(fs.GetFolder("restricted").HasCode);
            Assert.False(fs.TryUnlock("restricted", "1234"));
            Assert.True(fs.IsInsideLocked("restricted"));
            Assert.False(fs.TryUnlock("vault", "0217"));
            Assert.False(fs.TryUnlock("missing", "0217"));
            Assert.True(fs.TryUnlock("restricted", "2:17"));
            Assert.False(fs.GetFolder("restricted").Locked);
            // Already open: nothing to unlock.
            Assert.False(fs.TryUnlock("restricted", "0217"));
        }
    }

    /// <summary>Night 3's rules: log off, the config files, trust, the shelf check, KEEP's lines, the time exits (spec 5, 6).</summary>
    public class Night3RulesTests
    {
        [Fact]
        public void LogOffIsEarlyBeforeSevenAndNeedsTheConfig()
        {
            Assert.Equal(LogOffCheck.Early, Night3Rules.CheckLogOff(6 * 60 + 59, true));
            Assert.Equal(LogOffCheck.Disabled, Night3Rules.CheckLogOff(7 * 60, false));
            Assert.Equal(LogOffCheck.Allowed, Night3Rules.CheckLogOff(7 * 60, true));
            Assert.Equal(LogOffCheck.Allowed, Night3Rules.CheckLogOff(7 * 60 + 7, true));
        }

        const string SessionCfg = "; NEXUS SESSION POLICY: WS-04\n[SESSION]\nHOST=WS-04\nLOGOFF_TIME=07:00\nALLOW_LOGOFF=0";

        [Fact]
        public void OnlyExactlyOneTurnsTheFlagsOn()
        {
            Assert.False(Night3Rules.AllowsLogoff(SessionCfg));
            // The edit the game teaches: Backspace, then 1.
            Assert.True(Night3Rules.AllowsLogoff(SessionCfg.Substring(0, SessionCfg.Length - 1) + "1"));
            Assert.False(Night3Rules.AllowsLogoff(SessionCfg + "1"));        // ALLOW_LOGOFF=01
            Assert.False(Night3Rules.AllowsLogoff(SessionCfg.Substring(0, SessionCfg.Length - 1) + "yes"));
            Assert.True(Night3Rules.AllowsLogoff("allow_logoff = 1 "));
            Assert.False(Night3Rules.AllowsLogoff("; ALLOW_LOGOFF=1"));
            Assert.False(Night3Rules.AllowsLogoff(""));
            Assert.True(Night3Rules.OperatorOverride("[CAMVIEW]\nACCESS=SECURITY\nOPERATOR_OVERRIDE=1"));
            Assert.False(Night3Rules.OperatorOverride("[CAMVIEW]\nOPERATOR_OVERRIDE=0"));
            // The last line with the key wins.
            Assert.Equal("1", Night3Rules.ConfigValue("A=0\nA=1", "A"));
            Assert.Null(Night3Rules.ConfigValue("A=0", "B"));
        }

        [Theory]
        [InlineData(0.5f, 0.9f, "n3_tug_letgo", "n3_shred_last_a", "n3_logoff_trust")]
        [InlineData(0.2f, 0.9f, "n3_tug_letgo", "n3_shred_last_a", "n3_logoff_trust")]
        [InlineData(0.1f, 1.0f, "n3_tug_refuse", "n3_shred_last_b", "n3_logoff_low")]
        [InlineData(-0.39f, 1.0f, "n3_tug_refuse", "n3_shred_last_b", "n3_logoff_low")]
        [InlineData(-0.4f, 1.1f, "n3_tug_refuse", "n3_shred_last_b", "n3_logoff_low")]
        public void TrustDecidesHerGripAndHerLines(float trust, float grip, string tug, string lastWords, string logOff)
        {
            Assert.Equal(grip, Night3Rules.GripMultForTrust(trust), 3);
            Assert.Equal(tug, Night3Rules.TugLineSet(trust));
            Assert.Equal(lastWords, Night3Rules.ShredLastWordsSet(trust));
            Assert.Equal(logOff, Night3Rules.LogOffLineSet(trust));
        }

        [Fact]
        public void TrustSoftensTheFinaleGripThroughTheFormula()
        {
            var p = DifficultyTable.For(3, DifficultyMode.Normal);
            float neutral = p.Grip(0, null);
            Assert.Equal(0.78f, neutral, 3);
            Assert.Equal(neutral * 0.9f, p.Grip(0, null, Night3Rules.GripMultForTrust(0.3f)), 3);
            Assert.Equal(neutral * 1.1f, p.Grip(0, null, Night3Rules.GripMultForTrust(-0.5f)), 3);
        }

        [Fact]
        public void ShelfNumbersAreReadFromLocationsAndCaptions()
        {
            Assert.Equal(14, Night3Rules.ShelfNumber("Sublevel C, SHELF 14"));
            Assert.Equal(16, Night3Rules.ShelfNumber("SHELF 16: 209 PRUITT G. (RESERVED)"));
            Assert.Equal(-1, Night3Rules.ShelfNumber("Sublevel C"));
            Assert.Equal(-1, Night3Rules.ShelfNumber(null));
            var captions = new[] { "SHELF 14: 142 LUNDY T.", "SHELF 15: 188 ACHTERBERG M." };
            Assert.Equal("approve", Night3Rules.ShelfDecision("Sublevel C, SHELF 14", "142", captions));
            Assert.Equal("reject", Night3Rules.ShelfDecision("Sublevel C, SHELF 15", "142", captions));
            Assert.Equal("reject", Night3Rules.ShelfDecision("Sublevel C, SHELF 20", "142", captions));
            // A number inside another number is not the owner (14 is not 142).
            Assert.Equal("reject", Night3Rules.ShelfDecision("Sublevel C, SHELF 14", "14", captions));
        }

        [Fact]
        public void KeepLinesPutHerNameBeforeTheLastLine()
        {
            var lines = new[] { "YOU STAYED", "GOOD", "WE WORK NIGHTS" };
            var speakers = new[] { "entity", "entity", "both" };
            Night3Rules.KeepLines(lines, speakers, new[] { "THANK YOU FOR MY NAME" }, new[] { "entity" }, true, out var l, out var s);
            Assert.Equal(new[] { "YOU STAYED", "GOOD", "THANK YOU FOR MY NAME", "WE WORK NIGHTS" }, l);
            Assert.Equal(new[] { "entity", "entity", "entity", "both" }, s);
            Night3Rules.KeepLines(lines, speakers, new[] { "THANK YOU FOR MY NAME" }, null, false, out l, out s);
            Assert.Equal(lines, l);
            Assert.Equal(speakers, s);
            // Missing speakers default to Ellen.
            Night3Rules.KeepLines(lines, null, null, null, true, out l, out s);
            Assert.Equal(new[] { "entity", "entity", "entity" }, s);
            Assert.Equal("n3_end_keep_stay", Night3Rules.KeepLineSet(true));
            Assert.Equal("n3_end_keep_default", Night3Rules.KeepLineSet(false));
        }

        [Fact]
        public void TimeEndsTheNightInKeepWithGraceForARunningExit()
        {
            Assert.False(Night3Rules.KeepByTime(7 * 60 + 4, false, 0f));
            Assert.True(Night3Rules.KeepByTime(7 * 60 + 5, false, 0f));
            // A shred or log off already running at 7:05 gets 20 s.
            Assert.False(Night3Rules.KeepByTime(7 * 60 + 5, true, 19.9f));
            Assert.True(Night3Rules.KeepByTime(7 * 60 + 5, true, 20f));
            // The hard cap wins whatever is running.
            Assert.True(Night3Rules.KeepByTime(7 * 60 + 8, true, 0f));
        }
    }

    /// <summary>Night 3's Custodial round and finale feed (spec 7.4).</summary>
    public class Night3RoundsTests
    {
        static void Tick(CustodialRounds r, float seconds, string cam)
        {
            const float dt = 1f / 60f;
            for (float t = 0f; t < seconds; t += dt) r.Tick(dt, cam);
        }

        [Fact]
        public void TheFullRoundGoesFromSublevelCToTheSeat()
        {
            var c = RoundsConfig.Night3(DifficultyMode.Normal, 0f, false, false);
            Assert.Equal(new[] { "SublevelC", "Lobby", "HallFar", "Corridor", "Doorway", "Middle", "BehindChair" }, c.Stages);
            Assert.Equal(new[] { "cam04", "cam01", "cam02", "cam02", "cam03", "cam03", "cam03" }, c.Cameras);
            Assert.Equal(0, c.StartStage);
            Assert.Equal(4f, c.WatchSeconds);
            Assert.Equal(1f, c.ReopenPenalty);
            Assert.Equal(360f, c.Duration);
            Assert.Equal(new[] { 0f }, c.ForcedOpenTimes);
            Assert.Equal(22f, c.ForcedOpenRepeatMin);
            Assert.Equal(30f, c.ForcedOpenRepeatMax);
            Assert.Equal(RoundsFinalRule.ClearSeat, c.AtLastStage);
        }

        [Fact]
        public void NightTwoChoicesChangeTheRound()
        {
            Assert.Equal(1, RoundsConfig.Night3(DifficultyMode.Normal, 0f, true, false).StartStage);
            Assert.Equal(5f, RoundsConfig.Night3(DifficultyMode.Normal, 0f, false, true).WatchSeconds);
            Assert.Equal(0.8f, RoundsConfig.Night3(DifficultyMode.Normal, 0.5f, false, false).CloseReactionMin, 3);
        }

        [Fact]
        public void TwentyEightWatchedSecondsClearTheSeat()
        {
            var r = new CustodialRounds(RoundsConfig.Night3(DifficultyMode.Normal, 0f, false, false));
            bool cleared = false;
            r.SeatCleared += () => cleared = true;
            // Following the figure camera by camera, as a player who stares would.
            for (int guard = 0; guard < 20 && !r.Finished; guard++) Tick(r, 4.05f, r.FigureCamera);
            Assert.True(cleared);
            Assert.Equal(6, r.MaxStage);
            Assert.Equal("BehindChair", r.FigureStage);
        }

        [Fact]
        public void WatchingSublevelCAfterTheFigureLeftIsSafe()
        {
            var r = new CustodialRounds(RoundsConfig.Night3(DifficultyMode.Normal, 0f, false, false));
            Tick(r, 4.05f, "cam04");
            Assert.Equal("Lobby", r.FigureStage);
            // Reading the shelves now never moves it.
            Tick(r, 120f, "cam04");
            Assert.Equal(1, r.Stage);
        }

        [Fact]
        public void StoryNeverClearsTheSeat()
        {
            var r = new CustodialRounds(RoundsConfig.Night3(DifficultyMode.Story, 0f, false, false));
            bool cleared = false;
            r.SeatCleared += () => cleared = true;
            for (int guard = 0; guard < 20; guard++) Tick(r, 10.1f, r.FigureCamera);
            Assert.False(cleared);
            Assert.Equal("Middle", r.FigureStage);
        }

        [Fact]
        public void TheFinaleFeedClearsTheSeatInTwelveSeconds()
        {
            var c = RoundsConfig.Night3Finale(DifficultyMode.Normal, 0f);
            Assert.Equal(new[] { "Corridor", "Doorway", "Middle", "BehindChair" }, c.Stages);
            Assert.Empty(c.ForcedOpenTimes);
            Assert.Equal(0f, c.ForcedOpenRepeatMax);
            Assert.Equal(0f, c.Duration);
            var r = new CustodialRounds(c);
            bool cleared = false;
            r.SeatCleared += () => cleared = true;
            for (int guard = 0; guard < 10 && !r.Finished; guard++) Tick(r, 3.05f, r.FigureCamera);
            Assert.True(cleared);
            var story = RoundsConfig.Night3Finale(DifficultyMode.Story, 0f);
            Assert.Equal(10f, story.WatchSeconds);
            Assert.Equal(2, story.ClampStage);
            Assert.Equal(0f, story.ForcedOpenRepeatMax);
        }

        [Fact]
        public void HasteMakesHerCloseSoonerButNeverInstantly()
        {
            var c = RoundsConfig.Night3(DifficultyMode.Normal, 0f, false, false);
            c.Hasten(0.4f);
            Assert.Equal(0.8f, c.CloseReactionMin, 3);
            Assert.Equal(1.6f, c.CloseReactionMax, 3);
            c.Hasten(5f);
            Assert.Equal(0.2f, c.CloseReactionMin, 3);
            Assert.Equal(0.2f, c.CloseReactionMax, 3);
        }
    }

    /// <summary>Night 3's content as ContentLoader.Load(3) builds it (base + night2 + night3).</summary>
    public class Night3ContentTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

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

        static bool Present => Directory.Exists(Path.Combine(Dir, "night3"));

        static ContentDatabase Night3() => Pack("").Overlay(Pack("night2")).Overlay(Pack("night3")).Build();

        [Fact]
        public void LoadsCleanlyOnTopOfBothEarlierNights()
        {
            if (!Present) return;
            var db = Night3();
            Assert.Empty(db.Problems);
            var fs = new VirtualFileSystem(db.FileSystem);
            // Night 2's files are still there; its temp file is gone without a placeholder.
            Assert.NotNull(fs.GetFile(ContentIds.Batch45A));
            Assert.NotNull(fs.GetFile(ContentIds.File209));
            Assert.Null(fs.GetFile(ContentIds.FileCacheN2));
            Assert.NotNull(fs.GetFile(ContentIds.FileCacheN3));
            var restricted = fs.GetFolder(ContentIds.FolderRestricted);
            Assert.True(restricted.Locked);
            Assert.Equal("0217", restricted.Code);
            Assert.Equal(ContentIds.FolderRestricted, fs.FolderOf(ContentIds.FileSessionCfg));
            Assert.Equal(ContentIds.FolderSystem, fs.FolderOf(ContentIds.FileCamviewCfg));
            Assert.True(db.Camera(ContentIds.Cam00).hidden);
            Assert.False(db.Camera(ContentIds.Cam04).hidden);
            Assert.Equal("Thank you for working nights at Letheworth.", db.Text("end.card.thanks"));
            Assert.Contains("{p3}", string.Join("\n", db.Story.biosLines));
        }

        [Fact]
        public void TheConfigFilesEndOnTheDigitABackspaceRemoves()
        {
            if (!Present) return;
            var fs = new VirtualFileSystem(Night3().FileSystem);
            foreach (var id in new[] { ContentIds.FileSessionCfg, ContentIds.FileCamviewCfg })
            {
                var f = fs.GetFile(id);
                Assert.EndsWith("=0", f.Content);
                Assert.True(f.HasTag("editable"), id + " must be editable");
                Assert.True(f.Protected, id + " must be protected from shredding");
            }
            Assert.False(Night3Rules.AllowsLogoff(fs.GetFile(ContentIds.FileSessionCfg).Content));
            Assert.False(Night3Rules.OperatorOverride(fs.GetFile(ContentIds.FileCamviewCfg).Content));
        }

        [Fact]
        public void TheShelfOrdersFollowTheCaptions()
        {
            if (!Present) return;
            var db = Night3();
            var shelves = db.Lines(ContentIds.LineSetShelves);
            Assert.Equal(8, shelves.Length);
            foreach (var flags in new[] { Kept(), Finished() })
            {
                var tokens = NightTemplates.Tokens(flags, null);
                var filled = shelves.Select(l => NightTemplates.Fill(l, tokens)).ToArray();
                foreach (var o in db.WorkOrders.orders.Where(o => o.rule == "shelf"))
                {
                    string listed = o.fields.First(f => f.label == "Listed Location").value;
                    Assert.Equal(o.correct, Night3Rules.ShelfDecision(listed, o.employeeRef, filled));
                }
            }
            Assert.Equal("approve", db.Order(ContentIds.Order3340).correct);
            Assert.Equal("reject", db.Order(ContentIds.Order3341).correct);
            Assert.Equal("approve", db.Order(ContentIds.Order3342).correct);
        }

        static NarrativeFlags Kept()
        {
            var f = new NarrativeFlags();
            f.Set(MemoryFlags.N2KeptGary);
            f.Set(MemoryFlags.N2WatchedToDoor);
            return f;
        }

        static NarrativeFlags Finished()
        {
            var f = new NarrativeFlags();
            f.Set(MemoryFlags.N2FinishedGary);
            f.Set(MemoryFlags.N2Hid214);
            return f;
        }

        [Fact]
        public void TemplatesAndTheBiosFillForBothGaryBranches()
        {
            if (!Present) return;
            var db = Night3();
            foreach (var flags in new[] { Kept(), Finished() })
            {
                var tokens = NightTemplates.Tokens(flags, new[] { "who are you" });
                foreach (var f in db.FileSystem.files.Where(f => f.tags.Contains("template")))
                    Assert.DoesNotContain("{", NightTemplates.Fill(f.content, tokens));
                foreach (var line in db.Story.biosLines) Assert.DoesNotContain("{", NightTemplates.Fill(line, tokens));
            }
            var queue = db.FileSystem.files.First(f => f.id == ContentIds.FileRetentionQueue).content;
            Assert.Contains("RETAINED (COMPLETE)", NightTemplates.Fill(queue, NightTemplates.Tokens(Finished(), null)));
            Assert.Contains("HELD (INCOMPLETE)", NightTemplates.Fill(queue, NightTemplates.Tokens(Kept(), null)));
        }

        [Fact]
        public void EveryLineSetTheDirectorTypesExists()
        {
            if (!Present) return;
            var db = Night3();
            string[] sets =
            {
                "n3_intro", "n3_intro_mem_watched", "n3_intro_mem_finished", "n3_corrupt", "n3_corrupt_content", "n3_code_hint1", "n3_code_hint2",
                "n3_restricted_open", "n3_rounds_start", "n3_rounds_advanced", "n3_rounds_teach", "n3_rounds_door", "n3_rounds_cleared",
                "n3_rounds_safe", "n3_lost", "n3_finale_feed", "n3_tug_letgo", "n3_tug_refuse", "n3_shred_last_a", "n3_shred_last_b",
                "n3_logoff_trust", "n3_logoff_low", "n3_cam00", "g3_intro", "g3_code", "g3_teach", "g3_personnel", "g3_lost", "g3_logoff",
                "g3_logoff_done", "g3_guard", "g3_finale", "g3c_intro", "g3c_easier", "g3c_feed", "g3c_shred", "g3c_seated", "n3_shelves",
                "n3_end_shred", "n3_end_keep_stay", "n3_end_keep_default", "n3_end_keep_name", "n3_end_logoff_sys", "n3_end_logoff", "n3_end_logoff_cleared",
            };
            foreach (var id in sets) Assert.True(db.Lines(id).Length > 0, "missing line set " + id);
            foreach (var id in new[] { ContentIds.ExchangeN3Ruth, ContentIds.ExchangeN3Final, ContentIds.ExchangeN3Confirm })
                Assert.NotNull(db.Exchange(id));
            // Every ending line has a speaker the ending knows.
            var known = new HashSet<string> { "entity", "casey", "both", "system" };
            foreach (var id in new[] { "n3_end_shred", "n3_end_keep_stay", "n3_end_keep_default", "n3_end_keep_name", "n3_end_logoff_sys", "n3_end_logoff", "n3_end_logoff_cleared" })
            {
                var set = db.LineSet(id);
                Assert.Equal(set.lines.Length, set.speakers.Length);
                Assert.All(set.speakers, s => Assert.Contains(s, known));
            }
        }

        [Fact]
        public void TheFinalExchangeTagsLeadToTheExits()
        {
            if (!Present) return;
            var db = Night3();
            var engine = new DialogueEngine(db);
            var final = db.Exchange(ContentIds.ExchangeN3Final);
            Assert.Equal("stay", engine.Respond(final, "i will stay").Tag);
            Assert.Equal("stay", engine.Respond(final, "yes").Tag);
            Assert.Equal("letgo", engine.Respond(final, "let you go").Tag);
            Assert.Equal("letgo", engine.Respond(final, "shred it").Tag);
            Assert.Equal("go", engine.Respond(final, "i want to go home").Tag);
            Assert.Equal("name", engine.Respond(final, "Ellen").Tag);
            var confirm = db.Exchange(ContentIds.ExchangeN3Confirm);
            Assert.Equal("confirm", engine.Respond(confirm, "yes").Tag);
            Assert.Equal("cancel", engine.Respond(confirm, "no").Tag);
            Assert.Equal("name", engine.Respond(db.Exchange(ContentIds.ExchangeN3Ruth), "ellen marsh").Tag);
        }
    }

    /// <summary>Night 3 in the save: the ending, the unlock past the last night, and memory across replays.</summary>
    public class Night3SaveTests
    {
        [Fact]
        public void FinishingNight3RecordsTheEndingAndFinishesTheGame()
        {
            var d = new SaveData { nightUnlocked = 3, currentNight = 3 };
            var mem = new NarrativeFlags();
            mem.Set(MemoryFlags.N2KeptGary);
            mem.Set(MemoryFlags.N3RestrictedOpen);
            mem.SetCounter(MemoryFlags.N3MaxStage, 4);
            d.RecordNightComplete(new NightResult { Night = 3, EndingId = ContentIds.EndingN3LogOff, Memory = mem.Snapshot(MemoryFlags.Prefix), Trust = 0.4f });
            Assert.Equal(4, d.nightUnlocked);
            Assert.Equal(3, d.currentNight);
            Assert.Contains(ContentIds.EndingN3LogOff, d.endingsSeen);
            Assert.False(d.checkpoint.valid);
            Assert.Contains(MemoryFlags.N3RestrictedOpen, d.memory.flags);
            // Replaying Night 3 starts without its own choices, with Night 2's.
            var replay = new NarrativeFlags();
            replay.Merge(d.MemoryForNight(3));
            Assert.True(replay.Has(MemoryFlags.N2KeptGary));
            Assert.False(replay.Has(MemoryFlags.N3RestrictedOpen));
            Assert.Equal(0, replay.Get(MemoryFlags.N3MaxStage));
            // A second ending is added, not replacing the first.
            d.RecordNightComplete(new NightResult { Night = 3, EndingId = ContentIds.EndingN3Shred, Memory = replay.Snapshot(MemoryFlags.Prefix) });
            Assert.Equal(new[] { ContentIds.EndingN3LogOff, ContentIds.EndingN3Shred }, d.endingsSeen);
            Assert.DoesNotContain(MemoryFlags.N3RestrictedOpen, d.memory.flags);
        }

        [Fact]
        public void CompletingANightKeepsEarlierNightsAndDropsLaterOnes()
        {
            var saved = new NarrativeFlags();
            saved.Set(MemoryFlags.N1Shredded017);
            saved.Set(MemoryFlags.N2KeptGary);
            saved.Set(MemoryFlags.N3SaidStay);
            saved.Set(MemoryFlags.SaidName);
            saved.SetCounter(MemoryFlags.N2Obeyed, 3);
            // A Night 2 run that did not carry Night 1's flag (e.g. an older save) and finished Gary this time.
            var run = new NarrativeFlags();
            run.Set(MemoryFlags.N2FinishedGary);
            var merged = new NarrativeFlags();
            merged.Merge(SaveData.MergeNightMemory(saved.Snapshot(MemoryFlags.Prefix), run.Snapshot(MemoryFlags.Prefix), 2));
            Assert.True(merged.Has(MemoryFlags.N1Shredded017));
            Assert.True(merged.Has(MemoryFlags.SaidName));
            Assert.True(merged.Has(MemoryFlags.N2FinishedGary));
            Assert.False(merged.Has(MemoryFlags.N2KeptGary));
            Assert.Equal(0, merged.Get(MemoryFlags.N2Obeyed));
            // Night 3 was built on the old Night 2: it is forgotten.
            Assert.False(merged.Has(MemoryFlags.N3SaidStay));
        }
    }
}
