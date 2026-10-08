using System;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Game;

namespace SecondCursor.Story
{
    public sealed partial class Night1Director
    {
        OpeningCheckpoint _openingToRestore;
        bool _quickStartDismissed, _openingDirty;

        void WatchOpeningProgress()
        {
            _g.Tasks.TaskCompleted += _ => QueueOpeningSave();
            _g.Tasks.TaskProgressed += _ => QueueOpeningSave();
            _g.Files.FileMoved += (file, from, to, actor) => QueueOpeningSave();
            _g.Files.FileShredded += (file, actor) => QueueOpeningSave();
            _g.Files.FileChanged += _ => QueueOpeningSave();
            _g.Mail.Read += _ => QueueOpeningSave();
            _g.Mail.Delivered += _ => QueueOpeningSave();
            _g.Orders.Decided += (id, decision, actor) => QueueOpeningSave();
        }

        void QueueOpeningSave()
        {
            if (!IsPreparing && OpeningCheckpoint.Supports(Night, CurrentBeat)) _openingDirty = true;
        }

        protected override void Update()
        {
            base.Update();
            // Event callbacks finish updating flags, mail and attribution before the snapshot is made.
            if (_openingDirty) SaveCurrentProgress();
        }

        public override void SaveCurrentProgress()
        {
            _openingDirty = false;
            if (IsPreparing || IsStandIn || !OpeningCheckpoint.Supports(Night, CurrentBeat)) return;
            SaveSystem.SaveCheckpoint(_g, CurrentBeat);
        }

        public override void CaptureCheckpointWorld(Checkpoint checkpoint)
        {
            if (!OpeningCheckpoint.Supports(checkpoint.night, checkpoint.beat)) return;
            var saved = new OpeningCheckpoint { quickStartDismissed = _quickStartDismissed };
            var files = new List<OpeningFile>();
            foreach (var f in _g.Files.AllFiles)
                files.Add(new OpeningFile { id = f.Id, folder = f.FolderId, name = f.Name, content = f.Content,
                    movedBy = f.MovedBy, shredded = f.Shredded, hidden = f.Hidden,
                    shredCredit = _g.Credits.Get(TaskType.DeleteFile, f.Id) });
            saved.files = files.ToArray();
            var mail = new List<OpeningMail>();
            foreach (var id in _g.Mail.Inbox)
                mail.Add(new OpeningMail { id = id, date = _g.Mail.DateOf(id), read = _g.Mail.IsRead(id),
                    live = _g.Mail.IsLive(id), credit = _g.Credits.Get(TaskType.ReadEmail, id) });
            saved.mail = mail.ToArray();
            var orders = new List<OpeningOrder>();
            foreach (var order in _g.Content.WorkOrders.orders)
            {
                string decision = _g.Orders.DecisionFor(order.id);
                if (decision != null) orders.Add(new OpeningOrder { id = order.id, decision = decision, credit = _g.Orders.DecidedBy(order.id) });
            }
            saved.orders = orders.ToArray();
            var tasks = new List<OpeningTask>();
            foreach (var task in _g.Tasks.Tasks)
                if (task.State == TaskState.Active || task.State == TaskState.Completed)
                    tasks.Add(new OpeningTask { id = task.Id, completed = task.IsDone, finishedBy = task.FinishedBy });
            saved.tasks = tasks.ToArray();
            checkpoint.opening = saved;
        }

        public override void RestoreCheckpointReplies(Checkpoint checkpoint)
        {
            base.RestoreCheckpointReplies(checkpoint);
            _openingToRestore = OpeningCheckpoint.Supports(checkpoint.night, checkpoint.beat) ? checkpoint.opening : null;
        }

        bool RestoreOpeningWork()
        {
            var saved = _openingToRestore;
            _openingToRestore = null;
            if (saved == null) return false;
            var g = _g;
            _quickStartDismissed = saved.quickStartDismissed;
            foreach (var f in saved.files ?? Array.Empty<OpeningFile>())
            {
                if (f == null || g.Files.GetFile(f.id) == null || g.Files.GetFolder(f.folder) == null) continue;
                g.Files.Restore(f.id, f.folder, Actor.System);
                var file = g.Files.GetFile(f.id);
                file.FolderId = f.folder;
                file.Name = f.name ?? file.Name;
                file.MovedBy = f.movedBy;
                g.Files.SetContent(f.id, f.content);
                g.Files.SetHidden(f.id, f.hidden);
                g.Credits.Set(TaskType.DeleteFile, f.id, f.shredCredit);
                if (f.shredded && g.Files.Shred(f.id, Actor.System)) g.Shred.MarkShredded();
            }
            int mistakes = g.Flags.Get(Flags.CounterWrongOrders);
            foreach (var order in saved.orders ?? Array.Empty<OpeningOrder>())
            {
                if (order == null || g.Content.Order(order.id) == null) continue;
                if (order.decision == "approve" || order.decision == "reject") g.Orders.Decide(order.id, order.decision, null, order.credit);
                else if (order.decision == SecondCursor.OS.WorkOrderService.Cancelled) g.Orders.Cancel(order.id);
            }
            g.Flags.SetCounter(Flags.CounterWrongOrders, mistakes);
            Note163(g.Orders.DecisionFor(ContentIds.Order3318), false);
            foreach (var mail in saved.mail ?? Array.Empty<OpeningMail>())
                if (mail != null) g.Mail.RestoreDelivery(mail.id, mail.date, mail.read, mail.live, mail.credit);
            g.Mail.SortByDate();
            foreach (var task in saved.tasks ?? Array.Empty<OpeningTask>())
            {
                if (task == null) continue;
                g.Tasks.MarkFinishedBy(task.id, task.finishedBy);
                g.Tasks.Activate(task.id);
                // A completed job remains credited even if the player moved its files again afterward.
                if (task.completed) g.Tasks.ForceComplete(task.id);
            }
            g.Apps.Launch(AppIds.WorkQueue, null);
            _openingDirty = false;
            GameLog.Info(LogChannel.System, "Restored opening work, decisions and inbox");
            return true;
        }
    }
}
