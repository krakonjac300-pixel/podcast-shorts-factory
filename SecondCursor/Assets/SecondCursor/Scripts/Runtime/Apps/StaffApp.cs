using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Core.Story;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Apps
{
    /// <summary>
    /// Staff Directory: employee list and an ID card with a (procedural) photo. Restricted records show
    /// "ACCESS RESTRICTED" until the story opens them. Used to verify work orders.
    /// </summary>
    public sealed class StaffApp : App
    {
        ListView _list;
        RectTransform _card;
        RawImage _photo;
        PixelText _photoCaption;
        PixelText _fields;
        PixelText _status;
        ReadingPane _notes;
        ReadingPane _fieldsPane;
        RectTransform _fieldsFrame, _photoFrame;
        float _factor = -1f;
        EmployeeData _shown;
        Texture2D _photoTex;
        float _staticTimer;
        UiButton _backToOrder;
        string _returnOrder;

        public void ShowForOrder(string employeeId, string orderId, CursorAgent by)
        {
            _returnOrder = orderId;
            ShowById(employeeId, by);
            if (_backToOrder != null) return;
            _backToOrder = UiButton.Create(Window.Client, "Back to work order", a =>
            {
                var orders = G.Apps.Launch(AppIds.WorkOrders, a) as WorkOrdersApp;
                orders?.ShowOrder(_returnOrder, a);
            }, "staff.backtoorder");
            ((RectTransform)_backToOrder.transform).TopRight(4, 3, Mathf.Ceil(154f * Game.DisplaySettings.ReadingFactor), 24);
            _card.Stretch(ListWidth + 4, 32, 2, 2);
            LayoutCard();
        }

        public override string AppId => AppIds.Staff;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            // Phase Q4 (A5): the record's text follows the Reading text size; the window opens larger with Medium and Large.
            float factor = Game.DisplaySettings.ReadingFactor;
            int w = factor >= 2f ? 860 : factor > 1f ? 700 : 540, h = factor >= 2f ? 510 : factor > 1f ? 440 : 340;
            CreateWindow(G.Content.Text("app.staff"), "icon_staff", 250, 60, w, h, WindowFlags.Standard, zoomFrom);
            var client = Window.Client;

            _list = new ListView(client, "Employees", new[] { 50, 150 }, new[] { "No.", "Name" }, false);
            _list.Root.anchorMin = new Vector2(0f, 0f);
            _list.Root.anchorMax = new Vector2(0f, 1f);
            _list.Root.pivot = new Vector2(0f, 1f);
            _list.Root.offsetMin = new Vector2(2f, 2f);
            _list.Root.offsetMax = new Vector2(212f, -2f);
            _list.RowSelected += (row, a) => Show((EmployeeData)row.Tag, a);

            var employees = new List<EmployeeData>(G.Content.Employees.employees);
            employees.Sort((x, y) => string.CompareOrdinal(x.number.PadLeft(6, '0'), y.number.PadLeft(6, '0')));
            foreach (var e in employees)
            {
                if (e == null) continue;
                _list.AddRow(null, e, "employee:" + e.id, e.number, e.restricted && !G.Flags.Has(Flags.Staff017Revealed) ? "[RESTRICTED]" : e.name);
            }

            var cardFrame = UIBuilder.Bevel(client, BevelStyle.Sunken, "Card");
            _card = cardFrame.rectTransform;
            _card.Stretch(216, 2, 2, 2);

            var photoFrame = UIBuilder.Bevel(_card, BevelStyle.Sunken, "Photo Frame");
            _photoFrame = photoFrame.rectTransform;
            photoFrame.rectTransform.At(10, 10, 68, 84);
            _photoTex = OwnedAssets.Own(Window.gameObject,
                new Texture2D(64, 80, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            _photo = UIBuilder.Raw(photoFrame.rectTransform, _photoTex, "Photo");
            _photo.rectTransform.Stretch(2, 2, 2, 2);
            _photoCaption = UIBuilder.Text(photoFrame.rectTransform, "", Palette.BiosBright, true);
            _photoCaption.rectTransform.Stretch(2, 2, 2, 2);
            _photoCaption.Align = TextAlign.Center;
            _photoCaption.VAlign = TextVAlign.Middle;

            _fieldsFrame = UIBuilder.Rect("Employee Fields", _card);
            _fieldsPane = ReadingPane.Create(_fieldsFrame, "Employee Fields Scroll", Palette.Text);
            _fields = _fieldsPane.Text;
            _fields.text = "Select an employee.";
            _status = UIBuilder.Text(_card, "", Palette.Text, true);
            _status.Wrap = true;
            var notesFrame = UIBuilder.Rect("Notes", _card);
            _notes = ReadingPane.Create(notesFrame, "Notes Scroll", Palette.Text);
            _notesFrame = notesFrame;
            LayoutCard();
            Window.Resized += _ => LayoutCard();
            DrawPhoto("none");
        }

        RectTransform _notesFrame;
        float ListWidth => Mathf.Round(Mathf.Min(210f * Mathf.Sqrt(Game.DisplaySettings.ReadingFactor), Window.Size.x * 0.34f));

        /// <summary>
        /// Keep status and notes available at every reading size. Long fields scroll separately, and a narrow card gives text the photo's room.
        /// </summary>
        void LayoutCard()
        {
            if (_card == null) return;
            float f = Game.DisplaySettings.ReadingFactor;
            _factor = f;
            _list.Root.offsetMax = new Vector2(ListWidth + 2f, -2f);
            _card.Stretch(ListWidth + 6f, _backToOrder != null ? 32 : 2, 2, 2);
            if (_backToOrder != null)
                ((RectTransform)_backToOrder.transform).TopRight(4, 3, Mathf.Ceil(154f * f), 24);
            Canvas.ForceUpdateCanvases();
            float cardW = _card.rect.width > 1f ? _card.rect.width : 322f;
            bool compact = cardW < 320f;
            _photoFrame.gameObject.SetActive(!compact);
            float left = compact ? 6f : 88f;
            float fieldsW = Mathf.Max(60f, cardW - left - 8f);
            float statusH = Mathf.Max(12f * f, PixelFont.Measure(_status.text, Mathf.FloorToInt(fieldsW), true, f).y + 4f);
            float wantedFieldsH = Mathf.Max(Mathf.Round(110f * f), PixelFont.Measure(_fields.text, Mathf.FloorToInt(fieldsW) - 28, false, f).y + 16f);
            float fieldsH = Mathf.Clamp(wantedFieldsH, 40f, Mathf.Max(40f, _card.rect.height - statusH - 100f));
            float statusY = 10f + fieldsH + 4f, notesTop = statusY + statusH + 8f;
            _fields.Factor = f;
            _status.Factor = f;
            _fieldsFrame.At(left, 10, fieldsW, fieldsH);
            _fieldsPane.SetText(_fields.text, false);
            _status.rectTransform.At(left, statusY, fieldsW, statusH);
            _notesFrame.Stretch(6, notesTop - 6f, 6, 4);
        }

        public void Show(EmployeeData e, CursorAgent by)
        {
            if (e == null) return;
            _shown = e;
            _fieldsPane.Scroll.ScrollTo(0f);
            bool locked = e.restricted && !G.Flags.Has(Flags.Staff017Revealed);
            if (locked)
            {
                _fields.text = "Employee No.  " + e.number + "\n\nACCESS RESTRICTED\nClearance level 3 required.";
                _status.text = "";
                _notes.SetText("", true);
                LayoutCard();
                DrawPhoto("redacted");
                return;
            }
            // Phase K: Custodial's office field follows it around the building, so it says so ("Office: B-7" read as a home office).
            string office = e.id == ContentIds.Employee000 ? "\nLocation now: " : "\nOffice:      ";
            _fields.text = "Name:        " + e.name + "\nEmployee No. " + e.number + "\nDepartment:  " + e.department + "\nPosition:    " + e.position +
                           office + e.office + "\nHired:       " + e.hired + "\nLast login:  " + e.lastLogin + "\nSupervisor:  " + e.supervisor;
            _status.text = "Status: " + e.status;
            _status.color = StatusColor(e.status);
            LayoutCard();
            _notes.SetText(string.IsNullOrEmpty(e.notes) ? "" : "Notes:\n" + e.notes, true);
            DrawPhoto(e.photo);
            if (e.id == ContentIds.Employee017) G.Flags.Increment("viewed:employee017");
            if (by != null && by.IsPlayer)
            {
                // Tasks such as "look up 163" count only records the player looked at themselves.
                G.Flags.Increment(Flags.ViewedByPlayerPrefix + e.id);
                G.Tasks.Evaluate();
            }
        }

        public void ShowById(string employeeId, CursorAgent by)
        {
            _list.SelectWhere(r => r.Tag is EmployeeData d && d.id == employeeId, by);
        }

        /// <summary>Records changed in memory (Night 3's live Personnel): show them again as they are now.</summary>
        public void Refresh() => RefreshNames();

        /// <summary>The record on the card, or null.</summary>
        public EmployeeData Shown => _shown;

        public void RefreshNames()
        {
            foreach (var r in _list.Rows)
            {
                if (!(r.Tag is EmployeeData e) || r.Columns.Count < 2) continue;
                r.Columns[1].text = e.restricted && !G.Flags.Has(Flags.Staff017Revealed) ? "[RESTRICTED]" : e.name;
            }
            _list.Layout(true);
            if (_shown != null) Show(_shown, null);
        }

        static Color32 StatusColor(string status)
        {
            switch ((status ?? "").ToUpperInvariant())
            {
                case "ACTIVE": return Palette.Green;
                case "TERMINATED": return Palette.Red;
                case "ON LEAVE": return Palette.Amber;
                case "DECEASED": return Palette.Dark;
                case "RETAINED": return Palette.Dark;
                default: return Palette.Shadow;
            }
        }

        void DrawPhoto(string kind)
        {
            _photoCaption.text = "";
            var px = new Color32[64 * 80];
            var bg = new Color32(0x9A, 0xA3, 0xA8, 255);
            switch (kind)
            {
                case "silhouette":
                    for (int y = 0; y < 80; y++)
                        for (int x = 0; x < 64; x++)
                        {
                            float dx = x - 31.5f, head = (dx * dx) / (11f * 11f) + ((y - 50f) * (y - 50f)) / (14f * 14f);
                            float sh = y < 30 ? (dx * dx) / (26f * 26f) + ((y - 4f) * (y - 4f)) / (26f * 26f) : 2f;
                            bool ink = head < 1f || sh < 1f;
                            byte n = (byte)(Random.Range(0, 10));
                            px[y * 64 + x] = ink ? new Color32((byte)(22 + n), (byte)(24 + n), (byte)(26 + n), 255) : new Color32((byte)(bg.r - n), (byte)(bg.g - n), (byte)(bg.b - n), 255);
                        }
                    break;
                case "redacted":
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(12, 12, 12, 255);
                    _photoCaption.text = "N/A";
                    break;
                case "static":
                    for (int i = 0; i < px.Length; i++) { byte v = (byte)Random.Range(40, 220); px[i] = new Color32(v, v, v, 255); }
                    break;
                default:
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(0xC2, 0xBF, 0xB2, 255);
                    _photoCaption.text = "NO\nPHOTO";
                    _photoCaption.color = Palette.Shadow;
                    break;
            }
            if (kind == "redacted") _photoCaption.color = Palette.BiosBright;
            _photoTex.SetPixels32(px);
            _photoTex.Apply(false, false);
        }

        public override void Tick(float dt)
        {
            if (_factor != Game.DisplaySettings.ReadingFactor) LayoutCard();
            _notes.Tick();
            _fieldsPane.Tick();
            if (_shown == null || _shown.photo != "static" || (_shown.restricted && !G.Flags.Has(Flags.Staff017Revealed))) return;
            _staticTimer -= dt;
            if (_staticTimer > 0f) return;
            _staticTimer = 0.08f;
            DrawPhoto("static");
        }
    }
}
