using SecondCursor.Core.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Apps
{
    /// <summary>
    /// Phase Q4 (review board A3): Recent notices. A read-only window with the last twenty notices (newest first, with the shift clock's time),
    /// from the Nexus menu or the "+N more" chip, so a notice that was missed or went by during a scare can be read again. A notice from session
    /// 017 or 209 is marked in that session's colours. It follows the Reading text size.
    /// </summary>
    public sealed class RecentNoticesApp : App
    {
        public const string Id = "recent";
        const int Gap = 6;

        ScrollArea _scroll;
        PixelText _empty;
        int _revision = -1;
        float _factor = -1f;

        public override string AppId => Id;

        public override void Open(Rect? zoomFrom, CursorAgent by)
        {
            float f = Game.DisplaySettings.ReadingFactor;
            int w = f >= 2f ? 460 : f > 1f ? 400 : 340, h = f >= 2f ? 420 : 300;
            CreateWindow(G.Content.Text("app.recent", "Recent notices"), "icon_info", 120, 40, w, h, WindowFlags.Standard, zoomFrom);
            var frame = UIBuilder.Bevel(Window.Client, BevelStyle.Sunken, "Recent Notices");
            frame.rectTransform.Stretch(2, 2, 2, 2);
            _scroll = ScrollArea.Create(frame.rectTransform, "Recent Scroll");
            ((RectTransform)_scroll.transform).Stretch(2, 2, 2, 2);
            Window.Resized += _ => _revision = -1;
            Refresh();
        }

        public override void Tick(float dt)
        {
            if (_scroll == null) return;
            if (_revision != G.Notifications.History.Revision || _factor != Game.DisplaySettings.ReadingFactor) Refresh();
        }

        void Refresh()
        {
            var history = G.Notifications.History;
            _revision = history.Revision;
            _factor = Game.DisplaySettings.ReadingFactor;
            float keep = _scroll.Offset;
            for (int i = _scroll.Content.childCount - 1; i >= 0; i--) Object.Destroy(_scroll.Content.GetChild(i).gameObject);
            int width = Mathf.Max(80, Mathf.FloorToInt(_scroll.Viewport.rect.width) - 16);
            float y = 6f;
            if (history.Count == 0)
            {
                _empty = UIBuilder.Text(_scroll.Content, G.Content.Text("recent.empty", "No notices yet."), Palette.TextDisabled);
                _empty.Factor = _factor;
                _empty.rectTransform.At(8, y, width, 14 * _factor);
                y += 18f * _factor;
            }
            foreach (var entry in history.Entries)
            {
                string head = (string.IsNullOrEmpty(entry.Stamp) ? "" : entry.Stamp + "  ") + entry.Title;
                if (ActorStyle.IsSession(entry.Kind)) head += "  (" + (entry.Kind == NoticeKind.Gary ? "session 209" : "session 017") + ")";
                var title = UIBuilder.Text(_scroll.Content, head, TitleColor(entry.Kind), true);
                title.Factor = _factor;
                int th = PixelFont.Measure(head, width, true, _factor).y;
                title.Wrap = true;
                title.rectTransform.At(8, y, width, th + 2);
                y += th + 2;
                var body = UIBuilder.Text(_scroll.Content, entry.Body, Palette.Text);
                body.Factor = _factor;
                body.Wrap = true;
                int bh = PixelFont.Measure(entry.Body, width, false, _factor).y;
                body.rectTransform.At(8, y, width, bh + 2);
                y += bh + Gap;
                var rule = UIBuilder.Solid(_scroll.Content, Palette.Midlight, "Rule");
                rule.rectTransform.At(6, y - 3, width + 4, 1);
                y += 3;
                // Stripe at the left edge in the session's colours, the same language as the notice itself.
                if (entry.Kind != NoticeKind.Plain)
                {
                    var mark = UIBuilder.Solid(_scroll.Content, Palette.StripeOf(entry.Kind), "Mark");
                    mark.rectTransform.At(2, y - (th + 2 + bh + Gap + 3), 3, th + 2 + bh + Gap);
                }
            }
            _scroll.ContentHeight = y + 6f;
            _scroll.ScrollTo(Mathf.Min(keep, _scroll.MaxOffset));
        }

        static Color32 TitleColor(NoticeKind kind) => kind == NoticeKind.Deadline ? Palette.Red : Palette.Text;
    }
}
