using System;
using System.Collections.Generic;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.UI
{
    public sealed class MenuItem
    {
        public string Label;
        public string Icon;
        public bool Enabled = true;
        public bool Separator;
        public bool Bold;
        public Action<CursorAgent> Action;
        public string ElementId;

        public static MenuItem Sep() => new MenuItem { Separator = true };

        public static MenuItem Of(string label, Action<CursorAgent> action, string icon = null, bool enabled = true, string elementId = null) =>
            new MenuItem { Label = label, Action = action, Icon = icon, Enabled = enabled, ElementId = elementId };
    }

    /// <summary>
    /// Popup / context menu (also the body of the start menu). Rows highlight under whichever cursor
    /// hovers them. Closes itself after a pick; the OS closes it on any click elsewhere.
    /// </summary>
    public sealed class PopupMenu : MonoBehaviour
    {
        const int RowHeight = 18;
        const int SepHeight = 8;
        int _rowHeight = RowHeight;
        float _factor = 1f;

        public event Action Closed;
        public RectTransform Rect { get; private set; }
        readonly List<Interactable> _rows = new List<Interactable>();

        static readonly List<PopupMenu> Open = new List<PopupMenu>();

        /// <summary>When true, the owner (e.g. the start menu) handles outside clicks itself.</summary>
        public bool ManagedExternally;

        /// <summary>Close every unmanaged popup when a press lands outside it. Wire to PointerRouter.AnyPointerDown.</summary>
        public static void HandlePointerDown(CursorAgent agent, Interactable hit)
        {
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                var m = Open[i];
                if (m == null) { Open.RemoveAt(i); continue; }
                if (m.ManagedExternally) continue;
                if (hit != null && hit.transform.IsChildOf(m.transform)) continue;
                m.Close();
            }
        }

        public static void CloseAll()
        {
            for (int i = Open.Count - 1; i >= 0; i--) if (Open[i] != null) Open[i].Close();
            Open.Clear();
        }

        void OnDestroy() => Open.Remove(this);

        public static PopupMenu Show(RectTransform layer, Vector2 topLeft, IList<MenuItem> items, int width = 150, int leftGutter = 0,
            int maxHeight = ScreenRig.Height)
        {
            int rowCount = 0, separators = 0;
            foreach (var it in items) { if (it.Separator) separators++; else rowCount++; }
            float factor = Game.DisplaySettings.ReadingFactor;
            // Long menus use the largest readable half step that keeps every choice on the desktop.
            int availableHeight = Mathf.Clamp(maxHeight, RowHeight + 4, ScreenRig.Height);
            while (factor > 1f && 4 + rowCount * Mathf.CeilToInt(RowHeight * factor) + separators * SepHeight > availableHeight)
                factor -= 0.5f;
            int rowHeight = Mathf.CeilToInt(RowHeight * factor);
            int h = 4;
            foreach (var it in items)
            {
                h += it.Separator ? SepHeight : rowHeight;
                if (!it.Separator) width = Mathf.Max(width, PixelFont.MeasureLine(it.Label, it.Bold, factor) + leftGutter + 34);
            }
            width = Mathf.Min(width, ScreenRig.Width);

            var rt = UIBuilder.Rect("Popup Menu", layer);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, h);
            // Keep on screen.
            float x = Mathf.Clamp(topLeft.x, 0f, ScreenRig.Width - width);
            float y = Mathf.Clamp(topLeft.y, h, ScreenRig.Height);
            rt.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));

            var menu = rt.gameObject.AddComponent<PopupMenu>();
            menu.Rect = rt;
            menu._rowHeight = rowHeight;
            menu._factor = factor;
            Open.Add(menu);
            var frame = rt.gameObject.AddComponent<BevelGraphic>();
            frame.Style = BevelStyle.Window;
            frame.raycastTarget = false;
            var block = UIBuilder.Hit(rt.gameObject, "menu");
            block.Click += (a, n) => { };

            int yy = 2;
            foreach (var item in items)
            {
                if (item.Separator)
                {
                    var line = UIBuilder.Bevel(rt, BevelStyle.Etched, "Separator");
                    line.rectTransform.TopStrip(yy + 3, 2, 3 + leftGutter, 3);
                    yy += SepHeight;
                    continue;
                }
                menu.AddRow(rt, item, yy, leftGutter);
                yy += rowHeight;
            }
            return menu;
        }

        void AddRow(RectTransform parent, MenuItem item, int y, int leftGutter)
        {
            var row = UIBuilder.Rect("Item " + item.Label, parent).TopStrip(y, _rowHeight, 3 + leftGutter, 3);
            var bg = UIBuilder.Solid(row, Palette.Selection, "Highlight");
            bg.rectTransform.Stretch();
            bg.enabled = false;
            int textX = 4;
            Image icon = null;
            if (!string.IsNullOrEmpty(item.Icon))
            {
                icon = UIBuilder.Icon(row, item.Icon, 1);
                icon.rectTransform.anchoredPosition = new Vector2(2f, -Mathf.Floor((_rowHeight - 16f) / 2f));
                textX = 22;
            }
            var label = UIBuilder.Text(row, item.Label, item.Enabled ? Palette.Text : Palette.TextDisabled, item.Bold);
            label.Factor = _factor;
            label.rectTransform.Stretch(textX, 0, 2, 0);
            label.VAlign = TextVAlign.Middle;

            var hit = UIBuilder.Hit(row.gameObject, item.ElementId ?? "menu:" + item.Label);
            hit.interactable = true;
            hit.HoverEnter += a =>
            {
                if (!item.Enabled) return;
                bg.enabled = true;
                label.color = Palette.SelectionText;
            };
            hit.HoverExit += a =>
            {
                if (hit.IsHovered) return;
                bg.enabled = false;
                label.color = item.Enabled ? Palette.Text : Palette.TextDisabled;
            };
            hit.Click += (a, n) =>
            {
                if (!item.Enabled) return;
                Sfx.Play("ui_click", a);
                Close();
                item.Action?.Invoke(a);
            };
            _rows.Add(hit);
        }

        public Interactable FindRow(string label)
        {
            foreach (var r in _rows)
                if (r != null && r.name == "Item " + label) return r;
            return null;
        }

        public void Close()
        {
            if (this == null || !gameObject.activeSelf) return;
            Closed?.Invoke();
            Closed = null;
            gameObject.SetActive(false); // no second click on a dying menu
            Destroy(gameObject);
        }
    }
}
