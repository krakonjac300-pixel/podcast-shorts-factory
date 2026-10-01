using System;
using System.Collections.Generic;
using SecondCursor.Core.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Balloon toasts that slide in above the Disposal bin ("You have 1 new message", "New input device
    /// detected"). Clicking one runs its action. Stacks upward when several are visible. They follow the Reading
    /// text option (Large doubles them, for the Steam Deck); a sticky toast stays until it is clicked.
    /// Phase H: toasts that arrive together come in one after another (<see cref="Stagger"/>), slide in and out
    /// sideways so they never pass over the bin, and a toast about something already done (a task's hint once the
    /// task is ticked) goes away by itself.
    /// Phase Q4 (review board R2, A3): a notice that belongs to a session wears its pointer and a stripe in that session's colours (017 black
    /// with a pale rim, 209 amber), a deadline notice a red stripe; a notice stays as long as it takes to read (<see cref="NoticeRules"/>), at
    /// most three show at once with a "+N more" chip, an important one is never dropped unseen, and the last twenty are kept for Recent notices.
    /// </summary>
    public sealed class Notifications : MonoBehaviour
    {
        const int W = 220;
        const int H = 58;
        /// <summary>A notice that waited for room longer than it would have shown, and was not important, goes unseen (at least this long).</summary>
        const float StaleFloor = NoticeRules.MinSeconds;
        /// <summary>Seconds between two toasts that were asked for at the same moment.</summary>
        public const float Stagger = 1.1f;
        float _nextShowAt = -100f, _nextRelease = -100f;

        /// <summary>
        /// Phase I: the highest a toast's top edge may reach (virtual px from the bottom), or null for no limit. A window with a part that
        /// must stay clickable at the right edge (the Work Orders' Approve and Reject during the shelf check) sets it, so a notice never
        /// sits on the button you are about to press: the newest notices wait for the older ones to go instead.
        /// </summary>
        public Func<float> Ceiling;
        /// <summary>
        /// Phase K (suggestion 3): windows the stack must not cover (virtual px, y up): the focused window and the Work Queue. The stack
        /// grows only up to them, and moves to the left of the screen when the right has less room.
        /// </summary>
        public Func<List<Rect>> Avoid;
        bool _left;
        /// <summary>
        /// Phase P (review board R1 item 5): while this says so (a tug-of-war is on), no new notice appears and the ones already up draw at
        /// 40% alpha, so nothing competes with the fight. The fight's own result is posted after it ends.
        /// </summary>
        public Func<bool> Hold;
        /// <summary>Phase Q1: the story holds the notices too (the first CAM 03 view on Night 1 is never covered by one).</summary>
        [NonSerialized] public bool HeldByStory;
        const float HeldAlpha = 0.4f;
        float _alpha = 1f;

        /// <summary>The last notices, newest first (the Recent notices window reads it).</summary>
        public readonly NoticeHistory History = new NoticeHistory();
        /// <summary>The shift clock's time as text, stamped on each history entry.</summary>
        public Func<string> Stamp;
        /// <summary>The "+N more" chip was clicked (opens Recent notices).</summary>
        public Action<CursorAgent> OpenRecent;
        RectTransform _more;
        PixelText _moreText;
        int _moreCount, _moreShown = -1;
        const int MoreWidth = 64, MoreHeight = 16;

        /// <summary>A shown toast: its body can change after it appears (a line that lands on its own beat).</summary>
        public sealed class Toast
        {
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal float Age;
            /// <summary>Seconds it stays once shown (by its length: Phase Q4).</summary>
            internal float Life = NoticeRules.MinSeconds;
            /// <summary>Phase Q4: never dropped unseen (a task, deadline, camera or order notice, a sticky one, one with its own condition).</summary>
            public bool Important;
            public NoticeKind Kind;
            /// <summary>Seconds its turn has come but there was no room for it (Review J2).</summary>
            internal float Waited;
            internal float Slot;
            internal bool Dismissed;
            internal bool Sticky;
            internal int Height;
            internal int Scale = 1;
            internal string Sound;
            /// <summary>While false the toast is dismissed (shown or still waiting its turn).</summary>
            internal Func<bool> KeepWhile;
            /// <summary>It has appeared (its turn came and there was room for it).</summary>
            internal bool Shown;
            public PixelText Body { get; internal set; }

            public bool IsShowing => Rect != null && !Dismissed;
            /// <summary>Still waiting for its turn behind a toast that arrived just before it, or for room to appear.</summary>
            internal bool Waiting => !Shown;

            public void SetBody(string text)
            {
                if (Body == null) return;
                Body.text = text ?? "";
                Fit();
            }

            /// <summary>Tall enough for the whole body (a wrapped third line used to spill out under the box).</summary>
            internal void Fit()
            {
                if (Rect == null || Body == null) return;
                int w = (int)Rect.sizeDelta.x, textLeft = 14 + 16 * Scale, top = 8 + 14 * Scale;
                int textH = PixelFont.Measure(Body.text, w - textLeft - 8, false, Scale).y;
                Height = Mathf.Max(H * Scale, top + textH + 8);
                Rect.sizeDelta = new Vector2(w, Height);
                Body.rectTransform.At(textLeft, top, w - textLeft - 8, textH + 4);
            }
        }

        RectTransform _layer;
        readonly List<Toast> _toasts = new List<Toast>();

        public static Notifications Create(RectTransform layer)
        {
            var n = layer.gameObject.AddComponent<Notifications>();
            n._layer = layer;
            return n;
        }

        public Toast Show(string title, string body, string icon = "icon_info", Action<CursorAgent> onClick = null, string sound = "notify_mail")
            => Show(title, body, icon, onClick, sound, false);

        /// <summary>Like the short form; a <paramref name="sticky"/> toast stays up until it is clicked.</summary>
        public Toast Show(string title, string body, string icon, Action<CursorAgent> onClick, string sound, bool sticky)
            => Show(title, body, icon, onClick, sound, sticky, null);

        /// <summary>
        /// Like the others; <paramref name="keepWhile"/> (optional) is checked every frame and the toast goes as soon as it
        /// returns false (a task hint once the task is done), even before its turn came. Phase Q4: <paramref name="kind"/> says whose
        /// notice it is (session 017, session 209, a deadline), which sets its pointer icon and stripe.
        /// </summary>
        public Toast Show(string title, string body, string icon, Action<CursorAgent> onClick, string sound, bool sticky, Func<bool> keepWhile,
            NoticeKind kind = NoticeKind.Plain)
        {
            History.Add(Stamp != null ? Stamp() : "", title, body, kind);
            int s = Mathf.Clamp(Game.DisplaySettings.ReadingScale, 1, 2);
            int w = W * s, h = H * s;
            var rt = UIBuilder.Rect("Toast " + title, _layer);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            var face = rt.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Window;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = _alpha;

            // Phase Q4 (R2): a session's notice shows its pointer in its colours instead of the info icon, and a stripe; a deadline gets a red stripe.
            if (ActorStyle.IsSession(kind))
            {
                var pointer = ActorSprites.Icon(rt, kind, s);
                pointer.rectTransform.anchoredPosition = new Vector2(8f * s, -8f * s);
            }
            else if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIBuilder.Icon(rt, icon, s);
                ic.rectTransform.anchoredPosition = new Vector2(8f, -8f);
            }
            if (kind != NoticeKind.Plain) AddStripe(rt, kind, w, s);
            int textLeft = 14 + 16 * s;
            var t = UIBuilder.Text(rt, title, Palette.Text, true);
            t.Scale = s;
            t.rectTransform.At(textLeft, 8, w - textLeft - 8, 12 * s);
            var b = UIBuilder.Text(rt, body, Palette.Text);
            b.Scale = s;
            b.Wrap = true;
            b.rectTransform.At(textLeft, 8 + 14 * s, w - textLeft - 8, h - 14 * s - 14);

            // Toasts asked for together arrive one after another, so a flood of three is read as three.
            float delay = Mathf.Max(0f, _nextShowAt - Time.time);
            _nextShowAt = Time.time + delay + Stagger;
            int visible = 0;
            foreach (var other in _toasts) if (!other.Waiting) visible++;
            var toast = new Toast
            {
                Group = group, Rect = rt, Slot = visible, Sticky = sticky, Height = h, Body = b, Scale = s, Age = -delay, Sound = sound, KeepWhile = keepWhile,
                Kind = kind, Life = NoticeRules.Duration((body ?? "").Length, Game.AccessSettings.NoticeTime),
                Important = NoticeRules.IsImportant(icon, sticky, keepWhile != null, kind),
            };
            toast.Fit();
            var hit = UIBuilder.Hit(rt.gameObject, "toast:" + title, onClick != null ? CursorShape.Hand : CursorShape.Arrow);
            hit.passThroughWhileCarrying = true;
            hit.Click += (a, n) =>
            {
                toast.Dismissed = true;
                onClick?.Invoke(a);
            };
            _toasts.Add(toast);
            // Not shown yet: its turn (the stagger) and room for it are settled in Layout.
            rt.gameObject.SetActive(false);
            Layout(0f);
            return toast;
        }

        /// <summary>
        /// Phase Q4 (A3): a line for Recent notices only (the tug's result, when its panel already says it beside the pointer the player is
        /// watching): it is not shown as a toast.
        /// </summary>
        public void Record(string title, string body, NoticeKind kind = NoticeKind.Plain)
            => History.Add(Stamp != null ? Stamp() : "", title, body, kind);

        void Update() => Layout(Time.deltaTime);   // game time: toasts wait behind the pause menu

        void Layout(float dt)
        {
            // Age them, drop the dismissed and the expired.
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                if (t.Shown || t.Age < 0f) t.Age += dt;
                if (t.Sticky && t.Age > t.Life - 0.01f) t.Age = t.Life - 0.01f;
                if (t.KeepWhile != null && !SafeKeep(t)) t.Dismissed = true;
                if (t.Rect == null || t.Dismissed || t.Age > t.Life + 0.3f)
                {
                    if (t.Rect != null) Destroy(t.Rect.gameObject);
                    _toasts.RemoveAt(i);
                }
            }

            bool held = SafeHold();
            _alpha = Mathf.MoveTowards(_alpha, held ? HeldAlpha : 1f, Time.unscaledDeltaTime * 6f);
            foreach (var t in _toasts) if (t.Group != null) t.Group.alpha = _alpha;

            // Oldest first: a toast whose turn has come appears when there is room for it above the bin (not during a fight).
            float baseY = WindowManager.TaskbarHeight + 84;
            float ceiling = PickColumn(baseY);
            float used = 0f;
            int shown = 0;
            foreach (var t in _toasts) if (t.Shown) { used += t.Height + 4; shown++; }
            _moreCount = 0;
            foreach (var t in _toasts)
            {
                if (t.Shown || t.Age < 0f) continue;
                t.Age = 0f;   // its time only starts when it is on screen
                if (held) continue;   // a fight holds it back (not counted as waiting for room)
                // Review J2: a notice that waited for room longer than it would have shown is stale: it goes unseen.
                // Phase Q4 (A3): unless it is important (a task, deadline, camera or order notice waits as long as it takes).
                t.Waited += dt;
                if (t.Waited > Mathf.Max(StaleFloor, t.Life) && !t.Important) { t.Dismissed = true; continue; }
                bool room = used == 0f || baseY + used + t.Height <= ceiling;
                if (!room || shown >= NoticeRules.VisibleCap)
                {
                    GiveWay();
                    _moreCount++;
                    continue;
                }
                // When room comes back, the ones that waited come in one after another, not in a burst.
                if (Time.time < _nextRelease) continue;
                _nextRelease = Time.time + Stagger;
                t.Shown = true;
                t.Rect.gameObject.SetActive(true);
                if (!string.IsNullOrEmpty(t.Sound)) Sfx.Play(t.Sound);
                t.Sound = null;
                t.Slot = -1f;   // takes the slot it lands in (below), instead of the one it was queued behind
                used += t.Height + 4;
                shown++;
            }

            float y = baseY;
            int slot = 0;
            for (int i = 0; i < _toasts.Count; i++)
            {
                var t = _toasts[i];
                if (!t.Shown) continue;
                t.Slot = t.Slot < 0f ? slot : Mathf.MoveTowards(t.Slot, slot, dt * 6f);
                float slideIn = Mathf.Clamp01(t.Age / 0.2f);
                float slideOut = Mathf.Clamp01((t.Age - t.Life) / 0.3f);
                // Stack above the Disposal bin (or right of the icon column); in and out sideways, never across the drop target.
                float ty = y + (t.Slot - slot) * (t.Height + 4);
                float w = t.Rect.sizeDelta.x;
                float tx = _left ? -(ScreenRig.Width - LeftColumnX - w) - ((1f - slideIn) + slideOut) * (LeftColumnX + w + 8f)
                    : -4f + ((1f - slideIn) + slideOut) * (w + 8f);
                t.Rect.anchoredPosition = new Vector2(Mathf.Round(tx), Mathf.Round(ty));
                y += t.Height + 4;
                slot++;
            }
            UpdateMoreChip(y);
        }

        /// <summary>The "+N more" chip over the stack: how many notices are waiting for a place (click: Recent notices).</summary>
        void UpdateMoreChip(float y)
        {
            bool show = _moreCount > 0 && !SafeHold();
            if (_more == null)
            {
                if (!show) return;
                _more = UIBuilder.Rect("More Notices", _layer);
                _more.anchorMin = _more.anchorMax = new Vector2(1f, 0f);
                _more.pivot = new Vector2(1f, 0f);
                var face = _more.gameObject.AddComponent<BevelGraphic>();
                face.Style = BevelStyle.Window;
                face.Fill = Palette.Tooltip;
                face.raycastTarget = false;
                _moreText = UIBuilder.Text(_more, "", Palette.Text, true);
                _moreText.Align = TextAlign.Center;
                _moreText.VAlign = TextVAlign.Middle;
                _moreText.rectTransform.Stretch(2, 0, 2, 0);
                var hit = UIBuilder.Hit(_more.gameObject, "notices:more", CursorShape.Hand);
                hit.passThroughWhileCarrying = true;
                hit.Click += (a, n) => OpenRecent?.Invoke(a);
                _more.sizeDelta = new Vector2(MoreWidth, MoreHeight);
            }
            _more.gameObject.SetActive(show);
            if (!show) return;
            if (_moreShown != _moreCount)
            {
                _moreShown = _moreCount;
                _moreText.text = "+" + _moreCount + " more";
            }
            float x = _left ? -(ScreenRig.Width - LeftColumnX - MoreWidth) : -4f;
            _more.anchoredPosition = new Vector2(Mathf.Round(x), Mathf.Round(y));
        }

        /// <summary>A stripe down the left inside of the notice in the actor's colour; session 017's has a pale line beside it.</summary>
        static void AddStripe(RectTransform rt, NoticeKind kind, int width, int scale)
        {
            int stripeW = ActorStyle.StripeWidth * scale;
            var stripe = UIBuilder.Solid(rt, Palette.StripeOf(kind), "Stripe");
            stripe.rectTransform.Stretch(2, 2, width - 2 - stripeW, 2);
            uint line = ActorStyle.StripeLine(kind);
            if (line == 0u) return;
            var edge = UIBuilder.Solid(rt, Palette.FromRgb(line), "Stripe Edge");
            edge.rectTransform.Stretch(2 + stripeW, 2, width - 2 - stripeW - scale, 2);
        }

        /// <summary>
        /// Phase N: a sticky notice that has been up for a whole notice's time makes room for a newer one that has none (a race's
        /// result behind Night 3's end-of-shift rule went unseen): the oldest goes, the news is read. Its content stays elsewhere.
        /// </summary>
        void GiveWay()
        {
            foreach (var t in _toasts)
                if (t.Shown && !t.Dismissed && t.Sticky && t.Age >= t.Life - 0.01f && Game.AccessSettings.NoticeTime != NoticeTime.UntilClicked) { t.Dismissed = true; return; }
        }

        /// <summary>Where the left-hand stack starts (just right of the desktop icon column).</summary>
        const float LeftColumnX = WindowManager.IconColumnRight + 4;

        /// <summary>
        /// Picks the side the stack uses and returns the highest a toast may reach there. A new side is taken only while nothing is
        /// showing (toasts on screen never jump across).
        /// </summary>
        float PickColumn(float baseY)
        {
            float ceiling = Ceiling != null ? Ceiling() : float.MaxValue;
            var avoid = Avoid?.Invoke();
            if (avoid == null || avoid.Count == 0)
            {
                if (!AnyShown()) _left = false;
                return _left ? float.MaxValue : ceiling;
            }
            float w = W * Mathf.Clamp(Game.DisplaySettings.ReadingScale, 1, 2) + 8f;
            float right = Mathf.Min(ceiling, Room(avoid, ScreenRig.Width - w, ScreenRig.Width, baseY));
            float left = Room(avoid, LeftColumnX, LeftColumnX + w, baseY);
            if (!AnyShown()) _left = right < baseY + H && left > right;
            return _left ? left : right;
        }

        /// <summary>The highest a stack in the columns from <paramref name="x0"/> to <paramref name="x1"/> can reach below the avoided windows.</summary>
        static float Room(List<Rect> avoid, float x0, float x1, float baseY)
        {
            float top = float.MaxValue;
            foreach (var r in avoid)
                if (r.xMax > x0 && r.xMin < x1 && r.yMax > baseY) top = Mathf.Min(top, Mathf.Max(baseY, r.yMin - 4f));
            return top;
        }

        bool AnyShown()
        {
            foreach (var t in _toasts) if (t.Shown && !t.Dismissed) return true;
            return false;
        }

        bool SafeHold()
        {
            try { return HeldByStory || (Hold != null && Hold()); }
            catch (Exception) { return false; }
        }

        static bool SafeKeep(Toast t)
        {
            try { return t.KeepWhile(); }
            catch (Exception) { return false; }
        }
    }
}
