using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Tasks;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q1 (owner 1 and 2, the outside tester's "the game will do the job while I look around"): a task is never finished for the
    /// player without asking. Waiting on a task runs the <see cref="TaskAssist"/> ladder: the task's hint, the hint again with the next
    /// step spelled out, then a quiet help notice. The Work Queue opens an offer only when the player asks. Time spent
    /// reading does not count. An accepted offer finishes the task through the world (files moved, orders decided, mail read), so the Work
    /// Queue, Work Orders, the folders and the notice always agree, and everything it did is marked as done by Night Operations.
    /// </summary>
    public abstract partial class NightDirector
    {
        MessageBox _offerBox;
        readonly Dictionary<string, TaskAssist> _taskAssists = new Dictionary<string, TaskAssist>();

        public bool CanRequestTaskHelp(string taskId) => taskId != null && _g.Tasks.IsActive(taskId)
            && _taskAssists.TryGetValue(taskId, out var assist) && assist.OfferAllowed && !assist.Accepted && CanOfferNow();

        public void RequestTaskHelp(string taskId)
        {
            if (!CanRequestTaskHelp(taskId)) return;
            var assist = _taskAssists[taskId];
            if (assist.RequestOffer()) ShowOffer(taskId, assist);
        }

        /// <summary>
        /// Waits for a task with the assist ladder. A beat that genuinely needs the task done waits as long as it takes: the offer comes back
        /// after "Not now". With <paramref name="onTimeout"/> (an order the player decides either way) there is no offer: after the difficulty's
        /// patience in active time, and never before <paramref name="minPatience"/> seconds of it, the order lapses instead.
        /// </summary>
        protected IEnumerator WaitTask(string taskId, float hintAfter = -1f, Action onTimeout = null, float minPatience = 0f)
        {
            var d = _g.Difficulty;
            if (hintAfter < 0f) hintAfter = d.TaskHintFirst;
            else if (d.Mode == DifficultyMode.Story) hintAfter = Mathf.Min(hintAfter, d.TaskHintFirst);
            var assist = NewAssist(taskId, hintAfter, onTimeout == null);
            float lapseAfter = Mathf.Max(hintAfter + d.TaskForceAfterHint, minPatience);
            while (!_g.Tasks.IsCompleted(taskId) && !_g.Tasks.IsWithdrawn(taskId))
            {
                KeepTaskFilesAlive(taskId);
                if (onTimeout != null && assist.ActiveSeconds > lapseAfter)
                {
                    onTimeout();
                    break;
                }
                StepAssist(taskId, assist);
                yield return null;
            }
            CloseOffer(assist);
            yield return Wait(0.8f);
        }

        /// <summary>
        /// The ladder alone, as a side routine for a beat that waits on a task in its own way (Night 2's Batch 46, Night 3's Batch 48). With
        /// <paramref name="offer"/> false (a choice the player must make) it only hints.
        /// </summary>
        protected IEnumerator AssistLadder(string taskId, bool offer, float hintAfter = -1f)
        {
            var d = _g.Difficulty;
            var assist = NewAssist(taskId, hintAfter < 0f ? d.TaskHintFirst : Mathf.Min(hintAfter, d.Mode == DifficultyMode.Story ? d.TaskHintFirst : hintAfter), offer);
            while (!_g.Tasks.IsCompleted(taskId) && !_g.Tasks.IsWithdrawn(taskId))
            {
                KeepTaskFilesAlive(taskId);
                StepAssist(taskId, assist);
                yield return null;
            }
            CloseOffer(assist);
        }

        TaskAssist NewAssist(string taskId, float hintAfter, bool offer)
        {
            var d = _g.Difficulty;
            var assist = new TaskAssist(hintAfter, d.TaskHintRepeat, d.TaskForceAfterHint, offer && TaskFinisher.CanOffer(_g.Tasks.Get(taskId)));
            _taskAssists[taskId] = assist;
            return assist;
        }

        /// <summary>Required ordinary work files recover to Intake with an explicit Night Operations receipt.</summary>
        void KeepTaskFilesAlive(string taskId)
        {
            var task = _g.Tasks.Get(taskId);
            if (task == null || task.Type != TaskType.MoveFile) return;
            var restored = new List<string>();
            foreach (var target in task.Data.targets)
            {
                var f = _g.Files.GetFile(target);
                if (f != null && f.Shredded && _g.Files.Restore(target, ContentIds.FolderIntake, Actor.System)
                    && !task.IsEntityAuthored && target != ContentIds.File017 && target != ContentIds.File209)
                    restored.Add(f.Name);
            }
            if (restored.Count > 0)
            {
                string folder = _g.Files.GetFolder(ContentIds.FolderIntake)?.Name ?? "Intake";
                string body = "Night Operations recovered required work files from a backup to " + folder + ": " + string.Join(", ", restored) + ".";
                _g.Notifications.Show(TaskFinisher.NightOperations, body, "icon_task_pending",
                    a => _g.Apps.OpenFolder(ContentIds.FolderIntake, a), "ui_select");
                GameLog.Info(LogChannel.Task, body);
            }
        }

        void StepAssist(string taskId, TaskAssist assist)
        {
            var t = _g.Tasks.Get(taskId);
            if (t == null) return;
            // If the player starts shredding with requested help still open, keep one decision on screen.
            if (_g.Shred.Busy && _offerBox != null && _offerBox.IsOpen)
                _offerBox.Window.Close(null, true);
            switch (assist.Tick(Time.deltaTime, PlayerReading(), CanOfferNow(), t.Progress))
            {
                case AssistStep.Hint:
                    ShowTaskHint(taskId);
                    break;
                case AssistStep.RepeatHint:
                    ShowTaskHint(taskId, NextStepLine(t));
                    break;
                case AssistStep.Offer:
                    // Availability is a quiet notice. Only an explicit request opens a dialog.
                    _g.Notifications.Show(_g.Content.Text("assist.offer.title"), "Help is available in the Work Queue.", "icon_info",
                        a => _g.Apps.Launch(AppIds.WorkQueue, a), "ui_select");
                    break;
            }
        }

        /// <summary>The player is reading (a focused Mail, document, Personnel, Help or Camera Viewer window, or a recent scroll).</summary>
        bool PlayerReading()
        {
            TrackInput();
            var w = _g.Windows.Active;
            string app = w != null && !w.IsClosed && !w.IsMinimized ? w.AppId : null;
            return ReadingRule.IsReading(app, Time.unscaledTime - ScrollArea.LastPlayerScrollAt, Time.time - _lastInputAt);
        }

        Vector2 _lastPointer;
        float _lastInputAt;

        /// <summary>When the player last did anything (moved the pointer, held a button, typed, scrolled), in game seconds.</summary>
        void TrackInput()
        {
            var p = _g.Player;
            bool input = (p.Position - _lastPointer).sqrMagnitude > 0.25f || p.Held || !string.IsNullOrEmpty(_g.Input.TypedText)
                         || (Time.unscaledTime - ScrollArea.LastPlayerScrollAt >= 0f && Time.unscaledTime - ScrollArea.LastPlayerScrollAt < 0.5f);
            _lastPointer = p.Position;
            if (input) _lastInputAt = Time.time;
        }

        /// <summary>A dialog may come up now: not in a tug, a shred, another dialog, a climax, the ending, or with a file in hand.</summary>
        bool CanOfferNow()
        {
            var g = _g;
            if (PauseMenu.IsPaused || g.Shred.Busy || (g.Conflict != null && g.Conflict.IsFighting) || g.Player.Payload != null) return false;
            if (g.Scares != null && g.Scares.ClimaxRunning) return false;
            if (g.Flags.Has(Core.Story.Flags.Ending) || AnySpeakerTyping || AwaitingReply) return false;
            // Any always-on-top window (a dialog, the Restricted code prompt) keeps the offer away: it would take the focus and the player's keys.
            var open = g.Windows.Windows;
            for (int i = 0; i < open.Count; i++)
            {
                var w = open[i];
                if (w != null && !w.IsClosed && !w.IsMinimized && (w.AlwaysOnTop || w.AppId == "dialog" || w.AppId == "progress")) return false;
            }
            return true;
        }

        /// <summary>
        /// The repeat hint's second line: what is still missing, read from the world ("Next: batch44_c.dat is in Intake. It goes in Archive.").
        /// </summary>
        string NextStepLine(WorkTask t)
        {
            var c = _g.Content;
            foreach (var id in t.Data.targets)
            {
                if (_g.Tasks.IsTargetDone(t, id)) continue;
                switch (t.Type)
                {
                    case TaskType.MoveFile:
                    {
                        var f = _g.Files.GetFile(id);
                        if (f == null) return null;
                        string where = f.Shredded ? c.Text("workqueue.check.shredded")
                            : f.FolderId == ContentIds.FolderDesktop ? c.Text("workqueue.check.desktop")
                            : c.Format("workqueue.check.in", _g.Files.GetFolder(f.FolderId)?.Name ?? f.FolderId);
                        return c.Format("assist.next.move", f.Name, where, _g.Files.GetFolder(t.Data.param)?.Name ?? t.Data.param);
                    }
                    case TaskType.DecideOrder:
                        return c.Format("assist.next.order", OrderLabel(id));
                    case TaskType.DeleteFile:
                        return c.Format("assist.next.shred", _g.Files.GetFile(id)?.Name ?? id);
                    case TaskType.ReadEmail:
                    {
                        var mail = c.Email(id);
                        return mail != null ? c.Format("assist.next.mail", string.IsNullOrEmpty(mail.subject) ? "(no subject)" : mail.subject) : null;
                    }
                    default:
                        return null;
                }
            }
            return null;
        }

        void ShowOffer(string taskId, TaskAssist assist)
        {
            var c = _g.Content;
            var t = _g.Tasks.Get(taskId);
            if (t == null) { assist.Withdraw(); return; }
            bool reading = t.Type == TaskType.ReadEmail;
            string accept = c.Text(reading ? "assist.offer.mail.yes" : "assist.offer.yes");
            GameLog.Info(LogChannel.Task, "Offer " + assist.OffersMade + ": Night Operations can finish " + taskId + " (stuck " + assist.StuckSeconds.ToString("0") + " s active)");
            _offerBox = Dialogs.Message(_g, c.Text("assist.offer.title"), c.Format(reading ? "assist.offer.mail.body" : "assist.offer.body", t.Title), "icon_question",
                new[] { accept, c.Text("assist.offer.no") }, (result, by) =>
                {
                    _offerBox = null;
                    if (!assist.OfferOpen) return;
                    // Only the player's own answer finishes anything; a closed box (or another pointer's click) is "Not now".
                    if (result == accept && by != null && by.IsPlayer)
                    {
                        assist.Accept();
                        if (reading)
                        {
                            foreach (var mailId in t.Data.targets)
                                if (!_g.Mail.IsRead(mailId))
                                {
                                    _g.Mail.Deliver(mailId, false);
                                    (_g.Apps.Launch(AppIds.Mail, by) as MailApp)?.ShowMail(mailId, by);
                                    break;
                                }
                            _g.Tasks.Evaluate();
                            if (!_g.Tasks.IsCompleted(taskId)) assist.AcceptFailed();
                        }
                        else if (!FinishByNightOperations(taskId)) assist.AcceptFailed();
                    }
                    else
                    {
                        assist.Decline();
                        GameLog.Info(LogChannel.Task, "Offer for " + taskId + ": not now");
                    }
                }, 1);
        }

        /// <summary>The task ended (or the wait stopped) with the offer still up: it goes away unanswered.</summary>
        void CloseOffer(TaskAssist assist)
        {
            if (assist == null || !assist.OfferOpen) return;
            assist.Withdraw();
            if (_offerBox != null && _offerBox.Window != null && !_offerBox.Window.IsClosed) _offerBox.Window.Close(null, true);
            _offerBox = null;
        }

        /// <summary>A jump: an open offer goes with the beat that made it.</summary>
        void CloseOfferForJump()
        {
            if (_offerBox != null && _offerBox.Window != null && !_offerBox.Window.IsClosed) _offerBox.Window.Close(null, true);
            _offerBox = null;
            _taskAssists.Clear();
        }

        /// <summary>
        /// The player said "Finish it": every target that is not done is done through the world, credited to Night Operations, and the task is
        /// ticked by the counter like any other (never force-completed), so the queue line, Work Orders and the folders agree. False when the
        /// world would not take it (a locked folder): nothing is ticked and nothing is announced; the offer comes back later.
        /// </summary>
        protected bool FinishByNightOperations(string taskId)
        {
            var g = _g;
            var t = g.Tasks.Get(taskId);
            if (t == null || t.State != TaskState.Active) return false;
            const string by = TaskFinisher.NightOperations;
            g.Tasks.MarkFinishedBy(taskId, by);
            var moved = new List<string>();
            bool recoveredOrdinary = false;
            foreach (var step in TaskFinisher.Plan(g.Tasks, t, id => g.Content.Order(id)?.correct))
            {
                switch (step.Kind)
                {
                    case FinishKind.MoveFile:
                        if (g.Files.GetFile(step.Target)?.Shredded == true && g.Files.Restore(step.Target, ContentIds.FolderIntake, Actor.System))
                            recoveredOrdinary |= step.Target != ContentIds.File017 && step.Target != ContentIds.File209 && !t.IsEntityAuthored;
                        if (g.Files.Move(step.Target, step.Param, Actor.System, by)) moved.Add(g.Files.GetFile(step.Target).Name);
                        break;
                    case FinishKind.ShredFile:
                        // The credit first: the shred ticks the task.
                        g.Credits.Set(TaskType.DeleteFile, step.Target, by);
                        if (g.Files.Shred(step.Target, Actor.System)) g.Shred.MarkShredded();
                        break;
                    case FinishKind.DecideOrder:
                        g.Orders.SetHidden(step.Target, false);
                        g.Orders.Decide(step.Target, step.Param, null, by);
                        break;
                    case FinishKind.ReadMail:
                        g.Mail.Deliver(step.Target, false);
                        g.Mail.MarkRead(step.Target, null, by);
                        // The mail is put in front of the player (it stays marked as read by Night Operations).
                        (g.Apps.Launch(AppIds.Mail, g.Player) as MailApp)?.ShowMail(step.Target, null);
                        break;
                    case FinishKind.OpenFile:
                        g.Credits.Set(TaskType.OpenFile, step.Target, by);
                        g.Apps.OpenFile(step.Target, g.Player);
                        break;
                    case FinishKind.ViewEmployee:
                        g.Credits.Set(TaskType.ViewEmployee, step.Target, by);
                        (g.Apps.Launch(AppIds.Staff, g.Player) as StaffApp)?.ShowById(step.Target, g.Player);
                        break;
                }
                GameLog.Info(LogChannel.Task, "Night Operations: " + step);
            }
            g.Tasks.Evaluate();
            if (!g.Tasks.IsCompleted(taskId))
            {
                // Nothing the plan could do (a locked folder): the task is not ticked (a tick the world disagrees with is the 3317 bug).
                GameLog.Warn(LogChannel.Task, "Night Operations could not finish " + taskId + " through the world (" + t.ProgressText + ")");
                g.Tasks.MarkFinishedBy(taskId, null);
                return false;
            }
            var c = g.Content;
            string body = moved.Count > 0 && t.Type == TaskType.MoveFile
                ? c.Format("task.filed.rest", t.Title, string.Join(", ", moved), g.Files.GetFolder(t.Data.param)?.Name ?? t.Data.param)
                : c.Format("assist.done", t.Title);
            if (recoveredOrdinary) body += "\nNight Operations recovered the required work files from a backup before filing them.";
            g.Notifications.Show(c.Text("app.workqueue"), body, "icon_task_done", a => g.Apps.Launch(AppIds.WorkQueue, a), "ui_select");
            GameLog.Info(LogChannel.Task, "Finished by Night Operations: " + taskId + " (" + t.ProgressText + ")");
            return true;
        }

        /// <summary>Phase Q1: a task ticked while the world disagrees (the 3317 report) is logged as a warning, outside jumps and setup.</summary>
        void WatchTaskConsistency(WorkTask t)
        {
            if (IsPreparing || _g.Tasks.Agrees(t)) return;
            GameLog.Warn(LogChannel.Task, "Ticked but not done in the world: " + t.Id);
        }
    }
}
