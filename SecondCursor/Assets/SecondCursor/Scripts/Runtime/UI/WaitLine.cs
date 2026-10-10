using SecondCursor.Game;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.Story;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.UI
{
    /// <summary>
    /// Phase R (sixth blind playtest: four minutes of waiting that "felt broken, not designed"): a short line above the taskbar while a story
    /// beat has the stage and there is nothing for the player to do ("Something is happening. You cannot act yet."). The director decides
    /// (<see cref="NightDirector.PlayerMustWait"/>); the line comes after the wait has lasted a moment and goes as soon as the player has
    /// something to do again. It never takes a click.
    /// </summary>
    public sealed class WaitLine : MonoBehaviour
    {
        const float ShowAfter = 0.8f, HideAfter = 0.4f;
        const int Height = 15, AboveTaskbar = 10;

        GameServices _g;
        RectTransform _box;
        PixelText _text;
        float _waiting, _free;

        public bool Shown => _box != null && _box.gameObject.activeSelf;

        public static WaitLine Create(GameServices g)
        {
            var root = UIBuilder.Rect("Wait Line", g.Layers.Effects);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            var line = root.gameObject.AddComponent<WaitLine>();
            line._g = g;
            line.Build(root);
            return line;
        }

        void Build(RectTransform root)
        {
            string text = _g.Content.Text("wait.line", "");
            _box = UIBuilder.Rect("Box", root);   // the component sits on the holder, which stays active
            _box.gameObject.SetActive(false);
            if (string.IsNullOrEmpty(text)) return;
            int w = PixelFont.MeasureLine(text, true) + 16;
            _box.anchorMin = _box.anchorMax = new Vector2(0.5f, 0f);
            _box.pivot = new Vector2(0.5f, 0f);
            _box.anchoredPosition = new Vector2(0f, WindowManager.TaskbarHeight + AboveTaskbar);
            _box.sizeDelta = new Vector2(w, Height);
            var edge = UIBuilder.Solid(_box, Palette.Dark, "Edge");
            edge.rectTransform.Stretch();
            var fill = UIBuilder.Solid(_box, Palette.Tooltip, "Fill");
            fill.rectTransform.Stretch(1, 1, 1, 1);
            _text = UIBuilder.Text(_box, text, Palette.Text, true, "Text");
            _text.Align = TextAlign.Center;
            _text.VAlign = TextVAlign.Middle;
            _text.rectTransform.Stretch(2, 1, 2, 1);
        }

        void LateUpdate()
        {
            if (_text == null || _g == null || _g.Director == null) return;
            bool must = _g.Director.PlayerMustWait;
            float dt = Time.deltaTime;
            if (must) { _waiting += dt; _free = 0f; }
            else { _free += dt; if (_free >= HideAfter) _waiting = 0f; }
            bool show = _waiting >= ShowAfter;
            if (show != _box.gameObject.activeSelf) _box.gameObject.SetActive(show);
        }
    }
}
