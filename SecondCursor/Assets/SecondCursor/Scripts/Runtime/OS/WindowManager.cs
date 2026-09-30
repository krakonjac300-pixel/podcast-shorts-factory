using System;
using System.Collections.Generic;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using SecondCursor.Core;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Creates windows, keeps z-order and focus convincing (click anywhere in a window to raise it,
    /// always-on-top dialogs stay above), animates open/minimize with retro outline "zoom" rectangles,
    /// and tells the taskbar what exists. Focus follows whichever cursor clicked - including the entity.
    /// </summary>
    public sealed class WindowManager
    {
        public const int TaskbarHeight = 28;
        // Desktop areas new windows prefer to leave visible (desktop px, y down): the icon column and the
        // Disposal bin, the drop target the whole shift revolves around.
        static readonly Rect[] KeepClear = { new Rect(0f, 0f, 86f, 512f), new Rect(868f, 428f, 92f, 84f) };
        // Relative cost per covered pixel (a window's pixels cost 1): hiding the bin is almost never worth it.
        static readonly float[] KeepClearWeight = { 0.6f, 8f };

        sealed class ZoomAnim
        {
            public Rect From;
            public Rect To;
            public float T;
            public readonly List<BevelGraphic> Frames = new List<BevelGraphic>();
        }

        readonly RectTransform _layer;
        readonly RectTransform _fxLayer;
        readonly List<OSWindow> _windows = new List<OSWindow>();
        readonly List<ZoomAnim> _anims = new List<ZoomAnim>();

        public OSWindow Active { get; private set; }
        public IReadOnlyList<OSWindow> Windows => _windows;

        public event Action<OSWindow> Opened;
        public event Action<OSWindow, CursorAgent> ClosedEvent;
        public event Action<OSWindow, CursorAgent> Focused;
        public event Action<OSWindow> Changed;
        /// <summary>A minimized window was restored (by whom).</summary>
        public event Action<OSWindow, CursorAgent> Restored;
        /// <summary>Resolves where a window minimizes to (the taskbar button), in world coordinates.</summary>
        public Func<OSWindow, Rect?> TaskbarRectOf;

        public WindowManager(RectTransform windowLayer, RectTransform fxLayer, PointerRouter router)
        {
            _layer = windowLayer;
            _fxLayer = fxLayer;
            router.AnyPointerDown += OnAnyPointerDown;
        }

        public OSWindow Create(string appId, string title, string icon, int x, int y, int w, int h, WindowFlags flags, Rect? zoomFrom = null)
        {
            var win = OSWindow.Build(this, _layer, appId, title, icon, x, y, w, h, flags);
            _windows.Add(win);
            if (zoomFrom.HasValue) Zoom(zoomFrom.Value, win.WorldRect);
            Opened?.Invoke(win);
            Focus(win, null);
            return win;
        }

        /// <summary>
        /// Phase H: app windows open right of the desktop icon column (the icons stay reachable: the player's Help, Camera
        /// Viewer and Personnel live there), unless a window is too wide to fit beside it.
        /// </summary>
        public const int IconColumnRight = 88;
        /// <summary>
        /// Where the notices stack, above the Disposal bin (desktop px): reading windows keep clear of it. As wide and tall as the
        /// notices are at the current Reading text size (Review J4).
        /// </summary>
        static Rect ToastColumn
        {
            get
            {
                int s = Mathf.Clamp(Game.DisplaySettings.ReadingScale, 1, 2);
                float w = 230f * s, top = s > 1 ? 0f : 150f;
                return new Rect(ScreenRig.Width - w, top, w, 430f - top);
            }
        }

        /// <summary>Phase J: a point (virtual px, y up) where notices appear, so a file set down there could be hidden by one.</summary>
        public static bool InToastColumn(Vector2 world) => ToastColumn.Contains(OSLayers.WorldToDesktop(world));
        /// <summary>Cost per covered pixel of a window's must-stay-visible part (the Work Orders' buttons).</summary>
        const float KeepVisibleWeight = 12f;
        const float ToastColumnWeight = 3f;

        /// <summary>
        /// A top-left (desktop pixels) near the requested one where a new app window covers as little of
        /// the open windows as possible, so opening Personnel does not bury the Work Orders you are reading.
        /// The requested spot wins when it already covers under 15% of its own area. <paramref name="avoidToasts"/>:
        /// a window read for a while (Mail) also keeps clear of the notices' column.
        /// </summary>
        public Vector2Int PlaceAvoidingOverlap(int x, int y, int w, int h, bool avoidToasts = false)
        {
            int minX = w <= ScreenRig.Width - IconColumnRight ? IconColumnRight : 0;
            int maxX = Mathf.Max(minX, ScreenRig.Width - w);
            int maxY = Mathf.Max(0, ScreenRig.Height - TaskbarHeight - h);
            x = Mathf.Clamp(x, minX, maxX);
            y = Mathf.Clamp(y, 0, maxY);
            float requested = CoveredArea(x, y, w, h, avoidToasts);
            if (requested <= w * h * 0.15f) return new Vector2Int(x, y);
            var best = new Vector2Int(x, y);
            float bestScore = requested;
            for (int cy = 0; cy <= maxY; cy += 16)
                for (int cx = minX; cx <= maxX; cx += 16)
                {
                    // Distance costs a little, so a window only moves far when that really uncovers things.
                    float score = CoveredArea(cx, cy, w, h, avoidToasts) + (Mathf.Abs(cx - x) + Mathf.Abs(cy - y)) * 20f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new Vector2Int(cx, cy);
                    }
                }
            return best;
        }

        static float Overlap(int x, int y, int w, int h, Rect k)
        {
            float kx = Mathf.Min(x + w, k.xMax) - Mathf.Max(x, k.xMin);
            float ky = Mathf.Min(y + h, k.yMax) - Mathf.Max(y, k.yMin);
            return kx > 0f && ky > 0f ? kx * ky : 0f;
        }

        float CoveredArea(int x, int y, int w, int h, bool avoidToasts)
        {
            float sum = 0f;
            for (int i = 0; i < KeepClear.Length; i++) sum += Overlap(x, y, w, h, KeepClear[i]) * KeepClearWeight[i];
            if (avoidToasts) sum += Overlap(x, y, w, h, ToastColumn) * ToastColumnWeight;
            foreach (var win in _windows)
            {
                if (win == null || win.IsClosed || win.IsMinimized || win.AlwaysOnTop) continue;
                Vector2 tl = win.TopLeft, size = win.Size;
                sum += Overlap(x, y, w, h, new Rect(tl, size)) * Mathf.Max(1f, win.CoverCost);
                var keep = win.KeepVisible;
                // A window that opens lower than the strip lets its owner move up to make room (MakeRoomFor), so only a spot
                // too high for that counts as covering it.
                if (keep.width > 0f && keep.height > 0f && y - MakeRoomMargin < keep.yMax)
                    sum += Overlap(x, y, w, h, new Rect(tl + keep.position, keep.size)) * KeepVisibleWeight;
            }
            return sum;
        }

        const int MakeRoomMargin = 2;

        /// <summary>
        /// The highest a notice may reach (virtual px from the bottom) so it never sits over a window's must-stay-visible part at
        /// the right edge (the Work Orders' Approve and Reject when they are in the notices' column). No limit is float.MaxValue.
        /// </summary>
        public float NoticeCeiling()
        {
            float ceiling = float.MaxValue;
            foreach (var win in _windows)
            {
                if (win == null || win.IsClosed || win.IsMinimized) continue;
                var keep = win.KeepVisible;
                if (keep.width <= 0f || keep.height <= 0f) continue;
                var r = new Rect(win.TopLeft + keep.position, keep.size);
                if (r.xMax < ToastColumn.xMin) continue;
                ceiling = Mathf.Min(ceiling, ScreenRig.Height - r.yMax - 2f);
            }
            return ceiling;
        }

        /// <summary>
        /// Phase I: a window that has just been placed at (x, y, w, h) sits over another window's must-stay-visible part (the Work
        /// Orders' Approve and Reject, which Personnel has to open next to): that window moves up until the part is clear above
        /// the new window's top edge. Nothing is closed or minimized, and a window being dragged or maximized stays put.
        /// </summary>
        public void MakeRoomFor(int x, int y, int w, int h)
        {
            var placed = new Rect(x, y, w, h);
            foreach (var win in _windows)
            {
                if (win == null || win.IsClosed || win.IsMinimized || win.IsMaximized || win.AlwaysOnTop || win.DraggedBy != null) continue;
                var keep = win.KeepVisible;
                if (keep.width <= 0f || keep.height <= 0f) continue;
                Vector2 tl = win.TopLeft;
                if (!new Rect(tl + keep.position, keep.size).Overlaps(placed)) continue;
                float top = y - MakeRoomMargin - keep.yMax;
                if (top < 0f || top >= tl.y) continue;
                win.MoveTo(new Vector2(tl.x, top));
                GameLog.Info(LogChannel.OS, "Window moved up to keep its buttons clear: " + win.Title);
            }
        }

        /// <summary>Centered on the desktop area.</summary>
        public static Vector2Int Centered(int w, int h) =>
            new Vector2Int((ScreenRig.Width - w) / 2, Mathf.Max(0, (ScreenRig.Height - TaskbarHeight - h) / 2));

        public OSWindow Find(string appId)
        {
            for (int i = _windows.Count - 1; i >= 0; i--)
                if (_windows[i] != null && !_windows[i].IsClosed && _windows[i].AppId == appId) return _windows[i];
            return null;
        }

        public List<OSWindow> FindAll(string appId, List<OSWindow> into)
        {
            into.Clear();
            foreach (var w in _windows) if (w != null && !w.IsClosed && w.AppId == appId) into.Add(w);
            return into;
        }

        /// <summary>Topmost visible window whose rect contains the world point.</summary>
        public OSWindow TopmostAt(Vector2 worldPoint)
        {
            OSWindow best = null;
            int bestIndex = -1;
            foreach (var w in _windows)
            {
                if (w == null || w.IsClosed || w.IsMinimized) continue;
                if (!w.WorldRect.Contains(worldPoint)) continue;
                int idx = w.transform.GetSiblingIndex();
                if (idx > bestIndex) { bestIndex = idx; best = w; }
            }
            return best;
        }

        public void Focus(OSWindow win, CursorAgent by)
        {
            if (win == null || win.IsClosed) return;
            if (win.IsMinimized) { win.Restore(by); return; }
            win.transform.SetAsLastSibling();
            // Keep always-on-top windows above normal ones.
            foreach (var w in _windows)
                if (w != null && w != win && w.AlwaysOnTop && !w.IsClosed) w.transform.SetAsLastSibling();
            if (win.AlwaysOnTop) win.transform.SetAsLastSibling();

            if (Active == win) return;
            if (Active != null && !Active.IsClosed) Active.SetActive(false);
            Active = win;
            win.SetActive(true);
            Focused?.Invoke(win, by);
            Changed?.Invoke(win);
        }

        void OnAnyPointerDown(CursorAgent a, Interactable hit)
        {
            if (hit == null) return;
            var win = hit.GetComponentInParent<OSWindow>();
            if (win != null) Focus(win, a);
        }

        internal void NotifyChanged(OSWindow w) => Changed?.Invoke(w);

        internal void OnClosed(OSWindow w, CursorAgent by)
        {
            _windows.Remove(w);
            if (Active == w)
            {
                Active = null;
                FocusTopmost(by);
            }
            ClosedEvent?.Invoke(w, by);
            Changed?.Invoke(w);
        }

        internal void OnMinimized(OSWindow w, CursorAgent by)
        {
            var target = TaskbarRectOf?.Invoke(w);
            if (target.HasValue) Zoom(w.WorldRect, target.Value);
            if (Active == w)
            {
                w.SetActive(false);
                Active = null;
                FocusTopmost(by);
            }
            Changed?.Invoke(w);
        }

        internal void OnRestored(OSWindow w, CursorAgent by)
        {
            var from = TaskbarRectOf?.Invoke(w);
            if (from.HasValue) Zoom(from.Value, w.WorldRect);
            Changed?.Invoke(w);
            Restored?.Invoke(w, by);
        }

        void FocusTopmost(CursorAgent by)
        {
            OSWindow top = null;
            int best = -1;
            foreach (var w in _windows)
            {
                if (w == null || w.IsClosed || w.IsMinimized) continue;
                int i = w.transform.GetSiblingIndex();
                if (i > best) { best = i; top = w; }
            }
            if (top != null) Focus(top, by);
        }

        /// <summary>Close every window (desktop reset).</summary>
        public void CloseAll()
        {
            foreach (var w in _windows.ToArray()) if (w != null) w.Close();
            _windows.Clear();
            Active = null;
        }

        // ------------------------------------------------------------ zoom rectangles

        public void Zoom(Rect from, Rect to)
        {
            var anim = new ZoomAnim { From = from, To = to };
            for (int i = 0; i < 3; i++)
            {
                var g = UIBuilder.Bevel(_fxLayer, BevelStyle.Outline, "Zoom");
                g.Fill = Palette.Shadow;
                var rt = g.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = Vector2.zero;
                rt.anchoredPosition = from.position;
                rt.sizeDelta = from.size;
                g.enabled = false;
                anim.Frames.Add(g);
            }
            _anims.Add(anim);
        }

        public void Update(float dt)
        {
            for (int i = _anims.Count - 1; i >= 0; i--)
            {
                var a = _anims[i];
                a.T += dt / 0.16f;
                if (a.T >= 1.25f)
                {
                    foreach (var f in a.Frames) if (f != null) UnityEngine.Object.Destroy(f.gameObject);
                    _anims.RemoveAt(i);
                    continue;
                }
                for (int k = 0; k < a.Frames.Count; k++)
                {
                    float t = Mathf.Clamp01(a.T - k * 0.12f);
                    var r = new Rect(Vector2.Lerp(a.From.position, a.To.position, t), Vector2.Lerp(a.From.size, a.To.size, t));
                    var rt = a.Frames[k].rectTransform;
                    rt.anchoredPosition = new Vector2(Mathf.Round(r.x), Mathf.Round(r.y));
                    rt.sizeDelta = new Vector2(Mathf.Round(r.width), Mathf.Round(r.height));
                    a.Frames[k].enabled = a.T - k * 0.12f > 0f && t < 1f;
                }
            }
        }
    }
}
