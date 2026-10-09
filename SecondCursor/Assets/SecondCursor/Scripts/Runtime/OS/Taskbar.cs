using System.Collections.Generic;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.OS
{
    /// <summary>
    /// Bottom bar: NEXUS start button, one button per open window, the current Work Queue task, and a tray
    /// with pointing-device icons and the shift clock. The tray shows ONE mouse normally... two once the
    /// second cursor is here, and three when Gary is.
    /// </summary>
    public sealed class Taskbar : MonoBehaviour
    {
        const int Height = WindowManager.TaskbarHeight;
        const int MenuWidth = 28;
        /// <summary>Left and right edges of the strip the window buttons and the task button share (px from each side of the bar).</summary>
        const int StripLeft = 72, TrayWidth = 104;
        const int StripTotal = ScreenRig.Width - StripLeft - TrayWidth - MenuWidth;

        GameServices _g;
        RectTransform _root;
        RectTransform _buttonArea;
        RectTransform _tray;
        PixelText _clock;

        /// <summary>The tray clock in amber (Night 3: the last five minutes before 7:00).</summary>
        public bool ClockAmber;
        string _clockOverride;
        float _clockOverrideUntil;
        int _clockMinuteShown = int.MinValue;
        string _clockText = "";

        /// <summary>Phase Q1 (owner 4): the tray clock reads <paramref name="text"/> for <paramref name="seconds"/>, then the real time again.</summary>
        public void ShowClockOnce(string text, float seconds)
        {
            _clockOverride = text;
            _clockOverrideUntil = Time.time + seconds;
        }
        /// <summary>A deep amber that still reads on the grey tray.</summary>
        static readonly Color32 AmberClock = new Color32(0xA8, 0x62, 0x00, 0xFF);
        readonly List<Image> _mice = new List<Image>();
        readonly Dictionary<OSWindow, UiButton> _buttons = new Dictionary<OSWindow, UiButton>();
        readonly List<OSWindow> _order = new List<OSWindow>();
        bool _dirty = true;
        int _deviceCount = 1;
        float _deviceFlash;
        int _blinkIndex = -1;
        float _blinkUntil;
        UiButton _task;
        int _taskRevision = -1;
        float _taskFlash;
        // Phase Q4 (R3, R10): the deadline chip on the task button, the widths the strip is shared in, a tooltip for icon-only buttons.
        int _taskWidth = TaskbarLayout.TaskDefault;
        bool _chipOn;
        int _chipSecond = -2;
        RectTransform _chip;
        Image _chipFill;
        PixelText _chipText;
        Image _taskIcon;
        bool _iconOnly;
        RectTransform _tip;
        PixelText _tipText;
        readonly Dictionary<OSWindow, Image> _icons = new Dictionary<OSWindow, Image>();
        readonly Dictionary<OSWindow, Image> _edges = new Dictionary<OSWindow, Image>();

        public UiButton StartButton { get; private set; }
        public StartMenu StartMenu { get; private set; }

        public static Taskbar Create(GameServices g)
        {
            DefineTraySprites();
            ActorSprites.Define();
            var root = UIBuilder.Rect("Taskbar", g.Layers.Taskbar).BottomStrip(0, Height);
            var bar = root.gameObject.AddComponent<Taskbar>();
            bar._g = g;
            bar._root = root;

            var face = root.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Flat;
            face.raycastTarget = false;
            var topLine = UIBuilder.Solid(root, Palette.Highlight, "Top Highlight");
            topLine.rectTransform.TopStrip(1, 1);
            var topLine2 = UIBuilder.Solid(root, Palette.Midlight, "Top Midlight");
            topLine2.rectTransform.TopStrip(0, 1);
            UIBuilder.Hit(root.gameObject, "taskbar");

            bar.StartButton = UiButton.Create(root, "", null, "taskbar.start");
            ((RectTransform)bar.StartButton.transform).At(2, 4, 62, 22);
            var logo = UIBuilder.Icon(bar.StartButton.transform.GetChild(0), "icon_nexus", 1);
            logo.rectTransform.anchoredPosition = new Vector2(2f, -1f);
            var startLabel = UIBuilder.Text(bar.StartButton.transform.GetChild(0), g.Content.Text("start.button", "Nexus"), Palette.Text, true);
            startLabel.rectTransform.Stretch(21, 0, 0, 0);
            startLabel.VAlign = TextVAlign.Middle;
            bar.StartMenu = new StartMenu(g, bar.StartButton);
            bar.StartButton.Clicked += a => bar.StartMenu.Toggle(a);

            var sep = UIBuilder.Bevel(root, BevelStyle.Etched, "Separator");
            sep.rectTransform.At(67, 5, 2, 20);

            bar._tray = UIBuilder.Rect("Tray", root).BottomRight(3, 3, 96, 22);
            var trayFace = bar._tray.gameObject.AddComponent<BevelGraphic>();
            trayFace.Style = BevelStyle.StatusField;
            trayFace.raycastTarget = false;
            UIBuilder.Hit(bar._tray.gameObject, "taskbar.tray");
            bar._clock = UIBuilder.Text(bar._tray, "", Palette.Text);
            bar._clock.rectTransform.Stretch(40, 0, 4, 0);
            bar._clock.Align = TextAlign.Right;
            bar._clock.VAlign = TextVAlign.Middle;
            for (int i = 0; i < 3; i++)
            {
                var m = UIBuilder.Icon(bar._tray, "tray_mouse", 1, "Mouse " + i);
                m.rectTransform.anchoredPosition = new Vector2(3 + i * 10, -5f);
                m.enabled = i == 0;
                bar._mice.Add(m);
            }

            // The current task, always in view even with the Work Queue closed; click it to open the queue.
            bar._task = UiButton.Create(root, " ", a => g.Apps.Launch(AppIds.WorkQueue, a), "taskbar.task");
            if (bar._task.Label != null)
            {
                bar._task.Label.Align = TextAlign.Left;
                bar._task.Label.rectTransform.Stretch(20, 0, 2, 0);
            }
            var taskIcon = UIBuilder.Icon(bar._task.transform.GetChild(0), "icon_task_active", 1);
            taskIcon.rectTransform.anchoredPosition = new Vector2(1f, -1f);
            bar._taskIcon = taskIcon;
            bar.BuildChip();
            bar._task.gameObject.SetActive(false);
            g.Tasks.TaskActivated += t => bar._taskFlash = 2.4f;

            // Menu button: the pause menu is reachable with the mouse alone (Steam Deck, mouse-only players).
            var menu = UiButton.Create(root, "||", a => Game.PauseMenu.Current?.OpenMenu(), "taskbar.menu", true);
            ((RectTransform)menu.transform).At(ScreenRig.Width - 104 - MenuWidth, 4, MenuWidth - 4, 22);

            bar._buttonArea = UIBuilder.Rect("Window Buttons", root).Stretch(StripLeft, 4, TrayWidth + MenuWidth + TaskbarLayout.TaskDefault, 2);
            bar.ApplyWidths();

            g.Windows.Changed += w => bar._dirty = true;
            g.Windows.Opened += w => { bar._order.Add(w); bar._dirty = true; };
            g.Windows.ClosedEvent += (w, a) => { bar._order.Remove(w); bar._dirty = true; };
            g.Windows.TaskbarRectOf = w => bar._buttons.TryGetValue(w, out var b) && b != null ? ((RectTransform)b.transform).WorldRect() : (Rect?)null;
            return bar;
        }

        static void DefineTraySprites()
        {
            SpriteLibrary.Define("tray_mouse", 0, 0,
                "..KKK..",
                ".KWKWK.",
                "KWWKWWK",
                "KWWKWWK",
                "KKKKKKK",
                "KWWWWWK",
                "KWWWWWK",
                "KWWWWWK",
                ".KWWWK.",
                "..KKK..");
        }

        /// <summary>Number of pointing devices shown in the tray (1 = just you).</summary>
        public int PointingDevices
        {
            get => _deviceCount;
            set
            {
                _deviceCount = Mathf.Clamp(value, 1, _mice.Count);
                for (int i = 0; i < _mice.Count; i++) _mice[i].enabled = i < _deviceCount;
            }
        }

        public UiButton ButtonFor(OSWindow w) => _buttons.TryGetValue(w, out var b) ? b : null;

        /// <summary>The second mouse in the tray blinks for a moment (a new device just arrived).</summary>
        public void FlashDevices(float seconds = 1.6f) => _deviceFlash = seconds;

        /// <summary>
        /// One tray mouse (1-based: 3 = Gary's) blinks for <paramref name="seconds"/>, or keeps blinking while
        /// the device is unwell (0 = until another call; a negative value stops it).
        /// </summary>
        public void BlinkDevice(int index, float seconds = 1.6f)
        {
            if (seconds < 0f)
            {
                if (_blinkIndex == index - 1) _blinkIndex = -1;
                PointingDevices = _deviceCount;
                return;
            }
            _blinkIndex = index - 1;
            _blinkUntil = seconds <= 0f ? float.MaxValue : Time.unscaledTime + seconds;
        }

        void Update()
        {
            if (_g == null) return;
            if (_clockOverride != null && Time.time >= _clockOverrideUntil) _clockOverride = null;
            // Phase Q4 (CH7): the tray clock's text is made once a game minute, not every frame.
            if (_clockOverride != null) _clock.text = _clockOverride;
            else
            {
                int minute = _g.Clock.TotalMinutes;
                if (minute != _clockMinuteShown)
                {
                    _clockMinuteShown = minute;
                    _clockText = _g.Clock.Format12();
                }
                _clock.text = _clockText;
            }
            Color32 clockColor = ClockAmber ? AmberClock : Palette.Text;
            if (!_clock.color.Equals((Color)clockColor)) _clock.color = clockColor;
            // Phase K: the buttons never reflow under the player's pointer (a window opened or closed by another session moved the
            // button the blind tester was about to click); they catch up as soon as the pointer leaves them. Phase Q4: the task button, which
            // grows over them while its deadline chip shows, waits for the pointer too.
            if (_dirty && !PointerOnStrip()) Rebuild();
            UpdateTask();
            if (_deviceFlash > 0f)
            {
                _deviceFlash -= Time.unscaledDeltaTime;
                bool on = _deviceFlash <= 0f || (_deviceFlash * 4f) % 1f < 0.5f;
                for (int i = 1; i < _mice.Count; i++) _mice[i].enabled = i < _deviceCount && on;
            }
            if (_blinkIndex >= 0 && _blinkIndex < _mice.Count)
            {
                bool on = Time.unscaledTime >= _blinkUntil || (Time.unscaledTime * 2.5f) % 1f < 0.5f;
                _mice[_blinkIndex].enabled = _blinkIndex < _deviceCount && on;
                if (Time.unscaledTime >= _blinkUntil) _blinkIndex = -1;
            }
            foreach (var kv in _buttons)
            {
                if (kv.Key == null || kv.Value == null) continue;
                kv.Value.Toggled = kv.Key == _g.Windows.Active && !kv.Key.IsMinimized;
            }
            UpdateTip();
        }

        bool PointerOnStrip()
        {
            var p = _g.Player.Position;
            return _buttonArea.WorldRect().Contains(p) || (_task != null && _task.gameObject.activeSelf && ((RectTransform)_task.transform).WorldRect().Contains(p));
        }

        // ------------------------------------------------------------------ the strip: widths, the deadline chip, icon-only buttons

        /// <summary>Puts the task button and the window-button area at the widths of the current plan.</summary>
        void ApplyWidths()
        {
            ((RectTransform)_task.transform).At(ScreenRig.Width - TrayWidth - MenuWidth - _taskWidth, 4, _taskWidth - 4, 22);
            _buttonArea.Stretch(StripLeft, 4, TrayWidth + MenuWidth + _taskWidth, 2);
        }

        /// <summary>The red chip that replaces the task button's play triangle while its deadline is under two real minutes.</summary>
        void BuildChip()
        {
            var content = _task.transform.GetChild(0);
            _chip = UIBuilder.Rect("Deadline Chip", content);
            _chip.anchorMin = _chip.anchorMax = new Vector2(0f, 1f);
            _chip.pivot = new Vector2(0f, 1f);
            _chip.sizeDelta = new Vector2(DeadlineChip.ChipWidth, DeadlineChip.ChipHeight);
            _chip.anchoredPosition = new Vector2(0f, -1f);
            var border = UIBuilder.Solid(_chip, Palette.Dark, "Chip Border");
            border.rectTransform.Stretch();
            _chipFill = UIBuilder.Solid(_chip, Palette.Red, "Chip Fill");
            _chipFill.rectTransform.Stretch(1, 1, 1, 1);
            _chipText = UIBuilder.Text(_chip, "", Palette.Highlight, true, "Chip Text");
            _chipText.Align = TextAlign.Center;
            _chipText.VAlign = TextVAlign.Middle;
            _chipText.rectTransform.Stretch(1, 0, 1, 0);
            _chip.gameObject.SetActive(false);
        }

        /// <summary>The chip's state each frame: shown under two real minutes, counting down in whole seconds, blinking in the last 15 (never with Reduce flashing).</summary>
        void UpdateChip(float real)
        {
            bool on = DeadlineChip.Shows(real, _chipOn);
            if (on != _chipOn)
            {
                _chipOn = on;
                _dirty = true;          // the buttons collapse to icons and the task button widens, once the pointer is clear of them
                _taskRevision = -1;     // the task line is fitted again
            }
            if (!_chipOn)
            {
                if (_chip.gameObject.activeSelf) _chip.gameObject.SetActive(false);
                if (_taskIcon != null) _taskIcon.enabled = true;
                return;
            }
            if (!_chip.gameObject.activeSelf) _chip.gameObject.SetActive(true);
            if (_taskIcon != null) _taskIcon.enabled = false;
            int second = Mathf.CeilToInt(Mathf.Max(0f, real));
            if (second != _chipSecond)
            {
                _chipSecond = second;
                _chipText.text = DeadlineChip.Text(real);
            }
            bool dark = DeadlineChip.BlinkDark(real, _g.Fx != null && _g.Fx.ReduceFlashing, Time.unscaledTime);
            _chipFill.color = dark ? (Color)Palette.Dark : (Color)Palette.Red;
        }

        /// <summary>Phase Q4 (R10): a button narrower than 64 px shows only its icon; the title shows in a small note while the pointer is on it.</summary>
        void UpdateTip()
        {
            OSWindow hovered = null;
            if (_iconOnly)
                foreach (var kv in _buttons)
                    if (kv.Key != null && kv.Value != null && kv.Value.Hit != null && kv.Value.Hit.IsHoveredBy(_g.Player)) { hovered = kv.Key; break; }
            if (hovered == null)
            {
                if (_tip != null && _tip.gameObject.activeSelf) _tip.gameObject.SetActive(false);
                return;
            }
            if (_tip == null)
            {
                _tip = UIBuilder.Rect("Taskbar Tip", _g.Layers.Popups);
                _tip.anchorMin = _tip.anchorMax = Vector2.zero;
                _tip.pivot = Vector2.zero;
                var face = _tip.gameObject.AddComponent<BevelGraphic>();
                face.Style = BevelStyle.Window;
                face.Fill = Palette.Tooltip;
                face.raycastTarget = false;
                _tipText = UIBuilder.Text(_tip, "", Palette.Text);
                _tipText.VAlign = TextVAlign.Middle;
                _tipText.rectTransform.Stretch(5, 0, 5, 0);
            }
            string title = hovered.Title ?? "";
            _tipText.text = title;
            int w = PixelFont.MeasureLine(title, false) + 12;
            _tip.sizeDelta = new Vector2(w, 18);
            var rect = ((RectTransform)_buttons[hovered].transform).WorldRect();
            _tip.anchoredPosition = new Vector2(Mathf.Clamp(rect.x, 2f, ScreenRig.Width - w - 2f), Height + 2f);
            _tip.gameObject.SetActive(true);
        }

        /// <summary>Minutes left at or below which a due task's button text turns red.</summary>
        const int DueSoonMinutes = 5;
        int _taskMinute = -1;

        void UpdateTask()
        {
            var current = _g.Tasks.Current;
            bool timed = current != null && !string.IsNullOrEmpty(current.Data.deadline);
            // Phase Q4 (R3): the chip counts real seconds, so it is read every frame (a timed task only).
            float realNow = timed ? Core.Tasks.TaskDeadline.RealSeconds(current.Data.deadline, _g.Clock.ExactMinutes, _g.Clock.Rate, _g.Clock.Frozen) : -1f;
            UpdateChip(realNow);
            if (_taskRevision != _g.Tasks.Revision || (timed && _taskMinute != _g.Clock.TotalMinutes))
            {
                _taskRevision = _g.Tasks.Revision;
                _taskMinute = _g.Clock.TotalMinutes;
                _task.gameObject.SetActive(current != null);
                if (current != null)
                {
                    string progress = current.Goal > 1 ? " (" + current.ProgressText + ")" : "";
                    // Phase H: a task with a due time counts down on the button (the clock's speed changes during a night).
                    int left = timed ? Core.Tasks.TaskDeadline.MinutesLeft(current.Data.deadline, _g.Clock.TotalMinutes) : -1;
                    // Phase K: in real time where the clock's speed allows ("About 1 min left: ..."), not only in shift minutes.
                    float real = realNow;
                    string text = left > 0 && real >= 0f ? _g.Content.Format("taskbar.due.real", current.Title + progress, Capital(Core.Tasks.TaskDeadline.Approx(real)))
                        : left > 0 ? _g.Content.Format("taskbar.due", current.Title + progress, left)
                        : left == 0 ? _g.Content.Format("taskbar.duenow", current.Title + progress)
                        : "Task: " + current.Title + progress;
                    // The chip stands where the triangle was: the line starts after it.
                    int lead = _chipOn ? DeadlineChip.ChipWidth + 6 : 20;
                    if (_task.Label != null) _task.Label.rectTransform.Stretch(lead, 0, 2, 0);
                    _task.SetLabel(Ellipsize(text, _taskWidth - 10 - lead));
                    if (_task.Label != null) _task.Label.color = left >= 0 && left <= DueSoonMinutes ? (Color)Palette.Red : (Color)Palette.Text;
                }
            }
            // A new task blinks a few times so the eye finds it.
            if (_taskFlash > 0f)
            {
                _taskFlash -= Time.unscaledDeltaTime;
                _task.Toggled = _taskFlash > 0f && (_taskFlash * 3f) % 1f > 0.5f;
            }
        }

        void Rebuild()
        {
            _dirty = false;
            _order.RemoveAll(w => w == null || w.IsClosed);
            var visible = new List<OSWindow>();
            foreach (var w in _order) if (w.ShowInTaskbar) visible.Add(w);

            // Remove buttons for windows that are gone.
            var stale = new List<OSWindow>();
            foreach (var kv in _buttons) if (kv.Key == null || kv.Key.IsClosed || !visible.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var w in stale)
            {
                if (_buttons.TryGetValue(w, out var b) && b != null)
                {
                    b.gameObject.SetActive(false);
                    Destroy(b.gameObject);
                }
                _buttons.Remove(w);
                _icons.Remove(w);
                _edges.Remove(w);
            }

            // Phase Q4 (R10): the strip is shared by a plan: icon-only buttons when they would be under 64 px (or a deadline chip shows), and the
            // task button never under 236 px (up to 360 while the chip shows).
            var plan = TaskbarLayout.Plan(StripTotal, visible.Count, _chipOn);
            _iconOnly = plan.IconOnly;
            if (plan.TaskWidth != _taskWidth)
            {
                _taskWidth = plan.TaskWidth;
                ApplyWidths();
            }
            _taskRevision = -1;   // the task line is fitted to the width it has now
            int bw = plan.ButtonWidth;
            for (int i = 0; i < visible.Count; i++)
            {
                var w = visible[i];
                if (!_buttons.TryGetValue(w, out var b) || b == null)
                {
                    b = CreateButton(w);
                    _buttons[w] = b;
                }
                ((RectTransform)b.transform).At(i * (bw + TaskbarLayout.ButtonGap), 0, bw, 22);
                SyncButton(w, b, plan.IconOnly, bw);
            }
        }

        /// <summary>A window's button: its title (or only its icon), the session's pointer as its icon and a 2 px edge in the session's colour.</summary>
        void SyncButton(OSWindow w, UiButton b, bool iconOnly, int width)
        {
            if (b.Label != null)
            {
                b.Label.enabled = !iconOnly;
                if (!iconOnly) b.SetLabel(Ellipsize(w.Title, width - 26));
            }
            bool session = ActorStyle.IsSession(w.Actor);
            if (_icons.TryGetValue(w, out var icon) && icon != null)
            {
                if (session) icon.sprite = ActorSprites.For(ActorSprites.Tiny, w.Actor);
                var size = session ? SpriteLibrary.Size(ActorSprites.Tiny) : SpriteLibrary.Size(string.IsNullOrEmpty(w.IconSprite) ? "icon_info" : w.IconSprite);
                icon.rectTransform.sizeDelta = new Vector2(size.x, size.y);
                // Centred in a 24 px button, else at the left edge where the title starts.
                float x = iconOnly ? Mathf.Floor((width - 4 - size.x) * 0.5f) : (session ? 5f : 1f);
                icon.rectTransform.anchoredPosition = new Vector2(x, session ? -4f : -1f);
            }
            uint edge = ActorStyle.TaskbarEdge(w.Actor);
            if (edge == 0u)
            {
                if (_edges.TryGetValue(w, out var old) && old != null) old.enabled = false;
                return;
            }
            if (!_edges.TryGetValue(w, out var line) || line == null)
            {
                line = UIBuilder.Solid(b.transform, Palette.FromRgb(edge), "Actor Edge");
                _edges[w] = line;
            }
            line.color = Palette.FromRgb(edge);
            line.enabled = true;
            line.rectTransform.anchorMin = Vector2.zero;
            line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.pivot = Vector2.zero;
            line.rectTransform.offsetMin = new Vector2(3f, 2f);
            line.rectTransform.offsetMax = new Vector2(-3f, 4f);
        }

        UiButton CreateButton(OSWindow w)
        {
            var b = UiButton.Create(_buttonArea, string.IsNullOrEmpty(w.Title) ? " " : w.Title, null, "taskbar.window:" + w.AppId);
            b.ClickSound = "";
            if (b.Label != null)
            {
                b.Label.Align = TextAlign.Left;
                b.Label.rectTransform.Stretch(20, 0, 2, 0);
            }
            if (!string.IsNullOrEmpty(w.IconSprite) || ActorStyle.IsSession(w.Actor))
            {
                var icon = UIBuilder.Icon(b.transform.GetChild(0), string.IsNullOrEmpty(w.IconSprite) ? "icon_info" : w.IconSprite, 1);
                icon.rectTransform.anchoredPosition = new Vector2(1f, -1f);
                _icons[w] = icon;
            }
            b.Clicked += a =>
            {
                if (w == null || w.IsClosed) return;
                if (w.IsMinimized) w.Restore(a);
                else if (_g.Windows.Active == w) w.Minimize(a);
                else _g.Windows.Focus(w, a);
            };
            return b;
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string Ellipsize(string s, int width)
        {
            // Phase S (Large text: taskbar entries were tiny once six windows were open): the label is cut to fit at the Reading text size, so the
            // button keeps that size (it used to be cut at normal size, and the button then shrank the text to fit).
            float f = Mathf.Min(Game.DisplaySettings.ReadingFactor, 2f);
            if (PixelFont.MeasureLine(s, false, f) <= width) return s;
            while (s.Length > 1 && PixelFont.MeasureLine(s + "...", false, f) > width) s = s.Substring(0, s.Length - 1);
            return s + "...";
        }
    }
}
