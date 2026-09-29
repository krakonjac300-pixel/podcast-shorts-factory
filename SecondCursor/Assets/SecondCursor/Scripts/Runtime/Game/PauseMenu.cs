using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Game
{
    /// <summary>Esc: freeze the shift (time and audio), toggle CRT effects, adjust volume, restart or quit.</summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        GameServices _g;
        RectTransform _panel;
        UiButton _crt;
        PixelText _volume;
        float _savedScale = 1f;

        public static bool IsPaused { get; private set; }

        public static PauseMenu Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Pause Menu");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PauseMenu>();
            p._g = g;
            IsPaused = false;
            return p;
        }

        void Update()
        {
            if (!_g.Flags.Has(Core.Story.Flags.LoggedIn)) return;
            if (_g.Input.KeyDown(GameKey.Escape))
            {
                if (IsPaused) Resume();
                else Pause();
            }
        }

        void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            _savedScale = Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Build();
        }

        void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            Time.timeScale = _savedScale <= 0f ? 1f : _savedScale;
            AudioListener.pause = false;
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null;
            SaveSystem.SaveSettings(_g);
        }

        void Build()
        {
            _panel = UIBuilder.Rect("Pause", _g.Layers.Fullscreen).Stretch();
            var dim = _panel.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = false;
            UIBuilder.Hit(_panel.gameObject, "pause");

            const int w = 240, h = 206;
            var box = UIBuilder.Rect("Pause Box", _panel).At((ScreenRig.Width - w) / 2, (ScreenRig.Height - h) / 2, w, h);
            var frame = box.gameObject.AddComponent<BevelGraphic>();
            frame.Style = BevelStyle.Window;
            frame.raycastTarget = false;
            var cap = UIBuilder.Bevel(box, BevelStyle.Gradient, "Caption");
            cap.SetGradient(Palette.TitleActiveA, Palette.TitleActiveB);
            cap.rectTransform.TopStrip(3, 18, 3, 3);
            var t = UIBuilder.Text(cap.rectTransform, _g.Content.Text("pause.title"), Palette.TitleText, true);
            t.rectTransform.Stretch(6, 0, 4, 0);
            t.VAlign = TextVAlign.Middle;

            int y = 32;
            Button(box, "Resume", a => Resume(), ref y);
            _crt = Button(box, CrtLabel(), a => { _g.Fx.CrtEnabled = !_g.Fx.CrtEnabled; _crt.SetLabel(CrtLabel()); }, ref y);
            var volRow = UIBuilder.Rect("Volume", box).At(20, y, w - 40, 22);
            var minus = UiButton.Create(volRow, "-", a => Volume(-0.1f), "button:VolDown");
            ((RectTransform)minus.transform).At(0, 0, 30, 22);
            var plus = UiButton.Create(volRow, "+", a => Volume(0.1f), "button:VolUp");
            ((RectTransform)plus.transform).At(w - 70, 0, 30, 22);
            _volume = UIBuilder.Text(volRow, "", Palette.Text);
            _volume.rectTransform.Stretch(34, 0, 34, 0);
            _volume.Align = TextAlign.Center;
            _volume.VAlign = TextVAlign.Middle;
            Volume(0f);
            y += 28;
            Button(box, "Restart shift", a => { Resume(); GameBootstrap.Restart(); }, ref y);
            Button(box, "Quit", a => Quit(), ref y);
        }

        string CrtLabel() => "CRT effects: " + (_g.Fx.CrtEnabled ? "On" : "Off");

        void Volume(float delta)
        {
            _g.Audio.MasterVolume = Mathf.Clamp01(Mathf.Round((_g.Audio.MasterVolume + delta) * 10f) / 10f);
            _volume.text = "Volume " + Mathf.RoundToInt(_g.Audio.MasterVolume * 100f) + "%";
        }

        UiButton Button(RectTransform box, string label, System.Action<CursorAgent> click, ref int y)
        {
            var b = UiButton.Create(box, label, click, "button:" + label);
            ((RectTransform)b.transform).At(20, y, 200, 22);
            y += 28;
            return b;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            if (IsPaused)
            {
                IsPaused = false;
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }
    }
}
