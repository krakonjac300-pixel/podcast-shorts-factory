using System;
using System.Collections.Generic;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.UI
{
    /// <summary>
    /// Sunken, scrollable, single-selection list with optional column header. Rows can be clicked,
    /// double-clicked, right-clicked and dragged, by either cursor.
    /// </summary>
    public sealed class ListView
    {
        public sealed class Row
        {
            public int Index;
            public RectTransform Rect;
            public Image Background;
            public Image Icon;
            public readonly List<PixelText> Columns = new List<PixelText>();
            public Interactable Hit;
            public object Tag;
            public bool Bold;
            /// <summary>Phase Q4 (R7): the whole text of each column and the pixels it may use (0 = the last column, which takes the rest), so a long text ends in "..." before the next column.</summary>
            public string[] Full;
            public int[] Room;
            public string[] Displayed;
        }

        public readonly RectTransform Root;
        public readonly ScrollArea Scroll;
        public int RowHeight = 16;
        public readonly int[] ColumnWidths;
        readonly List<Row> _rows = new List<Row>();
        int _headerHeight;
        readonly List<RectTransform> _headerRects = new List<RectTransform>();
        readonly List<PixelText> _headerTexts = new List<PixelText>();
        float _factor = -1f, _width = -1f;
        int _selected = -1;

        public event Action<Row, CursorAgent> RowSelected;
        public event Action<Row, CursorAgent> RowActivated;
        public event Action<Row, CursorAgent> RowRightClicked;
        public event Action<Row, CursorAgent> RowDragBegin;

        public IReadOnlyList<Row> Rows => _rows;
        public Row Selected => _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;
        public bool HasIcons;

        public ListView(Transform parent, string name, int[] columnWidths, string[] headers = null, bool icons = false)
        {
            HasIcons = icons;
            ColumnWidths = columnWidths ?? new[] { 200 };
            var frame = UIBuilder.Bevel(parent, BevelStyle.Sunken, name);
            Root = frame.rectTransform;
            _headerHeight = headers != null ? 16 : 0;
            if (headers != null)
            {
                int x = 2;
                for (int i = 0; i < headers.Length && i < ColumnWidths.Length; i++)
                {
                    bool last = i == headers.Length - 1;
                    var hb = UIBuilder.Bevel(Root, BevelStyle.Raised, "Header " + headers[i]);
                    if (last) hb.rectTransform.TopStrip(2, 16, x, 2);
                    else hb.rectTransform.At(x, 2, ColumnWidths[i], 16);
                    var ht = UIBuilder.Text(hb.rectTransform, headers[i], Palette.Text);
                    ht.rectTransform.Stretch(4, 0, 2, 0);
                    ht.VAlign = TextVAlign.Middle;
                    _headerRects.Add(hb.rectTransform);
                    _headerTexts.Add(ht);
                    x += ColumnWidths[i];
                }
            }
            Scroll = ScrollArea.Create(Root, name + " Scroll");
            ((RectTransform)Scroll.transform).Stretch(2, 2 + _headerHeight, 2, 2);
            Scroll.LineStep = RowHeight;
            Scroll.WheelStep = RowHeight * 3;
            var layout = Root.gameObject.AddComponent<ListViewLayout>();
            layout.View = this;
            Layout(true);
        }

        public Row AddRow(string icon, object tag, string elementId, params string[] columns)
        {
            var row = new Row { Index = _rows.Count, Tag = tag };
            var rt = UIBuilder.Rect("Row " + row.Index, Scroll.Content).TopStrip(row.Index * RowHeight, RowHeight);
            row.Rect = rt;
            row.Background = UIBuilder.Solid(rt, Palette.Selection, "Selection");
            row.Background.rectTransform.Stretch();
            row.Background.enabled = false;

            int x = 2;
            if (HasIcons)
            {
                if (!string.IsNullOrEmpty(icon))
                {
                    row.Icon = UIBuilder.Icon(rt, icon, 1);
                    row.Icon.rectTransform.anchoredPosition = new Vector2(2f, 0f);
                }
                x = 21;
            }
            row.Full = (string[])columns.Clone();
            row.Room = new int[columns.Length];
            row.Displayed = new string[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                var t = UIBuilder.Text(rt, columns[i], Palette.Text);
                int w = i < ColumnWidths.Length ? ColumnWidths[i] : 100;
                if (i == 0) w -= x - 2;
                bool last = i == columns.Length - 1;
                if (last) t.rectTransform.Stretch(x, 0, 2, 0);
                else
                {
                    t.rectTransform.At(x, 0, w - 4, RowHeight);
                    row.Room[i] = Mathf.Max(8, w - 8);
                    t.text = Ellipsize(columns[i], row.Room[i], false);
                }
                t.VAlign = TextVAlign.Middle;
                row.Columns.Add(t);
                x += w;
            }

            row.Hit = UIBuilder.Hit(rt.gameObject, elementId);
            row.Hit.Tag = row;
            row.Hit.Click += (a, n) =>
            {
                Select(row.Index, a);
                if (ClickRules.Opens(n, a)) RowActivated?.Invoke(row, a);
            };
            row.Hit.RightClick += a =>
            {
                Select(row.Index, a);
                RowRightClicked?.Invoke(row, a);
            };
            row.Hit.DragBegin += a =>
            {
                Select(row.Index, a);
                RowDragBegin?.Invoke(row, a);
            };
            _rows.Add(row);
            Layout(true);
            return row;
        }

        /// <summary>Reflow headers, row hit areas and ellipses together when the reading size or window width changes.</summary>
        public void Layout(bool force = false)
        {
            float factor = Game.DisplaySettings.ReadingFactor;
            float width = Scroll.Viewport.rect.width;
            if (width < 1f)
            {
                width = 0f;
                foreach (int column in ColumnWidths) width += column;
            }
            if (!force && factor == _factor && Mathf.Approximately(width, _width)) return;
            float oldRowHeight = RowHeight;
            float rowAtTop = Scroll.Offset / Mathf.Max(1f, oldRowHeight);
            _factor = factor;
            _width = width;
            RowHeight = Mathf.CeilToInt(16f * factor);
            _headerHeight = _headerRects.Count > 0 ? RowHeight : 0;
            ((RectTransform)Scroll.transform).Stretch(2, 2 + _headerHeight, 2, 2);
            Scroll.LineStep = RowHeight;
            Scroll.WheelStep = RowHeight * 3;
            int total = 0;
            foreach (int column in ColumnWidths) total += column;
            float ratio = Mathf.Max(1f, width - 4f) / Mathf.Max(1, total);
            int x = 2;
            for (int i = 0; i < _headerRects.Count; i++)
            {
                int w = Mathf.Max(12, Mathf.RoundToInt(ColumnWidths[i] * ratio));
                if (i == _headerRects.Count - 1) _headerRects[i].TopStrip(2, _headerHeight, x, 2);
                else _headerRects[i].At(x, 2, w, _headerHeight);
                _headerTexts[i].Factor = factor;
                x += w;
            }
            foreach (var row in _rows)
            {
                row.Rect.TopStrip(row.Index * RowHeight, RowHeight);
                if (row.Icon != null) row.Icon.rectTransform.anchoredPosition = new Vector2(2f, -Mathf.Floor((RowHeight - 16f) / 2f));
                int columnX = 2;
                for (int i = 0; i < row.Columns.Count; i++)
                {
                    var text = row.Columns[i];
                    // Some live monitor columns update their text directly. Keep that value on the next reflow.
                    if (row.Displayed[i] != null && text.text != row.Displayed[i]) row.Full[i] = text.text;
                    int cellW = Mathf.Max(12, Mathf.RoundToInt((i < ColumnWidths.Length ? ColumnWidths[i] : 100) * ratio));
                    int inset = i == 0 && HasIcons ? 19 : 0;
                    int left = columnX + inset;
                    int room = i == row.Columns.Count - 1 ? Mathf.FloorToInt(width) - left - 4 : cellW - inset - 8;
                    row.Room[i] = Mathf.Max(4, room);
                    if (i == row.Columns.Count - 1) text.rectTransform.Stretch(left, 0, 2, 0);
                    else text.rectTransform.At(left, 0, Mathf.Max(4, cellW - inset - 4), RowHeight);
                    text.Factor = factor;
                    text.text = Ellipsize(row.Full[i], row.Room[i], row.Bold, factor);
                    row.Displayed[i] = text.text;
                    columnX += cellW;
                }
            }
            Scroll.ContentHeight = _rows.Count * RowHeight;
            if (oldRowHeight != RowHeight) Scroll.ScrollTo(rowAtTop * RowHeight);
        }

        /// <summary>Phase Q4 (A7): opens a row as a double-click would (the Enter key).</summary>
        public void Activate(Row row, CursorAgent by)
        {
            if (row != null) RowActivated?.Invoke(row, by);
        }

        public void SetDraggable(bool draggable)
        {
            foreach (var r in _rows) r.Hit.draggable = draggable;
        }

        public void SetBold(Row row, bool bold)
        {
            row.Bold = bold;
            for (int i = 0; i < row.Columns.Count; i++)
            {
                row.Columns[i].Bold = bold;
                // Bold is wider: the text is cut again to the room its column has.
                if (row.Full != null && i < row.Full.Length && row.Room[i] > 0)
                {
                    row.Columns[i].text = Ellipsize(row.Full[i], row.Room[i], bold, Game.DisplaySettings.ReadingFactor);
                    row.Displayed[i] = row.Columns[i].text;
                }
            }
        }

        /// <summary>Phase Q4 (R7): <paramref name="text"/> cut to <paramref name="room"/> px with "..." (unchanged when it fits).</summary>
        static string Ellipsize(string text, int room, bool bold, float factor = 1f)
        {
            if (string.IsNullOrEmpty(text) || PixelFont.MeasureLine(text, bold, factor) <= room) return text;
            if (PixelFont.MeasureLine("...", bold, factor) > room) return "";
            string s = text;
            while (s.Length > 1 && PixelFont.MeasureLine(s + "...", bold, factor) > room) s = s.Substring(0, s.Length - 1);
            return s.TrimEnd() + "...";
        }

        public void Clear()
        {
            foreach (var r in _rows)
            {
                if (r.Rect == null) continue;
                r.Rect.gameObject.SetActive(false); // unregister now; Destroy is deferred to end of frame
                UnityEngine.Object.Destroy(r.Rect.gameObject);
            }
            _rows.Clear();
            _selected = -1;
            Scroll.ContentHeight = 0;
        }

        public void Select(int index, CursorAgent by)
        {
            if (index == _selected)
            {
                if (index >= 0 && index < _rows.Count) RowSelected?.Invoke(_rows[index], by);
                return;
            }
            if (_selected >= 0 && _selected < _rows.Count) Paint(_rows[_selected], false);
            _selected = index;
            if (index >= 0 && index < _rows.Count)
            {
                Paint(_rows[index], true);
                Scroll.Reveal(index * RowHeight, RowHeight);
                RowSelected?.Invoke(_rows[index], by);
            }
        }

        public void SelectWhere(Func<Row, bool> predicate, CursorAgent by)
        {
            for (int i = 0; i < _rows.Count; i++)
                if (predicate(_rows[i])) { Select(i, by); return; }
        }

        static void Paint(Row r, bool selected)
        {
            if (r.Rect == null) return;
            r.Background.enabled = selected;
            foreach (var c in r.Columns) c.color = selected ? Palette.SelectionText : Palette.Text;
        }
    }

    sealed class ListViewLayout : MonoBehaviour
    {
        [NonSerialized]
        public ListView View;
        void LateUpdate() => View?.Layout();
    }
}
