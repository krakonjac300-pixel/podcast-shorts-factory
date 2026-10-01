using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
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

        /// <summary>Something was shredded outside the dialogs (story setup): the bin shows full.</summary>
        public void MarkShredded() => AnyShredded = true;

        readonly HashSet<string> _purged = new HashSet<string>();

        /// <summary>
        /// Custodial empties the Disposal bin: it shows empty again and its window forgets what was shredded
        /// so far. Nothing comes back.
        /// </summary>
        public void ResetBin()
        {
            AnyShredded = false;
            foreach (var f in _g.Files.AllFiles) if (f.Shredded) _purged.Add(f.Id);
            GameLog.Info(LogChannel.OS, "Disposal emptied");
        }

        /// <summary>A shredded file the bin no longer lists (emptied by <see cref="ResetBin"/>).</summary>
        public bool IsPurged(string fileId) => fileId != null && _purged.Contains(fileId);
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
        /// <summary>
        /// One more line for a file's Confirm Shred box, chosen when it opens (Phase I: Night 2's employee_209.dat after 3:00
        /// says a late shred releases only part of the record). Null or empty = none.
        /// </summary>
        public Func<string, string> ConfirmNote;

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
            // Some files carry one more line (M7: employee_209.dat names its owner, which makes the file a person).
            string body = c.Format("shred.confirm.body", file.Name);
            string note = c.Text("shred.confirm.note." + fileId, "");
            if (note.Length > 0) body += "\n" + note;
            string late = ConfirmNote?.Invoke(fileId);
            if (!string.IsNullOrEmpty(late)) body += "\n" + late;
            // Phase N: a file another session defends is a race to No, and the dialog shows it.
            var brain = _g.Entity != null ? _g.Entity.Brain : null;
            // Phase P (T1): not when she lets it go (the finale's LetGo): she rests on Yes instead.
            Raced = brain != null && brain.Enabled && brain.ProtectedFileId == fileId && !brain.LetsGo;
            Confirm = Dialogs.Message(_g, c.Text("shred.confirm.title"), body, "icon_question",
                new[] { "Yes", "No" }, OnConfirm, 0, null, Raced ? RaceStatusHeight : 0);
            if (Raced) ConfirmRace.Attach(_g, Confirm, _g.Entity, () => true, "race.idle.shred");
            ConfirmShown?.Invoke(fileId, Confirm);
        }

        /// <summary>Phase N: room in a dialog for the race line and its bar.</summary>
        public const int RaceStatusHeight = 26;
        /// <summary>Phase N: the shred under way is one another session races (its outcome is always a notice).</summary>
        public bool Raced { get; private set; }
        /// <summary>Phase N: the last cancel was an answer to the Confirm Shred (No or closed), not a Cancel during the shred.</summary>
        public bool CancelledAtConfirm { get; private set; }

        bool IsPendingArchive(string fileId)
        {
            foreach (var t in _g.Tasks.Tasks)
            {
                // A request the second cursor wrote into the queue, or a withdrawn one, protects nothing.
                if (t.Type != TaskType.MoveFile || t.IsDone || t.IsWithdrawn || t.IsEntityAuthored) continue;
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
                CancelledAtConfirm = true;
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
            CancelledAtConfirm = false;
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
            // Phase Q1: the Work Queue names whoever shredded a file a task asked for (null: the player). Set first: the shred ticks the task.
            _g.Credits.Set(TaskType.DeleteFile, fileId, by != null && by.IsEntity ? SystemNotices.SessionOf(_g, by) : null);
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
        /// <summary>Mail that arrived during this shift with a notice (not the preloaded or story-restored mail), oldest first.</summary>
        readonly List<string> _live = new List<string>();
        /// <summary>Phase I: the date mail that arrived during the shift shows (never later than the clock at that moment).</summary>
        readonly Dictionary<string, string> _received = new Dictionary<string, string>();

        /// <summary>The newest mail that arrived with a notice this shift and is still unread (null = none); the Work Queue lists it.</summary>
        public string NewestUnreadLive
        {
            get
            {
                for (int i = _live.Count - 1; i >= 0; i--) if (!_read.Contains(_live[i])) return _live[i];
                return null;
            }
        }

        /// <summary>How many mails that arrived with a notice this shift are still unread.</summary>
        public int UnreadLiveCount
        {
            get
            {
                int n = 0;
                foreach (var id in _live) if (!_read.Contains(id)) n++;
                return n;
            }
        }

        public event Action<string> Delivered;
        public event Action<string> Read;
        /// <summary>Phase Q2: a mail was put on screen in Mail (every time, not only the first read): (id, who opened it).</summary>
        public event Action<string, CursorAgent> Opened;
        public int Revision { get; private set; }

        /// <summary>Phase Q2: Mail shows a message (the capture profile counts how often the player opened the briefing).</summary>
        public void NoteOpened(string id, CursorAgent by) => Opened?.Invoke(id, by);

        public MailService(GameServices g)
        {
            _g = g;
            foreach (var e in g.Content.Emails.emails)
            {
                if (e == null || !e.preload) continue;
                _inbox.Add(e.id);
                if (e.read) _read.Add(e.id);
            }
            // Oldest first, so the mail client (which lists newest deliveries on top) opens in date order.
            var byDate = new List<string>(_inbox);
            _inbox.Sort((a, b) =>
            {
                int c = SortDate(a).CompareTo(SortDate(b));
                return c != 0 ? c : byDate.IndexOf(a).CompareTo(byDate.IndexOf(b)); // stable for equal dates
            });
        }

        System.DateTime SortDate(string id)
        {
            var mail = _g.Content.Email(id);
            return mail != null && System.DateTime.TryParseExact(DateOf(id), "ddd MM/dd/yy h:mm tt",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : System.DateTime.MinValue;
        }

        /// <summary>The date a mail shows in the list and its header: when it arrived, for mail that came in tonight.</summary>
        public string DateOf(string id)
        {
            if (id != null && _received.TryGetValue(id, out var d)) return d;
            return _g.Content.Email(id)?.date ?? "";
        }

        /// <summary>
        /// Put the inbox back in date order (oldest first, so the newest shows on top). A later night's setup
        /// delivers the earlier nights' mail after tonight's preloads; this keeps tonight's briefing on top.
        /// </summary>
        public void SortByDate()
        {
            var before = new List<string>(_inbox);
            _inbox.Sort((a, b) =>
            {
                int c = SortDate(a).CompareTo(SortDate(b));
                return c != 0 ? c : before.IndexOf(a).CompareTo(before.IndexOf(b));
            });
            Revision++;
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
            _received[id] = MailDates.Received(_g.Content.Email(id).date, _g.Night, _g.Clock != null ? _g.Clock.TotalMinutes : int.MaxValue);
            Revision++;
            GameLog.Info(LogChannel.Story, "Mail delivered " + id);
            Delivered?.Invoke(id);
            if (notify)
            {
                _live.Add(id);
                var mail = _g.Content.Email(id);
                // Sender on the toast, and the date too when it is not from tonight's year (the 1987 mail).
                string sender = string.IsNullOrEmpty(mail.from) ? "(no sender)" : (mail.from.IndexOf('<') > 0 ? mail.from.Substring(0, mail.from.IndexOf('<')).Trim() : mail.from);
                bool oldDate = !string.IsNullOrEmpty(mail.date) && mail.date.IndexOf("/98 ", StringComparison.Ordinal) < 0;
                string subject = string.IsNullOrEmpty(mail.subject) ? "(no subject)" : mail.subject;
                _g.Notifications.Show(_g.Content.Text("app.mail"), subject + "\nFrom " + sender + (oldDate ? ", " + mail.date : ""),
                    "icon_mail_unread", a => _g.Apps.Launch(AppIds.Mail, a));
            }
        }

        public void MarkRead(string id, CursorAgent by) => MarkRead(id, by, null);

        /// <param name="credit">Phase Q1: who read it for the player ("Night Operations"); another session's agent is named by itself.</param>
        public void MarkRead(string id, CursorAgent by, string credit)
        {
            if (!_inbox.Contains(id) || !_read.Add(id)) return;
            Revision++;
            _g.Credits.Set(TaskType.ReadEmail, id, by != null && by.IsEntity ? SystemNotices.SessionOf(_g, by) : credit);
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
        /// <summary>An order was opened in Work Orders (order id, by whom).</summary>
        public event Action<string, CursorAgent> Viewed;
        public int Revision { get; private set; }

        public void NotifyViewed(string orderId, CursorAgent by) => Viewed?.Invoke(orderId, by);

        public WorkOrderService(GameServices g)
        {
            _g = g;
        }

        public string DecisionFor(string orderId) => orderId != null && _decisions.TryGetValue(orderId, out var d) ? d : null;

        /// <summary>The decision an order gets when the company takes it back undecided (not a wrong answer).</summary>
        public const string Cancelled = "cancelled";

        readonly HashSet<string> _hidden = new HashSet<string>();

        /// <summary>Orders that are not in Work Orders yet (Night 3's shelf checks arrive with the round).</summary>
        public bool IsHidden(string orderId) => orderId != null && _hidden.Contains(orderId);

        public void SetHidden(string orderId, bool hidden)
        {
            if (string.IsNullOrEmpty(orderId) || (hidden ? !_hidden.Add(orderId) : !_hidden.Remove(orderId))) return;
            Revision++;
        }

        /// <summary>An undecided order is withdrawn by the company: it shows Cancelled and counts as decided.</summary>
        public void Cancel(string orderId)
        {
            if (string.IsNullOrEmpty(orderId) || DecisionFor(orderId) != null) return;
            _decisions[orderId] = Cancelled;
            Revision++;
            GameLog.Info(LogChannel.OS, "Order " + orderId + " cancelled");
            _g.Tasks.Evaluate();
        }

        public void Decide(string orderId, string decision, CursorAgent by) => Decide(orderId, decision, by, null);

        /// <summary>Phase Q1: who decided an order other than the player ("session 017", "Night Operations"); null = the player or the shift's setup.</summary>
        public string DecidedBy(string orderId) => _g.Credits.Get(TaskType.DecideOrder, orderId);

        /// <param name="credit">Phase Q1: who decided it for the player ("Night Operations"); another session's agent is named by itself.</param>
        public void Decide(string orderId, string decision, CursorAgent by, string credit)
        {
            if (DecisionFor(orderId) != null) return;
            _decisions[orderId] = decision;
            Revision++;
            _g.Credits.Set(TaskType.DecideOrder, orderId, by != null && by.IsEntity ? SystemNotices.SessionOf(_g, by) : credit);
            var order = _g.Content.Order(orderId);
            bool correct = order != null && string.Equals(order.correct, decision, StringComparison.OrdinalIgnoreCase);
            // Phase L: an order the player may decide either way has no wrong answer.
            bool choice = Core.Content.WorkOrderRules.IsChoice(order);
            if (!correct && !choice) _g.Flags.Increment(Core.Story.Flags.CounterWrongOrders);
            if (by != null && by.IsPlayer) _g.Memory.Record(MemoryKind.DecidedOrder, orderId, _g.Now);
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.Player,
                (by?.Name ?? credit ?? "System") + " " + (decision.EndsWith("e") ? decision + "d " : decision + "ed ") + orderId + (choice ? " (choice)" : correct ? " (correct)" : " (WRONG)"));
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
        public bool IsFileOpenedByPlayer(string fileId) => _g.Flags.Get(Core.Story.Flags.OpenedByPlayerPrefix + fileId) > 0;
        public bool IsEmployeeViewedByPlayer(string employeeId) => _g.Flags.Get(Core.Story.Flags.ViewedByPlayerPrefix + employeeId) > 0;
        public string CreditFor(TaskType type, string targetId) =>
            type == TaskType.MoveFile ? _g.Files.GetFile(targetId)?.MovedBy : _g.Credits.Get(type, targetId);
    }
}
