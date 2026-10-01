using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
using SecondCursor.Core.Tasks;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase Q1 (the outside tester's feedback and the board's T5, T8, A10): progress that can be trusted (a ticked task always agrees with
    /// the world, and work done by someone else is named), help before takeover (hint, repeat hint, an offer the player answers; reading is
    /// not being stuck), quiet action scares under their own budget, the shelf check cut to your own shelf, and the reading and CRT options.
    /// </summary>
    public class PhaseQ1Tests
    {
        const float Dt = 0.5f;

        // ------------------------------------------------------------------ a world every task type can be finished in

        sealed class World : ITaskWorld
        {
            public readonly VirtualFileSystem Files;
            public readonly HashSet<string> Read = new HashSet<string>();
            public readonly Dictionary<string, string> Decisions = new Dictionary<string, string>();
            public readonly HashSet<string> Opened = new HashSet<string>();
            public readonly HashSet<string> Viewed = new HashSet<string>();
            public readonly TaskCredits Credits = new TaskCredits();

            public World(VirtualFileSystem files) { Files = files; }
            public bool IsEmailRead(string emailId) => Read.Contains(emailId);
            public string FolderOf(string fileId) => Files.FolderOf(fileId);
            public bool IsShredded(string fileId) => Files.GetFile(fileId)?.Shredded ?? false;
            public string DecisionFor(string orderId) => Decisions.TryGetValue(orderId, out var d) ? d : null;
            public bool IsFileOpenedByPlayer(string fileId) => Opened.Contains(fileId);
            public bool IsEmployeeViewedByPlayer(string employeeId) => Viewed.Contains(employeeId);
            public string CreditFor(TaskType type, string targetId) => type == TaskType.MoveFile ? Files.GetFile(targetId)?.MovedBy : Credits.Get(type, targetId);

            /// <summary>What the runtime's FinishByNightOperations does with each step (the same calls, on this world).</summary>
            public void Apply(FinishStep s, string by)
            {
                switch (s.Kind)
                {
                    case FinishKind.MoveFile:
                        if (Files.GetFile(s.Target)?.Shredded == true) Files.Restore(s.Target, "intake", Actor.System);
                        Files.Move(s.Target, s.Param, Actor.System, by);
                        break;
                    case FinishKind.ShredFile: if (Files.Shred(s.Target, Actor.System)) Credits.Set(TaskType.DeleteFile, s.Target, by); break;
                    case FinishKind.DecideOrder: Decisions[s.Target] = s.Param; Credits.Set(TaskType.DecideOrder, s.Target, by); break;
                    case FinishKind.ReadMail: Read.Add(s.Target); Credits.Set(TaskType.ReadEmail, s.Target, by); break;
                    case FinishKind.OpenFile: Opened.Add(s.Target); Credits.Set(TaskType.OpenFile, s.Target, by); break;
                    case FinishKind.ViewEmployee: Viewed.Add(s.Target); Credits.Set(TaskType.ViewEmployee, s.Target, by); break;
                }
            }
        }

        static VirtualFileSystem Files() => new VirtualFileSystem(new FileSystemData
        {
            folders = new[] { new FolderData { id = "intake", name = "Intake" }, new FolderData { id = "archive", name = "Archive" }, new FolderData { id = "desktop", name = "Desktop" } },
            files = new[] { "a", "b", "c" }.Select(x => new FileData { id = "batch44_" + x, name = "batch44_" + x + ".dat", type = "dat", folder = "intake" })
                .Concat(new[] { new FileData { id = "cache", name = "~nxs0042.tmp", type = "tmp", folder = "desktop" } }).ToArray(),
        });

        static TaskData Task(string id, string type, string param, params string[] targets) =>
            new TaskData { id = id, title = id, type = type, param = param, targets = targets };

        static readonly TaskData[] AllTypes =
        {
            Task("t_briefing", "ReadEmail", "", "mail_welcome"),
            Task("t_archive", "MoveFile", "archive", "batch44_a", "batch44_b", "batch44_c"),
            Task("t_orders", "DecideOrder", "", "wo_3317", "wo_3318"),
            Task("t_cache", "DeleteFile", "", "cache"),
            Task("t_open", "OpenFile", "", "notes"),
            Task("t_view", "ViewEmployee", "", "214"),
        };

        static readonly Dictionary<string, string> Rule = new Dictionary<string, string> { { "wo_3317", "reject" }, { "wo_3318", "approve" } };

        static (World world, WorkTaskManager tasks) Shift()
        {
            var w = new World(Files());
            var tasks = new WorkTaskManager(AllTypes, w);
            foreach (var t in AllTypes) tasks.Activate(t.id);
            return (w, tasks);
        }

        static void Finish(World w, WorkTaskManager tasks, string id)
        {
            var t = tasks.Get(id);
            tasks.MarkFinishedBy(id, TaskFinisher.NightOperations);
            foreach (var step in TaskFinisher.Plan(tasks, t, o => Rule.TryGetValue(o, out var d) ? d : null)) w.Apply(step, TaskFinisher.NightOperations);
            tasks.Evaluate();
        }

        // ------------------------------------------------------------------ owner 1: progress that can be trusted

        [Fact]
        public void EveryTaskTypeFinishedByTheOfferAgreesWithTheWorld()
        {
            var (w, tasks) = Shift();
            foreach (var t in AllTypes) Finish(w, tasks, t.id);
            foreach (var t in tasks.Tasks)
            {
                Assert.True(t.IsDone, t.Id + " ticked");
                Assert.True(tasks.Agrees(t), t.Id + " agrees with the world");
                Assert.Equal("finished by Night Operations", t.CreditNote);
            }
            Assert.Empty(tasks.Disagreements());
            Assert.Equal("archive", w.FolderOf("batch44_b"));
            Assert.True(w.IsShredded("cache"));
            Assert.Equal("Night Operations", w.Files.GetFile("batch44_a").MovedBy);
        }

        [Fact]
        public void AnOrderTaskIsFinishedByDecidingItsOrdersByTheirOwnRule()
        {
            // The 3317 report: the queue ticked the task while Work Orders still said Pending. Finishing decides the orders themselves.
            var (w, tasks) = Shift();
            w.Decisions["wo_3317"] = "reject";
            tasks.Evaluate();
            var steps = TaskFinisher.Plan(tasks, tasks.Get("t_orders"), o => Rule[o]);
            Assert.Single(steps);
            Assert.Equal(FinishKind.DecideOrder, steps[0].Kind);
            Assert.Equal("wo_3318", steps[0].Target);
            Assert.Equal("approve", steps[0].Param);
            Finish(w, tasks, "t_orders");
            Assert.True(tasks.IsCompleted("t_orders"));
            Assert.Equal("approve", w.DecisionFor("wo_3318"));
            Assert.Equal("2/2, 1 by Night Operations", tasks.Get("t_orders").ProgressText);
            Assert.Equal("1 of 2 by Night Operations", tasks.Get("t_orders").CreditNote);
        }

        [Fact]
        public void ATaskTickedWithoutTheWorldIsReportedAsADisagreement()
        {
            var (w, tasks) = Shift();
            tasks.ForceComplete("t_orders");
            Assert.False(tasks.Agrees(tasks.Get("t_orders")));
            Assert.Equal(new[] { "t_orders" }, tasks.Disagreements().Select(t => t.Id));
            w.Decisions["wo_3317"] = "reject";
            w.Decisions["wo_3318"] = "approve";
            Assert.Empty(tasks.Disagreements());
        }

        [Fact]
        public void WorkByAnotherSessionIsNamedForEveryTaskType()
        {
            var (w, tasks) = Shift();
            w.Read.Add("mail_welcome");
            w.Credits.Set(TaskType.ReadEmail, "mail_welcome", "session 017");
            w.Decisions["wo_3317"] = "reject";
            w.Credits.Set(TaskType.DecideOrder, "wo_3317", "session 017");
            w.Files.Shred("cache", Actor.Entity);
            w.Credits.Set(TaskType.DeleteFile, "cache", "session 209");
            w.Files.Move("batch44_a", "archive", Actor.Entity, "session 017");
            w.Files.Move("batch44_b", "archive", Actor.Player);
            tasks.Evaluate();
            Assert.Equal("finished by session 017", tasks.Get("t_briefing").CreditNote);
            Assert.Equal("1/2, 1 by session 017", tasks.Get("t_orders").ProgressText);
            Assert.Equal("finished by session 209", tasks.Get("t_cache").CreditNote);
            Assert.Equal("2/3, 1 by session 017", tasks.Get("t_archive").ProgressText);
            // The player taking a file back out and in again takes the credit back.
            w.Files.Move("batch44_a", "intake", Actor.Player);
            w.Files.Move("batch44_a", "archive", Actor.Player);
            tasks.Evaluate();
            Assert.Equal("2/3", tasks.Get("t_archive").ProgressText);
            Assert.Null(tasks.Get("t_archive").CreditNote);
        }

        [Fact]
        public void TheShiftsOwnSetupIsNeverCreditedButANamedSystemMoveIs()
        {
            var fs = Files();
            fs.Move("batch44_a", "archive", Actor.System);
            fs.Move("batch44_b", "archive", Actor.System, "Night Operations");
            Assert.Null(fs.GetFile("batch44_a").MovedBy);
            Assert.Equal("Night Operations", fs.GetFile("batch44_b").MovedBy);
        }

        [Fact]
        public void CreditNotesReadPlainly()
        {
            var none = new List<KeyValuePair<string, int>>();
            Assert.Null(TaskProgress.CreditNote(1, none, null));
            Assert.Equal("finished by Night Operations", TaskProgress.CreditNote(1, none, "Night Operations"));
            var mixed = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("session 017", 1), new KeyValuePair<string, int>("Night Operations", 2) };
            Assert.Equal("1 of 4 by session 017, 2 of 4 by Night Operations", TaskProgress.CreditNote(4, mixed, "Night Operations"));
            var all = new List<KeyValuePair<string, int>> { new KeyValuePair<string, int>("session 017", 3) };
            Assert.Equal("finished by session 017", TaskProgress.CreditNote(3, all, null));
        }

        [Fact]
        public void NothingIsOfferedForRemoteRequestsOrStoryLines()
        {
            var remote = new WorkTask(new TaskData { id = "e2_door_log", type = "OpenFile", author = "entity", targets = new[] { "door_log" } });
            var wait = new WorkTask(new TaskData { id = "t3_wait_rounds", type = "Wait", targets = Array.Empty<string>() });
            var company = new WorkTask(Task("t_archive", "MoveFile", "archive", "batch44_a"));
            Assert.False(TaskFinisher.CanOffer(remote));
            Assert.False(TaskFinisher.CanOffer(wait));
            Assert.True(TaskFinisher.CanOffer(company));
        }

        // ------------------------------------------------------------------ owner 2: assistance before takeover

        static List<(float at, AssistStep step)> Run(TaskAssist a, float seconds, Func<float, bool> reading = null, Func<float, int> progress = null, Func<float, bool> canOffer = null)
        {
            var steps = new List<(float, AssistStep)>();
            for (float t = Dt; t <= seconds + 1e-3f; t += Dt)
            {
                var s = a.Tick(Dt, reading != null && reading(t), canOffer == null || canOffer(t), progress != null ? progress(t) : 0);
                if (s != AssistStep.None) steps.Add((t, s));
            }
            return steps;
        }

        [Fact]
        public void TheLadderIsHintRepeatHintThenAnOffer()
        {
            var a = new TaskAssist(30f, 40f, 150f, true);
            var steps = Run(a, 200f);
            Assert.Equal(AssistStep.Hint, steps[0].step);
            Assert.Equal(30f, steps[0].at, 1);
            Assert.Equal(AssistStep.RepeatHint, steps[1].step);
            Assert.Equal(70f, steps[1].at, 1);
            Assert.Equal(AssistStep.Offer, steps[2].step);
            Assert.Equal(110f, steps[2].at, 1);
            // While the offer waits for an answer nothing else happens, and nothing is ever finished by the ladder itself.
            Assert.Equal(3, steps.Count);
            Assert.True(a.OfferOpen);
            Assert.False(a.Accepted);
        }

        [Fact]
        public void ReadingDoesNotCountAsBeingStuck()
        {
            var a = new TaskAssist(30f, 40f, 150f, true);
            // Reading from 10 s to 300 s: the first hint only comes 20 s of active time later.
            var steps = Run(a, 330f, reading: t => t > 10f && t <= 300f);
            Assert.Equal(AssistStep.Hint, steps[0].step);
            Assert.Equal(320f, steps[0].at, 1);
            Assert.Equal(40f, a.ActiveSeconds, 1);
        }

        [Fact]
        public void ProgressStartsTheCountAgain()
        {
            var a = new TaskAssist(30f, 40f, 150f, true);
            var steps = Run(a, 160f, progress: t => t >= 100f ? 1 : 0);
            Assert.Equal(new[] { AssistStep.Hint, AssistStep.RepeatHint }, steps.Where(s => s.at < 100f).Select(s => s.step));
            // Progress at 100 s: the offer (due at 110 s) waits for a fresh hint, repeat and offer.
            Assert.DoesNotContain(steps, s => s.step == AssistStep.Offer);
            Assert.Contains(steps, s => s.step == AssistStep.RepeatHint && Math.Abs(s.at - 130f) < 0.6f);
        }

        [Fact]
        public void NotNowBringsTheOfferBackLaterWithHintsBetween()
        {
            var a = new TaskAssist(30f, 40f, 150f, true);
            Run(a, 110f);
            Assert.True(a.OfferOpen);
            a.Decline();
            Assert.False(a.OfferOpen);
            Assert.Equal(1, a.Declines);
            var after = Run(a, 160f);
            Assert.Equal(AssistStep.RepeatHint, after[0].step);
            Assert.Equal(40f, after[0].at, 1);
            var offer = after.First(s => s.step == AssistStep.Offer);
            Assert.Equal(150f, offer.at, 1);
            a.Accept();
            Assert.True(a.Accepted);
            Assert.Empty(Run(a, 400f));
        }

        [Fact]
        public void AChoiceOrderOnlyGetsHintsAndItsLapseClockIsActiveTime()
        {
            var a = new TaskAssist(30f, 40f, 150f, false);
            var steps = Run(a, 400f, reading: t => t <= 100f);
            Assert.DoesNotContain(steps, s => s.step == AssistStep.Offer);
            Assert.Equal(AssistStep.Hint, steps[0].step);
            Assert.True(steps.Count(s => s.step == AssistStep.RepeatHint) >= 5);
            Assert.Equal(300f, a.ActiveSeconds, 1);
        }

        [Fact]
        public void TheOfferWaitsForAMomentADialogCanComeUp()
        {
            var a = new TaskAssist(30f, 40f, 150f, true);
            // A tug or a shred until 140 s: no offer before then, and it comes the moment the way is clear.
            var steps = Run(a, 150f, canOffer: t => t > 140f);
            var offer = steps.Single(s => s.step == AssistStep.Offer);
            Assert.True(offer.at > 140f && offer.at < 141f);
            Assert.Contains(steps, s => s.step == AssistStep.RepeatHint && s.at > 100f && s.at < 140f);
        }

        [Fact]
        public void AFinishTheWorldRefusedIsOfferedAgainLater()
        {
            var a = new TaskAssist(15f, 25f, 90f, true);
            Run(a, 65f);
            a.Accept();
            Assert.True(a.Accepted);
            a.AcceptFailed();
            Assert.False(a.Accepted);
            Assert.Contains(Run(a, 91f), s => s.step == AssistStep.Offer);
        }

        [Fact]
        public void AWithdrawnOfferComesBackLikeNotNow()
        {
            var a = new TaskAssist(15f, 25f, 90f, true);
            Run(a, 65f);
            Assert.True(a.OfferOpen);
            a.Withdraw();
            Assert.False(a.OfferOpen);
            Assert.Equal(0, a.Declines);
            Assert.Contains(Run(a, 91f), s => s.step == AssistStep.Offer);
        }

        [Fact]
        public void ReadingIsAReadingWindowOrARecentScroll()
        {
            foreach (var app in new[] { AppIds.Mail, AppIds.DataViewer, AppIds.Notepad, AppIds.Staff, AppIds.Help, AppIds.Camera })
                Assert.True(ReadingRule.IsReading(app, 1000f, 5f), app);
            foreach (var app in new[] { AppIds.Files, AppIds.WorkOrders, AppIds.WorkQueue, AppIds.Disposal, "dialog", null })
                Assert.False(ReadingRule.IsReading(app, 1000f, 5f), app ?? "no window");
            Assert.True(ReadingRule.IsReading(AppIds.Files, 2f, 5f));
            Assert.False(ReadingRule.IsReading(AppIds.Files, ReadingRule.ScrollGrace + 0.1f, 5f));
        }

        [Fact]
        public void AReadingWindowLeftAloneIsNotReadingForever()
        {
            // Nothing done for longer than the idle limit with Mail in front: more likely lost than reading, so the hints come.
            Assert.True(ReadingRule.IsReading(AppIds.Mail, 1000f, ReadingRule.IdleLimit - 1f));
            Assert.False(ReadingRule.IsReading(AppIds.Mail, 1000f, ReadingRule.IdleLimit + 1f));
            // A scroll is input and reading at once.
            Assert.True(ReadingRule.IsReading(AppIds.Mail, 1f, ReadingRule.IdleLimit + 1f));
        }

        // ------------------------------------------------------------------ owner 4: action scares

        static ScareContext Quiet() => new ScareContext { NightSeconds = 600f, BeatSeconds = 60f, SinceScare = 100f, SinceStinger = 100f, SinceEvent = 100f };

        [Fact]
        public void ActionScaresHaveTheirOwnBudgetAndKeepAwayFromSounds()
        {
            var n = ScareRules.For(1);
            var c = Quiet();
            c.Played = n.Budget; // the sound budget is spent: an action scare does not use it
            Assert.Equal(ScareGate.None, ScareRules.CheckAction(c, n, 0, 1000f));
            Assert.Equal(ScareGate.Budget, ScareRules.CheckAction(c, n, n.ActionBudget, 1000f));
            c.SinceScare = 5f;
            Assert.Equal(ScareGate.Cooldown, ScareRules.CheckAction(c, n, 0, 1000f));
            c.SinceScare = 100f;
            Assert.Equal(ScareGate.Cooldown, ScareRules.CheckAction(c, n, 1, 3f));
        }

        [Fact]
        public void ActionScaresRespectEveryNoScareGate()
        {
            var n = ScareRules.For(2);
            var c = Quiet();
            c.Tug = true;
            Assert.Equal(ScareGate.Tug, ScareRules.CheckAction(c, n, 0, 1000f));
            c = Quiet();
            c.Dragging = true;
            Assert.Equal(ScareGate.Drag, ScareRules.CheckAction(c, n, 0, 1000f));
            c = Quiet();
            c.TutorialOpen = true;
            Assert.Equal(ScareGate.Tutorial, ScareRules.CheckAction(c, n, 0, 1000f));
            c = Quiet();
            c.Climax = true;
            Assert.Equal(ScareGate.Climax, ScareRules.CheckAction(c, n, 0, 1000f));
            foreach (int night in new[] { 1, 2, 3 }) Assert.InRange(ScareRules.For(night).ActionBudget, 1, 3);
        }

        // ------------------------------------------------------------------ owner 7, F A10: reading text and CRT

        [Fact]
        public void OlderSettingsFilesReadAsTheirOldSwitches()
        {
            Assert.Equal(ReadingSize.Large, DisplayOptions.SizeFrom(-1, true));
            Assert.Equal(ReadingSize.Normal, DisplayOptions.SizeFrom(-1, false));
            Assert.Equal(ReadingSize.Medium, DisplayOptions.SizeFrom(1, true));
            Assert.Equal(ReadingSize.Normal, DisplayOptions.SizeFrom(7, false));
            Assert.Equal(CrtLevel.Full, DisplayOptions.CrtFrom(-1, true));
            Assert.Equal(CrtLevel.Off, DisplayOptions.CrtFrom(-1, false));
            Assert.Equal(CrtLevel.Low, DisplayOptions.CrtFrom(1, false));
        }

        [Fact]
        public void TheOptionsCycleAndScale()
        {
            Assert.Equal(ReadingSize.Medium, DisplayOptions.Next(ReadingSize.Normal));
            Assert.Equal(ReadingSize.Large, DisplayOptions.Next(ReadingSize.Medium));
            Assert.Equal(ReadingSize.Normal, DisplayOptions.Next(ReadingSize.Large));
            Assert.Equal(CrtLevel.Low, DisplayOptions.Next(CrtLevel.Off));
            Assert.Equal(CrtLevel.Off, DisplayOptions.Next(CrtLevel.Full));
            Assert.Equal(1.5f, DisplayOptions.Factor(ReadingSize.Medium));
            Assert.Equal(2f, DisplayOptions.Factor(ReadingSize.Large));
            Assert.Equal(0f, DisplayOptions.Strength(CrtLevel.Off));
            Assert.InRange(DisplayOptions.Strength(CrtLevel.Low), 0.3f, 0.6f);
            Assert.Equal(1f, DisplayOptions.Strength(CrtLevel.Full));
        }

        [Fact]
        public void MediumTextGetsASquarePixelScreenTexture()
        {
            // 1080p (2x, integer): the texture goes to 2x for Medium only, so a 1.5x glyph is exactly 3 texture pixels per font pixel.
            Assert.Equal(1, DisplayOptions.TextureScale(2f, true, ReadingSize.Normal));
            Assert.Equal(2, DisplayOptions.TextureScale(2f, true, ReadingSize.Medium));
            Assert.Equal(1, DisplayOptions.TextureScale(2f, true, ReadingSize.Large));
            Assert.Equal(2, DisplayOptions.TextureScale(4f, true, ReadingSize.Medium));
            // Non-integer displays (the Deck, 1.33x) were 2x already; a 1x display cannot show Medium crisply and keeps 1x.
            Assert.Equal(2, DisplayOptions.TextureScale(1.333f, false, ReadingSize.Medium));
            Assert.Equal(1, DisplayOptions.TextureScale(1f, true, ReadingSize.Medium));
        }

        // ------------------------------------------------------------------ content

        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));
        static string Text(string rel) => File.ReadAllText(Path.Combine(Dir, rel));

        static Dictionary<string, string> Strings(string rel)
        {
            using var doc = JsonDocument.Parse(Text(rel));
            return doc.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("key").GetString(), e => e.GetProperty("value").GetString());
        }

        [Fact]
        public void TheOfferAndTheCreditsAreInTheDemo()
        {
            if (!Present) return;
            var s = Strings("strings.json");
            foreach (var key in new[] { "assist.offer.title", "assist.offer.body", "assist.offer.yes", "assist.offer.no", "assist.done", "assist.next.move",
                                        "assist.next.order", "assist.next.shred", "assist.next.mail", "workorder.decidedby", "workorder.status.nightops",
                                        "pause.textsize.medium", "pause.crt", "pause.crt.low", "task.filed.rest" })
                Assert.True(s.ContainsKey(key), key);
            Assert.Contains("{0}", s["assist.offer.body"]);
            Assert.Contains("Night Operations offers", s["help.body"]);
            Assert.Contains("Night Operations offers", s["help.body.deck"]);
            Assert.EndsWith("2", s["shutdown.denied.body"]);
        }

        [Fact]
        public void NightTwoCountsThreeSessionsAndMailsTheFirstReplyBack()
        {
            if (!Present) return;
            Assert.EndsWith("3", Strings("night2/strings.json")["shutdown.denied.body"]);
            Assert.Contains("\"mail_n2_self\"", Text("night2/emails.json"));
            Assert.Contains("{line1}", Text("night2/emails.json"));
            Assert.DoesNotContain("mail_n2_self", Text("emails.json"));
        }

        [Fact]
        public void TheShelfCheckIsYourOwnShelfAndTheDamagedFileSaysWhoWroteIt()
        {
            if (!Present) return;
            using var doc = JsonDocument.Parse(Text("night3/tasks.json"));
            var shelf = doc.RootElement.GetProperty("tasks").EnumerateArray().Single(t => t.GetProperty("id").GetString() == ContentIds.TaskN3Shelf);
            Assert.Equal(new[] { ContentIds.Order3342 }, shelf.GetProperty("targets").EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("(1 order)", shelf.GetProperty("title").GetString());
            Assert.Contains("WRITTEN BY 000", Text("night3/dialogue.json"));
        }
    }
}
