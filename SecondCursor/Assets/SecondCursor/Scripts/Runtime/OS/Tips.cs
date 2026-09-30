using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.OS
{
    /// <summary>
    /// Phase L (suggestion 2, a playable introduction): one-time tips. Each teaches one action the first time it is needed, in a small
    /// note beside the thing it explains, with a pointer at it. A tip never covers what it points at, only one shows at a time (the
    /// next waits its turn), it goes away as soon as the player has done what it says, and it shows once per save (the save remembers
    /// which). It takes no clicks: whatever is under it stays usable. Tips belong to runs that count (a debug jump shows none).
    /// </summary>
    public sealed class Tips : MonoBehaviour
    {
        const int Width = 196, Gap = 12, Pad = 6, TextTop = 16, PointerDepth = 6;
        /// <summary>A tip waiting for its turn keeps waiting this long; a tip on screen with no place to stand gives up after the second.</summary>
        const float WaitSeconds = 60f, GiveUpSeconds = 4f;
        /// <summary>A place may hide at most this share of the tip's own area (windows, desktop icons, notices), else the tip waits.</summary>
        const float MaxHidden = 0.2f;

        sealed class Tip
        {
            public string Id, Text;
            public Func<Rect?> Anchor;
            public Func<bool> KeepWhile;
            public float Life, Since, Age, Lost, MaxHidden;
        }

        GameServices _g;
        RectTransform _panel;
        PixelText _title, _body;
        /// <summary>The pointer: a little triangle (pale fill inside a dark rim) between the tip and what it points at.</summary>
        readonly Image[] _pointerRim = new Image[PointerDepth + 1], _pointerFill = new Image[PointerDepth];
        readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        readonly List<Tip> _pending = new List<Tip>();
        Tip _active;
        int _slot = -1;

        public static Tips Create(GameServices g)
        {
            var root = UIBuilder.Rect("Tips", g.Layers.Effects);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            var tips = root.gameObject.AddComponent<Tips>();
            tips._g = g;
            foreach (var id in g.Save.tipsShown) tips._seen.Add(id);
            tips.BuildPanel(root);
            return tips;
        }

        void BuildPanel(RectTransform root)
        {
            _panel = UIBuilder.Rect("Tip", root);
            _panel.anchorMin = _panel.anchorMax = Vector2.zero;
            _panel.pivot = Vector2.zero;
            var face = _panel.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Window;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;
            _title = UIBuilder.Text(_panel, "", Palette.Shadow, true, "Title");
            _body = UIBuilder.Text(_panel, "", Palette.Text, false, "Body");
            _body.Wrap = true;
            for (int i = 0; i <= PointerDepth; i++) _pointerRim[i] = UIBuilder.Solid(root, Palette.Dark, "Pointer Rim " + i);
            for (int i = 0; i < PointerDepth; i++) _pointerFill[i] = UIBuilder.Solid(root, Palette.Tooltip, "Pointer " + i);
            foreach (var img in _pointerRim) Anchor0(img.rectTransform);
            foreach (var img in _pointerFill) Anchor0(img.rectTransform);
            SetVisible(false);
        }

        static void Anchor0(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
        }

        /// <summary>Tips show only in runs that count; a debug jump shows none and remembers none.</summary>
        bool Enabled => _g.RecordsArmed;

        public bool Seen(string id) => _seen.Contains(id);

        /// <summary>A tip is showing or waiting for its turn (a low-value tip holds back rather than take the place of a task's).</summary>
        public bool Busy => _active != null || _pending.Count > 0;

        /// <summary>The caller shows this tip itself (in its own panel): true once per save, and it is remembered as shown.</summary>
        public bool Claim(string id)
        {
            if (!Enabled || !_seen.Add(id)) return false;
            SaveSystem.MarkTipShown(id);
            GameLog.Info(LogChannel.OS, "Tip shown: " + id);
            return true;
        }

        /// <summary>
        /// Offers a tip (its text is the string "tip.&lt;id&gt;", or <paramref name="textKey"/>). It shows when it is its turn and
        /// <paramref name="anchor"/> (the rectangle it points at, virtual px, y up; null = nothing to point at yet) has a place beside
        /// it, hiding at most <paramref name="maxHidden"/> of its own area. It goes when <paramref name="keepWhile"/> returns false, or after
        /// <paramref name="life"/> seconds. False: this tip will not show (it was shown before on this save, or this run shows no tips).
        /// True only means it is queued: <see cref="Seen"/> says when it has shown.
        /// </summary>
        public bool Offer(string id, Func<Rect?> anchor, Func<bool> keepWhile, float life = 30f, string textKey = null, float maxHidden = MaxHidden)
        {
            if (!Enabled || _seen.Contains(id)) return false;
            if ((_active != null && _active.Id == id) || _pending.Exists(t => t.Id == id)) return true;
            _pending.Add(new Tip
            {
                Id = id, Text = _g.Content.Text(textKey ?? "tip." + id), Anchor = anchor, KeepWhile = keepWhile, Life = life, Since = Time.time, MaxHidden = maxHidden,
            });
            return true;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (_active != null) Follow(dt);
            if (_active != null) return;
            for (int i = 0; i < _pending.Count; i++)
            {
                var t = _pending[i];
                if (!StillWanted(t) || Time.time - t.Since > WaitSeconds)
                {
                    _pending.RemoveAt(i--);
                    continue;
                }
                if (!Show(t)) continue;
                _pending.RemoveAt(i);
                return;
            }
        }

        static bool StillWanted(Tip t) => t.KeepWhile();

        void Follow(float dt)
        {
            var t = _active;
            t.Age += dt;
            var anchor = t.Anchor();
            bool placed = anchor.HasValue && Place(anchor.Value, t.MaxHidden);
            // Nothing to point at, or no free place beside it (windows opened over its spot): it waits a moment, then gives up.
            t.Lost = placed ? 0f : t.Lost + dt;
            if (t.Age > t.Life || !StillWanted(t) || t.Lost > GiveUpSeconds) Hide();
            else SetVisible(placed);
        }

        void SetVisible(bool on)
        {
            _panel.gameObject.SetActive(on);
            foreach (var img in _pointerRim) img.enabled = on;
            foreach (var img in _pointerFill) img.enabled = on;
        }

        bool Show(Tip t)
        {
            var anchor = t.Anchor();
            if (!anchor.HasValue) return false;
            _active = t;
            Layout(t.Text);
            if (!Place(anchor.Value, t.MaxHidden))
            {
                _active = null;
                return false;
            }
            _seen.Add(t.Id);
            SaveSystem.MarkTipShown(t.Id);
            SetVisible(true);
            GameLog.Info(LogChannel.OS, "Tip shown: " + t.Id);
            return true;
        }

        void Hide()
        {
            _active = null;
            _slot = -1;
            SetVisible(false);
        }

        void Layout(string text)
        {
            int s = Mathf.Clamp(DisplaySettings.ReadingScale, 1, 2);
            int w = Width * s;
            _title.text = _g.Content.Text("tip.title", "Tip");
            _title.Scale = s;
            _title.rectTransform.At(Pad, Pad - 1, w - Pad * 2, 12 * s);
            _body.text = text;
            _body.Scale = s;
            int textH = PixelFont.Measure(text, w - Pad * 2, false, s).y;
            _body.rectTransform.At(Pad, Pad + TextTop * s - 4, w - Pad * 2, textH + 2);
            _panel.sizeDelta = new Vector2(w, Pad * 2 + TextTop * s - 4 + textH);
        }

        /// <summary>
        /// Puts the tip beside the anchor (one of four sides, three alignments each) where it hides the least, never on the anchor and
        /// never over much else. False: there is no such place now (the tip waits, and comes back when a window moves).
        /// </summary>
        bool Place(Rect anchor, float maxHidden)
        {
            Vector2 size = _panel.sizeDelta;
            float bestScore = float.MaxValue;
            int best = -1, bestSide = 0;
            var bestRect = default(Rect);
            var keepOut = new Rect(anchor.x - 3f, anchor.y - 3f, anchor.width + 6f, anchor.height + 6f);
            for (int slot = 0; slot < 12; slot++)
            {
                var r = Candidate(anchor, size, slot / 3, slot % 3);
                if (r.Overlaps(keepOut)) continue;
                float hidden = Hidden(r);
                if (hidden > size.x * size.y * maxHidden) continue;
                float score = hidden + Vector2.Distance(r.center, anchor.center) * 0.1f - (slot == _slot ? 40f : 0f);
                if (score >= bestScore) continue;
                bestScore = score;
                best = slot;
                bestSide = slot / 3;
                bestRect = r;
            }
            if (best < 0) return false;
            _slot = best;
            bestRect = new Rect(Mathf.Round(bestRect.x), Mathf.Round(bestRect.y), bestRect.width, bestRect.height);
            _panel.anchoredPosition = bestRect.position;
            PlacePointer(bestRect, anchor, bestSide);
            return true;
        }

        /// <param name="side">0 right of the anchor, 1 left, 2 below, 3 above.</param>
        /// <param name="align">0 centred on the anchor, 1 aligned with its top (or left), 2 with its bottom (or right).</param>
        static Rect Candidate(Rect a, Vector2 size, int side, int align)
        {
            float x, y;
            bool across = side < 2;
            float along = across ? (align == 0 ? a.center.y - size.y * 0.5f : align == 1 ? a.yMax - size.y : a.yMin)
                : (align == 0 ? a.center.x - size.x * 0.5f : align == 1 ? a.xMin : a.xMax - size.x);
            switch (side)
            {
                case 0: x = a.xMax + Gap; y = along; break;
                case 1: x = a.xMin - Gap - size.x; y = along; break;
                case 2: x = along; y = a.yMin - Gap - size.y; break;
                default: x = along; y = a.yMax + Gap; break;
            }
            x = Mathf.Clamp(x, 2f, ScreenRig.Width - size.x - 2f);
            y = Mathf.Clamp(y, WindowManager.TaskbarHeight + 2f, ScreenRig.Height - size.y - 2f);
            return new Rect(x, y, size.x, size.y);
        }

        /// <summary>What a spot would hide (px, weighted): open windows, the desktop icons (counted four times) and the notices' column (three times).</summary>
        float Hidden(Rect r)
        {
            float sum = r.OverlapArea(new Rect(0f, 0f, WindowManager.IconColumnRight - 2f, ScreenRig.Height)) * 4f
                        + r.OverlapArea(WindowManager.NoticeColumnWorld) * 3f;
            foreach (var w in _g.Windows.Windows)
            {
                if (w == null || w.IsClosed || w.IsMinimized) continue;
                sum += r.OverlapArea(w.WorldRect);
            }
            return sum;
        }

        /// <summary>
        /// A small triangle on the tip's edge that faces the anchor, level with the anchor's centre (kept on the edge), pointing at it:
        /// PointerDepth columns from the edge outward, each two pixels narrower than the one before.
        /// </summary>
        void PlacePointer(Rect r, Rect anchor, int side)
        {
            bool horizontal = side < 2;
            float c = horizontal ? Mathf.Clamp(anchor.center.y, r.yMin + 8f, r.yMax - 8f) : Mathf.Clamp(anchor.center.x, r.xMin + 8f, r.xMax - 8f);
            c = Mathf.Round(c);
            for (int m = 0; m <= PointerDepth; m++)
            {
                float cross = 2 * (PointerDepth - m) + 1;
                PlaceColumn(_pointerRim[m], r, side, m, c, m == PointerDepth ? 1f : cross + 2f);
                if (m < PointerDepth) PlaceColumn(_pointerFill[m], r, side, m, c, cross);
            }
        }

        /// <summary>One column of the pointer: <paramref name="m"/> px out from the tip's edge, <paramref name="cross"/> px across, centred on <paramref name="c"/>.</summary>
        static void PlaceColumn(Image img, Rect r, int side, int m, float c, float cross)
        {
            float half = Mathf.Floor(cross * 0.5f);
            Vector2 pos, size;
            switch (side)
            {
                case 0: pos = new Vector2(r.xMin - 1 - m, c - half); size = new Vector2(1f, cross); break;
                case 1: pos = new Vector2(r.xMax + m, c - half); size = new Vector2(1f, cross); break;
                case 2: pos = new Vector2(c - half, r.yMax + m); size = new Vector2(cross, 1f); break;
                default: pos = new Vector2(c - half, r.yMin - 1 - m); size = new Vector2(cross, 1f); break;
            }
            img.rectTransform.anchoredPosition = pos;
            img.rectTransform.sizeDelta = size;
        }
    }
}
