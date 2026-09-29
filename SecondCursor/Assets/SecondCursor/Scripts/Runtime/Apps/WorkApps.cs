using System.Text;
using SecondCursor.Core.Content;
using SecondCursor.Core.Tasks;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>Work Orders: drive-wipe requests to approve or reject after checking the Staff Directory.</summary>
    public sealed class WorkOrdersApp : App
    {
        ListView _list;
        PixelText _form;
        PixelText _stamp;
        UiButton _approve;
        UiButton _reject;
        WorkOrderData _shown;
        int _revision = -1;

        public override string AppId => AppIds.WorkOrders;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.workorders"), "icon_workorders", 120, 110, 500, 320, WindowFlags.Standard, zoomFrom);
            var client = Window.Client;

            _list = new ListView(client, "Orders", new[] { 60, 110 }, new[] { "Order", "Status" }, false);
            _list.Root.anchorMin = new Vector2(0f, 0f);
            _list.Root.anchorMax = new Vector2(0f, 1f);
            _list.Root.pivot = new Vector2(0f, 1f);
            _list.Root.offsetMin = new Vector2(2f, 2f);
            _list.Root.offsetMax = new Vector2(176f, -2f);
            _list.RowSelected += (row, a) => Show((WorkOrderData)row.Tag);

            var paper = UIBuilder.Bevel(client, BevelStyle.Sunken, "Form");
            paper.rectTransform.Stretch(180, 2, 2, 34);
            _form = UIBuilder.Text(paper.rectTransform, "Select a work order.", Palette.Text);
            _form.Wrap = true;
            _form.rectTransform.Stretch(8, 8, 8, 8);
            _stamp = UIBuilder.Text(paper.rectTransform, "", Palette.Red, true);
            _stamp.Scale = 2;
            _stamp.rectTransform.BottomRight(8, 8, 150, 20);
            _stamp.Align = TextAlign.Right;

            _approve = UiButton.Create(client, "Approve", a => Decide("approve", a), "button:Approve");
            ((RectTransform)_approve.transform).BottomRight(92, 6, 82, 22);
            _reject = UiButton.Create(client, "Reject", a => Decide("reject", a), "button:Reject");
            ((RectTransform)_reject.transform).BottomRight(4, 6, 82, 22);
            Refresh();
            if (_list.Rows.Count > 0)
            {
                // Open on the first undecided order.
                int idx = 0;
                for (int i = 0; i < _list.Rows.Count; i++)
                    if (G.Orders.DecisionFor(((WorkOrderData)_list.Rows[i].Tag).id) == null) { idx = i; break; }
                _list.Select(idx, by);
            }
        }

        void Refresh()
        {
            _revision = G.Orders.Revision;
            string sel = _shown?.id;
            _list.Clear();
            foreach (var o in G.Content.WorkOrders.orders)
            {
                if (o == null || G.Orders.IsHidden(o.id)) continue;
                string d = G.Orders.DecisionFor(o.id);
                _list.AddRow(null, o, "order:" + o.id, o.id.Replace("wo_", "WO-"), StatusText(d));
            }
            if (_shown != null && G.Orders.IsHidden(_shown.id)) _shown = null;
            if (sel != null) _list.SelectWhere(r => ((WorkOrderData)r.Tag).id == sel, null);
            UpdateButtons();
        }

        void Show(WorkOrderData o)
        {
            _shown = o;
            var sb = new StringBuilder();
            sb.Append(o.title).Append("\n\n");
            foreach (var f in o.fields) sb.Append(f.label).Append(": ").Append(f.value).Append('\n');
            if (!string.IsNullOrEmpty(o.instructions)) sb.Append('\n').Append(o.instructions);
            _form.text = sb.ToString();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            string d = _shown != null ? G.Orders.DecisionFor(_shown.id) : null;
            bool open = _shown != null && d == null;
            _approve.Enabled = open;
            _reject.Enabled = open;
            _stamp.text = d == null ? "" : StatusText(d).ToUpperInvariant();
            _stamp.color = d == "approve" ? Palette.Green : d == WorkOrderService.Cancelled ? Palette.Shadow : Palette.Red;
        }

        static string StatusText(string decision)
        {
            if (decision == null) return "Pending";
            if (decision == "approve") return "Approved";
            return decision == WorkOrderService.Cancelled ? "Cancelled" : "Rejected";
        }

        void Decide(string decision, CursorAgent a)
        {
            if (_shown == null) return;
            G.Orders.Decide(_shown.id, decision, a);
            Sfx.Play("ui_click", a);
        }

        public override void Tick(float dt)
        {
            if (_revision != G.Orders.Revision) Refresh();
        }
    }

    /// <summary>Work Queue: tonight's checklist with the current task's instructions and hint.</summary>
    public sealed class WorkQueueApp : App
    {
        RectTransform _listRoot;
        PixelText _detail;
        PixelText _header;
        int _revision = -1;

        public override string AppId => AppIds.WorkQueue;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.workqueue"), "icon_workorders", 690, 16, 262, 290, WindowFlags.Standard, zoomFrom);
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
            _detail.rectTransform.Stretch(6, 6, 6, 6);
            Refresh();
        }

        /// <summary>Rows that fit the list without scrolling.</summary>
        const int MaxRows = 7;

        void Refresh()
        {
            _revision = G.Tasks.Revision;
            for (int i = _listRoot.childCount - 1; i >= 0; i--) Object.Destroy(_listRoot.GetChild(i).gameObject);
            int y = 0;
            WorkTask current = null;
            foreach (var t in G.Tasks.Tasks)
                if (t.State == TaskState.Active) { current = t; break; }
            foreach (var t in VisibleTasks())
            {
                var row = UIBuilder.Rect("Task " + t.Id, _listRoot).TopStrip(y, 18);
                bool remote = t.IsEntityAuthored;
                if (remote && t.State != TaskState.Completed)
                {
                    // Written by the remote session: the entity's own inverted colours.
                    var band = UIBuilder.Solid(row, Palette.EntityFill, "Remote");
                    band.rectTransform.Stretch(18, 1, 0, 1);
                }
                string icon = t.State == TaskState.Completed ? "icon_task_done" : (t == current ? "icon_task_active" : "icon_task_pending");
                var ic = UIBuilder.Icon(row, icon, 1);
                ic.rectTransform.anchoredPosition = new Vector2(1f, -1f);
                string title = t.Title + (t.Goal > 1 && t.State != TaskState.Completed ? " (" + t.Progress + "/" + t.Goal + ")" : "");
                if (remote) title += " " + G.Content.Text("workqueue.remote");
                var color = t.State == TaskState.Completed ? Palette.TextDisabled : (remote ? Palette.EntityText : Palette.Text);
                var label = UIBuilder.Text(row, title, color, t == current);
                label.rectTransform.Stretch(20, 0, 2, 0);
                label.VAlign = TextVAlign.Middle;
                y += 18;
            }
            if (y == 0)
            {
                var none = UIBuilder.Text(_listRoot, G.Content.Text("workqueue.empty"), Palette.TextDisabled);
                none.rectTransform.TopStrip(4, 12, 4, 4);
            }
            if (current == null)
            {
                _detail.text = "";
                return;
            }
            string due = string.IsNullOrEmpty(current.Data.deadline) ? "" : G.Content.Format("workqueue.deadline", current.Data.deadline) + "\n";
            _detail.text = due + current.Data.description + (string.IsNullOrEmpty(current.Data.hint) ? "" : "\n\nHint: " + current.Data.hint);
        }

        /// <summary>Tasks shown in the list: given and not withdrawn, at most <see cref="MaxRows"/> (oldest done ones go first).</summary>
        System.Collections.Generic.List<WorkTask> VisibleTasks()
        {
            var list = new System.Collections.Generic.List<WorkTask>();
            foreach (var t in G.Tasks.Tasks)
                if (t.State == TaskState.Active || t.State == TaskState.Completed) list.Add(t);
            while (list.Count > MaxRows)
            {
                int done = list.FindIndex(t => t.State == TaskState.Completed);
                list.RemoveAt(done >= 0 ? done : 0);
            }
            return list;
        }

        public override void Tick(float dt)
        {
            if (_revision != G.Tasks.Revision) Refresh();
        }
    }

    /// <summary>Help viewer.</summary>
    public sealed class HelpApp : App
    {
        public override string AppId => AppIds.Help;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.help"), "icon_help", 270, 80, 440, 330, WindowFlags.Standard, zoomFrom);
            var frame = UIBuilder.Bevel(Window.Client, BevelStyle.Sunken, "Help Text");
            frame.rectTransform.Stretch(2, 2, 2, 2);
            // help.body carries its own "NEXUS OS 4.1 -- QUICK HELP" heading.
            var t = UIBuilder.Text(frame.rectTransform, G.Content.Text("help.body"), Palette.Text);
            t.Wrap = true;
            t.rectTransform.Stretch(8, 8, 8, 8);
        }
    }

    /// <summary>The Disposal bin's window: what was shredded this shift.</summary>
    public sealed class DisposalApp : App
    {
        ListView _list;
        int _revision = -1;

        public override string AppId => AppIds.Disposal;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.disposal"), "icon_disposal_empty", 520, 200, 330, 220, WindowFlags.Standard, zoomFrom);
            var note = UIBuilder.Text(Window.Client, "Drag files here to shred them permanently.", Palette.Text);
            note.rectTransform.TopStrip(4, 12, 6, 4);
            _list = new ListView(Window.Client, "Shredded", new[] { 200, 90 }, new[] { "Shredded this shift", "Size" }, true);
            _list.Root.Stretch(2, 20, 2, 2);
            var bg = _list.Scroll.Viewport.GetComponent<Interactable>();
            bg.AcceptsDrop = (a, p) => p.Kind == PayloadKind.File && G.Files.Exists(p.FileId);
            bg.Drop += (a, p) => G.Shred.Request(p.FileId, a);
            Refresh();
        }

        void Refresh()
        {
            _revision = G.Files.Revision;
            _list.Clear();
            foreach (var f in G.Files.AllFiles)
                if (f.Shredded && !G.Shred.IsPurged(f.Id)) _list.AddRow("icon_file_corrupt", f.Id, "shredded:" + f.Id, f.Name, f.Size);
            Window.SetIcon(G.Shred.AnyShredded ? "icon_disposal_full" : "icon_disposal_empty");
        }

        public override void Tick(float dt)
        {
            if (_revision != G.Files.Revision) Refresh();
        }
    }
}
