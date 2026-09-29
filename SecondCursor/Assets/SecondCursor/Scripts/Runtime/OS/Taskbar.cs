using System.Collections.Generic;
using SecondCursor.Core.Content;
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
        const int ButtonMax = 150;
        const int TaskWidth = 236;
        const int MenuWidth = 28;

        GameServices _g;
        RectTransform _root;
        RectTransform _buttonArea;
        RectTransform _tray;
        PixelText _clock;

        /// <summary>The tray clock in amber (Night 3: the last five minutes before 7:00).</summary>
        public bool ClockAmber;
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

        public UiButton StartButton { get; private set; }
        public StartMenu StartMenu { get; private set; }

        public static Taskbar Create(GameServices g)
        {
            DefineTraySprites();
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
            var startLabel = UIBuilder.Text(bar.StartButton.transform.GetChild(0), "Nexus", Palette.Text, true);
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
            ((RectTransform)bar._task.transform).At(ScreenRig.Width - 104 - MenuWidth - TaskWidth, 4, TaskWidth - 4, 22);
            if (bar._task.Label != null)
            {
                bar._task.Label.Align = TextAlign.Left;
                bar._task.Label.rectTransform.Stretch(20, 0, 2, 0);
            }
            var taskIcon = UIBuilder.Icon(bar._task.transform.GetChild(0), "icon_task_active", 1);
            taskIcon.rectTransform.anchoredPosition = new Vector2(1f, -1f);
            bar._task.gameObject.SetActive(false);
            g.Tasks.TaskActivated += t => bar._taskFlash = 2.4f;

            // Menu button: the pause menu is reachable with the mouse alone (Steam Deck, mouse-only players).
            var menu = UiButton.Create(root, "||", a => Game.PauseMenu.Current?.OpenMenu(), "taskbar.menu", true);
            ((RectTransform)menu.transform).At(ScreenRig.Width - 104 - MenuWidth, 4, MenuWidth - 4, 22);

            bar._buttonArea = UIBuilder.Rect("Window Buttons", root).Stretch(72, 4, 104 + MenuWidth + TaskWidth, 2);

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
            _clock.text = _g.Clock.Format12();
            Color32 clockColor = ClockAmber ? AmberClock : Palette.Text;
            if (!_clock.color.Equals((Color)clockColor)) _clock.color = clockColor;
            if (_dirty) Rebuild();
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
        }

        void UpdateTask()
        {
            if (_taskRevision != _g.Tasks.Revision)
            {
                _taskRevision = _g.Tasks.Revision;
                var current = _g.Tasks.Current;
                _task.gameObject.SetActive(current != null);
                if (current != null)
                {
                    string progress = current.Goal > 1 ? " (" + current.Progress + "/" + current.Goal + ")" : "";
                    _task.SetLabel(Ellipsize("Task: " + current.Title + progress, TaskWidth - 30));
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
            }

            float area = _buttonArea.rect.width;
            int n = Mathf.Max(1, visible.Count);
            int bw = Mathf.Min(ButtonMax, Mathf.FloorToInt((area - (n - 1) * 3) / n));
            for (int i = 0; i < visible.Count; i++)
            {
                var w = visible[i];
                if (!_buttons.TryGetValue(w, out var b) || b == null)
                {
                    b = CreateButton(w);
                    _buttons[w] = b;
                }
                ((RectTransform)b.transform).At(i * (bw + 3), 0, bw, 22);
                b.SetLabel(Ellipsize(w.Title, bw - 26));
            }
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
            if (!string.IsNullOrEmpty(w.IconSprite))
            {
                var icon = UIBuilder.Icon(b.transform.GetChild(0), w.IconSprite, 1);
                icon.rectTransform.anchoredPosition = new Vector2(1f, -1f);
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

        static string Ellipsize(string s, int width)
        {
            if (PixelFont.MeasureLine(s, false) <= width) return s;
            while (s.Length > 1 && PixelFont.MeasureLine(s + "...", false) > width) s = s.Substring(0, s.Length - 1);
            return s + "...";
        }
    }
}
