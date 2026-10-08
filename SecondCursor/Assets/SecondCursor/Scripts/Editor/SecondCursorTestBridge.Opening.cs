using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Game;
using SecondCursor.OS;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        static IEnumerator RunOpeningChecks()
        {
            // EditorApplication does not execute nested coroutine yields like Unity's coroutine runner.
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            stack.Push(OpeningCheck());
            while (stack.Count > 0)
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); stack.Pop(); }
                else if (current.Current is IEnumerator nested) stack.Push(nested);
                else yield return current.Current;
            }
        }

        static IEnumerator OpeningCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("openingcheck requires an isolated savedir first");
            foreach (string decision in new[] { "approve", "reject" })
            {
                var previous = G;
                GameBootstrap.Restart(1, "work");
                yield return WaitFor(() => G != null && G != previous && G.Director != null && G.Director.CurrentBeat == "work", 30f, "new opening");
                var g = G;
                yield return WaitFor(() => FindOpeningDialog(g, "NEXUS OS Quick Start") != null, 15f, "quick start");
                FindOpeningDialog(g, "NEXUS OS Quick Start").Close(g.Player);
                yield return WaitFor(() => g.Tasks.IsActive(ContentIds.TaskReadBriefing), 10f, "briefing task");
                g.Mail.MarkRead(ContentIds.MailWelcome, g.Player);
                g.Orders.Decide(ContentIds.Order3318, decision, g.Player);
                yield return WaitFor(() => g.Tasks.IsActive(ContentIds.TaskArchiveLedger), 10f, "archive task");
                int mistakes = g.Flags.Get(Flags.CounterWrongOrders);
                string consequence = "mail_wo_3318_" + decision;
                string date = g.Mail.DateOf(consequence);
                // Read one message, leave the consequence unread, and make no decision for Paul yet.
                g.Director.SaveCurrentProgress();
                SaveSystem.Flush();
                GameBootstrap.RestartFromCheckpoint(1, false, false);
                yield return WaitFor(() => G != null && G != g && G.Director != null && G.Director.CurrentBeat == "work", 30f, "resume early work");
                g = G;
                yield return WaitFor(() => g.Tasks.IsActive(ContentIds.TaskArchiveLedger), 10f, "resume next unfinished job");
                ExperienceAssert(FindOpeningDialog(g, "NEXUS OS Quick Start") == null, "Continue skips dismissed Quick Start");
                ExperienceAssert(g.Tasks.IsCompleted(ContentIds.TaskReadBriefing), "Continue preserves completed briefing");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3317) == null, "Continue does not invent a pending decision");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3318) == decision, "Continue preserves early decision: " + decision);
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == mistakes, "early Continue does not double-count mistakes");
                ExperienceAssert(!g.Mail.IsRead(consequence) && g.Mail.IsLive(consequence) && g.Mail.DateOf(consequence) == date,
                    "unread consequence link and received date survive");
                ExperienceAssert(g.Mail.IsRead(ContentIds.MailWelcome), "read briefing stays read");
                ExperienceAssert(g.Files.FolderOf(ContentIds.FileLedger) == ContentIds.FolderIntake, "unfinished ledger stays in Intake");

                g.Tasks.MarkFinishedBy(ContentIds.TaskArchiveLedger, TaskFinisher.NightOperations);
                g.Files.Move(ContentIds.FileLedger, ContentIds.FolderArchive, Actor.System, TaskFinisher.NightOperations);
                g.Orders.Decide(ContentIds.Order3317, "approve", g.Player);
                g.Files.Shred(ContentIds.FileCache, Actor.Player);
                g.Tasks.Evaluate();
                yield return WaitFor(() => g.Director.CurrentBeat == "anomaly", 25f, "ordinary work complete");
                yield return WaitFor(() => g.Tasks.IsActive(ContentIds.TaskArchiveBatch), 15f, "batch task");
                g.Files.Move(ContentIds.FileBatchA, ContentIds.FolderArchive, Actor.Player);
                g.Files.Move(ContentIds.FileBatchB, ContentIds.FolderArchive, Actor.System, TaskFinisher.NightOperations);
                g.Tasks.Evaluate();
                g.Mail.MarkRead(consequence, g.Player);
                g.Director.SaveCurrentProgress();
                SaveSystem.Flush();
                ExperienceAssert(SaveSystem.Load().CheckpointFor(1)?.opening != null, "ordinary work is written in the checkpoint");
                GameBootstrap.RestartFromCheckpoint(1, false, false);
                yield return WaitFor(() => G != null && G != g && G.Director != null && G.Director.CurrentBeat == "anomaly", 30f, "resume batch");
                g = G;
                ExperienceAssert(g.Tasks.IsCompleted(ContentIds.TaskShredCache) && g.Files.GetFile(ContentIds.FileCache).Shredded,
                    "completed shred and removed file survive");
                ExperienceAssert(g.Tasks.IsCompleted(ContentIds.TaskArchiveLedger) && g.Files.FolderOf(ContentIds.FileLedger) == ContentIds.FolderArchive,
                    "completed archive and file location survive");
                ExperienceAssert(g.Tasks.Get(ContentIds.TaskArchiveLedger).FinishedBy == TaskFinisher.NightOperations,
                    "completed assistance remains attributed");
                ExperienceAssert(g.Tasks.Get(ContentIds.TaskArchiveBatch).Progress == 2 && g.Files.FolderOf(ContentIds.FileBatchC) == ContentIds.FolderIntake,
                    "partial batch resumes at two of three without finishing the last file");
                ExperienceAssert(g.Files.GetFile(ContentIds.FileBatchB).MovedBy == TaskFinisher.NightOperations,
                    "partial filing assistance remains attributed");
                ExperienceAssert(g.Mail.IsRead(consequence) && g.Mail.DateOf(consequence) == date, "read consequence and date survive a second Continue");
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == mistakes, "repeated Continue preserves mistake count");

                var orders = g.Apps.Launch(AppIds.WorkOrders, g.Player) as WorkOrdersApp;
                orders.ShowOrder(ContentIds.Order3318, g.Player);
                ExperienceAssert(orders.Window.Element("workorder.owner") != null, "work order has an owner-record action");
                var staff = g.Apps.Launch(AppIds.Staff, g.Player) as StaffApp;
                staff.ShowForOrder(ContentIds.Employee163, ContentIds.Order3318, g.Player);
                ExperienceAssert(staff.Window.Element("staff.backtoorder") != null, "owner record has a return action");
                var help = g.Apps.Launch(AppIds.Help, g.Player);
                ExperienceAssert(help.Window.Element("help.mode") != null, "contextual Help retains the full manual");
                yield return WaitFor(() => g.Director.CanRequestTaskHelp(ContentIds.TaskArchiveBatch), 100f, "batch assistance available");
                g.Shred.Request(ContentIds.File017, g.Player);
                ExperienceAssert(!g.Director.CanRequestTaskHelp(ContentIds.TaskArchiveBatch), "another dialog blocks assistance");
                foreach (var w in new System.Collections.Generic.List<OSWindow>(g.Windows.Windows))
                    if (w.AlwaysOnTop) w.Close(g.Player, true);
                g.Director.RequestTaskHelp(ContentIds.TaskArchiveBatch);
                ExperienceAssert(FindOpeningDialog(g, g.Content.Text("assist.offer.title")) != null, "assistance opens only on request");
            }
            Say("PASS opening integration checks complete");
            GameBootstrap.ToTitle();
        }

        static OSWindow FindOpeningDialog(GameServices g, string title)
        {
            foreach (var w in g.Windows.Windows)
                if (!w.IsClosed && w.Title == title) return w;
            return null;
        }
    }
}
