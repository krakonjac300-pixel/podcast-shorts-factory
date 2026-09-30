using System;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.Story;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Game
{
    /// <summary>
    /// Esc: freeze the shift (time and audio) and change settings: CRT effects, reduced flashing (photosensitivity),
    /// fullscreen or windowed, frame rate, reading text size, volume and difficulty (it takes effect at the next
    /// checkpoint). Restart from the checkpoint, quit to the title or quit. On the title the same panel opens as
    /// Options (settings only). A standalone build also pauses itself when the window loses focus, the app is
    /// suspended (Steam Deck) or the Steam overlay opens, so the second cursor never plays on while you are away.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        const int BoxWidth = 240, RowStep = 28, ButtonHeight = 22;

        GameServices _g;
        RectTransform _panel;
        MenuNav _nav;
        bool _settingsOnly;
        /// <summary>A Yes/No question replaces the menu: Quit to Title, or Quit.</summary>
        enum Confirm { None, Title, Quit }
        Confirm _confirm;
        bool _fullscreen = true;
        PixelText _volume;
        float _savedScale = 1f;

        public static bool IsPaused { get; private set; }
        /// <summary>
        /// The frame the menu last opened or closed: the key press that did it must not also press a button of the menu
        /// underneath (title, end card) or of the menu that just opened (every <see cref="MenuNav"/> skips this frame).
        /// </summary>
        public static int StateChangeFrame { get; private set; } = -1;
        /// <summary>The live menu (for the title's Options and the taskbar menu button).</summary>
        public static PauseMenu Current { get; private set; }

        /// <summary>In a shift: the whole menu.</summary>
        public void OpenMenu() => Pause(false);

        /// <summary>On the title: the settings rows only (Options).</summary>
        public void OpenSettings() => Pause(true);

        public static PauseMenu Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Pause Menu");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<PauseMenu>();
            p._g = g;
            p._nav = new MenuNav(g) { RespectPause = false };
            IsPaused = false;
            Current = p;
            SteamBridge.OverlayActivated += p.OnOverlay;
            return p;
        }

        void Update()
        {
            var input = _g.Input;
            if (!IsPaused)
            {
                // Esc works from the very first screen: settings and Quit must never need a log-on.
                if (!input.KeyDown(GameKey.Escape) || Apps.AppManager.EscapeHandledFrame == Time.frameCount) return;
                var title = TitleMenu.Current;
                // Before the title menu is built (the disclaimer of a title root) only the settings make sense.
                if (title == null) Pause(_g.ShowTitle);
                else if (!title.HandlesEscape) OpenSettings();
                return;
            }
            if (input.KeyDown(GameKey.Escape))
            {
                if (_confirm != Confirm.None)
                {
                    _confirm = Confirm.None;
                    Rebuild();
                }
                else Resume();
                return;
            }
            _nav.Tick();
        }

        bool InShift => _g != null && _g.Flags != null && _g.Flags.Has(Core.Story.Flags.LoggedIn);

        void OnApplicationFocus(bool focus)
        {
            if (!focus && !Application.isEditor && InShift) OpenMenu();
        }

        /// <summary>The app is suspended (Steam Deck sleep, alt-tab on some platforms).</summary>
        void OnApplicationPause(bool paused)
        {
            if (paused && InShift) OpenMenu();
        }

        void OnOverlay()
        {
            if (InShift && !IsPaused) OpenMenu();
        }

        void Pause(bool settingsOnly)
        {
            if (IsPaused) return;
            IsPaused = true;
            StateChangeFrame = Time.frameCount;
            _settingsOnly = settingsOnly;
            _confirm = Confirm.None;
            // Letting go of the mouse to use the menu must not decide a tug-of-war: call it off instead.
            if (_g.Conflict != null) _g.Conflict.Interrupt();
            // Inside a tug win's hit-stop the scale is a 0.03 freeze: keep the speed it will return to.
            float beforeHitStop = _g.Conflict != null ? _g.Conflict.ScaleBeforeHitStop : -1f;
            _savedScale = beforeHitStop > 0f ? beforeHitStop : Time.timeScale;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Rebuild();
        }

        void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            StateChangeFrame = Time.frameCount;
            Time.timeScale = _savedScale <= 0f ? 1f : _savedScale;
            AudioListener.pause = false;
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null;
            _nav.Clear();
            SaveSystem.SaveSettings(_g);
        }

        void Rebuild()
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _nav.Clear();
            _panel = UIBuilder.Rect("Pause", _g.Layers.Fullscreen).Stretch();
            var dim = _panel.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = false;
            UIBuilder.Hit(_panel.gameObject, "pause");
            if (_confirm != Confirm.None) BuildConfirm();
            else BuildMenu();
        }

        RectTransform Box(int rows, int extra, string caption)
        {
            int h = 32 + rows * RowStep + extra + 12;
            var box = UIBuilder.Rect("Pause Box", _panel).At((ScreenRig.Width - BoxWidth) / 2, (ScreenRig.Height - h) / 2, BoxWidth, h);
            var frame = box.gameObject.AddComponent<BevelGraphic>();
            frame.Style = BevelStyle.Window;
            frame.raycastTarget = false;
            var cap = UIBuilder.Bevel(box, BevelStyle.Gradient, "Caption");
            cap.SetGradient(Palette.TitleActiveA, Palette.TitleActiveB);
            cap.rectTransform.TopStrip(3, 18, 3, 3);
            var t = UIBuilder.Text(cap.rectTransform, caption, Palette.TitleText, true);
            t.rectTransform.Stretch(6, 0, 4, 0);
            t.VAlign = TextVAlign.Middle;
            // The build's version, at the right of the caption (support requests need it).
            var v = UIBuilder.Text(cap.rectTransform, "v" + Application.version, Palette.TitleTextInactive);
            v.rectTransform.Stretch(4, 0, 6, 0);
            v.Align = TextAlign.Right;
            v.VAlign = TextVAlign.Middle;
            return box;
        }

        void BuildMenu()
        {
            var c = _g.Content;
            bool pending = PendingDifficulty(out var saved);
            int rows = _settingsOnly ? 8 : 11;
            var box = Box(rows, pending || _settingsOnly ? 14 : 0, _settingsOnly ? c.Text("title.settings") : c.Text("pause.title"));
            int y = 32;
            UiButton first;
            if (_settingsOnly) first = Button(box, c.Text("pause.back"), "pause:back", a => Resume(), ref y);
            else first = Button(box, c.Text("pause.resume"), "pause:resume", a => Resume(), ref y);
            Button(box, CrtLabel(), "pause:crt", a => { _g.Fx.CrtEnabled = !_g.Fx.CrtEnabled; Changed(); }, ref y);
            Button(box, FlashingLabel(), "pause:flashing", a => { _g.Fx.ReduceFlashing = !_g.Fx.ReduceFlashing; Changed(); }, ref y);
            _fullscreen = Screen.fullScreen || Application.isEditor;
            Button(box, DisplayLabel(), "pause:display", a => ToggleDisplay(), ref y);
            Button(box, c.Format("pause.framerate", DisplaySettings.FrameRateLabel(DisplaySettings.FrameRate, c)), "pause:framerate", a =>
            {
                DisplaySettings.ApplyFrameRate(DisplaySettings.NextFrameRate(DisplaySettings.FrameRate));
                Changed();
            }, ref y);
            Button(box, c.Format("pause.textsize", c.Text(DisplaySettings.LargeText ? "pause.textsize.large" : "pause.textsize.normal")), "pause:textsize", a =>
            {
                DisplaySettings.SetLargeText(!DisplaySettings.LargeText);
                Changed();
            }, ref y);
            VolumeRow(box, ref y);
            string mode = c.Text(saved == DifficultyMode.Story ? "title.story" : "title.normal") + (pending ? "*" : "");
            Button(box, c.Format("pause.difficulty", mode), "pause:difficulty", a => ToggleDifficulty(), ref y);
            if (pending || _settingsOnly)
            {
                var note = UIBuilder.Text(box, c.Text(_settingsOnly ? "pause.difficulty.next" : "pause.difficulty.note"), Palette.Shadow);
                note.rectTransform.At(20, y - 4, BoxWidth - 40, 12);
                note.Align = TextAlign.Center;
                y += 14;
            }
            if (!_settingsOnly)
            {
                bool hasCheckpoint = SaveSystem.Load().CheckpointFor(_g.Night) != null;
                Button(box, c.Text(hasCheckpoint ? "pause.restart" : "pause.restart.night"), "pause:restart", a =>
                {
                    bool armed = _g.RecordsArmed, fromSelect = _g.FromNightSelect;
                    int night = _g.Night;
                    Resume();
                    GameBootstrap.RestartFromCheckpoint(night, armed, fromSelect);
                }, ref y);
                Button(box, c.Text("pause.totitle"), "pause:totitle", a =>
                {
                    _confirm = Confirm.Title;
                    Rebuild();
                }, ref y);
            }
            // Quit asks first too: one stray click must not end the session.
            if (!_settingsOnly) Button(box, c.Text("pause.quit"), "pause:quit", a =>
            {
                _confirm = Confirm.Quit;
                Rebuild();
            }, ref y);
            _nav.Focus(first);
        }

        void BuildConfirm()
        {
            var c = _g.Content;
            bool quit = _confirm == Confirm.Quit;
            var box = Box(2, 10, c.Text(quit ? "pause.quit" : "pause.totitle"));
            var text = UIBuilder.Text(box, c.Text(quit ? "pause.quit.confirm" : "pause.totitle.confirm"), Palette.Text);
            text.rectTransform.At(12, 30, BoxWidth - 24, 30);
            text.Align = TextAlign.Center;
            text.Wrap = true;
            var yes = UiButton.Create(box, c.Text("pause.yes"), a =>
            {
                if (quit)
                {
                    QuitGame();
                    return;
                }
                Resume();
                GameBootstrap.ToTitle();
            }, "pause:yes");
            ((RectTransform)yes.transform).At(BoxWidth / 2 - 90, 72, 80, ButtonHeight);
            var no = UiButton.Create(box, c.Text("pause.no"), a =>
            {
                _confirm = Confirm.None;
                Rebuild();
            }, "pause:no");
            ((RectTransform)no.transform).At(BoxWidth / 2 + 10, 72, 80, ButtonHeight);
            _nav.Add(yes);
            _nav.Add(no);
            _nav.Focus(no);
        }

        void VolumeRow(RectTransform box, ref int y)
        {
            var row = UIBuilder.Rect("Volume", box).At(20, y, BoxWidth - 40, ButtonHeight);
            var minus = UiButton.Create(row, "-", a => Volume(-0.1f), "pause:voldown");
            ((RectTransform)minus.transform).At(0, 0, 30, ButtonHeight);
            var plus = UiButton.Create(row, "+", a => Volume(0.1f), "pause:volup");
            // Phase M: the new level is previewed instead of the click (the menu's sounds play through the pause).
            minus.ClickSound = plus.ClickSound = "";
            ((RectTransform)plus.transform).At(BoxWidth - 70, 0, 30, ButtonHeight);
            _volume = UIBuilder.Text(row, "", Palette.Text);
            _volume.rectTransform.Stretch(34, 0, 34, 0);
            _volume.Align = TextAlign.Center;
            _volume.VAlign = TextVAlign.Middle;
            _nav.Add(minus);
            _nav.Add(plus);
            Volume(0f);
            y += RowStep;
        }

        /// <summary>The saved difficulty differs from the one this night is running (it applies at the next checkpoint).</summary>
        bool PendingDifficulty(out DifficultyMode saved)
        {
            saved = DifficultyTable.ParseMode(SaveSystem.Load().difficulty);
            return !_settingsOnly && _g.Difficulty != null && saved != _g.Difficulty.Mode;
        }

        void ToggleDifficulty()
        {
            var saved = DifficultyTable.ParseMode(SaveSystem.Load().difficulty);
            SaveSystem.SetDifficulty(saved == DifficultyMode.Story ? DifficultyMode.Normal : DifficultyMode.Story);
            Changed();
        }

        /// <summary>A setting changed: save it at once and redraw the labels (focus stays on the same row).</summary>
        void Changed()
        {
            SaveSystem.SaveSettings(_g);
            string focused = _nav.Focused != null && _nav.Focused.Hit != null ? _nav.Focused.Hit.elementId : null;
            Rebuild();
            foreach (var b in _nav.Buttons)
                if (b.Hit != null && b.Hit.elementId == focused) _nav.Focus(b);
        }

        string CrtLabel() => "CRT effects: " + (_g.Fx.CrtEnabled ? "On" : "Off");
        string FlashingLabel() => "Flashing: " + (_g.Fx.ReduceFlashing ? "Reduced" : "Full");
        string DisplayLabel() => "Display: " + (_fullscreen ? "Fullscreen" : "Windowed");

        void ToggleDisplay()
        {
            _fullscreen = !_fullscreen;
            GameRoot.Display(_fullscreen);
            Changed();
        }

        void Volume(float delta)
        {
            _g.Audio.MasterVolume = Mathf.Clamp01(Mathf.Round((_g.Audio.MasterVolume + delta) * 10f) / 10f);
            _volume.text = "Volume " + Mathf.RoundToInt(_g.Audio.MasterVolume * 100f) + "%";
            if (delta == 0f) return;
            SaveSystem.SaveSettings(_g);
            _g.Audio.Play("ui_select");
        }

        UiButton Button(RectTransform box, string label, string id, Action<CursorAgent> click, ref int y)
        {
            var b = UiButton.Create(box, label, click, id);
            ((RectTransform)b.transform).At(20, y, BoxWidth - 40, ButtonHeight);
            _nav.Add(b);
            y += RowStep;
            return b;
        }

        public static void QuitGame()
        {
            GameLog.Info(LogChannel.Player, "Quit");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void OnDestroy()
        {
            SteamBridge.OverlayActivated -= OnOverlay;
            if (Current == this) Current = null;
            if (IsPaused)
            {
                IsPaused = false;
                Time.timeScale = 1f;
                AudioListener.pause = false;
            }
        }
    }
}
