using System;
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
    /// Phase L (the owner's suggestions 2, 4 and 6, and the fourth blind playtest's findings 13 to 15): the three-line Quick Start and
    /// the one-time tips, the recovered-text view, "THEN WATCH THE SCREEN", and the four new decisions (Joan's box, the pointer
    /// patch, Gary's box, Ruth's drive).
    /// </summary>
    public class PhaseLTests
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

        // ------------------------------------------------------------------ the introduction

        [Fact]
        public void TheQuickStartIsThreeShortLines()
        {
            if (!Present) return;
            foreach (var variant in new[] { "", "deck" })
            {
                var db = Demo();
                db.Variant = variant;
                var bullets = db.Text("quickstart.body").Split('\n').Where(l => l.StartsWith("- ", StringComparison.Ordinal)).ToList();
                Assert.Equal(3, bullets.Count);
                Assert.Contains("Work Queue", bullets[0]);
                Assert.Contains("hint", bullets[0]);
                Assert.Contains(variant == "deck" ? "Menu button" : "|| button", bullets[1]);
                Assert.Contains("NEXUS Help", bullets[2]);
            }
        }

        [Fact]
        public void WelcomeBackHasTheNexusLineAndTheTugWording()
        {
            if (!Present) return;
            foreach (var variant in new[] { "", "deck" })
            {
                var db = Demo();
                db.Variant = variant;
                string body = db.Text("welcome.body");
                Assert.Contains("The Nexus button (bottom left) lists every program and Help", body);
                Assert.Contains("the way the arrow points until the bar is yours, then", body);
                Assert.Contains(variant == "deck" ? "hold R2" : "hold the button", body);
            }
        }

        [Fact]
        public void EveryTipHasItsTextAndTheOnesNamingAnInputHaveDeckWording()
        {
            if (!Present) return;
            var db = Demo();
            foreach (var id in new[] { "open", "files", "move", "move.guided", "orders", "shred", "nexus", "reply" })
                Assert.True(db.HasText("tip." + id), "missing tip." + id);
            foreach (var id in new[] { "open", "files", "move", "move.guided", "orders", "shred", "reply" })
                Assert.True(db.HasText("tip." + id + ".deck"), "no Deck wording for tip." + id);
            db.Variant = "deck";
            Assert.Contains("R2", db.Text("tip.move"));
            Assert.Contains("R2", db.Text("tip.shred"));
            Assert.Contains("A twice", db.Text("tip.open"));
            Assert.DoesNotContain("click", db.Text("tip.orders"));
            Assert.Contains("HOLD R2", db.Text("tug.label.first"));
            Assert.Contains("{0}", db.Text("tug.label.first"));
            Assert.Contains("THEN LET GO", Demo().Text("tug.label.first"));
        }

        [Fact]
        public void TipsAreRememberedOncePerSaveAndNewGameKeepsThem()
        {
            var save = new SaveData();
            Assert.True(save.MarkTipShown("open"));
            Assert.False(save.MarkTipShown("open"));
            Assert.False(save.MarkTipShown(""));
            Assert.True(save.MarkTipShown("move"));
            save.NewGame();
            Assert.Equal(new[] { "open", "move" }, save.tipsShown);
            save.tipsShown = null;   // a save written before tips existed loads without the field
            save.Migrate();
            Assert.Empty(save.tipsShown);
        }

        [Fact]
        public void ResistingHerCostsTrustAndAMilderRefusalCostsLess()
        {
            var m = new EntityMemory();
            m.Record(MemoryKind.ResistedEntity, "x", 0f);
            Assert.Equal(-0.15f, m.Trust, 3);
            m.Seed(0f);
            m.Record(MemoryKind.ResistedEntity, "wo_3322", 0f, 0.10f);
            Assert.Equal(-0.10f, m.Trust, 3);
            Assert.Equal(2, m.Resistance);
        }

        // ------------------------------------------------------------------ "THEN WATCH THE SCREEN"

        [Fact]
        public void EveryWayIntoTheReplayTellsThePlayerToWatchTheScreen()
        {
            if (!Present) return;
            var three = Demo().Exchange("ex_three");
            var lines = three.responses.SelectMany(r => r.reply).Concat(three.fallback).ToList();
            Assert.DoesNotContain("WATCH", lines);
            Assert.DoesNotContain("THEN WATCH", lines);
            Assert.Contains("THEN WATCH THE SCREEN", lines);
            Assert.Contains("WATCH THE SCREEN", lines);
            foreach (var line in lines.Where(l => l.Contains("WATCH THE SCREEN")))
                Assert.InRange(line.Split(' ').Length, 1, 6);
        }

        // ------------------------------------------------------------------ recovered text

        [Fact]
        public void RecoveredTextDropsTheGlitchAndKeepsWhatWasSaid()
        {
            if (!Present) return;
            var glitch = new[] { '%', '#', '@', '&', '~', '^', '$' };
            var files = Night(3).FileSystem.files.Concat(Night(1).FileSystem.files).Where(f => f.tags.Contains("story") && f.name.StartsWith("employee_")).ToList();
            Assert.True(files.Count >= 3);
            foreach (var f in files)
            {
                string clean = RecoveredText.From(f.content.Replace("{line1}", "who are you").Replace("{line2}", "stop"));
                Assert.True(clean.IndexOfAny(glitch) < 0, f.id + " still has glitch characters");
                Assert.DoesNotContain("..", clean);
                Assert.True(clean.Length > 100, f.id + " lost its text");
            }
            string r017 = RecoveredText.From(files.First(f => f.id == "employee_017").content);
            Assert.Contains("is this on. the heater is clicking again.", r017);
            Assert.Contains("the door was closed when i sat down.", r017);   // d0wn
            Assert.Contains("REMAIN SEATED.", r017);
            Assert.Contains("i will keep the next one.", r017);
            Assert.StartsWith("LW-RETAIN 2.3 / SUBJ 017", r017);            // the header stays as it was
            Assert.Contains("[END OF READABLE SEGMENTS]", r017);
            // Repeats and cut-off echoes collapse: "017..017..017" and "i am still here..i am still..i am".
            Assert.DoesNotContain("REMA.", r017);
            Assert.DoesNotContain("i am still. ", r017);
        }

        [Fact]
        public void RecoveredTextLeavesAnOrdinaryDocumentAlone()
        {
            if (!Present) return;
            string ledger = Night(1).FileSystem.files.First(f => f.id == "ledger_1994").content;
            Assert.False(RecoveredText.IsGlitched(ledger));
            Assert.Equal(ledger, RecoveredText.From(ledger));
            string damaged = "RECLAIMED RECORDS: BATCH 47-B\n..%%..remain seated..remain seated..#..\nremain seated..remain seated..remain\n..@..seated..remain seated..~..\n[RECORD DAMAGED]";
            string clean = RecoveredText.From(damaged);
            Assert.Equal("RECLAIMED RECORDS: BATCH 47-B\nremain seated.\n[RECORD DAMAGED]", clean);
            Assert.Equal(clean, RecoveredText.From(clean));
            Assert.Equal("", RecoveredText.From(null));
            // A bare ellipsis is not damage: the line stays, and a document with only such lines has no recovered view.
            Assert.False(RecoveredText.IsGlitched("11/02 POINTER 2 IDLE\n...\n11/18 POINTER 1 ATTACHED"));
            Assert.Equal("a\n...\nb", RecoveredText.From("a\n...\nb"));
            Assert.True(RecoveredText.IsGlitched("a\n..%%..hello there..\nb"));
        }

        // ------------------------------------------------------------------ the decisions

        [Fact]
        public void TheDecisionsLiveOnlyInTheirNightsAndTheDemoHasNone()
        {
            if (!Present) return;
            Assert.DoesNotContain(Demo().WorkOrders.orders, WorkOrderRules.IsChoice);
            Assert.DoesNotContain(Night(1).WorkOrders.orders, WorkOrderRules.IsChoice);
            Assert.Equal(new[] { "wo_3320", "wo_3322" }, Night(2).WorkOrders.orders.Where(WorkOrderRules.IsChoice).Select(o => o.id).ToArray());
            Assert.Equal(new[] { "wo_3320", "wo_3322", "wo_3332", "wo_3333" }, Night(3).WorkOrders.orders.Where(WorkOrderRules.IsChoice).Select(o => o.id).ToArray());
            // The base strings (which the demo has) carry no text of the new orders; the shared result strings are full-game only.
            Assert.False(Demo().HasText("workorder.result"));
            Assert.True(Night(2).HasText("workorder.result"));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryChoiceOrderIsFair(int night)
        {
            if (!Present) return;
            var db = Night(night);
            foreach (var o in db.WorkOrders.orders.Where(WorkOrderRules.IsChoice))
            {
                string label = o.id.Replace("wo_", "WO-");
                Assert.Contains(label + " approved", o.resultApprove);
                Assert.Contains(label + " rejected", o.resultReject);
                Assert.Contains("ONLY if the owner's status is TERMINATED", o.instructions);   // the rule is stated in the order
                Assert.Contains("(" + label + ")", o.noteApprove + o.noteReject);              // the Personnel note names the order
                // One task decides exactly this order, says either decision is filed, and names where the evidence is.
                var task = db.Tasks.tasks.Single(t => t.type == "DecideOrder" && t.targets.Length == 1 && t.targets[0] == o.id);
                Assert.Contains("Either decision is filed.", task.hint);
                Assert.Contains("Personnel", task.hint);
                Assert.Contains("approve only if TERMINATED", task.hint);
                Assert.NotNull(db.Employee(o.employeeRef));
            }
        }

        [Fact]
        public void ADecisionIsRememberedUnderItsNightAndItsOrder()
        {
            Assert.Equal("m.n2.wo3320.reject", WorkOrderRules.MemoryKey(2, "wo_3320", "reject"));
            Assert.Equal(MemoryFlags.N2Box163Held, WorkOrderRules.MemoryKey(2, "wo_3320", "reject"));
            Assert.Equal(MemoryFlags.N2Box163Released, WorkOrderRules.MemoryKey(2, "wo_3320", "approve"));
            Assert.Equal(MemoryFlags.N2TookHand, WorkOrderRules.MemoryKey(2, "wo_3322", "approve"));
            Assert.Equal(MemoryFlags.N2LeftHand, WorkOrderRules.MemoryKey(2, "wo_3322", "reject"));
            Assert.Equal(MemoryFlags.N3Box209Kept, WorkOrderRules.MemoryKey(3, "wo_3332", "reject"));
            var flags = new NarrativeFlags();
            Assert.Null(WorkOrderRules.Remembered(flags, 2, "wo_3320"));
            flags.Set(MemoryFlags.N2Box163Held);
            Assert.Equal("reject", WorkOrderRules.Remembered(flags, 2, "wo_3320"));
            Assert.Null(WorkOrderRules.Remembered(flags, 3, "wo_3320"));
        }

        [Fact]
        public void OnlyOrdersWithBothResultsAreChoices()
        {
            var o = new WorkOrderData { resultApprove = "a" };
            Assert.False(WorkOrderRules.IsChoice(o));
            o.resultReject = "b";
            Assert.True(WorkOrderRules.IsChoice(o));
            Assert.False(WorkOrderRules.IsChoice(null));
            var db = Night(3);
            foreach (var id in new[] { "wo_3340", "wo_3341", "wo_3342", "wo_3319", "wo_3321", "wo_3330", "wo_3331" })
                Assert.False(WorkOrderRules.IsChoice(db.Order(id)), id);
            var box = db.Order("wo_3332");
            Assert.Equal(box.resultApprove, WorkOrderRules.ResultFor(box, "approve"));
            Assert.Equal(box.noteReject, WorkOrderRules.NoteFor(box, "reject"));
            // An order nobody decided has no result and no note.
            Assert.Equal("", WorkOrderRules.NoteFor(box, null));
            Assert.Equal("", WorkOrderRules.ResultFor(box, null));
        }

        [Fact]
        public void Night3sPersonnelReadsWhatNight2sChoicesDid()
        {
            if (!Present) return;
            var db = Night(3);
            string Filled(string id, params string[] flags)
            {
                var f = new NarrativeFlags();
                foreach (var flag in flags) f.Set(flag);
                return NightTemplates.Fill(db.Employee(id).notes, NightTemplates.Tokens(f, null));
            }
            Assert.Contains("released to family at Reception 11/19/98 (WO-3320)", Filled("163", MemoryFlags.N2Box163Held));
            Assert.Contains("disposed by Custodial 11/19/98 (WO-3320)", Filled("163", MemoryFlags.N2Box163Released));
            Assert.Contains("disposed by Custodial 11/19/98.", Filled("163"));
            Assert.Contains("Device 2 would not detach. Patch withdrawn.", Filled("127", MemoryFlags.N2TookHand));
            Assert.Contains("rejected by the WS-04 operator", Filled("127", MemoryFlags.N2LeftHand));
            Assert.Contains("WS-04: scheduled 11/21/98. Do not call about WS-04.", Filled("127"));
            foreach (var e in db.Employees.employees)
                Assert.DoesNotContain("{", NightTemplates.Fill(e.notes, NightTemplates.Tokens(new NarrativeFlags(), null)));
        }

        [Fact]
        public void NightThreeStaysConsistentWithTheNewOrders()
        {
            if (!Present) return;
            var db = Night(3);
            Assert.Contains("three work orders", db.Email("mail_n3_briefing").body);
            Assert.Contains("Approve it.", db.Email("mail_n3_ruth_drive").body);
            Assert.Equal("209", db.Order("wo_3332").employeeRef);
            Assert.Equal("118", db.Order("wo_3333").employeeRef);
            // Ruth's order comes after Batch 48 in the queue, the box after work order 3331.
            var ids = db.Tasks.tasks.Select(t => t.id).ToList();
            Assert.True(ids.IndexOf("t3_verify_3332") == ids.IndexOf("t3_verify_3331") + 1);
            Assert.True(ids.IndexOf("t3_verify_3333") == ids.IndexOf("t3_archive_batch48") + 1);
            Assert.True(ids.IndexOf("t2_verify_3320") == ids.IndexOf("t2_verify_3319") + 1);
            Assert.True(ids.IndexOf("t2_verify_3322") == ids.IndexOf("t2_verify_3321") + 1);
            // Gary and Ruth answer in their own voices (the content tests check the words).
            Assert.Equal("gary", db.LineSet("g3_box_view").voice);
            Assert.Equal("", db.LineSet("n3_ruth_wiped").voice);
            Assert.Equal("system", db.LineSet("n3_end_logoff_box").voice);
        }
    }
}
