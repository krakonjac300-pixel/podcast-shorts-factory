using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase G: the demo's spoiler-free base strings, the clarity fixes (hints, Log Off, Jotter, the last operator),
    /// the stream-moment rules (session log lines, CAM 04's caption loop, the slang group) and the review fixes.
    /// </summary>
    public class PhaseGTests
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

        /// <summary>The full game's content for a night (base, full-game strings, night overlays).</summary>
        static ContentDatabase Night(int night)
        {
            var pack = Pack("").Overlay(Pack("full"));
            for (int n = 2; n <= night; n++) pack = pack.Overlay(Pack("night" + n));
            return pack.Build();
        }

        /// <summary>The demo's content: base only (the demo build leaves Content/full, night2 and night3 out).</summary>
        static ContentDatabase Demo() => Pack("").Build();

        static Dictionary<string, string> Table(string folder)
        {
            var d = new Dictionary<string, string>();
            var t = Read<StringTableData>(folder, "strings");
            if (t != null) foreach (var e in t.entries) d[e.key] = e.value;
            return d;
        }

        // ------------------------------------------------------------------ demo spoilers (LaunchAudit 15)

        [Fact]
        public void TheDemosBaseStringsCarryNoNightTwoOrThreeSpoilers()
        {
            if (!Present) return;
            var base_ = Table("");
            foreach (var kv in base_)
            {
                Assert.False(kv.Key.StartsWith("ach.", StringComparison.Ordinal), "achievement text belongs in Content/full: " + kv.Key);
                foreach (var banned in new[] { "Gary", "CAM 00", "Custodial rounds", "session.cfg", "Log Off CROURKE", "ALLOW_LOGOFF", "HHMM", "He is complete", "He is still held", "Pointing device 3" })
                    Assert.False(kv.Value.Contains(banned), kv.Key + " mentions '" + banned + "' (demo spoiler)");
            }
            var full = Table("full");
            foreach (var a in AchievementIds.All)
            {
                Assert.True(full.ContainsKey(a.NameKey), "missing " + a.NameKey);
                Assert.True(full.ContainsKey(a.DescKey), "missing " + a.DescKey);
            }
        }

        [Fact]
        public void NightOneKeysTheCodeUsesStayInTheBaseStrings()
        {
            if (!Present) return;
            var base_ = Table("");
            foreach (var k in new[]
                     {
                         "notify.jotter.reply", "notepad.editable.hint", "notepad.save.prompt", "pause.quit.confirm", "disclaimer.continue",
                         "camera.operatortag", "welcome.title", "welcome.body", "quickstart.body", "help.body", "select.endings",
                         "end.card.cta", "end.card.wishlist", "title.tagline",
                     })
                Assert.True(base_.ContainsKey(k), "missing base string " + k);
            // The demo build has no Content/full: nothing it shows may come from there.
            var demo = Demo();
            Assert.Equal("CROURKE / WS-04", demo.Text("camera.operatortag"));
        }

        // ------------------------------------------------------------------ clarity fixes

        [Fact]
        public void LogOffSaysWhereThePolicyAndTheMenuItemAre()
        {
            if (!Present) return;
            var db = Night(3);
            Assert.Contains("Restricted\\session.cfg", db.Text("logoff.early.disabled"));
            Assert.Contains("7:00 AM", db.Text("logoff.early.disabled"));
            Assert.Contains("Restricted\\session.cfg", db.Text("logoff.disabled"));
            Assert.Contains("Nexus menu", db.Text("logoff.available"));
            Assert.Contains("Nexus menu", db.Text("logoff.added"));
        }

        [Fact]
        public void TheFileManagerHintNamesTheIconOnTheDesktop()
        {
            if (!Present) return;
            Assert.Contains("Workstation (File Manager)", Night(1).Task("t_archive_ledger").hint);
            Assert.Contains("Workstation (File Manager)", Night(2).Task("t2_archive_batch45").hint);
            Assert.Contains("Workstation opens File Manager", Night(1).Text("quickstart.body"));
            // Help explains every mechanic of the three nights in the UI's own words.
            foreach (var variant in new[] { null, "deck" })
            {
                var db = Night(1);
                db.Variant = variant;
                var help = db.Text("help.body");
                foreach (var topic in new[] { "Work Queue", "(remote session)", "MOVE FILES", "SHRED", "WORK ORDERS", "TUG-OF-WAR", "JOTTER",
                                              "CAMERAS", "LOCKED FOLDERS", "SAVING", "File > Save", "NEXUS MENU", "Log Off" })
                    Assert.True(help.Contains(topic), (variant ?? "mouse") + " help does not explain " + topic);
                Assert.Contains("NEXUS Help", db.Text("quickstart.body"));
            }
            Assert.Contains("Options (Esc", Night(1).Text("disclaimer.body"));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryDragHintHasASteamDeckVersion(int night)
        {
            if (!Present) return;
            var db = Night(night);
            foreach (var t in db.Tasks.tasks)
            {
                if (string.IsNullOrEmpty(t.hint) || t.hint.IndexOf("drag", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Assert.False(string.IsNullOrEmpty(t.hintDeck), "night " + night + ": " + t.id + " has a drag hint but no hintDeck");
                Assert.DoesNotContain("drag", t.hintDeck, StringComparison.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void RemoteTasksSayWhatAndWhereTheirTargetIs()
        {
            if (!Present) return;
            var db = Night(2);
            foreach (var id in new[] { "e2_door_log", "e2_lookup_163", "e2_hide_214", "e2_archive_209" })
            {
                var d = db.Task(id).description;
                Assert.Contains("Added by remote session 017.", d);
                Assert.Contains("Not assigned by Night Operations.", d);
                Assert.Contains("Target: ", d);
            }
            Assert.Contains("Documents\\b7_door_log.txt", db.Task("e2_door_log").description);
            Assert.Contains("Contains operator input.", db.Task("t2_shred_cache").description);
        }

        [Fact]
        public void SilenceInvitesTypingAndTheLastOperatorIsAKeyword()
        {
            if (!Present) return;
            var db = Night(1);
            Assert.Equal("TYPE SOMETHING", db.Exchange("ex_stop").silence[0]);
            var engine = new DialogueEngine(db);
            Assert.Equal("HE TRIED TOO", engine.Respond(db.Exchange("ex_stop"), "who was the last operator?").Lines[0]);
            Assert.Equal("HE LEFT HIS GLASSES", engine.Respond(db.Exchange("ex_two"), "what about the previous operator").Lines[0]);
            Assert.Equal("THAT WORD IS NEW", engine.Respond(db.Exchange("ex_stop"), "skibidi toilet").Lines[0]);
            Assert.Equal("THAT WORD IS NEW", engine.Respond(db.Exchange("ex_three"), "rizz").Lines[0]);
            // The slang group sits above "who/what": a question with slang in it still gets the slang reply.
            Assert.Equal("THAT WORD IS NEW", engine.Respond(db.Exchange("ex_stop"), "what is sigma").Lines[0]);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryEllenExchangeKnowsTheLastOperatorAndTheSlang(int night)
        {
            if (!Present) return;
            var db = Night(night);
            var engine = new DialogueEngine(db);
            foreach (var ex in db.Dialogue.exchanges)
            {
                if (!string.IsNullOrEmpty(ex.voice) || ex.id == "ex3_confirm") continue;
                Assert.False(engine.Respond(ex, "the last guy").IsFallback, ex.id + ": 'the last guy' falls back");
                Assert.False(engine.Respond(ex, "gyatt").IsFallback, ex.id + ": slang falls back");
            }
        }

        [Fact]
        public void NightTwoTeachesLookingAwayAndNightThreeSteersTheLastMiss()
        {
            if (!Present) return;
            Assert.Equal(new[] { "I SAID CLOSE IT", "OR LOOK AT ANOTHER CAMERA" }, Night(2).Lines("n2_rounds_again"));
            Assert.Equal(new[] { "READ IT FIRST" }, Night(2).Lines("n2_read_it_first"));
            Assert.Equal(new[] { "STAY", "OR LET ME GO" }, Night(3).Lines("n3_final_third"));
            Assert.Equal("Record owner: 209 (held).", Night(2).Text("shred.confirm.note.employee_209"));
        }

        [Fact]
        public void HelloIsAGreetingNotASwear()
        {
            Assert.Equal("other", DialogueEngine.Categorize(DialogueEngine.Normalize("hello")));
            Assert.Equal("other", DialogueEngine.Categorize(DialogueEngine.Normalize("in a shell")));
            Assert.Equal("swear", DialogueEngine.Categorize(DialogueEngine.Normalize("what the hell")));
        }

        // ------------------------------------------------------------------ M3: the session log

        [Fact]
        public void TypedLinesComeBackAsASessionLog()
        {
            Assert.Equal("02:04 CROURKE> make me", NightTemplates.LogLine(2 * 60 + 4, "make me"));
            Assert.Equal("CROURKE> hello", NightTemplates.LogLine(-1, "hello"));
            var t = NightTemplates.Tokens(null, new[] { "make me", "who are you" }, new[] { 124, 131 });
            Assert.Equal("02:04 CROURKE> make me", t["log1"]);
            Assert.Equal("02:11 CROURKE> who are you", t["log2"]);
            Assert.Equal("CROURKE> " + NightTemplates.NoReply, t["log3"]);
            Assert.Equal("make me", t["line1"]);
            Assert.Contains("log1", NightTemplates.Known);
        }

        [Fact]
        public void TheNightTwoCacheFileUsesTheLogTokens()
        {
            if (!Present) return;
            var cache = Night(2).FileSystem.files.First(f => f.id == ContentIds.FileCacheN2);
            Assert.Contains("{log1}", cache.content);
            Assert.DoesNotContain("{line1}", cache.content);
        }

        [Fact]
        public void TheSaveKeepsWhenEachLineWasTyped()
        {
            var d = new SaveData();
            d.RecordNightComplete(new NightResult
            {
                Night = 1, EndingId = "n1_blackout", Memory = new FlagSnapshot(), Seconds = 700f,
                PlayerLines = new[] { "make me", "   ", "who", "why", "extra" },
                PlayerLineMinutes = new[] { 124, 125, 126, 127, 128 },
            });
            Assert.Equal(new[] { "make me", "who", "why" }, d.playerLines);
            Assert.Equal(new[] { 124, 126, 127 }, d.playerLineMinutes);
            d.NewGame();
            Assert.Empty(d.playerLineMinutes);
        }

        // ------------------------------------------------------------------ M10: CAM 04's caption loop

        [Fact]
        public void TheShelfLoopStartsAtSeventeenAndHoldsEighteen()
        {
            string[] labels =
            {
                "SHELF 12: a", "SHELF 13: b", "SHELF 14: c", "SHELF 15: d", "SHELF 16: e", "SHELF 17: f", "SHELF 18: g", "SHELF 19: h",
            };
            int start = ShelfCaptions.Find(labels, ShelfCaptions.StartShelf), hold = ShelfCaptions.Find(labels, ShelfCaptions.HoldShelf);
            Assert.Equal(5, start);
            Assert.Equal(6, hold);
            Assert.Equal(5, ShelfCaptions.Index(0f, labels.Length, start, hold));
            Assert.Equal(6, ShelfCaptions.Index(3.5f, labels.Length, start, hold));
            Assert.Equal(6, ShelfCaptions.Index(3.5f + 4.9f, labels.Length, start, hold));
            Assert.Equal(7, ShelfCaptions.Index(3.5f + 5.05f, labels.Length, start, hold));
            // One cycle shows every label (the shelf-check orders need 14, 16 and 18).
            var seen = new HashSet<int>();
            float cycle = ShelfCaptions.CycleSeconds(labels.Length, hold);
            Assert.Equal(8 * 3.5f + 1.5f, cycle, 3);
            for (float t = 0f; t < cycle; t += 0.25f) seen.Add(ShelfCaptions.Index(t, labels.Length, start, hold));
            Assert.Equal(labels.Length, seen.Count);
            // Missing labels fall back to a plain loop from the first one.
            Assert.Equal(0, ShelfCaptions.Index(0f, 3, -1, -1));
        }

        // ------------------------------------------------------------------ review fixes (ReviewPhaseF)

        [Fact]
        public void SetLevelNeverGoesBelowTheModesFloor()
        {
            var story = new AdaptiveAssist(2, 2);
            story.SetLevel(0);
            Assert.Equal(2, story.Level);
            var normal = new AdaptiveAssist(1, 0);
            normal.SetLevel(0);
            Assert.Equal(0, normal.Level);
        }
    }
}
