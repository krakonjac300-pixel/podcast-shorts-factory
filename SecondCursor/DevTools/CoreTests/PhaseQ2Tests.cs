using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase Q2 (memory made loud, personal work): the echo filter, name capture and the forbidden names, the measured capture profile,
    /// the Retention Record, what New Game and the demo's handoff carry, the tokens in her lines and the endings, and the new content
    /// (the name groups, D3's audit of Ruth, the personal Night 1 and Night 2 lines).
    /// </summary>
    public class PhaseQ2Tests
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

        static Func<string, object[], string> Fmt(ContentDatabase db) => (k, a) => a == null ? db.Text(k) : db.Format(k, a);

        // ------------------------------------------------------------------ the echo filter

        [Fact]
        public void OrdinarySwearingIsQuotedBack()
        {
            foreach (var s in new[] { "make me", "fuck you", "who are you?", "shit", "damn it", "leave me alone", "go to hell" })
            {
                Assert.False(EchoFilter.Withheld(s), s);
                Assert.Equal(SaveData.SanitizePlayerLine(s), EchoFilter.ForFile(s));
            }
        }

        [Fact]
        public void SlursAndThePersonalAreWithheldInEveryDisguise()
        {
            // Built from parts so the test file carries no slur either.
            string n = "n" + "igg" + "er", f = "f" + "agg" + "ot";
            foreach (var s in new[] { n, "you " + n, n.ToUpperInvariant() + "S", "n i g g e r", "n1gger", "f" + "aaagg" + "ot", f + "ry", "kys", "kys!", "nazi!", "kill yourself", "c" + "unt" })
            {
                Assert.True(EchoFilter.IsBlocked(s), s);
                Assert.Equal(EchoFilter.RedactedFile, EchoFilter.ForFile(s));
                Assert.Equal(EchoFilter.RedactedVoice, EchoFilter.ForVoice(s));
            }
            // Whole-word entries leave ordinary words alone.
            foreach (var s in new[] { "spicy food", "a raccoon", "grape juice", "niger river", "scunthorpe", "skyscraper", "therapist" })
                Assert.False(EchoFilter.IsBlocked(s), s);
            foreach (var s in new[] { "me@example.com", "www.mysite.tv", "call 5551234567", "12 Elm Street", "https://x.io" })
            {
                Assert.True(EchoFilter.IsPersonal(s), s);
                Assert.Equal(EchoFilter.RedactedFile, EchoFilter.ForFile(s));
            }
            Assert.False(EchoFilter.IsPersonal("017 is 214"));
            Assert.False(EchoFilter.IsPersonal("wait...me"));
            Assert.False(EchoFilter.IsPersonal("why... me"));
        }

        [Fact]
        public void HerVoiceIsCapitalsWithoutPunctuationAndShort()
        {
            Assert.Equal("WHO ARE YOU", EchoFilter.ForVoice("who are you?"));
            Assert.Equal("DONT TOUCH THAT", EchoFilter.ForVoice("don't touch that!!"));
            Assert.Equal("", EchoFilter.ForVoice("   "));
            Assert.Equal("", EchoFilter.ForFile(null));
            string v = EchoFilter.ForVoice("please please please let me go home now");
            Assert.True(v.Length <= EchoFilter.VoiceMax, v);
            Assert.Equal("PLEASE PLEASE PLEASE LET ME", v);
            Assert.Equal(new string('A', 28), EchoFilter.ForVoice(new string('a', 40)));
            Assert.Matches("^[A-Z0-9 ]+$", EchoFilter.ForVoice("asdf; qwer, 99!"));
        }

        // ------------------------------------------------------------------ the name

        [Theory]
        [InlineData("my name is Kosta", "kosta")]
        [InlineData("My name's Kosta", "kosta")]
        [InlineData("call me k0sta99", "k0sta99")]
        [InlineData("ok fine. I'm called Sam", "sam")]
        [InlineData("name is Dee and i am scared", "dee")]
        [InlineData("i am called Sam", "sam")]
        public void ANameIsTakenOnlyFromItsForms(string line, string name)
        {
            var r = NameCapture.Read(line);
            Assert.Equal(NameKind.Name, r.Kind);
            Assert.Equal(name, r.Name);
        }

        [Theory]
        [InlineData("i am scared")]
        [InlineData("my name is not important")]
        [InlineData("call me later")]
        [InlineData("my name is x")]
        [InlineData("my name is abcdefghijklm")]
        [InlineData("your name is bob")]
        [InlineData("what is my name")]
        [InlineData("call me at 5")]
        [InlineData("call me now")]
        [InlineData("the file name is employee_017")]
        [InlineData("hi, name is dee")]
        [InlineData("my name is")]
        public void NotEveryLineIsAName(string line) => Assert.Equal(NameKind.None, NameCapture.Read(line).Kind);

        [Fact]
        public void ABlockedNameIsSilentlyIgnored()
        {
            Assert.Equal(NameKind.None, NameCapture.Read("my name is " + "c" + "unt").Kind);
            Assert.Equal(NameKind.None, NameCapture.Read("call me 5551234567").Kind);
            Assert.False(NameCapture.IsValid("ab cd"));
            Assert.True(NameCapture.IsValid("kosta"));
        }

        [Theory]
        [InlineData("my name is Ellen", "taken")]
        [InlineData("call me marsh", "taken")]
        [InlineData("my name is Casey", "casey")]
        [InlineData("my name is rourke", "casey")]
        [InlineData("call me gary", "gary")]
        [InlineData("my name is Voss", "voss")]
        [InlineData("my name is 017", "017")]
        public void TheStorysNamesAreAnsweredAndNeverKept(string line, string key)
        {
            var r = NameCapture.Read(line);
            Assert.Equal(NameKind.Forbidden, r.Kind);
            Assert.Equal(key, r.ForbiddenKey);
            Assert.False(NameCapture.IsValid(r.Name));
        }

        [Fact]
        public void EveryForbiddenNameHasHerAnswerInTheDemo()
        {
            if (!Present) return;
            var db = Demo();
            foreach (var key in new[] { "taken", "casey", "gary", "voss", "017" })
                Assert.True(db.Lines("name_" + key).Length > 0, key);
        }

        [Fact]
        public void TheNameGroupNeverAnswersAKeyword()
        {
            if (!Present) return;
            var db = Night(3);
            var engine = new DialogueEngine(db);
            foreach (var id in new[] { "ex_stop", "ex_two", "ex_three", "ex2_back", "ex2_gary_one", "ex3_ruth" })
            {
                var ex = db.Exchange(id);
                Assert.NotNull(DialogueEngine.NameResponse(ex));
                Assert.NotEqual(DialogueEngine.NameTag, engine.Respond(ex, "my name is bob").Tag);
                Assert.NotEqual(DialogueEngine.NameTag, engine.Respond(ex, "").Tag);
            }
            // The finale's decisions never get one: a name must never stand in for STAY.
            Assert.Null(DialogueEngine.NameResponse(db.Exchange("ex3_final")));
            Assert.Null(DialogueEngine.NameResponse(db.Exchange("ex3_confirm")));
        }

        // ------------------------------------------------------------------ tokens

        static SaveData Save(string[] lines = null, string name = "", CaptureStats n1 = null)
        {
            var d = new SaveData { playerLines = lines ?? Array.Empty<string>(), playerName = name };
            if (n1 != null) d.capture[0] = n1;
            return d;
        }

        [Fact]
        public void WithoutANameTheRecordKeepsItsC()
        {
            var t = NightTemplates.ForSave(new NarrativeFlags(), Save());
            Assert.Equal("", t["name"]);
            Assert.Equal("", t["NAME"]);
            Assert.Equal("C", t["n0"]);
            Assert.Equal("", t["namerows"]);
            Assert.Equal("", t["said1"]);
            Assert.Equal("", t["LINE1"]);
            Assert.Equal(NightTemplates.NoReply, t["line1"]);
            Assert.Equal(NightTemplates.NoLastInput, t["lastn3"]);
            var named = NightTemplates.ForSave(new NarrativeFlags(), Save(new[] { "make me!" }, "kosta"));
            Assert.Equal("kosta", named["name"]);
            Assert.Equal("KOSTA", named["NAME"]);
            Assert.Equal("K", named["n0"]);
            Assert.Contains("..name given: kosta..", named["namerows"]);
            Assert.Equal("make me!", named["said1"]);
            Assert.Equal("MAKE ME", named["LINE1"]);
        }

        [Fact]
        public void EveryEchoIsFiltered()
        {
            string bad = "you " + "n" + "igg" + "er";
            var t = NightTemplates.ForSave(new NarrativeFlags(), Save(new[] { bad, "my email is me@x.com" }));
            Assert.Equal(EchoFilter.RedactedFile, t["line1"]);
            Assert.Equal(EchoFilter.RedactedFile, t["said1"]);
            Assert.Equal(EchoFilter.RedactedVoice, t["LINE1"]);
            Assert.Equal(EchoFilter.RedactedFile, t["line2"]);
            Assert.Contains(EchoFilter.RedactedFile, t["log1"]);
            var prev = new SaveData { previousLines = new[] { bad } };
            Assert.Equal(EchoFilter.RedactedVoice, NightTemplates.ForSave(null, prev)["PREV1"]);
        }

        [Fact]
        public void TheProfileRowsShowOnlyWhatWasMeasured()
        {
            var none = NightTemplates.ForSave(null, Save());
            Assert.Contains("no drag on record", none["dragrow"]);
            Assert.Contains("never opened", none["mailrow"]);
            Assert.Contains("never reached for Yes", none["yesrow"]);
            Assert.Contains("never looked", none["camrow"]);
            Assert.Equal("3520", none["segments"]);   // 88%, as Personnel says after Night 1
            var n1 = new CaptureStats
            {
                recorded = true, dragSeconds = 1.74f, longestPause = 0.9f, dragFile = "ledger_1994.dat", mailReads = 3, yesSeconds = 1.6f, yesCount = 2, camLooks = 1,
            };
            var t = NightTemplates.ForSave(null, Save(null, "", n1));
            Assert.Equal("..ledger_1994.dat..drag..1.7s..hesitation 0.9s..", t["dragrow"]);
            Assert.Equal("..%%..read mail_welcome..read it 3 times..", t["mailrow"]);
            Assert.Equal("..reached for Yes..0.8s..reached again..", t["yesrow"]);
            n1.yesCount = 1;
            n1.yesSeconds = 0.6f;
            Assert.Equal("..reached for Yes..0.6s..", NightTemplates.ForSave(null, Save(null, "", n1))["yesrow"]);
            n1.yesCount = 2;
            n1.yesSeconds = 1.6f;
            Assert.Equal("..camera 03..looked once..#..", t["camrow"]);
            // A record from an older save (not recorded) is never shown as numbers.
            n1.recorded = false;
            Assert.Contains("no drag on record", NightTemplates.ForSave(null, Save(null, "", n1))["dragrow"]);
        }

        [Fact]
        public void ThePostGameEchoNeedsShred()
        {
            Assert.Equal("", NightTemplates.ForSave(null, new SaveData { endingsSeen = new[] { "n3_keep" } })["p2own"]);
            var t = NightTemplates.ForSave(null, new SaveData { endingsSeen = new[] { "n1_blackout", "n3_shred" } });
            Assert.Equal(" (214)", t["p2own"]);
            Assert.Equal("214", t["p2owner"]);
            if (!Present) return;
            var db = Demo();
            Assert.Contains("{p2own}", string.Join("|", db.Story.biosLines));
            var cfg = db.FileSystem.files.Single(f => f.id == "nexus_cfg");
            Assert.Contains("template", cfg.tags);
            Assert.Contains("POINTER_2_OWNER=214", NightTemplates.Fill(cfg.content, t));
        }

        [Fact]
        public void ALineWhoseTokensAreEmptyIsLeftOutWithItsSpeaker()
        {
            var tokens = new Dictionary<string, string> { ["LINE1"] = "", ["NAME"] = "KOSTA", ["said1"] = "" };
            var lines = NightTemplates.FillLines(new[] { "A", "YOU SAID {LINE1}", "GOODNIGHT {NAME}", "{said1}", "B {unknown}" }, tokens,
                new[] { "entity", "entity", "entity", "casey", "both" }, out var speakers);
            Assert.Equal(new[] { "A", "GOODNIGHT KOSTA", "B {unknown}" }, lines);
            Assert.Equal(new[] { "entity", "entity", "both" }, speakers);
        }

        [Fact]
        public void TheEndingsCarryThePlayersWordsOnlyWhenThereAreAny()
        {
            if (!Present) return;
            var db = Night(3);
            var shred = db.LineSet("n3_end_shred");
            var plain = NightTemplates.FillLines(shred.lines, NightTemplates.ForSave(null, Save()), shred.speakers, out var s0);
            Assert.Equal(new[] { "IS THIS ON", "THE HEATER IS CLICKING", "MY NAME IS C", "I HAD IT A MINUTE AGO", "SOMEONE NEW IS LOGGING ON", "I WILL KEEP THE NEXT ONE" }, plain);
            Assert.Equal(plain.Length, s0.Length);
            var yours = NightTemplates.FillLines(shred.lines, NightTemplates.ForSave(null, Save(new[] { "make me" }, "kosta")), shred.speakers, out _);
            Assert.Equal(new[] { "IS THIS ON", "make me", "THE HEATER IS CLICKING", "MY NAME IS K", "I HAD IT A MINUTE AGO" }, yours.Take(5).ToArray());
            foreach (var id in new[] { "n3_end_keep_default", "n3_end_keep_stay" })
            {
                var set = db.LineSet(id);
                var none = NightTemplates.FillLines(set.lines, NightTemplates.ForSave(null, Save()), set.speakers, out _);
                Assert.DoesNotContain(none, l => l.StartsWith("YOU SAID") || l.StartsWith("GOODNIGHT"));
                var with = NightTemplates.FillLines(set.lines, NightTemplates.ForSave(null, Save(new[] { "who are you?" }, "kosta")), set.speakers, out _);
                Assert.Contains("YOU SAID WHO ARE YOU", with);
                Assert.Contains("GOODNIGHT KOSTA", with);
                Assert.Equal("WE WORK NIGHTS", with.Last());
            }
            var log = db.LineSet("n3_end_logoff_sys");
            Assert.Equal("LAST INPUT (214) ... NONE", NightTemplates.FillLines(log.lines, NightTemplates.ForSave(null, Save()))[3]);
            Assert.Equal(log.lines.Length, log.speakers.Length);
        }

        [Fact]
        public void NightTwoQuotesTheFirstLineInHerVoice()
        {
            if (!Present) return;
            var db = Night(2);
            Assert.Equal(new[] { "LAST NIGHT YOU TYPED", "MAKE ME", "I KEPT IT" },
                NightTemplates.FillLines(db.Lines("n2_back_quote"), NightTemplates.ForSave(null, Save(new[] { "make me." }))));
            Assert.Equal(3, db.Lines("n2_back_quote.none").Length);
            Assert.Equal(3, db.Lines("n2_back_quote.unread").Length);
            var f214 = db.FileSystem.files.Single(f => f.id == "employee_214");
            string filled = NightTemplates.Fill(f214.content, NightTemplates.ForSave(null, Save(new[] { "hi" }, "kosta")));
            Assert.DoesNotContain("{", filled);
            Assert.DoesNotContain("0.4s", filled);   // the old hardcoded numbers are gone
            Assert.Contains("..name given: kosta..", filled);
            Assert.Contains("SEGMENTS: 3520 OF 4000", filled);
        }

        // ------------------------------------------------------------------ measuring

        [Fact]
        public void ADragIsMeasuredWithItsLongestStillness()
        {
            var m = new DragMeter();
            Assert.Equal((-1f, -1f), m.End(1f));
            m.Begin(0f, 0f, 0f);
            float t = 0f;
            for (int i = 0; i < 10; i++) m.Sample(t += 0.1f, i * 20f, 0f);   // moving
            for (int i = 0; i < 8; i++) m.Sample(t += 0.1f, 180f, 0f);       // still (with the next sample, 0.9 s)
            for (int i = 0; i < 5; i++) m.Sample(t += 0.1f, 180f + i * 20f, 0f);
            var (seconds, pause) = m.End(t);
            Assert.Equal(2.3f, seconds, 3);
            Assert.InRange(pause, 0.85f, 0.95f);
            Assert.False(m.Running);
        }

        [Fact]
        public void TheProfileFollowsTheStorysNumbers()
        {
            Assert.Equal(0, CaptureProfile.Percent(0, false, ""));
            Assert.Equal(88, CaptureProfile.Percent(1, false, ""));
            Assert.Equal(96, CaptureProfile.Percent(2, false, ""));
            Assert.Equal(88, CaptureProfile.Percent(2, true, ""));
            Assert.Equal(100, CaptureProfile.Percent(3, true, "n3_shred"));
            Assert.Equal(96, CaptureProfile.Percent(3, false, "n3_keep"));
            Assert.Equal(3, CaptureProfile.Words("  who are   you "));
            Assert.Equal("twice", CaptureProfile.Times(2));
        }

        // ------------------------------------------------------------------ the Retention Record

        static SaveData Run()
        {
            var d = new SaveData { playerName = "kosta", endingsSeen = new[] { "n1_blackout", "n3_keep" } };
            d.capture[0] = new CaptureStats { recorded = true, words017 = 6, camLooks = 3, firstLine = "who are you", seconds = 600f, yesSeconds = 1f, yesCount = 1, tugWins = 1 };
            d.capture[1] = new CaptureStats { recorded = true, words017 = 10, camLooks = 2, seconds = 900f, yesSeconds = 2f, yesCount = 2, tugLosses = 2 };
            d.capture[2] = new CaptureStats { recorded = true, words017 = 4, camLooks = 5, seconds = 1200f };
            d.memory = new FlagSnapshot { flags = new[] { "m.n2.wo3320.reject", "m.n2.wo3322.reject", "m.n3.wo3333.approve", MemoryFlags.N3SaidName } };
            return d;
        }

        static readonly Dictionary<string, string> Correct = new Dictionary<string, string>
        {
            ["wo_3320"] = "approve", ["wo_3322"] = "reject", ["wo_3324"] = "reject", ["wo_3332"] = "reject", ["wo_3333"] = "reject",
        };

        [Fact]
        public void OrdersAgainstTheRuleComeFromTheRememberedDecisions()
        {
            Assert.Equal(new[] { "WO-3320", "WO-3333" }, RetentionRecord.AgainstTheRule(Run().memory, Correct));
            Assert.Empty(RetentionRecord.AgainstTheRule(null, Correct));
        }

        [Fact]
        public void TheFullRecordIsTrueAndSpoilerFree()
        {
            if (!Present) return;
            var db = Night(3);
            var rows = RetentionRecord.Full(Run(), "n3_shred", Correct, Fmt(db));
            string all = string.Join("\n", rows.Select(r => r.Label + " " + r.Value));
            Assert.Contains("SHIFTS LOGGED 3   (45 MIN AT TERMINAL)", all);
            Assert.Contains("WORDS TYPED TO 017 20", all);
            Assert.Contains("FIRST WORDS \"who are you\"", all);
            Assert.Contains("HESITATION BEFORE YES 1.0 S   (AVERAGE OF 3)", all);
            Assert.Contains("LOOKED AT CAM 03 10 TIMES", all);
            Assert.Contains("TUGS 1 WON, 2 LOST", all);
            Assert.Contains("ORDERS AGAINST THE RULE 2   (WO-3320, WO-3333)", all);
            Assert.Contains("NAME GIVEN \"kosta\"", all);
            Assert.Contains("SAID HER NAME YES", all);
            Assert.Contains("CAPTURE 214 100%", all);
            // KEEP was seen and SHRED is this one: only LOG OFF is sealed, with its hint.
            Assert.Contains("1 SEALED", all);
            Assert.Contains("SEVEN O'CLOCK", all);
            Assert.DoesNotContain("SAY IT", all);
            foreach (var spoiler in new[] { "SHRED", "KEEP", "LOG OFF", "Ellen", "ELLEN", "Marsh", "MARSH", "Gary", "GARY", "Ruth", "Custodial", "{" })
                Assert.DoesNotContain(spoiler, all);
            foreach (var r in rows) Assert.Equal(r.Label + "\t" + r.Value, r.Pack());
            Assert.Equal("A", RecordRow.Unpack("A\tB").Label);
            Assert.Equal("B", RecordRow.Unpack("A\tB").Value);
        }

        [Fact]
        public void TheCardRecordIsFourRowsAndFitsTheDemo()
        {
            if (!Present) return;
            var demo = Demo();
            var d = Run();
            var n1 = RetentionRecord.Card(1, d, Fmt(demo));
            Assert.Equal(4, n1.Count);
            Assert.Equal(new[] { "CAPTURE 214", "WORDS TYPED TO 017", "LOOKED AT CAM 03", "FIRST WORDS" }, n1.Select(r => r.Label).ToArray());
            Assert.Equal(new[] { "88%", "6", "3 TIMES", "\"who are you\"" }, n1.Select(r => r.Value).ToArray());
            var n2 = RetentionRecord.Card(2, d, Fmt(Night(2)));
            Assert.Equal("96%", n2[0].Value);
            Assert.Equal("HESITATION BEFORE YES", n2[3].Label);
            Assert.Empty(RetentionRecord.Card(1, new SaveData(), Fmt(demo)));   // nothing measured, nothing shown
            Assert.Equal("NEVER", RetentionRecord.Card(1, new SaveData { capture = new[] { new CaptureStats { recorded = true }, new CaptureStats(), new CaptureStats() } }, Fmt(demo))[2].Value);
            // The demo has the card's words but none of the full page's.
            foreach (var key in new[] { "notify.kept.copy", "end.n1.kept", "kept.reply", "kept.bin", "kept.name", "records.profile" }) Assert.True(demo.HasText(key), key);
            foreach (var key in new[] { "record.status.n3_shred", "record.hint.n3_keep", "title.tagline.214" }) Assert.False(demo.HasText(key), key);
        }

        // ------------------------------------------------------------------ what carries over

        [Fact]
        public void NewGameKeepsTheNameThePathAndTheLastRunsWords()
        {
            var d = new SaveData { playerLines = new[] { "who are you" }, playerName = "kosta", ghostPath = new[] { 1, 2, 3, 4 }, lastRecord = new[] { "A\tB" }, endingsSeen = new[] { "n3_keep" } };
            d.capture[0].recorded = true;
            d.NewGame();
            Assert.Equal(new[] { "who are you" }, d.previousLines);
            Assert.Empty(d.playerLines);
            Assert.Equal("kosta", d.playerName);
            Assert.Equal(new[] { 1, 2, 3, 4 }, d.ghostPath);
            Assert.Single(d.lastRecord);
            Assert.False(d.capture[0].recorded);
            Assert.True(d.EchoesLastRun);
            // A run with no lines keeps the older words.
            d.NewGame();
            Assert.Equal(new[] { "who are you" }, d.previousLines);
            // No ending seen and no demo: nothing to echo yet.
            Assert.False(new SaveData { previousLines = new[] { "hi" } }.EchoesLastRun);
            Assert.True(new SaveData { previousLines = new[] { "hi" }, demoImported = true }.EchoesLastRun);
            // A line her voice cannot say (only symbols) is never quoted as an empty LAST TIME YOU SAID.
            Assert.False(new SaveData { previousLines = new[] { "???" }, demoImported = true }.EchoesLastRun);
        }

        [Fact]
        public void ANightKeepsItsMeasurementsAndAReplayClearsTheLaterOnes()
        {
            var d = new SaveData();
            d.RecordNightComplete(new NightResult { Night = 2, Capture = new CaptureStats { recorded = true, words017 = 5, firstLine = "  hi  " } });
            d.RecordNightComplete(new NightResult { Night = 3, Capture = new CaptureStats { recorded = true, words017 = 7 } });
            Assert.True(d.capture[1].recorded);
            Assert.Equal("hi", d.capture[1].firstLine);
            Assert.True(d.capture[2].recorded);
            d.RecordNightComplete(new NightResult { Night = 2, Capture = new CaptureStats { recorded = true, words017 = 1 } });
            Assert.Equal(1, d.capture[1].words017);
            Assert.False(d.capture[2].recorded);
            // A night measured only in part (Continue, a jump) is kept as not recorded: nothing of it is shown.
            d.RecordNightComplete(new NightResult { Night = 2, Capture = new CaptureStats { recorded = false, words017 = 9 } });
            Assert.False(d.capture[1].recorded);
            Assert.Null(RetentionRecord.Night(d, 2));
            var path = new int[400];
            d.RecordNightComplete(new NightResult { Night = 1, GhostPath = path });
            Assert.Equal(SaveData.GhostMaxPoints * 2, d.ghostPath.Length);
            d.RecordNightComplete(new NightResult { Night = 1 });
            Assert.Equal(SaveData.GhostMaxPoints * 2, d.ghostPath.Length);   // a night without a drag keeps the saved one
        }

        [Fact]
        public void TheNameMustBeValidToBeKept()
        {
            var d = new SaveData();
            Assert.False(d.SetPlayerName("ellen"));
            Assert.False(d.SetPlayerName("x"));
            Assert.True(d.SetPlayerName("Kosta"));
            Assert.Equal("kosta", d.playerName);
            Assert.False(d.SetPlayerName("kosta"));
        }

        [Fact]
        public void AnOlderSaveLoadsWithEmptyMemory()
        {
            var d = new SaveData { capture = null, previousLines = null, playerName = null, ghostPath = null, lastRecord = null, demoLines = null, lastRecordEnding = null };
            d.Migrate();
            Assert.Equal(SaveData.Nights, d.capture.Length);
            Assert.All(d.capture, c => Assert.False(c.recorded));
            Assert.Equal("", d.playerName);
            Assert.Empty(d.previousLines);
            Assert.Empty(d.ghostPath);
            Assert.Equal(SaveData.CurrentVersion, d.version);
        }

        // ------------------------------------------------------------------ the demo's handoff

        [Fact]
        public void TheDemoHandoffRoundTripsAndIsReadOnce()
        {
            var demo = new SaveData { playerLines = new[] { "who are you", "  stop {now} " }, playerName = "kosta" };
            string text = DemoHandoff.Compose(demo, true);
            Assert.StartsWith(DemoHandoff.Header, text);
            var h = DemoHandoff.Parse(text.Replace("\n", "\r\n"));
            Assert.NotNull(h);
            Assert.Equal("kosta", h.Name);
            Assert.Equal(new[] { "who are you", "stop now" }, h.Lines);
            Assert.True(h.Shredded017);
            var full = new SaveData();
            Assert.True(h.ApplyTo(full));
            Assert.True(full.demoImported);
            Assert.Equal("kosta", full.playerName);
            Assert.Equal(h.Lines, full.previousLines);
            Assert.Equal(h.Lines, full.demoLines);
            Assert.True(full.EchoesLastRun);
            Assert.False(h.ApplyTo(full));   // once
            // A name given in the full game, and its own last run, stay.
            var own = new SaveData { playerName = "sam", previousLines = new[] { "mine" } };
            h.ApplyTo(own);
            Assert.Equal("sam", own.playerName);
            Assert.Equal(new[] { "mine" }, own.previousLines);
        }

        [Fact]
        public void ABadHandoffIsSkipped()
        {
            Assert.Null(DemoHandoff.Parse(null));
            Assert.Null(DemoHandoff.Parse("hello\nline=hi"));
            Assert.Null(DemoHandoff.Parse(DemoHandoff.Header + "\n"));
            Assert.Null(DemoHandoff.Parse(DemoHandoff.Header + "\nline=" + new string('a', DemoHandoff.MaxBytes)));
            var h = DemoHandoff.Parse(DemoHandoff.Header + "\nname=ellen\nline=a\nline=b\nline=c\nline=d\nother=1");
            Assert.Equal("", h.Name);   // the story's names are never taken
            Assert.Equal(3, h.Lines.Length);
        }

        // ------------------------------------------------------------------ personal work

        [Fact]
        public void TheAuditOfRuthIsAFairChoiceWithItsEvidence()
        {
            if (!Present) return;
            var db = Night(2);
            var o = db.Order("wo_3324");
            Assert.True(WorkOrderRules.IsChoice(o));
            Assert.Equal("reject", o.correct);
            Assert.Equal("118", o.employeeRef);
            Assert.Contains("no calls to ext. 2204", db.Employee("118").notes);
            Assert.Contains("If anyone asks, tell them I checked in.", db.Email("mail_n2_ruth_warning").body);
            Assert.Single(db.Tasks.tasks, t => t.id == "t2_audit_3324" && t.targets.SequenceEqual(new[] { "wo_3324" }));
            foreach (var id in new[] { "n2_audit_view", "n2_audit_covered", "n2_audit_reported" }) Assert.True(db.Lines(id).Length > 0, id);
            Assert.Equal(MemoryFlags.N2CoveredRuth, WorkOrderRules.MemoryKey(2, "wo_3324", "approve"));
            Assert.Equal(MemoryFlags.N2ReportedRuth, WorkOrderRules.MemoryKey(2, "wo_3324", "reject"));
            Assert.Null(Demo().Order("wo_3324"));
        }

        [Fact]
        public void NightThreeRemembersWhatYouToldSecurity()
        {
            if (!Present) return;
            var db = Night(3);
            string Notes(NarrativeFlags f) => NightTemplates.Fill(db.Employee("118").notes, NightTemplates.Tokens(f, null));
            string Mail(NarrativeFlags f) => NightTemplates.Fill(db.Email("mail_n3_ruth_drive").body, NightTemplates.Tokens(f, null));
            var covered = new NarrativeFlags();
            covered.Set(MemoryFlags.N2CoveredRuth);
            var reported = new NarrativeFlags();
            reported.Set(MemoryFlags.N2ReportedRuth);
            Assert.Contains("confirmed by WS-04", Notes(covered));
            Assert.Contains("Under review", Notes(reported));
            Assert.DoesNotContain("{", Notes(new NarrativeFlags()));
            Assert.Contains("You covered for me last night", Mail(covered));
            Assert.Contains("you told them the truth", Mail(reported));
            Assert.Contains("I'm asking you to break a rule for me.", Mail(new NarrativeFlags()));
        }

        [Fact]
        public void JoanIsAPersonBeforeHerOrderComes()
        {
            if (!Present) return;
            var demo = Demo();
            Assert.Contains("Joan Nakamura", demo.Email("mail_castell_gary").body);
            Assert.Equal("163", demo.Order("wo_3318").employeeRef);
            Assert.Contains("Retention review moved up", demo.Text("n1.163.reject"));
            Assert.Contains("Retention review moved up", demo.Text("n1.163.approve"));
            Assert.Contains("WO-3318", Night(2).Order("wo_3319").fields.Single(f => f.label == "Reason").value);
        }
    }
}
