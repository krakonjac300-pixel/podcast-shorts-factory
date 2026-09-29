using System;
using System.Collections.Generic;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.UI
{
    /// <summary>
    /// Keyboard (and Steam Deck D-pad) navigation for a list of menu buttons: Up/Down/Left/Right/Tab move the focus
    /// (wrapping, skipping disabled buttons), Enter or Space presses the focused button, Esc calls <see cref="Back"/>.
    /// Moving the mouse over a button focuses it. The focused button gets a ring just outside it (flat list rows look
    /// pressed instead). Used by the title, its sub-screens, the pause menu and the end cards.
    /// </summary>
    public sealed class MenuNav
    {
        readonly GameServices _g;
        readonly List<UiButton> _buttons = new List<UiButton>();
        Vector2 _lastPointer = new Vector2(float.NaN, float.NaN);
        BevelGraphic _ring;

        /// <summary>The focus ring's colour: light on the black title and end cards, dark on grey windows.</summary>
        public Color32 RingColor = Palette.Dark;

        /// <summary>Esc (null = Esc does nothing here).</summary>
        public Action Back;
        /// <summary>No input while set (the title while a choice fades out).</summary>
        public bool Frozen;
        /// <summary>Freeze while the pause menu is open (every menu but the pause menu itself).</summary>
        public bool RespectPause = true;
        /// <summary>Raised when the focus moves (Records shows the focused achievement's description).</summary>
        public event Action<UiButton> FocusChanged;

        public MenuNav(GameServices g)
        {
            _g = g;
        }

        public UiButton Focused { get; private set; }
        /// <summary>The frame Esc last went Back here (the pause menu must not also open on that same key press).</summary>
        public int LastBackFrame { get; private set; } = -1;
        public IReadOnlyList<UiButton> Buttons => _buttons;

        public void Add(UiButton b)
        {
            if (b == null) return;
            _buttons.Add(b);
        }

        public void Clear()
        {
            _buttons.Clear();
            Focused = null;
            if (_ring != null) UnityEngine.Object.Destroy(_ring.gameObject);
            _ring = null;
        }

        public void Focus(UiButton b)
        {
            if (b == Focused) return;
            if (Focused != null && Focused.Flat) Focused.Toggled = false;
            Focused = b;
            ShowRing(b);
            FocusChanged?.Invoke(b);
        }

        void ShowRing(UiButton b)
        {
            if (b != null && b.Flat)
            {
                // A flat list row shows its focus as a pressed row.
                b.Toggled = true;
                if (_ring != null) _ring.enabled = false;
                return;
            }
            if (b == null)
            {
                if (_ring != null) _ring.enabled = false;
                return;
            }
            if (_ring == null)
            {
                _ring = UIBuilder.Bevel(b.transform, BevelStyle.Outline, "Focus Ring");
                _ring.raycastTarget = false;
            }
            _ring.transform.SetParent(b.transform, false);
            _ring.Fill = RingColor;
            _ring.rectTransform.Stretch(-3, -3, -3, -3);
            _ring.transform.SetAsLastSibling();
            _ring.enabled = true;
        }

        public void Tick()
        {
            _buttons.RemoveAll(x => x == null);
            if (Frozen || (RespectPause && PauseMenu.IsPaused) || _buttons.Count == 0) return;
            // The key press that opened or closed the pause menu this frame belongs to that menu only.
            if (Time.frameCount == PauseMenu.StateChangeFrame) return;
            var input = _g.Input;
            var player = _g.Player;

            // The mouse (or touch) takes the focus when it moves onto a button.
            Vector2 p = player.Position;
            if (p != _lastPointer)
            {
                bool first = float.IsNaN(_lastPointer.x);
                _lastPointer = p;
                if (!first)
                    foreach (var b in _buttons)
                        if (b.Enabled && b.isActiveAndEnabled && b.Hit != null && b.Hit.IsHoveredBy(player)) { Focus(b); break; }
            }

            if (Focused == null || !Focused.Enabled) Move(1);
            bool shift = input.KeyHeld(GameKey.Shift);
            if (input.KeyDown(GameKey.Down) || input.KeyDown(GameKey.Right) || (input.KeyDown(GameKey.Tab) && !shift)) Move(1);
            else if (input.KeyDown(GameKey.Up) || input.KeyDown(GameKey.Left) || (input.KeyDown(GameKey.Tab) && shift)) Move(-1);
            else if ((input.KeyDown(GameKey.Enter) || input.KeyDown(GameKey.Space)) && Focused != null) Focused.Press(player);
            else if (input.KeyDown(GameKey.Escape) && Back != null)
            {
                LastBackFrame = Time.frameCount;
                Back();
            }
        }

        void Move(int step)
        {
            int n = _buttons.Count;
            if (n == 0) return;
            int start = Focused != null ? _buttons.IndexOf(Focused) : (step > 0 ? -1 : 0);
            for (int k = 1; k <= n; k++)
            {
                int i = ((start + step * k) % n + n) % n;
                var b = _buttons[i];
                if (b != null && b.Enabled && b.isActiveAndEnabled)
                {
                    Focus(b);
                    return;
                }
            }
        }
    }
}
