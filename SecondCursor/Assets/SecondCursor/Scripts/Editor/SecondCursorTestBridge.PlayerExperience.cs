using System;
using System.Collections;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        static void ExperienceAssert(bool pass, string message)
        {
            if (!pass) throw new InvalidOperationException("Player experience regression: " + message);
            Say("PASS " + message);
        }

        // Run only against an explicitly isolated test save. This never runs in a player build.
        static IEnumerator PlayerExperienceCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("experiencecheck requires savedir PATH first");
            foreach (string decision in new[] { "approve", "reject" })
            {
                var previous = G;
                GameBootstrap.Restart(1, "work");
                yield return WaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "work", 30f, "fresh test shift");
                var g = G;
                g.Tasks.Activate(ContentIds.TaskArchiveLedger);
                g.Files.Move(ContentIds.FileLedger, ContentIds.FolderArchive, Actor.Player);
                g.Tasks.Evaluate();
                ExperienceAssert(g.Mail.Has("mail_ledger_receipt"), "archiving reveals the discrepancy receipt");
                g.Orders.Decide(ContentIds.Order3317, decision, g.Player);
                g.Orders.Decide(ContentIds.Order3318, decision, g.Player);
                ExperienceAssert(g.Mail.Has("mail_wo_3318_" + decision), "Joan receives the correct consequence: " + decision);
                ExperienceAssert(!g.Mail.Has("mail_wo_3318_" + (decision == "approve" ? "reject" : "approve")), "opposite consequence stays hidden");
                int mistakes = g.Flags.Get(Flags.CounterWrongOrders);
                string deliveredAt = g.Content.Email("mail_wo_3318_" + decision).date;
                SaveSystem.SaveCheckpoint(g, "reveal");
                var saved = SaveSystem.Load();
                saved.checkpoint.CaptureReplies(new[] { "ruth", "no" }, new[] { 119, 120 });
                SaveSystem.Save(saved);
                SaveSystem.Flush();
                GameBootstrap.RestartFromCheckpoint(1, false, false);
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "reveal", 30f, "resume before camera");
                g = G;
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3317) == decision && g.Orders.DecisionFor(ContentIds.Order3318) == decision,
                    "retry preserves actual order decisions: " + decision);
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == mistakes, "retry does not duplicate mistakes");
                ExperienceAssert(g.Content.Email("mail_wo_3318_" + decision).date == deliveredAt, "retry preserves the mail delivery time");
                ExperienceAssert(g.Content.Employee(ContentIds.Employee163).notes.Contains(g.Content.Text(decision == "approve" ? "n1.163.approve" : "n1.163.reject")),
                    "Joan's personnel consequence survives retry");
                var after = SaveSystem.Load().CheckpointFor(1);
                ExperienceAssert(after != null && after.beat == "reveal", "reveal automatically saves a checkpoint");
                ExperienceAssert(after.playerLines.Length == 2 && after.playerLines[1] == "no" && after.playerLineMinutes[1] == 120,
                    "reply text and timestamps survive a rebuilt game");
                ExperienceAssert(!g.RecordsArmed, "debug retry stays excluded from records");
                g.Flags.Set("m.n1.camera_declined");
                ExperienceAssert(!g.Apps.CanLaunch(AppIds.Camera, g.Player), "an offline choice keeps live video disconnected");
            }
            Say("PASS player experience integration checks complete");
            GameBootstrap.ToTitle();
        }
    }
}
