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
        PixelText _label;
        RectTransform _arrow;
        int _width, _right, _bottom;
        float _factor = -1f;
        float _strip = 16f;
        /// <summary>Phase Q4 (R7): while the chip shows, a strip at the bottom of the area is reserved for it, so it never lies over the last visible line.</summary>
        bool _reserved;

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
            watcher._label = label;
            watcher._arrow = arrow.rectTransform;
            watcher._width = width;
            watcher._right = right;
            watcher._bottom = bottom;
            return watcher;
        }

        void Update()
        {
            if (_area == null || _chip == null) return;
            float factor = Game.DisplaySettings.ReadingFactor;
            if (_factor != factor)
            {
                _factor = factor;
                _strip = Mathf.Ceil(16f * factor);
                _chip.BottomRight(_right, _bottom, Mathf.Ceil(_width * factor), Mathf.Ceil(14f * factor));
                _label.Factor = factor;
                _arrow.anchoredPosition = new Vector2(4f, -Mathf.Floor((_chip.rect.height - 8f) / 2f));
                if (_reserved) _area.Viewport.offsetMin = new Vector2(_area.Viewport.offsetMin.x, _strip);
            }
            // The end is judged against the whole viewport (with the strip given back), so reserving the strip cannot flip the answer.
            float full = _area.Viewport.rect.height + (_reserved ? _strip : 0f);
            float fullMax = Mathf.Max(0f, _area.ContentHeight - full);
            bool more = fullMax > 2f && _area.Offset < fullMax - 2f;
            if (more != _reserved)
            {
                _reserved = more;
                _area.Viewport.offsetMin = new Vector2(_area.Viewport.offsetMin.x, more ? _strip : 0f);
            }
            if (_chip.gameObject.activeSelf != more) _chip.gameObject.SetActive(more);
        }
    }
}
