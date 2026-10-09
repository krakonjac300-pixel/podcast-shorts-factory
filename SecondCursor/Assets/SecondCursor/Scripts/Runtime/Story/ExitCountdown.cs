// Night 3 is not in the free demo (SC_DEMO): its code stays out of that build, like its content.
#if !SC_DEMO
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase S (a blind tester logged off with 10 s to spare and could not see the clock): from 7:00 until the shift ends, a strip at the top of
    /// the screen counts the real seconds left to log off ("LOG OFF BEFORE 7:05 AM: 0:42 LEFT"). It reads the clock's own rate, so Relaxed
    /// timing and Story show their longer window; the last 15 seconds are red. It only shows the time: nothing here decides the ending.
    /// </summary>
    public sealed class ExitCountdown
    {
        const int Top = 0;
        const float UrgentSeconds = 15f;

        readonly GameServices _g;
        readonly RectTransform _box;
        readonly PixelText _text;
        readonly UnityEngine.UI.Image _fill;
        int _shownSeconds = -1;
        float _factor = -1f;

        public ExitCountdown(GameServices g)
        {
            _g = g;
            var root = UIBuilder.Rect("Exit Countdown", g.Layers.Effects);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            _box = UIBuilder.Rect("Box", root);
            _box.anchorMin = _box.anchorMax = new Vector2(0.5f, 1f);
            _box.pivot = new Vector2(0.5f, 1f);
            _box.anchoredPosition = new Vector2(0f, -Top);
            var edge = UIBuilder.Solid(_box, Palette.Dark, "Edge");
            edge.rectTransform.Stretch();
            _fill = UIBuilder.Solid(_box, Palette.Tooltip, "Fill");
            _fill.rectTransform.Stretch(1, 1, 1, 1);
            _text = UIBuilder.Text(_box, "", Palette.Text, true, "Text");
            _text.Align = TextAlign.Center;
            _text.VAlign = TextVAlign.Middle;
            _text.rectTransform.Stretch(2, 1, 2, 1);
            _box.gameObject.SetActive(false);
        }

        /// <summary>Real seconds until <paramref name="endMinute"/> at the clock's own rate (never negative).</summary>
        public static float SecondsLeft(double exactMinutes, double endMinute, float clockRate) => Core.Story.Night3Rules.ExitSecondsLeft(exactMinutes, endMinute, clockRate);

        public static string Format(float seconds) => Core.Story.Night3Rules.ExitClock(seconds);

        /// <summary>Every frame while the finale runs: shows the strip while there is time left to log off.</summary>
        public void Tick(bool show, float seconds)
        {
            if (!show)
            {
                if (_box.gameObject.activeSelf) _box.gameObject.SetActive(false);
                _shownSeconds = -1;
                return;
            }
            float f = DisplaySettings.ReadingFactor;
            int whole = Mathf.CeilToInt(seconds);
            if (whole == _shownSeconds && Mathf.Approximately(f, _factor) && _box.gameObject.activeSelf) return;
            _shownSeconds = whole;
            _factor = f;
            string line = _g.Content.Format("logoff.countdown", Format(seconds));
            _text.Factor = f;
            _text.text = line;
            _text.color = seconds <= UrgentSeconds ? (Color)Palette.Red : (Color)Palette.Text;
            _box.sizeDelta = new Vector2(PixelFont.MeasureLine(line, true, f) + 16, Mathf.CeilToInt(15 * f));
            _box.gameObject.SetActive(true);
        }

        public void Destroy()
        {
            if (_box != null && _box.parent != null) Object.Destroy(_box.parent.gameObject);
        }
    }
}
#endif
