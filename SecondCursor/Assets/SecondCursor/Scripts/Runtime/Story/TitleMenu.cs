using System;
using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Story
{
    /// <summary>Which title screen opens first (an end card's Night Select opens straight on it).</summary>
    public enum TitleScreenId { Main, NightSelect, Records, Credits }

    /// <summary>
    /// The title menu (expansion spec 8.1): the red ghost title and the drone, then Continue, New Game, Night Select,
    /// Records, Options, Credits and Quit as the save allows (<see cref="TitleMenuModel"/>). Mouse and keyboard both
    /// work (<see cref="MenuNav"/>). It runs inside the boot beat of a Night 1 root; every choice that starts a night
    /// fades out and builds a fresh root (<see cref="GameBootstrap.StartFromMenu"/>), so <see cref="Run"/> never
    /// returns normally.
    /// </summary>
    public sealed partial class TitleMenu
    {
        enum Screen { Main, NewGameConfirm, Difficulty, NightSelect, SelectConfirm, Records, Credits, Record }

        const int ColumnWidth = 180, RowHeight = 22, RowStep = 26, ColumnTop = 292;
        const float LeaveSeconds = 0.4f;
        static readonly Color32 Faint = new Color32(0x6A, 0x6A, 0x66, 0xFF);
        static readonly Color32 GhostRed = new Color32(0xC0, 0x39, 0x2B, 0xFF);

        static TitleMenu _current;
        /// <summary>The screen the next title opens on (reset to Main when it is used).</summary>
        internal static TitleScreenId StartScreen;

        readonly GameServices _g;
        readonly RectTransform _panel;
        readonly MenuNav _nav;
        RectTransform _content;
        Screen _screen;
        Action<UiButton> _onFocus;
        PixelText _ghost;
        StepTimer _ghostTimer;
        Image _fade;
        bool _leaving;
        float _leaveT;
        Action _leaveAction;
        /// <summary>Phase Q2 (T5, V9): the second pointer on the main screen.</summary>
        TitleGhost _titleGhost;

        /// <summary>The live title menu, or null (none, or its panel was destroyed by a jump).</summary>
        public static TitleMenu Current => _current != null && _current._panel != null ? _current : null;

        /// <summary>The game root this menu belongs to (the test bridge waits for a new root's menu).</summary>
        internal GameServices Services => _g;

        /// <summary>Esc belongs to the title (going back) rather than opening Options.</summary>
        public bool HandlesEscape => _screen != Screen.Main || _leaving || _nav.LastBackFrame == Time.frameCount;

        public TitleMenu(GameServices g, RectTransform panel)
        {
            _g = g;
            _panel = panel;
            _nav = new MenuNav(g) { RingColor = Palette.BiosBright };
            _nav.FocusChanged += b => _onFocus?.Invoke(b);
        }

        public IEnumerator Run()
        {
            _current = this;
            _g.Player.Enabled = true;
            _g.Player.Visible = true;
            _g.Audio.PlayLoop("drone_tension", 0.35f, 2f);
            // Phase Q2 (V6): the demo's words, handed over once (the full game only; nothing happens if there are none).
            DemoHandoffIO.ImportOnce();
            var start = StartScreen;
            StartScreen = TitleScreenId.Main;
            if (start == TitleScreenId.NightSelect && !GameBootstrap.NightSelectAvailable) start = TitleScreenId.Main;
            if (start == TitleScreenId.NightSelect) ShowNightSelect();
            else if (start == TitleScreenId.Records) ShowRecords();
            else if (start == TitleScreenId.Credits) ShowCredits();
            else ShowMain();
            while (_panel != null)
            {
                if (_leaving)
                {
                    // The choice builds a new root and this one is destroyed at the end of the frame: never hand
                    // control back to the boot beat (it would carry on with the log-on on the old root).
                    if (TickLeave())
                        while (true) yield return null;
                }
                else
                {
                    _nav.Tick();
                    TickScreen();
                }
                AnimateGhost();
                if (!_leaving) _titleGhost?.Tick(Time.unscaledDeltaTime);
                yield return null;
            }
        }

        // ------------------------------------------------------------------ main screen

        void ShowMain()
        {
            Begin(Screen.Main);
            var c = _g.Content;
            TitleKeyArt.Create(_content);   // Phase Q4 (R9): the capsule's two pointers, the file and the red line, on a dark teal desktop
            _ghost = Label("SECOND CURSOR", GhostRed, 0, 206, ScreenRig.Width, 40, TextAlign.Center, true, 4);
            Label("SECOND CURSOR", Palette.BiosBright, 0, 204, ScreenRig.Width, 40, TextAlign.Center, true, 4);
            var d = SaveSystem.Load();
            // Phase Q2 (T5): after SHRED the tagline names who is logged in.
            bool shredSeen = Array.IndexOf(d.endingsSeen, "n3_shred") >= 0;
            Label(c.Text(shredSeen && c.HasText("title.tagline.214") ? "title.tagline.214" : "title.tagline"), Palette.BiosText, 0, 256, ScreenRig.Width, 12);

            var items = TitleMenuModel.Items(d, GameBootstrap.IsDemo, SteamBridge.CanOpenStore);
            var focus = TitleMenuModel.DefaultFocus(items, d);
            int y = ColumnTop;
            UiButton focusButton = null;
            foreach (var item in items)
            {
                var it = item;
                var b = MenuButton(ItemLabel(it, d), "title:" + ItemId(it), a => Choose(it), (ScreenRig.Width - ColumnWidth) / 2, y, ColumnWidth, RowHeight);
                if (it == focus) focusButton = b;
                y += RowStep;
            }
            _nav.Focus(focusButton);
            // The second pointer: beside the first button, or (after a Night 3 ending) replaying your own Night 1 drag.
            bool afterNight3 = false;
            foreach (var id in AchievementIds.Night3Endings) afterNight3 |= Array.IndexOf(d.endingsSeen, id) >= 0;
            _titleGhost = new TitleGhost(_g, new Vector2((ScreenRig.Width + ColumnWidth) / 2 + 90, ScreenRig.Height - ColumnTop - RowHeight / 2), d.ghostPath, afterNight3);
            if (SaveSystem.CorruptThisLaunch) Label(c.Text("title.corrupt"), Palette.Amber, 0, 482, ScreenRig.Width, 12);
            Label(c.Text("title.headphones"), Faint, 0, 500, ScreenRig.Width, 12);
            Label("v" + Application.version, new Color32(0x4A, 0x4A, 0x46, 0xFF), 8, 522, 200, 12, TextAlign.Left);
            GameLog.Info(LogChannel.Story, "Title menu: " + string.Join(", ", items));
        }

        string ItemLabel(TitleItem item, SaveData d)
        {
            var c = _g.Content;
            switch (item)
            {
                case TitleItem.Continue:
                {
                    var target = d.ContinueTarget(GameBootstrap.MaxNight);
                    if (target == null) return c.Text("title.continue");
                    return target.Checkpoint != null
                        ? c.Format("title.continue.at", target.Night, GameClock.Format12(target.Checkpoint.clockMinutes))
                        : c.Format("title.continue", target.Night);
                }
                case TitleItem.NewGame: return c.Text("title.new");
                case TitleItem.NightSelect: return c.Text("title.select");
                case TitleItem.Records: return c.Text("title.records");
                case TitleItem.Options: return c.Text("title.settings");
                case TitleItem.Credits: return c.Text("title.credits");
                case TitleItem.Wishlist: return c.Text("title.wishlist");
                default: return c.Text("title.quit");
            }
        }

        static string ItemId(TitleItem item)
        {
            switch (item)
            {
                case TitleItem.Continue: return "continue";
                case TitleItem.NewGame: return "new";
                case TitleItem.NightSelect: return "select";
                case TitleItem.Records: return "records";
                case TitleItem.Options: return "options";
                case TitleItem.Credits: return "credits";
                case TitleItem.Wishlist: return "wishlist";
                default: return "quit";
            }
        }

        void Choose(TitleItem item)
        {
            if (_leaving) return;
            GameLog.Info(LogChannel.Player, "Title: " + item);
            switch (item)
            {
                case TitleItem.Continue:
                {
                    var target = SaveSystem.Load().ContinueTarget(GameBootstrap.MaxNight);
                    if (target == null) return;
                    Leave(() => GameBootstrap.StartFromMenu(target.Night, target.Checkpoint != null));
                    return;
                }
                case TitleItem.NewGame:
                    if (SaveSystem.Load().HasProgress) ShowNewGameConfirm();
                    else ShowDifficulty();
                    return;
                case TitleItem.NightSelect: ShowNightSelect(); return;
                case TitleItem.Records: ShowRecords(); return;
                case TitleItem.Options: PauseMenu.Current?.OpenSettings(); return;
                case TitleItem.Credits: ShowCredits(); return;
                case TitleItem.Wishlist: SteamBridge.OpenStorePage(); return;
                default: PauseMenu.QuitGame(); return;
            }
        }

        // ------------------------------------------------------------------ New Game

        void ShowNewGameConfirm()
        {
            Begin(Screen.NewGameConfirm);
            var c = _g.Content;
            Label(c.Text("title.new.confirm"), Palette.BiosBright, 0, 250, ScreenRig.Width, 30);
            int x = ScreenRig.Width / 2;
            MenuButton(c.Text("title.yes"), "title:yes", a => ShowDifficulty(), x - 110, 310, 100, RowHeight);
            var no = MenuButton(c.Text("title.no"), "title:no", a => ShowMain(), x + 10, 310, 100, RowHeight);
            _nav.Focus(no);
            _nav.Back = ShowMain;
        }

        void ShowDifficulty()
        {
            Begin(Screen.Difficulty);
            var c = _g.Content;
            Label(c.Text("title.difficulty.choose"), Palette.BiosBright, 0, 240, ScreenRig.Width, 12);
            int x = ScreenRig.Width / 2;
            var normal = MenuButton(c.Text("title.normal"), "title:normal", a => StartNewGame(DifficultyMode.Normal), x - 110, 270, 100, RowHeight);
            var story = MenuButton(c.Text("title.story"), "title:story", a => StartNewGame(DifficultyMode.Story), x + 10, 270, 100, RowHeight);
            var body = Label("", Palette.BiosText, (ScreenRig.Width - 480) / 2, 310, 480, 40);
            body.Wrap = true;
            _onFocus = b => body.text = c.Text(b == story ? "title.difficulty.story.body" : "title.difficulty.normal.body");
            _nav.Focus(SaveSystem.Load().IsStory ? story : normal);
            _nav.Back = ShowMain;
        }

        void StartNewGame(DifficultyMode mode)
        {
            Leave(() =>
            {
                SaveSystem.NewGame();
                SaveSystem.SetDifficulty(mode);
                GameBootstrap.StartFromMenu(1);
            });
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>A fresh screen: the old one's elements and buttons go.</summary>
        void Begin(Screen screen)
        {
            if (_content != null) UnityEngine.Object.Destroy(_content.gameObject);
            _content = UIBuilder.Rect("Screen " + screen, _panel).Stretch();
            _nav.Clear();
            _nav.Back = null;
            _onFocus = null;
            _ghost = null;
            _credits = null;
            _screen = screen;
            _titleGhost?.Hide();
            _titleGhost = null;
        }

        UiButton MenuButton(string label, string id, Action<CursorAgent> click, int x, int y, int w, int h)
        {
            var b = UiButton.Create(_content, label, click, id);
            ((RectTransform)b.transform).At(x, y, w, h);
            _nav.Add(b);
            return b;
        }

        PixelText Label(string text, Color32 color, int x, int y, int w, int h, TextAlign align = TextAlign.Center, bool bold = false, int scale = 1)
        {
            var t = UIBuilder.Text(_content, text, color, bold);
            t.Scale = scale;
            t.rectTransform.At(x, y, w, h);
            t.Align = align;
            return t;
        }

        void AnimateGhost()
        {
            if (_ghost == null) return;
            // The "second cursor" motif: a red twin of the title slips out of alignment now and then (a 3% roll every 1/60 s, held in between).
            if (!_ghostTimer.Tick(Time.unscaledDeltaTime)) return;
            bool slip = UnityEngine.Random.value < 0.03f;
            _ghost.rectTransform.anchoredPosition = new Vector2(slip ? UnityEngine.Random.Range(-6f, 6f) : 0f, -(slip ? 206f + UnityEngine.Random.Range(-2f, 2f) : 204f));
            _ghost.enabled = slip || UnityEngine.Random.value < 0.3f;
            if (slip && !PauseMenu.IsPaused) _g.Fx.Glitch(0.05f, 0.4f);
        }

        /// <summary>A choice that starts a night: fade to black, stop the drone, then build the new root.</summary>
        void Leave(Action action)
        {
            if (_leaving) return;
            _leaving = true;
            _nav.Frozen = true;
            _leaveAction = action;
            _leaveT = 0f;
            _g.Audio.StopLoop("drone_tension", LeaveSeconds);
            _g.Audio.Play("ui_click");
            _fade = UIBuilder.Solid(_panel, new Color(0f, 0f, 0f, 0f), "Fade");
            _fade.rectTransform.Stretch();
            _fade.raycastTarget = false;
            _fade.transform.SetAsLastSibling();
        }

        bool TickLeave()
        {
            _leaveT += Time.unscaledDeltaTime;
            if (_fade != null) _fade.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_leaveT / LeaveSeconds));
            if (_leaveT < LeaveSeconds) return false;
            var action = _leaveAction;
            _leaveAction = null;
            action?.Invoke();
            return true;
        }
    }
}
