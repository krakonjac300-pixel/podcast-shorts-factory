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
    /// Phase I: a long task title wraps to a second line instead of running off the edge; the instructions and the hint
    /// scroll (with a "More below" button) instead of being cut off; the queue says so when it is clear; and a remote
    /// request that ran out stays listed as expired.
    /// Phase J: clicking a line shows that task's instructions and hint (the tester could not read the hint of a second task).
    /// </summary>
    public sealed class WorkQueueApp : App
    {
        RectTransform _listRoot;
        /// <summary>The line the player clicked, and the current task it was clicked under (a new current task clears it).</summary>
        string _selectedId, _selectedUnder;
        /// <summary>M9: the remote rows' band and label, faded as their time runs out.</summary>
        readonly Dictionary<string, (UnityEngine.UI.Image band, PixelText label)> _remoteRows =
            new Dictionary<string, (UnityEngine.UI.Image band, PixelText label)>();
        PixelText _detail;
        PixelText _header;
        ScrollArea _detailScroll;
        string _detailTaskId;
        int _revision = -1, _mailRevision = -1, _clockMinute = -1;

        public override string AppId => AppIds.WorkQueue;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            // Large reading text (Steam Deck) doubles the task description and hint, so the window opens wider.
            float scale = Game.DisplaySettings.ReadingFactor;
            // Phase Q1: Medium text gets a window between the two.
            int w = scale >= 2f ? 420 : scale > 1f ? 340 : 262, h = scale >= 2f ? 420 : scale > 1f ? 350 : 290;
            CreateWindow(G.Content.Text("app.workqueue"), "icon_workorders", ScreenRig.Width - w - 8, 16, w, h, WindowFlags.Standard, zoomFrom);
            // Phase H: it holds the player's instructions, so windows opened later (the Camera Viewer at 3:00) avoid it.
            Window.CoverCost = 2.5f;
            var client = Window.Client;
            _header = UIBuilder.Text(client, G.Content.Text("workqueue.header"), Palette.Text, true);
            _header.rectTransform.TopStrip(4, 12, 6, 4);
            var frame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Tasks");
            frame.rectTransform.TopStrip(20, ListHeight + 6, 2, 2);
            _listRoot = UIBuilder.Rect("Rows", frame.rectTransform).Stretch(3, 3, 3, 3);
            UIBuilder.Clip(_listRoot);
            var detailFrame = UIBuilder.Bevel(client, BevelStyle.StatusField, "Detail");
            detailFrame.rectTransform.Stretch(2, 30 + ListHeight, 2, 2);
            // Phase I: the instructions and the hint scroll (a long hint used to be cut off at the bottom of the pane).
            _detailScroll = ScrollArea.Create(detailFrame.rectTransform, "Detail Scroll");
            ((RectTransform)_detailScroll.transform).Stretch(2, 2, 2, 2);
            _detail = UIBuilder.Text(_detailScroll.Content, "", Palette.Text);
            _detail.Wrap = true;
            _detail.Factor = scale;
            MoreBelow.Create(detailFrame.rectTransform, _detailScroll, G.Content.Text("mail.more", "More below"), "morebelow:workqueue");
            // Phase L: the list's titles and the instructions follow the window's width when it is resized or snapped to a half.
            Window.Resized += _ => _resized = true;
            Refresh();
        }

        /// <summary>Height of the task list's inner area (px).</summary>
        const int ListHeight = 122;
        /// <summary>One line of a row, and the extra a second line adds.</summary>
        const int RowH = 18, LineH = 12;
        const int TextLeft = 20;

        WorkTask Current
        {
            get
            {
                foreach (var t in G.Tasks.Tasks)
                    if (t.State == TaskState.Active) return t;
                return null;
            }
        }

        /// <summary>The task whose instructions show: the clicked line while the current task is the same, else the current task.</summary>
        WorkTask Shown()
        {
            var current = Current;
            if (_selectedId != null && current?.Id != _selectedUnder) _selectedId = null;
            return _selectedId != null ? G.Tasks.Get(_selectedId) ?? current : current;
        }

        float ListWidth => _listRoot != null && _listRoot.rect.width > 1f ? _listRoot.rect.width : Window.Size.x - 14f;

        /// <summary>Text width of a row (px): the list less the icon column and a right margin.</summary>
        int TextWidth => Mathf.Max(60, Mathf.FloorToInt(ListWidth) - TextLeft - 4);

        /// <summary>The title as shown, and how many lines (1 or 2) it needs; a title of three lines or more is ended with "...".</summary>
        (string text, int lines) FitTitle(string title, bool bold)
        {
            int width = TextWidth;
            if (PixelFont.MeasureLine(title, bold, 1) <= width) return (title, 1);
            if (PixelFont.Measure(title, width, bold, 1).y <= LineH * 2 + 1) return (title, 2);
            string s = title;
            while (s.Length > 1 && PixelFont.Measure(s + "...", width, bold, 1).y > LineH * 2 + 1) s = s.Substring(0, s.Length - 1);
            return (s.TrimEnd() + "...", 2);
        }

        static int HeightFor(int lines) => RowH + (lines - 1) * LineH;

        void Refresh()
        {
            _revision = G.Tasks.Revision;
            _mailRevision = G.Mail != null ? G.Mail.Revision : -1;
            for (int i = _listRoot.childCount - 1; i >= 0; i--) Object.Destroy(_listRoot.GetChild(i).gameObject);
            _remoteRows.Clear();
            var current = Current;
            var shown = Shown();   // first: a new current task clears the clicked line before the frame is drawn
            string mail = G.Mail != null ? G.Mail.NewestUnreadLive : null;
            // Nothing left to do: say so under the ticked rows (Phase I: the queue used to just stop).
            bool clear = current == null && HasAnyRow();
            int budget = ListHeight + 4 - (mail != null ? RowH : 0) - (clear ? RowH : 0);
            var rows = FitRows(budget, current);

            int y = 0;
            foreach (var (t, lines) in rows)
            {
                AddTaskRow(t, t == current, y, lines);
                y += HeightFor(lines);
            }
            if (rows.Count == 0 && !clear)
            {
                var none = UIBuilder.Text(_listRoot, G.Content.Text("workqueue.empty"), Palette.TextDisabled);
                none.rectTransform.TopStrip(4, 12, 4, 4);
                y = RowH;
            }
            if (clear)
            {
                var line = UIBuilder.Text(_listRoot, G.Content.Text("workqueue.empty"), Palette.TextDisabled);
                line.rectTransform.TopStrip(y + 4, 12, 4, 4);
                y += RowH;
            }
            if (mail != null) AddMailRow(mail, y);
            RefreshDetail(shown, false);
        }

        bool HasAnyRow()
        {
            foreach (var t in G.Tasks.Tasks)
                if (t.State == TaskState.Completed || WorkTaskManager.IsListedWithdrawn(t)) return true;
            return false;
        }

        /// <summary>
        /// The rows shown, in list order: given and not withdrawn (or withdrawn with a reason after the player saw them) that fit
        /// <paramref name="budget"/> px; the oldest finished or struck-through ones go first.
        /// </summary>
        List<(WorkTask task, int lines)> FitRows(int budget, WorkTask current)
        {
            var list = new List<(WorkTask task, int lines)>();
            foreach (var t in G.Tasks.Tasks)
            {
                if (t.State != TaskState.Active && t.State != TaskState.Completed && !WorkTaskManager.IsListedWithdrawn(t)) continue;
                bool withdrawn = WorkTaskManager.IsListedWithdrawn(t);
                // Struck-through rows keep one line: the reason sits at their right end. Phase Q1: a ticked row that names who did the work
                // may take a second line, so the name is never cut off.
                bool credited = t.State == TaskState.Completed && t.CreditNote != null;
                int lines = withdrawn || (t.State == TaskState.Completed && !credited) ? 1 : FitTitle(FullTitle(t), t == current).lines;
                list.Add((t, lines));
            }
            int Total()
            {
                int sum = 0;
                foreach (var r in list) sum += HeightFor(r.lines);
                return sum;
            }
            while (list.Count > 1 && Total() > budget)
            {
                int done = list.FindIndex(r => r.task.State != TaskState.Active);
                list.RemoveAt(done >= 0 ? done : 0);
            }
            return list;
        }

        string FullTitle(WorkTask t)
        {
            string title = t.State == TaskState.Completed && !string.IsNullOrEmpty(t.ResultNote) ? t.ResultNote
                : t.Title + (t.Goal > 1 && t.State == TaskState.Active ? " (" + t.ProgressText + ")" : "");
            if (t.IsEntityAuthored) title += " " + G.Content.Text("workqueue.remote");
            // Phase Q1: work someone else did stays marked as theirs on the ticked line ("(finished by Night Operations)").
            string credit = t.State == TaskState.Completed ? t.CreditNote : null;
            if (credit != null) title += " (" + credit + ")";
            return title;
        }

        void AddTaskRow(WorkTask t, bool isCurrent, int y, int lines)
        {
            int rowH = HeightFor(lines);
            var row = UIBuilder.Rect("Task " + t.Id, _listRoot).TopStrip(y, rowH);
            bool remote = t.IsEntityAuthored;
            bool withdrawn = WorkTaskManager.IsListedWithdrawn(t);
            string id = t.Id;
            UIBuilder.Hit(row.gameObject, "workqueue:task:" + id, CursorShape.Hand).Click += (a, n) =>
            {
                if (a == null || !a.IsPlayer) return;
                _selectedId = id;
                _selectedUnder = Current?.Id;
                Refresh();
            };
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
            string title = FullTitle(t);
            var color = t.State != TaskState.Active ? Palette.TextDisabled : (remote ? Palette.EntityText : Palette.Text);
            var label = UIBuilder.Text(row, lines > 1 ? FitTitle(title, isCurrent).text : title, color, isCurrent);
            label.VAlign = TextVAlign.Middle;
            if (lines > 1) label.Wrap = true;
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
                float listW = ListWidth;
                label.text = Ellipsize(title, Mathf.Max(20, Mathf.FloorToInt(listW) - 20 - right), false);
                int width = Mathf.Min(PixelFont.MeasureLine(label.text, false, 1), Mathf.Max(20, Mathf.FloorToInt(listW) - 20 - right));
                var strike = UIBuilder.Solid(row, Palette.TextDisabled, "Strike");
                strike.rectTransform.At(20, RowH / 2, width, 1);
            }
            label.rectTransform.Stretch(TextLeft, 0, right, 0);
            if (band != null) _remoteRows[t.Id] = (band, label);
            if (id == _selectedId)
            {
                // The clicked line is framed (a remote line's dark band would hide a background).
                UIBuilder.Solid(row, Palette.Selection, "Selected Top").rectTransform.TopStrip(0, 1, 18, 0);
                UIBuilder.Solid(row, Palette.Selection, "Selected Bottom").rectTransform.BottomStrip(0, 1, 18, 0);
            }
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
            text = Ellipsize(text, Mathf.FloorToInt(ListWidth) - 24, true);
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

        /// <param name="toTop">A different task: the pane starts at its top (a countdown tick keeps the reader's place).</param>
        void RefreshDetail(WorkTask current, bool toTop)
        {
            _clockMinute = G.Clock.TotalMinutes;
            string text = "";
            if (current != null)
            {
                string deadline = current.Data.deadline;
                string due = "";
                if (!string.IsNullOrEmpty(deadline) && current.State == TaskState.Active)
                {
                    int left = TaskDeadline.MinutesLeft(deadline, G.Clock.TotalMinutes);
                    float real = TaskDeadline.RealSeconds(deadline, G.Clock.ExactMinutes, G.Clock.Rate, G.Clock.Frozen);
                    // Phase K: the shift clock runs fast, so the countdown also says how long that is in real time.
                    due = (left < 0 ? G.Content.Format("workqueue.deadline", deadline)
                        : real >= 0f ? G.Content.Format("workqueue.deadline.real", deadline, left, TaskDeadline.Approx(real))
                        : G.Content.Format("workqueue.deadline.left", deadline, left)) + "\n";
                }
                text = due + current.Description + Checklist(current) + (string.IsNullOrEmpty(current.Hint) ? "" : "\n\nHint: " + current.Hint);
            }
            string id = current?.Id;
            if (id != _detailTaskId) { toTop = true; _detailTaskId = id; }
            _detail.text = text;
            LayoutDetail();
            FitKeyTask(current);
            if (toTop) _detailScroll.ScrollTo(0f);
        }

        /// <summary>Where the Disposal bin's icon starts (desktop px, y down): a grown Work Queue above it stops short of it.</summary>
        static readonly Rect BinArea = new Rect(ScreenRig.Width - 92f, ScreenRig.Height - OS.WindowManager.TaskbarHeight - 88f, 92f, 88f);
        /// <summary>Where the window was and how big before a key task grew it (null = not grown), and its rect as grown.</summary>
        Rect? _before;
        Rect _grownTo;
        /// <summary>The width a key task may widen the window to (leftwards) when the bin stops it growing down (the Deck's width).</summary>
        const float KeyWidth = 420f;

        /// <summary>
        /// Phase N (fifth blind playtest, finding 10): the instructions of a PRIORITY task, a timed task or a Wait line (the rounds) are
        /// shown whole: the window grows down to fit them (never over the Disposal bin), then wider to the left if that is not enough, and
        /// gets its place back when an ordinary task follows. The tester never saw the end of the rounds hint behind "More below".
        /// </summary>
        void FitKeyTask(WorkTask t)
        {
            var w = Window;
            if (w.IsMaximized || w.IsSnapped || w.IsMinimized || w.DraggedBy != null) return;
            bool key = t != null && t.State == TaskState.Active
                       && (!string.IsNullOrEmpty(t.Data.deadline) || t.Type == TaskType.Wait || t.Title.StartsWith("PRIORITY", System.StringComparison.Ordinal));
            if (!key)
            {
                if (_before.HasValue && new Rect(w.TopLeft, w.Size) == _grownTo)
                {
                    w.SetSize(_before.Value.size);
                    w.MoveTo(_before.Value.position);
                }
                _before = null;
                w.KeepNoticesOff = false;
                return;
            }
            // The notices stack elsewhere while the whole hint is up (they would sit on its end).
            w.KeepNoticesOff = _before.HasValue;
            float need = w.Size.y + _detailScroll.ContentHeight - _detailScroll.Viewport.rect.height;
            var r = new Rect(w.TopLeft, w.Size);
            float bottom = r.xMax > BinArea.xMin && r.xMin < BinArea.xMax ? BinArea.yMin - 4f : ScreenRig.Height - OS.WindowManager.TaskbarHeight - 4f;
            float h = Mathf.Min(need, bottom - r.y);
            bool wider = need > h + 1f && r.width < KeyWidth && r.xMax - KeyWidth >= OS.WindowManager.IconColumnRight;
            if (h <= r.height + 1f && !wider) return;
            if (!_before.HasValue) _before = r;
            // Wider first: the text reflows, and the next pass sets the height it then needs.
            if (wider)
            {
                w.SetSize(new Vector2(KeyWidth, r.height));
                w.MoveTo(new Vector2(r.xMax - KeyWidth, r.y));
            }
            else w.SetSize(new Vector2(r.width, Mathf.Round(h)));
            _grownTo = new Rect(w.TopLeft, w.Size);
            w.KeepNoticesOff = true;
        }

        /// <summary>
        /// Phase K: a task with several files or orders lists each one and where it is now ("[x] batch46_b.dat: in Archive (session
        /// 017)", "[ ] batch46_d.dat: on the Desktop"), so the count can always be checked against the folders.
        /// </summary>
        string Checklist(WorkTask t)
        {
            var targets = t.Data.targets;
            bool files = t.Type == TaskType.MoveFile, orders = t.Type == TaskType.DecideOrder;
            if ((!files && !orders) || targets.Length < (files ? 1 : 2) || t.IsEntityAuthored) return "";
            var c = G.Content;
            var sb = new System.Text.StringBuilder("\n\n").Append(c.Text(files ? "workqueue.check.files" : "workqueue.check.orders"));
            foreach (var id in targets)
            {
                sb.Append('\n');
                if (files)
                {
                    var f = G.Files.GetFile(id);
                    string folder = f == null ? null : f.Shredded ? "" : f.FolderId;
                    bool done = folder == t.Data.param;
                    string name = f != null ? f.Name : id;
                    string original = OriginalName(id);
                    if (original != null && original != name) name += " (" + original + ")";
                    string where = folder == null ? c.Text("workqueue.check.missing") : folder.Length == 0 ? c.Text("workqueue.check.shredded")
                        : c.Format(folder == ContentIds.FolderDesktop ? "workqueue.check.desktop" : "workqueue.check.in", G.Files.GetFolder(folder)?.Name ?? folder);
                    if (done && !string.IsNullOrEmpty(f.MovedBy)) where += " (" + f.MovedBy + ")";
                    sb.Append(done ? "[x] " : "[ ] ").Append(name).Append(": ").Append(where);
                }
                else
                {
                    string d = G.Orders.DecisionFor(id);
                    sb.Append(d != null ? "[x] " : "[ ] ").Append(id.Replace("wo_", "WO-")).Append(": ")
                      .Append(d == null ? c.Text("workqueue.check.pending") : d == "approve" ? c.Text("workqueue.check.approved")
                          : d == WorkOrderService.Cancelled ? c.Text("workqueue.check.cancelled") : c.Text("workqueue.check.rejected"));
                    string by = d != null ? G.Orders.DecidedBy(id) : null;
                    if (!string.IsNullOrEmpty(by)) sb.Append(" (").Append(by).Append(')');
                    if (t.TargetNotes.TryGetValue(id, out var note)) sb.Append("\n    ").Append(note);
                }
            }
            return sb.ToString();
        }

        string OriginalName(string fileId)
        {
            foreach (var f in G.Content.FileSystem.files)
                if (f != null && f.id == fileId) return f.name;
            return null;
        }

        void LayoutDetail()
        {
            float scale = Game.DisplaySettings.ReadingFactor;
            _detail.Factor = scale;
            Canvas.ForceUpdateCanvases();
            int width = Mathf.Max(60, Mathf.FloorToInt(_detailScroll.Viewport.rect.width) - 10);
            var size = PixelFont.Measure(_detail.text, width, false, scale);
            _detail.rectTransform.At(5, 4, width, size.y + 4);
            _detailScroll.ContentHeight = size.y + 12;
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
        int _worldRevision = -1;

        int WorldRevision() => G.Files.Revision * 31 + (G.Orders != null ? G.Orders.Revision : 0);

        /// <summary>The window was resized since the last frame (the grip fires on every pointer move).</summary>
        bool _resized;

        public override void Tick(float dt)
        {
            if (_resized)
            {
                _resized = false;
                Canvas.ForceUpdateCanvases();
                Refresh();
            }
            else if (_revision != G.Tasks.Revision || (G.Mail != null && _mailRevision != G.Mail.Revision)) Refresh();
            else if (_clockMinute != G.Clock.TotalMinutes || _worldRevision != WorldRevision())
            {
                // The countdown ticks with the clock; the checklist follows files and orders that moved without changing the count.
                var current = Shown();
                _worldRevision = WorldRevision();
                if (current != null && (!string.IsNullOrEmpty(current.Data.deadline) || current.Type == TaskType.MoveFile || current.Type == TaskType.DecideOrder))
                    RefreshDetail(current, false);
                else _clockMinute = G.Clock.TotalMinutes;
            }
            FadeRemoteRows();
            float scale = Game.DisplaySettings.ReadingFactor;
            if (_detail != null && _detail.Factor != scale) LayoutDetail();
        }
    }
}
