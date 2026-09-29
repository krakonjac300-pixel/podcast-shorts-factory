using System;
using System.Collections.Generic;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Balloon toasts that slide up above the tray ("You have 1 new message", "New input device
    /// detected"). Clicking one runs its action. Stacks upward when several are visible. They follow the Reading
    /// text option (Large doubles them, for the Steam Deck); a sticky toast stays until it is clicked.
    /// </summary>
    public sealed class Notifications : MonoBehaviour
    {
        const int W = 220;
        const int H = 58;
        const float Life = 7f;

        /// <summary>A shown toast: its body can change after it appears (a line that lands on its own beat).</summary>
        public sealed class Toast
        {
            internal RectTransform Rect;
            internal float Age;
            internal float Slot;
            internal bool Dismissed;
            internal bool Sticky;
            internal int Height;
            internal int Scale = 1;
            public PixelText Body { get; internal set; }

            public bool IsShowing => Rect != null && !Dismissed;

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
        {
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

            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIBuilder.Icon(rt, icon, s);
                ic.rectTransform.anchoredPosition = new Vector2(8f, -8f);
            }
            int textLeft = 14 + 16 * s;
            var t = UIBuilder.Text(rt, title, Palette.Text, true);
            t.Scale = s;
            t.rectTransform.At(textLeft, 8, w - textLeft - 8, 12 * s);
            var b = UIBuilder.Text(rt, body, Palette.Text);
            b.Scale = s;
            b.Wrap = true;
            b.rectTransform.At(textLeft, 8 + 14 * s, w - textLeft - 8, h - 14 * s - 14);

            var toast = new Toast { Rect = rt, Slot = _toasts.Count, Sticky = sticky, Height = h, Body = b, Scale = s };
            toast.Fit();
            var hit = UIBuilder.Hit(rt.gameObject, "toast:" + title, onClick != null ? CursorShape.Hand : CursorShape.Arrow);
            hit.passThroughWhileCarrying = true;
            hit.Click += (a, n) =>
            {
                toast.Dismissed = true;
                onClick?.Invoke(a);
            };
            _toasts.Add(toast);
            if (!string.IsNullOrEmpty(sound)) Sfx.Play(sound);
            Layout(0f);
            return toast;
        }

        void Update() => Layout(Time.deltaTime);   // game time: toasts wait behind the pause menu

        void Layout(float dt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                t.Age += dt;
                if (t.Sticky && t.Age > Life - 0.01f) t.Age = Life - 0.01f;
                if (t.Rect == null || t.Dismissed || t.Age > Life + 0.3f)
                {
                    if (t.Rect != null) Destroy(t.Rect.gameObject);
                    _toasts.RemoveAt(i);
                }
            }
            float y = WindowManager.TaskbarHeight + 84;
            for (int i = 0; i < _toasts.Count; i++)
            {
                var t = _toasts[i];
                t.Slot = Mathf.MoveTowards(t.Slot, i, dt * 6f);
                float slideIn = Mathf.Clamp01(t.Age / 0.2f);
                float slideOut = Mathf.Clamp01((t.Age - Life) / 0.3f);
                // Stack above the Disposal bin so toasts never cover the drop target.
                float ty = y + (t.Slot - i) * (t.Height + 4);
                ty -= (1f - slideIn) * (t.Height + 8);
                t.Rect.anchoredPosition = new Vector2(-4f, Mathf.Round(ty - slideOut * (t.Height + 8)));
                y += t.Height + 4;
            }
        }
    }
}
