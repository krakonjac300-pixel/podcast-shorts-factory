using System;
using System.Collections;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.OS;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        // Runs against a disposable save only. Exercises the same rebuild paths as Continue and the night cards.
        static IEnumerator ContinuityCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride) || SaveSystem.ProgressReadOnly)
                throw new InvalidOperationException("continuitycheck requires savedir PATH and writable test progress");
            foreach (string decision in new[] { "reject", "approve" })
            {
                string opposite = decision == "approve" ? "reject" : "approve";
                var previous = G;
                GameBootstrap.Restart(1, "work");
                yield return WaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "work", 30f, "continuity night 1");
                var g = G;
                g.Orders.Decide(ContentIds.Order3317, decision, g.Player);
                g.Orders.Decide(ContentIds.Order3318, opposite, g.Player);
                int mistakes = g.Flags.Get(Flags.CounterWrongOrders);
                SaveSystem.SaveCheckpoint(g, "reveal");
                SaveSystem.Flush();
                GameBootstrap.RestartFromCheckpoint(1, false, false);
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "reveal", 30f, "continuity night 1 reload");
                g = G;
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3317) == decision, "night 1 reload preserves Paul: " + decision);
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3318) == opposite, "night 1 reload preserves Joan: " + opposite);
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == mistakes, "night 1 reload does not recount mistakes");
                ExperienceAssert(g.Mail.Has("mail_wo_3318_" + opposite), "night 1 reload restores matching consequence mail");
                SaveSystem.RecordNightComplete(g, "n1_blackout", Array.Empty<string>(), 0f);
                SaveSystem.Flush();
                GameBootstrap.Restart(2, "work");
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "work", 30f, "continuity night 2");
                g = G;
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3317) == decision, "night 2 preserves Paul's filed history: " + decision);
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3318) == opposite, "night 2 preserves Joan's filed history: " + opposite);
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == 0, "historical mistakes do not penalize the new night");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3319) == null, "tonight's undecided order remains pending");
                g.Orders.Decide(ContentIds.Order3319, decision, g.Player);
                g.Orders.Decide(ContentIds.Order3321, opposite, null, "Night Operations");
                g.Orders.SetHidden(ContentIds.Order3320, false);
                g.Orders.Decide(ContentIds.Order3320, decision, g.Player);
                g.Orders.Cancel(ContentIds.Order3322);
                g.Orders.SetHidden(ContentIds.Order3324, false);
                g.Orders.Decide(ContentIds.Order3324, opposite, g.Player);
                mistakes = g.Flags.Get(Flags.CounterWrongOrders);
                SaveSystem.SaveCheckpoint(g, "finish");
                SaveSystem.Flush();
                GameBootstrap.RestartFromCheckpoint(2, false, false);
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "finish", 30f, "continuity night 2 reload");
                g = G;
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3319) == decision, "night 2 checkpoint preserves ordinary decision: " + decision);
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3321) == opposite, "night 2 checkpoint preserves assisted decision");
                ExperienceAssert(g.Orders.DecidedBy(ContentIds.Order3321) == "Night Operations", "night 2 checkpoint preserves assistance credit");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3320) == decision && g.Orders.DecisionFor(ContentIds.Order3324) == opposite,
                    "night 2 checkpoint preserves both choice branches");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3322) == WorkOrderService.Cancelled, "cancelled choice stays cancelled");
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == mistakes, "night 2 checkpoint does not recount mistakes");
                SaveSystem.RecordNightComplete(g, "n2_blackout", Array.Empty<string>(), 0f);
                SaveSystem.Flush();
                GameBootstrap.Restart(3, "work");
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "work", 30f, "continuity night 3");
                g = G;
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3317) == decision && g.Orders.DecisionFor(ContentIds.Order3318) == opposite,
                    "night 1 decisions survive a second night transition");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3319) == decision && g.Orders.DecisionFor(ContentIds.Order3321) == opposite,
                    "night 3 preserves both ordinary night 2 decisions");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3322) == WorkOrderService.Cancelled, "night 3 preserves no-decision history");
                var order = g.Content.Order(ContentIds.Order3320);
                ExperienceAssert(g.Content.Employee(order.employeeRef).notes.Contains(WorkOrderRules.NoteFor(order, decision)),
                    "night 3 Personnel agrees with the remembered choice");
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == 0, "night 3 starts without duplicated historical mistakes");
                g.Orders.Decide(ContentIds.Order3330, decision, g.Player);
                g.Orders.Decide(ContentIds.Order3331, opposite, g.Player);
                g.Orders.SetHidden(ContentIds.Order3332, false);
                g.Orders.Decide(ContentIds.Order3332, decision, g.Player);
                g.Orders.Cancel(ContentIds.Order3333);
                g.Orders.SetHidden(ContentIds.Order3342, false);
                g.Orders.Decide(ContentIds.Order3342, opposite, g.Player);
                mistakes = g.Flags.Get(Flags.CounterWrongOrders);
                SaveSystem.SaveCheckpoint(g, "finale");
                SaveSystem.Flush();
                GameBootstrap.RestartFromCheckpoint(3, false, false);
                yield return WaitFor(() => G != null && G != g && G.Director.CurrentBeat == "finale", 30f, "continuity night 3 reload");
                g = G;
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3330) == decision && g.Orders.DecisionFor(ContentIds.Order3331) == opposite,
                    "night 3 checkpoint preserves ordinary decisions");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3342) == opposite && g.Tasks.IsCompleted(ContentIds.TaskN3Shelf),
                    "night 3 checkpoint preserves the completed shelf check");
                ExperienceAssert(g.Orders.DecisionFor(ContentIds.Order3333) == WorkOrderService.Cancelled,
                    "night 3 checkpoint does not invent Ruth's decision");
                ExperienceAssert(g.Flags.Get(Flags.CounterWrongOrders) == mistakes, "night 3 checkpoint does not recount mistakes");
            }
            // A legacy save may lack ordinary order history. Absence cannot be recovered as an approval.
            SaveSystem.NewGame();
            var beforeMissing = G;
            GameBootstrap.Restart(3, "work");
            yield return WaitFor(() => G != null && G != beforeMissing && G.Director.CurrentBeat == "work", 30f, "missing order history");
            ExperienceAssert(G.Orders.DecisionFor(ContentIds.Order3317) == WorkOrderService.Cancelled &&
                G.Orders.DecisionFor(ContentIds.Order3319) == WorkOrderService.Cancelled, "missing history never becomes an invented approval");
            ExperienceAssert(G.Orders.DecisionFor(ContentIds.Order3330) == null, "missing old history does not cancel tonight's pending work");
            Say("PASS decision continuity integration checks complete");
            GameBootstrap.ToTitle();
        }
    }
}
