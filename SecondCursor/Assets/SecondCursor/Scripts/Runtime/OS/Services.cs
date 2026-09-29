using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Tasks;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// The shred pipeline: confirmation dialog -> progress dialog with Cancel -> file destroyed. Every step
    /// raises an event so the entity can interfere at any point (race to "No", drag the dialog away,
    /// hit Cancel, make the file "in use").
    /// </summary>
    public sealed class ShredService
    {
        readonly GameServices _g;
        float _progressTime;
        CursorAgent _confirmedBy;

        public bool AnyShredded { get; private set; }
        public MessageBox Confirm { get; private set; }
        public ProgressDialog Progress { get; private set; }
        public string PendingFileId { get; private set; }
        public bool Busy => (Confirm != null && Confirm.IsOpen) || (Progress != null && Progress.IsOpen);

        /// <summary>Seconds a shred takes at speed 1.</summary>
        public float Duration = 3.4f;
        /// <summary>Story/entity can slow a shred to a crawl.</summary>
        public float SpeedMultiplier = 1f;
        /// <summary>Return true to refuse with "in use by another user".</summary>
        public Func<string, bool> IsInUse;

        public event Action<string, CursorAgent> Requested;
        public event Action<string, MessageBox> ConfirmShown;
        public event Action<string, ProgressDialog> ProgressStarted;
        public event Action<string, CursorAgent> Cancelled;
        public event Action<string, CursorAgent> Completed;
        public event Action<string> RefusedInUse;

        public ShredService(GameServices g)
        {
            _g = g;
        }

        public void Request(string fileId, CursorAgent by)
        {
            var file = _g.Files.GetFile(fileId);
            if (file == null || file.Shredded) return;
            if (Busy)
            {
                var w = Confirm != null && Confirm.IsOpen ? Confirm.Window : Progress?.Window;
                if (w != null) { w.Focus(by); w.Shake(0.2f, 2f); }
                return;
            }
            SpeedMultiplier = 1f;
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player, (by?.Name ?? "System") + " attempted shred " + fileId);
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.ShredAttempt, fileId, _g.Now);
            Requested?.Invoke(fileId, by);

            var c = _g.Content;
            if (IsInUse != null && IsInUse(fileId))
            {
                Dialogs.Message(_g, c.Text("error.inuse.title"), c.Format("error.inuse.body", file.Name), "icon_error", new[] { "OK" }, null);
                RefusedInUse?.Invoke(fileId);
                return;
            }
            if (_g.Files.IsInsideLocked(file.FolderId))
            {
                Dialogs.Message(_g, c.Text("restricted.denied.title"), c.Text("restricted.denied.body"), "icon_lock", new[] { "OK" }, null);
                return;
            }
            if (file.Protected)
            {
                Dialogs.Message(_g, c.Text("shred.confirm.title"), file.Name + " is protected.\nAccess is denied.", "icon_lock", new[] { "OK" }, null);
                return;
            }
            if (IsPendingArchive(fileId))
            {
                // Never let the player destroy a file a task still needs (that would soft-lock the shift).
                Dialogs.Message(_g, c.Text("shred.confirm.title"), file.Name + " is scheduled for archiving.\nIt cannot be shredded.", "icon_info", new[] { "OK" }, null);
                return;
            }

            PendingFileId = fileId;
            Confirm = Dialogs.Message(_g, c.Text("shred.confirm.title"), c.Format("shred.confirm.body", file.Name), "icon_question",
                new[] { "Yes", "No" }, OnConfirm, 0);
            ConfirmShown?.Invoke(fileId, Confirm);
        }

        bool IsPendingArchive(string fileId)
        {
            foreach (var t in _g.Tasks.Tasks)
            {
                if (t.Type != TaskType.MoveFile || t.IsDone) continue;
                foreach (var target in t.Data.targets) if (target == fileId) return true;
            }
            return false;
        }

        void OnConfirm(string result, CursorAgent by)
        {
            string fileId = PendingFileId;
            Confirm = null;
            if (result != "Yes")
            {
                GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.OS, "Shred of " + fileId + " declined (" + result + " by " + (by?.Name ?? "System") + ")");
                PendingFileId = null;
                Cancelled?.Invoke(fileId, by);
                return;
            }
            _confirmedBy = by;
            var c = _g.Content;
            var file = _g.Files.GetFile(fileId);
            Progress = Dialogs.Progress(_g, c.Text("shred.progress.title"), c.Format("shred.progress.body", file != null ? file.Name : fileId));
            Progress.Cancelled += a => CancelProgress(a);
            _progressTime = 0f;
            _g.Audio?.PlayLoop("shred_loop", 0.5f);
            ProgressStarted?.Invoke(fileId, Progress);
        }

        public void CancelProgress(CursorAgent by)
        {
            if (Progress == null) return;
            string fileId = PendingFileId;
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.OS, "Shred of " + fileId + " cancelled by " + (by?.Name ?? "System"));
            var p = Progress;
            Progress = null;
            PendingFileId = null;
            _g.Audio?.StopLoop("shred_loop");
            p.Close(by);
            Cancelled?.Invoke(fileId, by);
        }

        public void Tick(float dt)
        {
            if (Progress == null) return;
            if (!Progress.IsOpen)
            {
                CancelProgress(null);
                return;
            }
            // Progress stalls a little at random, like real disk operations.
            float speed = SpeedMultiplier * (Mathf.PerlinNoise(_progressTime * 1.3f, 0.37f) < 0.28f ? 0.15f : 1f);
            _progressTime += dt * speed;
            Progress.Progress = _progressTime / Mathf.Max(0.1f, Duration);
            if (Progress.Progress < 1f) return;

            string fileId = PendingFileId;
            var by = _confirmedBy;
            var p = Progress;
            Progress = null;
            PendingFileId = null;
            _g.Audio?.StopLoop("shred_loop");
            p.Close(null);
            var actor = by != null && by.IsEntity ? Actor.Entity : Actor.Player;
            if (_g.Files.Shred(fileId, actor))
            {
                AnyShredded = true;
                if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.ShredSucceeded, fileId, _g.Now);
                Completed?.Invoke(fileId, by);
            }
        }

        /// <summary>Close any shred dialogs without outcome (story resets).</summary>
        public void Abort()
        {
            if (Confirm != null && Confirm.IsOpen) Confirm.Window.Close();
            Confirm = null;
            if (Progress != null) Progress.Close();
            Progress = null;
            PendingFileId = null;
            _g.Audio?.StopLoop("shred_loop");
        }
    }

    /// <summary>Inbox state: which emails have arrived and which are read.</summary>
    public sealed class MailService
    {
        readonly GameServices _g;
        readonly List<string> _inbox = new List<string>();
        readonly HashSet<string> _read = new HashSet<string>();

        public event Action<string> Delivered;
        public event Action<string> Read;
        public int Revision { get; private set; }

        public MailService(GameServices g)
        {
            _g = g;
            foreach (var e in g.Content.Emails.emails)
            {
                if (e == null || !e.preload) continue;
                _inbox.Add(e.id);
                if (e.read) _read.Add(e.id);
            }
        }

        public IReadOnlyList<string> Inbox => _inbox;
        public bool Has(string id) => _inbox.Contains(id);
        public bool IsRead(string id) => _read.Contains(id);

        public int UnreadCount
        {
            get
            {
                int n = 0;
                foreach (var id in _inbox) if (!_read.Contains(id)) n++;
                return n;
            }
        }

        public void Deliver(string id, bool notify = true)
        {
            if (string.IsNullOrEmpty(id) || _inbox.Contains(id) || _g.Content.Email(id) == null) return;
            _inbox.Add(id);
            Revision++;
            GameLog.Info(LogChannel.Story, "Mail delivered " + id);
            Delivered?.Invoke(id);
            if (notify)
            {
                var mail = _g.Content.Email(id);
                _g.Notifications.Show(_g.Content.Text("app.mail"), _g.Content.Format("notify.newmail", UnreadCount) + "\n" + mail.subject,
                    "icon_mail_unread", a => _g.Apps.Launch(AppIds.Mail, a));
            }
        }

        public void MarkRead(string id, CursorAgent by)
        {
            if (!_inbox.Contains(id) || !_read.Add(id)) return;
            Revision++;
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.ReadEmail, id, _g.Now);
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player, (by?.Name ?? "System") + " read mail " + id);
            Read?.Invoke(id);
            _g.Tasks.Evaluate();
        }
    }

    /// <summary>Approve/reject decisions for work orders.</summary>
    public sealed class WorkOrderService
    {
        readonly GameServices _g;
        readonly Dictionary<string, string> _decisions = new Dictionary<string, string>();

        public event Action<string, string, CursorAgent> Decided;
        public int Revision { get; private set; }

        public WorkOrderService(GameServices g)
        {
            _g = g;
        }

        public string DecisionFor(string orderId) => orderId != null && _decisions.TryGetValue(orderId, out var d) ? d : null;

        public void Decide(string orderId, string decision, CursorAgent by)
        {
            if (DecisionFor(orderId) != null) return;
            _decisions[orderId] = decision;
            Revision++;
            var order = _g.Content.Order(orderId);
            bool correct = order != null && string.Equals(order.correct, decision, StringComparison.OrdinalIgnoreCase);
            if (!correct) _g.Flags.Increment(Core.Story.Flags.CounterWrongOrders);
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.DecidedOrder, orderId, _g.Now);
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player,
                (by?.Name ?? "System") + " " + decision + "d " + orderId + (correct ? " (correct)" : " (WRONG)"));
            Decided?.Invoke(orderId, decision, by);
            _g.Tasks.Evaluate();
        }
    }

    /// <summary>Adapter giving the engine-free task manager read access to the runtime world.</summary>
    public sealed class TaskWorld : ITaskWorld
    {
        readonly GameServices _g;
        public TaskWorld(GameServices g) { _g = g; }
        public bool IsEmailRead(string emailId) => _g.Mail != null && _g.Mail.IsRead(emailId);
        public string FolderOf(string fileId) => _g.Files.FolderOf(fileId);
        public bool IsShredded(string fileId) => _g.Files.GetFile(fileId)?.Shredded ?? false;
        public string DecisionFor(string orderId) => _g.Orders?.DecisionFor(orderId);
    }
}
