using System;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Game;
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
        const int RowStep = 28;
        static int BoxWidth => DisplaySettings.ReadingFactor >= 2f ? 600 : DisplaySettings.ReadingFactor > 1f ? 440 : 280;
        static int ButtonHeight => DisplaySettings.ReadingFactor >= 2f ? 24 : 22;
        static int NoteHeight => Mathf.CeilToInt(14f * DisplaySettings.ReadingFactor);

        GameServices _g;
        RectTransform _panel;
        MenuNav _nav;
        bool _settingsOnly;
        /// <summary>A Yes/No question replaces the menu: Quit to Title, or Quit.</summary>
        enum Confirm { None, Title, Quit }
        Confirm _confirm;
        /// <summary>Phase Q4 (A10): the Accessibility page replaces the main rows (Back returns to them).</summary>
        enum Page { Main, Access }
        Page _page;
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
                else if (_page != Page.Main)
                {
                    _page = Page.Main;
                    Rebuild();
                }
                else Resume();
                return;
            }
            _nav.Tick();
            UpdateDescription();
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
            if (InShift) _g.Director?.SaveCurrentProgress();
            IsPaused = true;
            StateChangeFrame = Time.frameCount;
            _settingsOnly = settingsOnly;
            _confirm = Confirm.None;
            _page = Page.Main;
            // Letting go of the mouse to use the menu must not decide a tug-of-war: call it off instead.
            if (_g.Conflict != null) _g.Conflict.Interrupt();
            // Phase P (A2): a click-locked drag ends here (it is dropped where it is when the game goes on).
            AccessSettings.Lock.Clear();
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
            if (_panel != null)
            {
                _panel.gameObject.SetActive(false);
                Destroy(_panel.gameObject);
            }
            _nav.Clear();
            _panel = UIBuilder.Rect("Pause", _g.Layers.Fullscreen).Stretch();
            var dim = _panel.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);
            dim.raycastTarget = false;
            UIBuilder.Hit(_panel.gameObject, "pause");
            if (_confirm != Confirm.None) BuildConfirm();
            else if (_page == Page.Access) BuildAccess();
            else BuildMenu();
        }

        /// <summary>Phase S: the one-line description of the setting under the pointer or the focus, in a strip at the bottom of the box.</summary>
        PixelText _desc;
        string _descShown;
        static int DescHeight => Mathf.CeilToInt(26f * DisplaySettings.ReadingFactor);

        RectTransform Box(int rows, int extra, string caption, bool withDescription = false)
        {
            _desc = null;
            _descShown = null;
            int h = 32 + rows * RowStep + extra + 12 + (withDescription ? DescHeight + 4 : 0);
            var box = UIBuilder.Rect("Pause Box", _panel).At((ScreenRig.Width - BoxWidth) / 2, (ScreenRig.Height - h) / 2, BoxWidth, h);
            var frame = box.gameObject.AddComponent<BevelGraphic>();
            frame.Style = BevelStyle.Window;
            frame.raycastTarget = false;
            var cap = UIBuilder.Bevel(box, BevelStyle.Gradient, "Caption");
            cap.SetGradient(Palette.TitleActiveA, Palette.TitleActiveB);
            cap.rectTransform.TopStrip(3, 18, 3, 3);
            var t = UIBuilder.Text(cap.rectTransform, caption, Palette.TitleText, true);
            t.Factor = DisplaySettings.ReadingFactor;
            t.rectTransform.Stretch(6, 0, 4, 0);
            t.VAlign = TextVAlign.Middle;
            // The build's version, at the right of the caption (support requests need it).
            var v = UIBuilder.Text(cap.rectTransform, "v" + Application.version, Palette.TitleTextInactive);
            v.rectTransform.Stretch(4, 0, 6, 0);
            v.Align = TextAlign.Right;
            v.VAlign = TextVAlign.Middle;
            if (withDescription)
            {
                _desc = UIBuilder.Text(box, "", Palette.TextMuted);
                _desc.Factor = DisplaySettings.ReadingFactor;
                _desc.Wrap = true;
                _desc.Align = TextAlign.Center;
                _desc.rectTransform.At(20, h - 8 - DescHeight, BoxWidth - 40, DescHeight);
            }
            return box;
        }

        /// <summary>
        /// Phase S (Story, Tug assist, Click lock, Relaxed timing meant nothing to a first-time reader): each setting says in one line what it
        /// does while the pointer is on it or it has the focus. The text key is <c>pause.desc.</c> plus the control's id after "pause:".
        /// </summary>
        void UpdateDescription()
        {
            if (_desc == null) return;
            string id = null;
            var hovered = _g.Player != null ? _g.Player.Hovered : null;
            if (hovered != null && !string.IsNullOrEmpty(hovered.elementId) && hovered.elementId.StartsWith("pause:", StringComparison.Ordinal)) id = hovered.elementId;
            if (id == null && _nav.Focused != null && _nav.Focused.Hit != null) id = _nav.Focused.Hit.elementId;
            if (id == null || !id.StartsWith("pause:", StringComparison.Ordinal)) return;
            if (id == _descShown) return;
            _descShown = id;
            _desc.text = _g.Content.Text("pause.desc." + id.Substring("pause:".Length), "");
        }

        void BuildMenu()
        {
            var c = _g.Content;
            bool pending = PendingDifficulty(out var saved);
            int rows = _settingsOnly ? 11 : 14;
            var box = Box(rows, pending || _settingsOnly ? NoteHeight : 0, _settingsOnly ? c.Text("title.settings") : c.Text("pause.title"), true);
            int y = 32;
            UiButton first;
            if (_settingsOnly) first = Button(box, c.Text("pause.back"), "pause:back", a => Resume(), ref y);
            else first = Button(box, c.Text("pause.resume"), "pause:resume", a => Resume(), ref y);
            // Phase Q1 (owner 7, F A10): Off, Low or Full.
            Button(box, CrtLabel(), "pause:crt", a => { _g.Fx.Crt = DisplayOptions.Next(_g.Fx.Crt); Changed(); }, ref y);
            Button(box, FlashingLabel(), "pause:flashing", a => { _g.Fx.ReduceFlashing = !_g.Fx.ReduceFlashing; Changed(); }, ref y);
            _fullscreen = Screen.fullScreen || Application.isEditor;
            Button(box, DisplayLabel(), "pause:display", a => ToggleDisplay(), ref y);
            Button(box, c.Format("pause.framerate", DisplaySettings.FrameRateLabel(DisplaySettings.FrameRate, c)), "pause:framerate", a =>
            {
                DisplaySettings.ApplyFrameRate(DisplaySettings.NextFrameRate(DisplaySettings.FrameRate));
                Changed();
            }, ref y);
            // Phase Q1: Normal, Medium (1.5x, crisp on a 2x screen texture) or Large.
            Button(box, c.Format("pause.textsize", c.Text(SizeKey(DisplaySettings.Size))), "pause:textsize", a =>
            {
                DisplaySettings.SetSize(DisplayOptions.Next(DisplaySettings.Size));
                Changed();
            }, ref y);
            VolumeRow(box, ref y);
            string mode = c.Text(saved == DifficultyMode.Story ? "title.story" : "title.normal") + (pending ? "*" : "");
            Button(box, c.Format("pause.difficulty", mode), "pause:difficulty", a => ToggleDifficulty(), ref y);
            if (pending || _settingsOnly)
            {
                var note = UIBuilder.Text(box, c.Text(_settingsOnly ? "pause.difficulty.next" : "pause.difficulty.note"), Palette.TextMuted);
                note.Factor = DisplaySettings.ReadingFactor;
                note.rectTransform.At(20, y - 4, BoxWidth - 40, NoteHeight);
                note.Align = TextAlign.Center;
                y += NoteHeight;
            }
            // Phase P (A2): the motor-access options, right after Difficulty.
            Button(box, c.Format("pause.tugassist", c.Text(AccessSettings.TugAssistHold ? "pause.tugassist.hold" : "pause.tugassist.off")), "pause:tugassist", a =>
            {
                AccessSettings.SetTugAssist(!AccessSettings.TugAssistHold);
                Changed();
            }, ref y);
            Button(box, c.Format("pause.clicklock", c.Text(AccessSettings.ClickLockOn ? "pause.clicklock.on" : "pause.clicklock.off")), "pause:clicklock", a =>
            {
                AccessSettings.SetClickLock(!AccessSettings.ClickLockOn);
                Changed();
            }, ref y);
            // Phase Q4 (A10): the rest of the access options live on their own page.
            Button(box, c.Text("pause.access", "Accessibility..."), "pause:access", a =>
            {
                _page = Page.Access;
                Rebuild();
            }, ref y);
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
            UpdateDescription();
        }

        /// <summary>
        /// Phase Q4 (review board A3, A4, A6, A7, A9, A10): the access options, separate from Reduce flashing and from each other. Everything is
        /// off or as before by default; each change applies at once and is saved.
        /// </summary>
        void BuildAccess()
        {
            var c = _g.Content;
            var box = Box(9, 0, c.Text("pause.access.title", "ACCESSIBILITY"), true);
            int y = 32;
            var back = Button(box, c.Text("pause.back"), "pause:accessback", a =>
            {
                _page = Page.Main;
                Rebuild();
            }, ref y);
            Button(box, c.Format("pause.noticetime", c.Text("pause.noticetime." + NoticeRules.Id(AccessSettings.NoticeTime))), "pause:noticetime", a =>
            {
                AccessSettings.SetNoticeTime(NoticeRules.Next(AccessSettings.NoticeTime));
                Changed();
            }, ref y);
            Button(box, c.Format("pause.relaxed", OnOff(AccessSettings.RelaxedTimingOn)), "pause:relaxed", a =>
            {
                AccessSettings.SetRelaxedTiming(!AccessSettings.RelaxedTimingOn);
                Changed();
            }, ref y);
            Button(box, c.Format("pause.captions", OnOff(AccessSettings.Captions)), "pause:captions", a =>
            {
                AccessSettings.SetCaptions(!AccessSettings.Captions);
                Changed();
            }, ref y);
            bool soft = AccessSettings.SoftSounds(_g.Fx.ReduceFlashing);
            Button(box, c.Format("pause.sudden", c.Text(soft ? "pause.sudden.soft" : "pause.sudden.normal")), "pause:sudden", a =>
            {
                AccessSettings.SetSuddenSoft(!soft);
                Changed();
            }, ref y);
            var shake = AccessSettings.Shake(_g.Fx.ReduceFlashing);
            Button(box, c.Format("pause.shake", c.Text("pause.shake." + shake.ToString().ToLowerInvariant())), "pause:shake", a =>
            {
                AccessSettings.SetShake(AccessOptions.Next(shake));
                Changed();
            }, ref y);
            Button(box, c.Format("pause.mono", OnOff(AccessSettings.MonoAudio)), "pause:mono", a =>
            {
                AccessSettings.SetMonoAudio(!AccessSettings.MonoAudio);
                Changed();
            }, ref y);
            Button(box, c.Format("pause.bigcursor", OnOff(AccessSettings.LargeCursor)), "pause:bigcursor", a =>
            {
                AccessSettings.SetLargeCursor(!AccessSettings.LargeCursor);
                Changed();
            }, ref y);
            Button(box, c.Format("pause.clickspeed", c.Text("pause.clickspeed." + AccessSettings.ClickSpeed.ToString().ToLowerInvariant())), "pause:clickspeed", a =>
            {
                AccessSettings.SetClickSpeed(AccessOptions.Next(AccessSettings.ClickSpeed));
                Changed();
            }, ref y);
            _nav.Focus(back);
            UpdateDescription();
        }

        string OnOff(bool on) => _g.Content.Text(on ? "pause.on" : "pause.off", on ? "On" : "Off");

        void BuildConfirm()
        {
            var c = _g.Content;
            bool quit = _confirm == Confirm.Quit;
            string body = c.Text(quit ? "pause.quit.q" : "pause.totitle.q") + "\n" + LostText();
            int bodyH = Mathf.Max(30, PixelFont.Measure(body, BoxWidth - 24, false, DisplaySettings.ReadingFactor).y + 4);
            var box = Box(2, 10 + (bodyH - 30), c.Text(quit ? "pause.quit" : "pause.totitle"));
            var text = UIBuilder.Text(box, body, Palette.Text);
            text.Factor = DisplaySettings.ReadingFactor;
            text.rectTransform.At(12, 30, BoxWidth - 24, bodyH);
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
            ((RectTransform)yes.transform).At(BoxWidth / 2 - 90, 42 + bodyH, 80, ButtonHeight);
            var no = UiButton.Create(box, c.Text("pause.no"), a =>
            {
                _confirm = Confirm.None;
                Rebuild();
            }, "pause:no");
            ((RectTransform)no.transform).At(BoxWidth / 2 + 10, 42 + bodyH, 80, ButtonHeight);
            _nav.Add(yes);
            _nav.Add(no);
            _nav.Focus(no);
        }

        /// <summary>
        /// Phase S (the tester lost 15 game minutes to a Quit that said "the last checkpoint"): says where Continue will put you and what that
        /// loses: the last checkpoint (and the clock time it saved) or the start of the night.
        /// </summary>
        string LostText()
        {
            var c = _g.Content;
            var cp = SaveSystem.Load().CheckpointFor(_g.Night);
            string now = Core.Story.GameClock.Format12(_g.Clock.TotalMinutes);
            if (cp == null) return c.Format("pause.lost.start", now);
            return c.Format("pause.lost.checkpoint", Core.Story.GameClock.Format12(cp.clockMinutes), now);
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
            _volume.Factor = DisplaySettings.ReadingFactor;
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

        string CrtLabel() => _g.Content.Format("pause.crt", _g.Content.Text(_g.Fx.Crt == CrtLevel.Full ? "pause.crt.full" : _g.Fx.Crt == CrtLevel.Low ? "pause.crt.low" : "pause.crt.off"));

        static string SizeKey(ReadingSize size) =>
            size == ReadingSize.Large ? "pause.textsize.large" : size == ReadingSize.Medium ? "pause.textsize.medium" : "pause.textsize.normal";
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
