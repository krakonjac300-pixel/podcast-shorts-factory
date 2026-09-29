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
    /// detected"). Clicking one runs its action. Stacks upward when several are visible.
    /// </summary>
    public sealed class Notifications : MonoBehaviour
    {
        const int W = 220;
        const int H = 58;
        const float Life = 7f;

        sealed class Toast
        {
            public RectTransform Rect;
            public float Age;
            public float Slot;
            public bool Dismissed;
        }

        RectTransform _layer;
        readonly List<Toast> _toasts = new List<Toast>();

        public static Notifications Create(RectTransform layer)
        {
            var n = layer.gameObject.AddComponent<Notifications>();
            n._layer = layer;
            return n;
        }

        public void Show(string title, string body, string icon = "icon_info", Action<CursorAgent> onClick = null, string sound = "notify_mail")
        {
            var rt = UIBuilder.Rect("Toast " + title, _layer);
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(W, H);
            var face = rt.gameObject.AddComponent<BevelGraphic>();
            face.Style = BevelStyle.Window;
            face.Fill = Palette.Tooltip;
            face.raycastTarget = false;

            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIBuilder.Icon(rt, icon, 1);
                ic.rectTransform.anchoredPosition = new Vector2(8f, -8f);
            }
            var t = UIBuilder.Text(rt, title, Palette.Text, true);
            t.rectTransform.At(30, 8, W - 38, 12);
            var b = UIBuilder.Text(rt, body, Palette.Text);
            b.Wrap = true;
            b.rectTransform.At(30, 22, W - 38, H - 28);

            var toast = new Toast { Rect = rt, Slot = _toasts.Count };
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
        }

        void Update() => Layout(Time.deltaTime);   // game time: toasts wait behind the pause menu

        void Layout(float dt)
        {
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                var t = _toasts[i];
                t.Age += dt;
                if (t.Rect == null || t.Dismissed || t.Age > Life + 0.3f)
                {
                    if (t.Rect != null) Destroy(t.Rect.gameObject);
                    _toasts.RemoveAt(i);
                }
            }
            for (int i = 0; i < _toasts.Count; i++)
            {
                var t = _toasts[i];
                t.Slot = Mathf.MoveTowards(t.Slot, i, dt * 6f);
                float slideIn = Mathf.Clamp01(t.Age / 0.2f);
                float slideOut = Mathf.Clamp01((t.Age - Life) / 0.3f);
                // Stack above the Disposal bin so toasts never cover the drop target.
                float y = WindowManager.TaskbarHeight + 84 + t.Slot * (H + 4);
                y -= (1f - slideIn) * (H + 8);
                t.Rect.anchoredPosition = new Vector2(-4f, Mathf.Round(y - slideOut * (H + 8)));
            }
        }
    }
}
