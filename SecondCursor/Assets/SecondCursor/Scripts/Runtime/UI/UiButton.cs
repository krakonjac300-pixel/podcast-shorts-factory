using System;
using SecondCursor.Core.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.UI
{
    /// <summary>
    /// Chunky 3D push button. Looks pressed while a cursor holds it down over it (either cursor - the
    /// entity pressing "No" looks exactly like you pressing it), fires Clicked with the agent responsible.
    /// </summary>
    public sealed class UiButton : MonoBehaviour
    {
        public BevelGraphic Face { get; private set; }
        public PixelText Label { get; private set; }
        public Image IconImage { get; private set; }
        public Interactable Hit { get; private set; }
        RectTransform _content;
        BevelGraphic _defaultRing;
        /// <summary>Phase Q4 (R5): a 1 px inner outline in the colour of the session whose pointer holds this button down (017 or 209).</summary>
        BevelGraphic _holderRing;
        NoticeKind _holder;

        /// <summary>
        /// Phase Q4 (R5): which pointer, if any, is covering this control to keep the player off it (set by the game root: session 017 or Gary
        /// guarding Yes or No). A covered button is drawn sunken with that session's outline, so the control itself says whose hand is on it.
        /// </summary>
        public static Func<Interactable, CursorAgent> GuardOf;

        bool _enabled = true;
        bool _toggled;
        bool _pressedLook;
        bool _flat;

        public event Action<CursorAgent> Clicked;

        /// <summary>Plays when clicked (id from the procedural sound bank; empty = silent).</summary>
        public string ClickSound = "ui_click";

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                Hit.interactable = value;
                if (Label != null) Label.color = value ? Palette.Text : Palette.TextDisabled;
                if (IconImage != null) IconImage.color = value ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }
        }

        /// <summary>Latched "down" look (taskbar button of the active window, toggle buttons).</summary>
        public bool Toggled
        {
            get => _toggled;
            set { _toggled = value; Refresh(); }
        }

        /// <summary>Thick dark ring marking the default button of a dialog.</summary>
        public bool IsDefault
        {
            get => _defaultRing != null && _defaultRing.enabled;
            set
            {
                if (value && _defaultRing == null)
                {
                    _defaultRing = UIBuilder.Bevel(transform, BevelStyle.Outline, "Default Ring");
                    _defaultRing.Fill = Palette.Dark;
                    _defaultRing.rectTransform.Stretch();
                    _defaultRing.transform.SetAsLastSibling();
                }
                if (_defaultRing != null) _defaultRing.enabled = value;
            }
        }

        public static UiButton Create(Transform parent, string label, Action<CursorAgent> onClick = null, string elementId = "", bool bold = false)
        {
            var rt = UIBuilder.Rect("Button " + label, parent);
            var b = rt.gameObject.AddComponent<UiButton>();
            b.Face = rt.gameObject.AddComponent<BevelGraphic>();
            b.Face.Style = BevelStyle.Raised;
            b.Face.raycastTarget = false;
            b._content = UIBuilder.Rect("Content", rt).Stretch(2, 2, 2, 2);
            if (!string.IsNullOrEmpty(label))
            {
                b.Label = UIBuilder.Text(b._content, label, Palette.Text, bold);
                b.Label.rectTransform.Stretch();
                b.Label.Align = TextAlign.Center;
                b.Label.VAlign = TextVAlign.Middle;
            }
            b.Hit = UIBuilder.Hit(rt.gameObject, string.IsNullOrEmpty(elementId) ? "button:" + label : elementId);
            b.Hit.Click += (a, n) =>
            {
                if (!b._enabled) return;
                if (!string.IsNullOrEmpty(b.ClickSound)) Sfx.Play(b.ClickSound, a);
                b.Clicked?.Invoke(a);
            };
            if (onClick != null) b.Clicked += onClick;
            return b;
        }

        /// <summary>Icon-only button (window caption buttons, toolbar).</summary>
        public static UiButton CreateIcon(Transform parent, string spriteName, Action<CursorAgent> onClick = null, string elementId = "")
        {
            var b = Create(parent, null, onClick, elementId);
            b.name = "Button " + spriteName;
            b.IconImage = UIBuilder.Icon(b._content, spriteName, 1, "Glyph");
            b._lastContentSize = new Vector2(-1f, -1f);
            return b;
        }

        /// <summary>Flat until hovered/pressed (menu items, toolbar icons).</summary>
        public bool Flat
        {
            get => _flat;
            set { _flat = value; Refresh(); }
        }

        Vector2 _lastContentSize;

        void Update()
        {
            Refresh();
            LayoutGlyph();
        }

        /// <summary>Centre the glyph on whole pixels (odd/even sizes would otherwise land on half pixels).</summary>
        void LayoutGlyph()
        {
            if (IconImage == null || _content == null) return;
            var size = _content.rect.size;
            if (size == _lastContentSize) return;
            _lastContentSize = size;
            var g = IconImage.rectTransform.sizeDelta;
            IconImage.rectTransform.anchoredPosition = new Vector2(Mathf.Floor((size.x - g.x) * 0.5f), -Mathf.Floor((size.y - g.y) * 0.5f));
        }

        void Refresh()
        {
            if (Hit == null) return;
            bool down = _toggled;
            var holder = NoticeKind.Plain;
            if (!down && _enabled)
            {
                foreach (var a in Hit.HoveredBy)
                {
                    if (!Hit.IsPressedBy(a)) continue;
                    down = true;
                    // Held by another session's pointer: the control says whose hand it is (R5).
                    if (a.IsEntity) holder = a.Actor;
                    break;
                }
            }
            if (GuardOf != null && _enabled)
            {
                var guard = GuardOf(Hit);
                if (guard != null)
                {
                    down = true;
                    holder = guard.Actor;
                }
            }
            if (holder != _holder) SetHolder(holder);
            if (down == _pressedLook && Face.enabled == (!_flat || down || Hit.IsHovered)) return;
            _pressedLook = down;
            Face.Style = down ? BevelStyle.Pressed : BevelStyle.Raised;
            Face.enabled = !_flat || down || Hit.IsHovered;
            if (_content != null)
            {
                float o = down ? 1f : 0f;
                _content.offsetMin = new Vector2(2 + o, 2 - o);
                _content.offsetMax = new Vector2(-2 + o, -2 - o);
            }
        }

        void SetHolder(NoticeKind holder)
        {
            _holder = holder;
            uint rgb = ActorStyle.ZoomFrame(holder);
            if (rgb == 0u)
            {
                if (_holderRing != null) _holderRing.enabled = false;
                return;
            }
            if (_holderRing == null)
            {
                _holderRing = UIBuilder.Bevel(transform, BevelStyle.Outline, "Holder Ring");
                _holderRing.rectTransform.Stretch(1, 1, 1, 1);
            }
            _holderRing.Fill = Palette.FromRgb(rgb);
            _holderRing.transform.SetAsLastSibling();
            _holderRing.enabled = true;
        }

        public void SetLabel(string text)
        {
            if (Label != null) Label.text = text;
        }

        /// <summary>A keyboard press (Enter or Space on the focused menu button): same sound and event as a click.</summary>
        public void Press(CursorAgent by)
        {
            if (!_enabled) return;
            if (!string.IsNullOrEmpty(ClickSound)) Sfx.Play(ClickSound, by);
            Clicked?.Invoke(by);
        }
    }

    /// <summary>
    /// Decoupled sound hook for UI widgets (set by the AudioManager at boot). Widgets live deep in the
    /// hierarchy; this avoids threading an audio reference through every constructor.
    /// </summary>
    public static class Sfx
    {
        public static Action<string, CursorAgent> Handler;

        public static void Play(string id, CursorAgent agent = null) => Handler?.Invoke(id, agent);
    }
}
