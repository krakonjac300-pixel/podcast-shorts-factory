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
        }

        public readonly RectTransform Root;
        public readonly ScrollArea Scroll;
        public int RowHeight = 16;
        public readonly int[] ColumnWidths;
        readonly List<Row> _rows = new List<Row>();
        readonly int _headerHeight;
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
                    x += ColumnWidths[i];
                }
            }
            Scroll = ScrollArea.Create(Root, name + " Scroll");
            ((RectTransform)Scroll.transform).Stretch(2, 2 + _headerHeight, 2, 2);
            Scroll.LineStep = RowHeight;
            Scroll.WheelStep = RowHeight * 3;
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
            for (int i = 0; i < columns.Length; i++)
            {
                var t = UIBuilder.Text(rt, columns[i], Palette.Text);
                int w = i < ColumnWidths.Length ? ColumnWidths[i] : 100;
                if (i == 0) w -= x - 2;
                bool last = i == columns.Length - 1;
                if (last) t.rectTransform.Stretch(x, 0, 2, 0);
                else t.rectTransform.At(x, 0, w - 4, RowHeight);
                t.VAlign = TextVAlign.Middle;
                row.Columns.Add(t);
                x += w;
            }

            row.Hit = UIBuilder.Hit(rt.gameObject, elementId);
            row.Hit.Tag = row;
            row.Hit.Click += (a, n) =>
            {
                Select(row.Index, a);
                if (n == 2) RowActivated?.Invoke(row, a);
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
            Scroll.ContentHeight = _rows.Count * RowHeight;
            return row;
        }

        public void SetDraggable(bool draggable)
        {
            foreach (var r in _rows) r.Hit.draggable = draggable;
        }

        public void SetBold(Row row, bool bold)
        {
            row.Bold = bold;
            foreach (var c in row.Columns) c.Bold = bold;
        }

        public void Clear()
        {
            foreach (var r in _rows) if (r.Rect != null) UnityEngine.Object.Destroy(r.Rect.gameObject);
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
}
