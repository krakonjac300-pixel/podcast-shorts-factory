using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Core.Tasks;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// Work Queue: tonight's checklist with the current task's instructions and hint. Phase H: a task taken back after
    /// the player saw it stays listed, struck through, with the reason ("missed 3:00 AM"); a finished task can show what
    /// it filed; the due time counts down; and the newest unread mail of the shift gets a line of its own until it is read.
    /// </summary>
    public sealed class WorkQueueApp : App
    {
        RectTransform _listRoot;
        /// <summary>M9: the remote rows' band and label, faded as their time runs out.</summary>
        readonly Dictionary<string, (UnityEngine.UI.Image band, PixelText label)> _remoteRows =
            new Dictionary<string, (UnityEngine.UI.Image band, PixelText label)>();
        PixelText _detail;
        PixelText _header;
        int _revision = -1, _mailRevision = -1, _clockMinute = -1;

        public override string AppId => AppIds.WorkQueue;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            // Large reading text (Steam Deck) doubles the task description and hint, so the window opens wider.
            int scale = Game.DisplaySettings.ReadingScale;
            int w = scale > 1 ? 420 : 262, h = scale > 1 ? 420 : 290;
            CreateWindow(G.Content.Text("app.workqueue"), "icon_workorders", ScreenRig.Width - w - 8, 16, w, h, WindowFlags.Standard, zoomFrom);
            // Phase H: it holds the player's instructions, so windows opened later (the Camera Viewer at 3:00) avoid it.
            Window.CoverCost = 2.5f;
            var client = Window.Client;
            _header = UIBuilder.Text(client, G.Content.Text("workqueue.header"), Palette.Text, true);
            _header.rectTransform.TopStrip(4, 12, 6, 4);
            var frame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Tasks");
            frame.rectTransform.TopStrip(20, 128, 2, 2);
            _listRoot = UIBuilder.Rect("Rows", frame.rectTransform).Stretch(3, 3, 3, 3);
            UIBuilder.Clip(_listRoot);
            var detailFrame = UIBuilder.Bevel(client, BevelStyle.StatusField, "Detail");
            detailFrame.rectTransform.Stretch(2, 152, 2, 2);
            _detail = UIBuilder.Text(detailFrame.rectTransform, "", Palette.Text);
            _detail.Wrap = true;
            _detail.Scale = scale;
            _detail.rectTransform.Stretch(6, 6, 6, 6);
            Refresh();
        }

        /// <summary>Rows that fit the list without scrolling.</summary>
        const int MaxRows = 7;
        const int RowH = 18;

        WorkTask Current
        {
            get
            {
                foreach (var t in G.Tasks.Tasks)
                    if (t.State == TaskState.Active) return t;
                return null;
            }
        }

        void Refresh()
        {
            _revision = G.Tasks.Revision;
            _mailRevision = G.Mail != null ? G.Mail.Revision : -1;
            for (int i = _listRoot.childCount - 1; i >= 0; i--) Object.Destroy(_listRoot.GetChild(i).gameObject);
            _remoteRows.Clear();
            int y = 0;
            var current = Current;
            string mail = G.Mail != null ? G.Mail.NewestUnreadLive : null;
            foreach (var t in VisibleTasks(mail != null ? MaxRows - 1 : MaxRows))
            {
                AddTaskRow(t, t == current, y);
                y += RowH;
            }
            if (y == 0)
            {
                var none = UIBuilder.Text(_listRoot, G.Content.Text("workqueue.empty"), Palette.TextDisabled);
                none.rectTransform.TopStrip(4, 12, 4, 4);
                y = RowH;
            }
            if (mail != null) AddMailRow(mail, y);
            RefreshDetail(current);
        }

        void AddTaskRow(WorkTask t, bool isCurrent, int y)
        {
            var row = UIBuilder.Rect("Task " + t.Id, _listRoot).TopStrip(y, RowH);
            bool remote = t.IsEntityAuthored;
            bool withdrawn = WorkTaskManager.IsListedWithdrawn(t);
            UnityEngine.UI.Image band = null;
            if (remote && t.State == TaskState.Active)
            {
                // Written by the remote session: the entity's own inverted colours.
                band = UIBuilder.Solid(row, Palette.EntityFill, "Remote");
                band.rectTransform.Stretch(18, 1, 0, 1);
            }
            string icon = t.State == TaskState.Completed ? "icon_task_done" : (isCurrent ? "icon_task_active" : "icon_task_pending");
            var ic = UIBuilder.Icon(row, icon, 1);
            ic.rectTransform.anchoredPosition = new Vector2(1f, -1f);
            string title = t.State == TaskState.Completed && !string.IsNullOrEmpty(t.ResultNote) ? t.ResultNote
                : t.Title + (t.Goal > 1 && t.State == TaskState.Active ? " (" + t.Progress + "/" + t.Goal + ")" : "");
            if (remote) title += " " + G.Content.Text("workqueue.remote");
            var color = t.State != TaskState.Active ? Palette.TextDisabled : (remote ? Palette.EntityText : Palette.Text);
            var label = UIBuilder.Text(row, title, color, isCurrent);
            label.VAlign = TextVAlign.Middle;
            int right = 2;
            if (withdrawn)
            {
                // Taken back: the line stays, struck through, and says why in red at its right end.
                string note = t.WithdrawNote.ToUpperInvariant();
                int noteW = PixelFont.MeasureLine(note, true, 1);
                var why = UIBuilder.Text(row, note, Palette.Red, true, "Why");
                why.rectTransform.Stretch(0, 0, 2, 0);
                why.Align = TextAlign.Right;
                why.VAlign = TextVAlign.Middle;
                right = noteW + 8;
                float listW = _listRoot.rect.width > 1f ? _listRoot.rect.width : Window.Size.x - 14f;
                label.text = Ellipsize(title, Mathf.Max(20, Mathf.FloorToInt(listW) - 20 - right), false);
                int width = Mathf.Min(PixelFont.MeasureLine(label.text, false, 1), Mathf.Max(20, Mathf.FloorToInt(listW) - 20 - right));
                var strike = UIBuilder.Solid(row, Palette.TextDisabled, "Strike");
                strike.rectTransform.At(20, RowH / 2, width, 1);
            }
            label.rectTransform.Stretch(20, 0, right, 0);
            if (band != null) _remoteRows[t.Id] = (band, label);
        }

        void AddMailRow(string mailId, int y)
        {
            var mail = G.Content.Email(mailId);
            if (mail == null) return;
            var row = UIBuilder.Rect("New Mail", _listRoot).TopStrip(y, RowH);
            var ic = UIBuilder.Icon(row, "icon_mail_unread", 1);
            ic.rectTransform.anchoredPosition = new Vector2(1f, -1f);
            string subject = string.IsNullOrEmpty(mail.subject) ? "(no subject)" : mail.subject;
            int count = G.Mail.UnreadLiveCount;
            string text = count > 1 ? G.Content.Format("workqueue.mail.more", subject, count) : G.Content.Format("workqueue.mail", subject);
            float listW = _listRoot.rect.width > 1f ? _listRoot.rect.width : Window.Size.x - 14f;
            text = Ellipsize(text, Mathf.FloorToInt(listW) - 24, true);
            var label = UIBuilder.Text(row, text, Palette.Link, true);
            label.rectTransform.Stretch(20, 0, 2, 0);
            label.VAlign = TextVAlign.Middle;
            var hit = UIBuilder.Hit(row.gameObject, "workqueue:mail", CursorShape.Hand);
            string id = mailId;
            hit.Click += (a, n) =>
            {
                var app = G.Apps.Launch(AppIds.Mail, a) as MailApp;
                app?.ShowMail(id, a);
            };
        }

        static string Ellipsize(string s, int width, bool bold)
        {
            if (PixelFont.MeasureLine(s, bold, 1) <= width) return s;
            while (s.Length > 1 && PixelFont.MeasureLine(s + "...", bold, 1) > width) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "...";
        }

        void RefreshDetail(WorkTask current)
        {
            _clockMinute = G.Clock.TotalMinutes;
            if (current == null)
            {
                _detail.text = "";
                return;
            }
            string deadline = current.Data.deadline;
            string due = "";
            if (!string.IsNullOrEmpty(deadline))
            {
                int left = TaskDeadline.MinutesLeft(deadline, G.Clock.TotalMinutes);
                due = (left >= 0 ? G.Content.Format("workqueue.deadline.left", deadline, left) : G.Content.Format("workqueue.deadline", deadline)) + "\n";
            }
            _detail.text = due + current.Data.description + (string.IsNullOrEmpty(current.Data.hint) ? "" : "\n\nHint: " + current.Data.hint);
        }

        /// <summary>M9: every 15 s of a remote item's life takes 10% off its row; the last 3 s it blinks.</summary>
        void FadeRemoteRows()
        {
            foreach (var kv in _remoteRows)
            {
                if (!G.RemoteTaskLife.TryGetValue(kv.Key, out var life) || life.y <= 0f) continue;
                float elapsed = Time.time - life.x, left = life.y - elapsed;
                float alpha = Mathf.Clamp01(1f - 0.10f * Mathf.Floor(Mathf.Max(0f, elapsed) / RemoteFadeStep));
                if (left < RemoteBlinkSeconds && (Time.time * 4f) % 1f < 0.5f) alpha = 0f;
                var (band, label) = kv.Value;
                if (band != null) band.color = new Color(band.color.r, band.color.g, band.color.b, alpha);
                if (label != null) label.color = new Color(label.color.r, label.color.g, label.color.b, Mathf.Max(alpha, 0.15f));
            }
        }

        const float RemoteFadeStep = 15f, RemoteBlinkSeconds = 3f;

        /// <summary>
        /// Tasks shown in the list: given and not withdrawn (or withdrawn with a reason after the player saw them), at most
        /// <paramref name="max"/> (the oldest finished or struck-through ones go first).
        /// </summary>
        List<WorkTask> VisibleTasks(int max)
        {
            var list = new List<WorkTask>();
            foreach (var t in G.Tasks.Tasks)
                if (t.State == TaskState.Active || t.State == TaskState.Completed || WorkTaskManager.IsListedWithdrawn(t)) list.Add(t);
            while (list.Count > max)
            {
                int done = list.FindIndex(t => t.State != TaskState.Active);
                list.RemoveAt(done >= 0 ? done : 0);
            }
            return list;
        }

        public override void Tick(float dt)
        {
            if (_revision != G.Tasks.Revision || (G.Mail != null && _mailRevision != G.Mail.Revision)) Refresh();
            else if (_clockMinute != G.Clock.TotalMinutes)
            {
                var current = Current;
                if (current != null && !string.IsNullOrEmpty(current.Data.deadline)) RefreshDetail(current);
                else _clockMinute = G.Clock.TotalMinutes;
            }
            FadeRemoteRows();
            int scale = Game.DisplaySettings.ReadingScale;
            if (_detail != null && _detail.Scale != scale) _detail.Scale = scale;
        }
    }
}
