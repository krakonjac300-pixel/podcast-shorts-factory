using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.UI
{
    /// <summary>
    /// Phase Q4 (review board A5): a scrolling block of wrapped text that follows the Reading text size (Normal, Medium, Large) and its own
    /// width. It is what the information windows use for their details (Work Orders' form, Personnel's notes), so the puzzle text reads at
    /// the size the player picked and never runs off the end of the pane.
    /// </summary>
    public sealed class ReadingPane
    {
        public ScrollArea Scroll { get; private set; }
        public PixelText Text { get; private set; }
        float _factor = -1f;
        float _width = -1f;

        /// <summary>Makes the pane inside <paramref name="frame"/> (the scroll area fills it, less a 2 px margin).</summary>
        public static ReadingPane Create(RectTransform frame, string name, Color32 color)
        {
            var pane = new ReadingPane();
            pane.Scroll = ScrollArea.Create(frame, name);
            ((RectTransform)pane.Scroll.transform).Stretch(2, 2, 2, 2);
            pane.Text = UIBuilder.Text(pane.Scroll.Content, "", color);
            pane.Text.Wrap = true;
            return pane;
        }

        /// <summary>Shows <paramref name="text"/>; <paramref name="toTop"/> starts at its beginning (a different document), else the reader keeps their place.</summary>
        public void SetText(string text, bool toTop)
        {
            Text.text = text ?? "";
            Layout();
            if (toTop) Scroll.ScrollTo(0f);
        }

        /// <summary>True when the layout was redone (the size option or the pane's width changed).</summary>
        public bool Tick()
        {
            float width = Scroll.Viewport.rect.width;
            if (_factor == Game.DisplaySettings.ReadingFactor && Mathf.Approximately(width, _width)) return false;
            Layout();
            return true;
        }

        void Layout()
        {
            Canvas.ForceUpdateCanvases();
            _factor = Game.DisplaySettings.ReadingFactor;
            Text.Factor = _factor;
            _width = Scroll.Viewport.rect.width;
            int width = Mathf.Max(60, Mathf.FloorToInt(_width) - 12);
            var size = PixelFont.Measure(Text.text, width, false, _factor);
            Text.rectTransform.At(6, 6, width, size.y + 4);
            Scroll.ContentHeight = size.y + 14;
        }
    }
}
