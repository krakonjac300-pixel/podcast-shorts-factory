using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.UI
{
    /// <summary>
    /// A small "More below" button at the bottom of a scroll area's frame (Mail's reading pane, the Work Queue's detail
    /// pane). It shows while the text goes on under the fold and hides once the end is in view. Clicking it scrolls one
    /// page (Phase I: it used to be a label, and a click on it did nothing).
    /// </summary>
    public sealed class MoreBelow : MonoBehaviour
    {
        ScrollArea _area;
        RectTransform _chip;

        /// <param name="frame">The frame around the scroll area (the chip sits at its bottom right, left of the scroll bar).</param>
        /// <param name="elementId">Logical id for the pointer system and the test bridge, e.g. "morebelow:mail".</param>
        public static MoreBelow Create(RectTransform frame, ScrollArea area, string text, string elementId, int right = 20, int bottom = 3, int width = 96)
        {
            var chip = UIBuilder.Rect("More Below", frame).BottomRight(right, bottom, width, 14);
            var face = chip.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Raised;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;
            var arrow = UIBuilder.Icon(chip, "glyph_arrow_down", 1);
            arrow.rectTransform.anchoredPosition = new Vector2(4f, -3f);
            var label = UIBuilder.Text(chip, text, Palette.Text, true);
            label.rectTransform.Stretch(16, 1, 2, 1);
            label.VAlign = TextVAlign.Middle;
            var hit = UIBuilder.Hit(chip.gameObject, elementId, CursorShape.Hand);
            hit.Click += (a, n) => area.ScrollBy(Mathf.Max(24f, area.ViewportHeight - area.LineStep * 2f));
            chip.gameObject.SetActive(false);

            // The watcher lives on the frame (always active), because a hidden chip cannot update itself.
            var watcher = frame.gameObject.AddComponent<MoreBelow>();
            watcher._area = area;
            watcher._chip = chip;
            return watcher;
        }

        /// <summary>True while the chip is showing.</summary>
        public bool IsShowing => _chip != null && _chip.gameObject.activeSelf;

        void Update()
        {
            if (_area == null || _chip == null) return;
            bool more = _area.MaxOffset > 2f && _area.Offset < _area.MaxOffset - 2f;
            if (_chip.gameObject.activeSelf != more) _chip.gameObject.SetActive(more);
        }
    }
}
