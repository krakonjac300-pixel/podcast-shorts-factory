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

        /// <summary>Phase I: the notices stack above the Disposal bin, at the right edge; Approve and Reject must never sit under one.</summary>
        protected override bool AvoidsNotices => true;

        const int WindowW = 500, WindowH = 320;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            CreateWindow(G.Content.Text("app.workorders"), "icon_workorders", 120, 110, WindowW, WindowH, WindowFlags.Standard, zoomFrom);
            // Phase H: Personnel (opened next, to check the owner) must never land on Approve and Reject. Phase I: the buttons
            // are at the top of the form (a window that opens below cannot cover them), and the window keeps clear of notices.
            Window.KeepVisible = new Rect(WindowW - 190f, 24f, 186f, 30f);
            var client = Window.Client;

            _list = new ListView(client, "Orders", new[] { 60, 110 }, new[] { "Order", "Status" }, false);
            _list.Root.anchorMin = new Vector2(0f, 0f);
            _list.Root.anchorMax = new Vector2(0f, 1f);
            _list.Root.pivot = new Vector2(0f, 1f);
            _list.Root.offsetMin = new Vector2(2f, 2f);
            _list.Root.offsetMax = new Vector2(176f, -2f);
            _list.RowSelected += (row, a) =>
            {
                var order = (WorkOrderData)row.Tag;
                Show(order);
                G.Orders.NotifyViewed(order.id, a);
            };

            var paper = UIBuilder.Bevel(client, BevelStyle.Sunken, "Form");
            paper.rectTransform.Stretch(180, 32, 2, 2);
            _form = UIBuilder.Text(paper.rectTransform, "Select a work order.", Palette.Text);
            _form.Wrap = true;
            _form.rectTransform.Stretch(8, 8, 8, 8);
            _stamp = UIBuilder.Text(paper.rectTransform, "", Palette.Red, true);
            _stamp.Scale = 2;
            _stamp.rectTransform.BottomRight(8, 8, 150, 20);
            _stamp.Align = TextAlign.Right;

            _approve = UiButton.Create(client, "Approve", a => Decide("approve", a), "button:Approve");
            ((RectTransform)_approve.transform).TopRight(92, 4, 82, 22);
            _reject = UiButton.Create(client, "Reject", a => Decide("reject", a), "button:Reject");
            ((RectTransform)_reject.transform).TopRight(4, 4, 82, 22);
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
            // Phase L: an order decided either way says what the decision did (Refresh shows the order again after it is decided).
            string decision = G.Orders.DecisionFor(o.id);
            if (WorkOrderRules.IsChoice(o) && (decision == "approve" || decision == "reject"))
                sb.Append("\n\n").Append(G.Content.Format("workorder.result", WorkOrderRules.ResultFor(o, decision)));
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

    /// <summary>Help viewer.</summary>
    public sealed class HelpApp : App
    {
        public override string AppId => AppIds.Help;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            // Every mechanic of the three nights is listed here, so the text scrolls (wheel or the bar on the right).
            CreateWindow(G.Content.Text("app.help"), "icon_help", 230, 30, 500, 440, WindowFlags.Standard, zoomFrom);
            var frame = UIBuilder.Bevel(Window.Client, BevelStyle.Sunken, "Help Text");
            frame.rectTransform.Stretch(2, 2, 2, 2);
            _scroll = ScrollArea.Create(frame.rectTransform, "Help Scroll");
            ((RectTransform)_scroll.transform).Stretch(2, 2, 2, 2);
            // help.body carries its own "NEXUS OS 4.1 -- QUICK HELP" heading.
            _text = UIBuilder.Text(_scroll.Content, G.Content.Text("help.body"), Palette.Text);
            _text.Wrap = true;
            Window.Resized += _ => Layout();
            Layout();
        }

        ScrollArea _scroll;
        PixelText _text;
        int _scale = -1;

        void Layout()
        {
            _scale = Game.DisplaySettings.ReadingScale;
            _text.Scale = _scale;
            int width = Mathf.Max(80, Mathf.FloorToInt(_scroll.Viewport.rect.width) - 12);
            var size = PixelFont.Measure(_text.text, width, false, _scale);
            _text.rectTransform.At(6, 6, width, size.y + 4);
            _scroll.ContentHeight = size.y + 14;
        }

        public override void Tick(float dt)
        {
            if (_text != null && _scale != Game.DisplaySettings.ReadingScale) Layout();
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
