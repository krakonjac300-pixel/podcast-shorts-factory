using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.OS
{
    [Flags]
    public enum WindowFlags
    {
        None = 0,
        CanMinimize = 1,
        CanMaximize = 2,
        CanClose = 4,
        Resizable = 8,
        AlwaysOnTop = 16,
        NoTaskbar = 32,
        Standard = CanMinimize | CanMaximize | CanClose | Resizable,
        Dialog = CanClose | AlwaysOnTop | NoTaskbar,
    }

    /// <summary>
    /// A NEXUS OS window: bevelled frame, gradient caption with icon and minimize/maximize/close, client
    /// area for app content, optional resize grip. Exposes logical operations (Open/Close/Focus/Move/
    /// Minimize/Lock/Element lookup) so the second cursor can target it by meaning, not coordinates.
    /// Layout units: x from the screen's left, y from the screen's TOP (desktop convention).
    /// </summary>
    public sealed class OSWindow : MonoBehaviour
    {
        public const int CaptionHeight = 18;
        public const int MinWidth = 140;
        public const int MinHeight = 80;

        public string AppId { get; private set; }
        public string Title { get; private set; }
        public string IconSprite { get; private set; }
        public WindowFlags Flags { get; private set; }
        public RectTransform Rect { get; private set; }
        public RectTransform Client { get; private set; }
        public Interactable TitleHit { get; private set; }
        public Interactable FrameHit { get; private set; }
        public UiButton CloseButton { get; private set; }
        public UiButton MinimizeButton { get; private set; }
        public UiButton MaximizeButton { get; private set; }
        public WindowManager Manager { get; private set; }

        /// <summary>Any object the owning app wants to hang on the window (the app instance).</summary>
        [System.NonSerialized] public object Owner;

        public bool IsActive { get; private set; }
        public bool IsMinimized { get; private set; }
        public bool IsMaximized { get; private set; }
        public bool IsClosed { get; private set; }
        /// <summary>A locked window cannot be moved or closed by the player (entity ability).</summary>
        public bool Locked;
        /// <summary>
        /// Phase H: how much a new window should avoid covering this one, per pixel (1 = any window; the Work Queue asks
        /// for more, it holds the player's instructions).
        /// </summary>
        [System.NonSerialized] public float CoverCost = 1f;
        /// <summary>
        /// Phase H: a part of this window new windows keep clear of at all costs (the Work Orders' Approve and Reject), as a
        /// rectangle measured from the window's top-left corner (desktop px, y down). Empty = none. Phase I: the buttons moved
        /// to the top of the form, where the windows that open next (Personnel) cannot land on them.
        /// </summary>
        [System.NonSerialized] public Rect KeepVisible;
        public bool AlwaysOnTop => (Flags & WindowFlags.AlwaysOnTop) != 0;
        public bool ShowInTaskbar => (Flags & WindowFlags.NoTaskbar) == 0;

        public event Action<OSWindow, CursorAgent> Closed;
        public event Action<OSWindow, CursorAgent> Moved;
        public event Action<OSWindow> Resized;

        BevelGraphic _caption;
        PixelText _titleText;
        Image _titleIcon;
        CursorAgent _dragOwner;
        Vector2 _dragGrab;
        CursorAgent _resizeOwner;
        Vector2 _resizeStartSize;
        Vector2 _resizeStartPointer;
        Rect _restore;
        float _shakeTime;
        float _shakeAmp;
        Vector2 _shakeBase;
        readonly Dictionary<string, Interactable> _elements = new Dictionary<string, Interactable>();

        internal static OSWindow Build(WindowManager wm, RectTransform layer, string appId, string title, string icon,
            int x, int y, int w, int h, WindowFlags flags)
        {
            var rt = UIBuilder.Rect("Window " + title, layer);
            var win = rt.gameObject.AddComponent<OSWindow>();
            win.Manager = wm;
            win.Rect = rt;
            win.AppId = appId;
            win.IconSprite = icon;
            win.Flags = flags;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(Mathf.Max(MinWidth, w), Mathf.Max(MinHeight, h));
            win.SetTopLeft(new Vector2(x, y));

            var frame = rt.gameObject.AddComponent<BevelGraphic>();
            frame.Style = BevelStyle.Window;
            frame.raycastTarget = false;
            win.FrameHit = UIBuilder.Hit(rt.gameObject, "window:" + appId);

            // Caption
            win._caption = UIBuilder.Bevel(rt, BevelStyle.Gradient, "Caption");
            win._caption.rectTransform.TopStrip(3, CaptionHeight, 3, 3);
            win.TitleHit = UIBuilder.Hit(win._caption.gameObject, "caption");
            win.TitleHit.draggable = true;
            win.TitleHit.dragThreshold = 4f;
            win.TitleHit.DragBegin += win.OnCaptionDragBegin;
            win.TitleHit.Drag += win.OnCaptionDrag;
            win.TitleHit.DragEnd += a => { if (win._dragOwner == a) win._dragOwner = null; };
            win.TitleHit.Click += (a, n) => { if (n == 2 && (win.Flags & WindowFlags.CanMaximize) != 0) win.ToggleMaximize(a); };

            int textLeft = 4;
            if (!string.IsNullOrEmpty(icon))
            {
                win._titleIcon = UIBuilder.Icon(win._caption.rectTransform, icon, 1, "Caption Icon");
                win._titleIcon.rectTransform.anchoredPosition = new Vector2(2f, -1f);
                textLeft = 21;
            }
            win._titleText = UIBuilder.Text(win._caption.rectTransform, title, Palette.TitleText, true, "Caption Text");
            win._titleText.rectTransform.Stretch(textLeft, 0, 58, 0);
            win._titleText.VAlign = TextVAlign.Middle;

            int bx = 2; // from the right edge of the caption
            if ((flags & WindowFlags.CanClose) != 0)
            {
                win.CloseButton = UiButton.CreateIcon(win._caption.rectTransform, "glyph_close", a => win.RequestClose(a), "window.close");
                ((RectTransform)win.CloseButton.transform).TopRight(bx, 2, 16, 14);
                bx += 18;
            }
            if ((flags & WindowFlags.CanMaximize) != 0)
            {
                win.MaximizeButton = UiButton.CreateIcon(win._caption.rectTransform, "glyph_maximize", a => win.ToggleMaximize(a), "window.maximize");
                ((RectTransform)win.MaximizeButton.transform).TopRight(bx, 2, 16, 14);
                bx += 16;
            }
            if ((flags & WindowFlags.CanMinimize) != 0)
            {
                win.MinimizeButton = UiButton.CreateIcon(win._caption.rectTransform, "glyph_minimize", a => win.Minimize(a), "window.minimize");
                ((RectTransform)win.MinimizeButton.transform).TopRight(bx, 2, 16, 14);
            }

            if ((flags & WindowFlags.Resizable) != 0)
            {
                var grip = UIBuilder.Rect("Resize Grip", rt).BottomRight(3, 3, 12, 12);
                var gi = UIBuilder.Icon(grip, "glyph_resize_grip", 1);
                gi.rectTransform.anchoredPosition = Vector2.zero;
                var gh = UIBuilder.Hit(grip.gameObject, "window.resize", CursorShape.Move);
                gh.draggable = true;
                gh.dragThreshold = 1f;
                gh.DragBegin += a =>
                {
                    if (win.IsMaximized) return;
                    win._resizeOwner = a;
                    win._resizeStartSize = win.Rect.sizeDelta;
                    win._resizeStartPointer = a.Position;
                };
                gh.Drag += (a, d) =>
                {
                    if (win._resizeOwner != a) return;
                    Vector2 delta = a.Position - win._resizeStartPointer;
                    win.SetSize(win._resizeStartSize + new Vector2(delta.x, -delta.y));
                };
                gh.DragEnd += a => { if (win._resizeOwner == a) win._resizeOwner = null; };
            }

            win.Client = UIBuilder.Rect("Client", rt).Stretch(4, 4 + CaptionHeight + 1, 4, 4);
            UIBuilder.Clip(win.Client);

            win.SetTitle(title);
            win.SetActive(false);
            return win;
        }

        // ------------------------------------------------------------ geometry

        /// <summary>Top-left in desktop coordinates (x from left, y from top).</summary>
        public Vector2 TopLeft => new Vector2(Rect.anchoredPosition.x, -Rect.anchoredPosition.y);
        public Vector2 Size => Rect.sizeDelta;
        public Rect WorldRect => Rect.WorldRect();

        public void SetTopLeft(Vector2 topLeft)
        {
            Rect.anchoredPosition = new Vector2(Mathf.Floor(topLeft.x + 0.5f), -Mathf.Floor(topLeft.y + 0.5f));
        }

        /// <summary>Move so the top-left lands at the given desktop position (clamped so the caption stays reachable).</summary>
        public void MoveTo(Vector2 topLeft, CursorAgent by = null)
        {
            if (IsMaximized) return;
            StopShake();
            var before = TopLeft;
            SetTopLeft(ClampTopLeft(topLeft));
            if (TopLeft != before) Moved?.Invoke(this, by);
        }

        public void MoveBy(Vector2 desktopDelta, CursorAgent by = null) => MoveTo(TopLeft + desktopDelta, by);

        public Vector2 ClampTopLeft(Vector2 p)
        {
            float w = Size.x;
            float x = Mathf.Clamp(p.x, -w + 60f, ScreenRig.Width - 60f);
            float y = Mathf.Clamp(p.y, 0f, ScreenRig.Height - WindowManager.TaskbarHeight - CaptionHeight - 4f);
            return new Vector2(x, y);
        }

        public void SetSize(Vector2 size)
        {
            var s = new Vector2(Mathf.Round(Mathf.Clamp(size.x, MinWidth, ScreenRig.Width)), Mathf.Round(Mathf.Clamp(size.y, MinHeight, ScreenRig.Height - WindowManager.TaskbarHeight)));
            if (s == Rect.sizeDelta) return;
            Rect.sizeDelta = s;
            Resized?.Invoke(this);
        }

        public Vector2 CaptionCenter => _caption.rectTransform.WorldCenter();

        // ------------------------------------------------------------ dragging

        void OnCaptionDragBegin(CursorAgent a)
        {
            if (IsMaximized) return;
            if (Locked && a.IsPlayer) { Shake(0.25f, 2f); return; }
            // Last grab wins: if the other cursor grabs the caption mid-drag, the window goes with it.
            _dragOwner = a;
            var worldTopLeft = new Vector2(Rect.WorldRect().xMin, Rect.WorldRect().yMax);
            _dragGrab = a.Position - worldTopLeft;
        }

        void OnCaptionDrag(CursorAgent a, Vector2 delta)
        {
            if (_dragOwner != a) return;
            Vector2 worldTopLeft = a.Position - _dragGrab;
            MoveTo(new Vector2(worldTopLeft.x, ScreenRig.Height - worldTopLeft.y), a);
        }

        public CursorAgent DraggedBy => _dragOwner;

        // ------------------------------------------------------------ state

        internal void SetActive(bool active)
        {
            IsActive = active;
            if (active) _caption.SetGradient(Palette.TitleActiveA, Palette.TitleActiveB);
            else _caption.SetGradient(Palette.TitleInactiveA, Palette.TitleInactiveB);
            _titleText.color = active ? Palette.TitleText : Palette.TitleTextInactive;
        }

        public void SetTitle(string title)
        {
            Title = title ?? "";
            if (_titleText != null) _titleText.text = Title;
            gameObject.name = "Window " + Title;
            Manager?.NotifyChanged(this);
        }

        public void SetIcon(string sprite)
        {
            IconSprite = sprite;
            if (_titleIcon != null) UIBuilder.SetIcon(_titleIcon, sprite, 1);
            Manager?.NotifyChanged(this);
        }

        public void Focus(CursorAgent by = null) => Manager.Focus(this, by);

        /// <summary>
        /// Asked before the player's close (the X, File > Exit): return false to keep the window open, for example while
        /// Jotter asks whether to save an edited file. Story code closes windows with <see cref="Close"/> directly.
        /// </summary>
        public Func<CursorAgent, bool> CloseGuard;

        public void RequestClose(CursorAgent by)
        {
            if (Locked && by != null && by.IsPlayer)
            {
                Shake(0.3f, 2f);
                Sfx.Play("sys_error", by);
                return;
            }
            if (CloseGuard != null && by != null && by.IsPlayer && !CloseGuard(by)) return;
            Close(by);
        }

        public void Close(CursorAgent by = null)
        {
            if (IsClosed) return;
            IsClosed = true;
            GameLog.Info(by != null && by.IsEntity ? LogChannel.Entity : LogChannel.OS, (by != null ? by.Name : "System") + " closed " + Title);
            Sfx.Play("ui_window", by);
            Closed?.Invoke(this, by);
            Manager.OnClosed(this, by);
            gameObject.SetActive(false); // unregister its interactables now, not at end of frame
            Destroy(gameObject);
        }

        public void Minimize(CursorAgent by = null)
        {
            if (IsMinimized || IsClosed) return;
            IsMinimized = true;
            Manager.OnMinimized(this, by);
            gameObject.SetActive(false);
        }

        public void Restore(CursorAgent by = null)
        {
            if (IsClosed) return;
            if (IsMinimized)
            {
                IsMinimized = false;
                gameObject.SetActive(true);
                Manager.OnRestored(this, by);
            }
            Manager.Focus(this, by);
        }

        public void ToggleMaximize(CursorAgent by = null)
        {
            if ((Flags & WindowFlags.CanMaximize) == 0) return;
            StopShake();
            if (!IsMaximized)
            {
                _restore = new Rect(TopLeft, Size);
                IsMaximized = true;
                SetTopLeft(Vector2.zero);
                Rect.sizeDelta = new Vector2(ScreenRig.Width, ScreenRig.Height - WindowManager.TaskbarHeight);
                if (MaximizeButton != null && MaximizeButton.IconImage != null) UIBuilder.SetIcon(MaximizeButton.IconImage, "glyph_restore");
            }
            else
            {
                IsMaximized = false;
                Rect.sizeDelta = _restore.size;
                SetTopLeft(_restore.position);
                if (MaximizeButton != null && MaximizeButton.IconImage != null) UIBuilder.SetIcon(MaximizeButton.IconImage, "glyph_maximize");
            }
            Resized?.Invoke(this);
        }

        /// <summary>Rattle the window in place (refused action, entity tugging at it).</summary>
        public void Shake(float duration, float amplitude)
        {
            if (_shakeTime <= 0f) _shakeBase = TopLeft;
            _shakeTime = Mathf.Max(_shakeTime, duration);
            _shakeAmp = Mathf.Max(_shakeAmp, amplitude);
        }

        void StopShake()
        {
            if (_shakeTime <= 0f) return;
            _shakeTime = 0f;
            _shakeAmp = 0f;
            SetTopLeft(_shakeBase);
        }

        void Update()
        {
            if (_shakeTime > 0f)
            {
                _shakeTime -= Time.deltaTime;
                if (_shakeTime <= 0f || _dragOwner != null)
                {
                    _shakeTime = 0f;
                    _shakeAmp = 0f;
                    if (_dragOwner == null) SetTopLeft(_shakeBase);
                }
                else
                {
                    SetTopLeft(_shakeBase + new Vector2(UnityEngine.Random.Range(-_shakeAmp, _shakeAmp), UnityEngine.Random.Range(-_shakeAmp * 0.5f, _shakeAmp * 0.5f)));
                }
            }
        }

        // ------------------------------------------------------------ logical elements

        /// <summary>Find an interactable inside this window by its elementId (e.g. "button:No", "file:employee_017").</summary>
        public Interactable Element(string elementId)
        {
            if (_elements.TryGetValue(elementId, out var cached) && cached != null && cached.isActiveAndEnabled) return cached;
            foreach (var it in GetComponentsInChildren<Interactable>(false))
            {
                if (it.elementId == elementId)
                {
                    _elements[elementId] = it;
                    return it;
                }
            }
            return null;
        }

        public List<Interactable> Elements(List<Interactable> into)
        {
            into.Clear();
            GetComponentsInChildren(false, into);
            return into;
        }
    }
}
