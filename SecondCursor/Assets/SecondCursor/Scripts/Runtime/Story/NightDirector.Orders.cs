using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Tasks;
using SecondCursor.Input;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase L (task escalation): work orders the player can decide either way (both decisions have a written result). The
    /// order stays hidden until its task is given; a decision is filed at once (a notice, the order's result line, the queue
    /// line, a note on the owner's Personnel record, a memory key for later nights); an order nobody decides lapses instead of
    /// being decided for the player.
    /// </summary>
    public abstract partial class NightDirector
    {
        void OnChoiceDecided(string id, string decision, CursorAgent by)
        {
            var g = _g;
            var order = g.Content.Order(id);
            if (!WorkOrderRules.IsChoice(order) || by == null || !by.IsPlayer || IsPreparing) return;
            g.Flags.Set(WorkOrderRules.MemoryKey(g.Night, id, decision));
            AppendNote(order.employeeRef, WorkOrderRules.NoteFor(order, decision));
            g.Notifications.Show(g.Content.Text("app.workorders"), WorkOrderRules.ResultFor(order, decision), "icon_info",
                a => g.Apps.Launch(AppIds.WorkOrders, a), "ui_select");
            SetFiledLine(id, decision);
        }

        /// <summary>The queue line of a finished choice order: "WO-3320 filed: approved".</summary>
        void SetFiledLine(string orderId, string decision)
        {
            var task = SingleOrderTask(orderId);
            if (task != null)
                _g.Tasks.SetResult(task.Id, _g.Content.Format(decision == "approve" ? "workqueue.order.approved" : "workqueue.order.rejected", OrderLabel(orderId)));
        }

        /// <summary>"wo_3320" as the game shows it: "WO-3320".</summary>
        static string OrderLabel(string orderId) => orderId.Replace("wo_", "WO-");

        WorkTask SingleOrderTask(string orderId)
        {
            foreach (var t in _g.Tasks.Tasks)
                if (t.Type == TaskType.DecideOrder && t.Data.targets.Length == 1 && t.Data.targets[0] == orderId) return t;
            return null;
        }

        /// <summary>Adds a line to an owner's Personnel notes (once), and shows it in an open Personnel window.</summary>
        void AppendNote(string employeeId, string note)
        {
            var e = _g.Content.Employee(employeeId);
            if (e == null || string.IsNullOrEmpty(note) || e.notes.Contains(note)) return;
            e.notes = e.notes.Length == 0 ? note : e.notes + " " + note;
            _g.Apps.Find<StaffApp>()?.Refresh();
        }

        /// <summary>The order appears in Work Orders with its task, and the mail that pulls the other way (if any) arrives.</summary>
        protected void RevealOrder(string taskId, string orderId, string mailId)
        {
            _g.Orders.SetHidden(orderId, false);
            GiveTask(taskId);
            if (mailId != null) _g.Mail.Deliver(mailId);
        }

        /// <summary>
        /// Waits for a decision (hints like any task); after the task's usual patience, and never before <see cref="ChoicePatience"/>
        /// (reading a mail and a Personnel record takes a slow reader longer than Story's 105 s), the order lapses.
        /// </summary>
        protected IEnumerator WaitOrder(string taskId, string orderId) => WaitTask(taskId, -1f, () => LapseOrder(taskId, orderId), ChoicePatience);

        const float ChoicePatience = 180f;

        /// <summary>Nobody decided: the task is taken back first (cancelling the order would complete it), then the order is cancelled.</summary>
        protected void LapseOrder(string taskId, string orderId)
        {
            var g = _g;
            bool shown = !g.Orders.IsHidden(orderId);
            g.Tasks.Withdraw(taskId, g.Content.Text("workqueue.withdrawn.nodecision"));
            g.Orders.Cancel(orderId);
            if (!shown) return;
            g.Notifications.Show(g.Content.Text("app.workorders"), g.Content.Format("notify.order.lapsed", OrderLabel(orderId)), "icon_info",
                a => g.Apps.Launch(AppIds.WorkOrders, a), "ui_select");
            GameLog.Info(LogChannel.Story, "Order " + orderId + " lapsed: no decision");
        }

        /// <summary>
        /// A jump or checkpoint past a choice order: the order is history, decided as the memory key says (a lapsed order stays
        /// cancelled, its task never listed), with its Personnel note and queue line back, and the mail that came with it read.
        /// </summary>
        protected void RestoreChoice(string taskId, string orderId, string mailId)
        {
            var g = _g;
            g.Orders.SetHidden(orderId, false);
            if (mailId != null)
            {
                g.Mail.Deliver(mailId, false);
                g.Mail.MarkRead(mailId, null);
            }
            string decision = WorkOrderRules.Remembered(g.Flags, g.Night, orderId);
            if (decision == null)
            {
                g.Tasks.Withdraw(taskId);
                g.Orders.Cancel(orderId);
                return;
            }
            var order = g.Content.Order(orderId);
            g.Orders.Decide(orderId, decision, null);
            AppendNote(order.employeeRef, WorkOrderRules.NoteFor(order, decision));
            SetFiledLine(orderId, decision);
            g.Tasks.ForceComplete(taskId);
        }
    }
}
