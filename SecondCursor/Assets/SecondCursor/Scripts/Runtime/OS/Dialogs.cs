using System;
using System.Collections.Generic;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>A message box window. Buttons are addressable as "button:&lt;Label&gt;" so the entity can race you to them.</summary>
    public sealed class MessageBox
    {
        public OSWindow Window;
        public readonly Dictionary<string, UiButton> Buttons = new Dictionary<string, UiButton>();
        public string Result;
        public CursorAgent AnsweredBy;
        /// <summary>Phase N: a strip between the text and the buttons for a live line (the race to No), or null.</summary>
        public RectTransform Status;
        public event Action<string, CursorAgent> Answered;

        public bool IsOpen => Window != null && !Window.IsClosed;

        public UiButton Button(string label) => Buttons.TryGetValue(label, out var b) ? b : null;

        internal void Answer(string result, CursorAgent by)
        {
            if (Result != null) return;
            Result = result;
            AnsweredBy = by;
            Answered?.Invoke(result, by);
            if (Window != null && !Window.IsClosed) Window.Close(by);
        }
    }

    /// <summary>Progress dialog with a chunky block bar and a Cancel button (used by shredding).</summary>
    public sealed class ProgressDialog
    {
        public OSWindow Window;
        public UiButton CancelButton;
        public PixelText Text;
        /// <summary>Phase S: a strip under the bar for a live line (who can press Cancel), or null.</summary>
        public RectTransform Status;
        RectTransform _barArea;
        readonly List<UnityEngine.UI.Image> _blocks = new List<UnityEngine.UI.Image>();
        float _progress;

        public event Action<CursorAgent> Cancelled;
        public bool IsOpen => Window != null && !Window.IsClosed;

        public float Progress
        {
            get => _progress;
            set
            {
                _progress = Mathf.Clamp01(value);
                int lit = Mathf.FloorToInt(_progress * _blocks.Count + 0.001f);
                for (int i = 0; i < _blocks.Count; i++) _blocks[i].enabled = i < lit;
            }
        }

        internal void BuildBar(RectTransform area)
        {
            _barArea = area;
            int blocks = Mathf.FloorToInt((area.rect.width - 4f) / 9f);
            for (int i = 0; i < blocks; i++)
            {
                var b = UIBuilder.Solid(area, Palette.Selection, "Block " + i);
                b.rectTransform.At(2 + i * 9, 2, 7, area.rect.height - 4);
                b.enabled = false;
                _blocks.Add(b);
            }
        }

        internal void RaiseCancel(CursorAgent by) => Cancelled?.Invoke(by);

        public void Close(CursorAgent by = null)
        {
            if (Window != null && !Window.IsClosed) Window.Close(by);
        }
    }

    public static class Dialogs
    {
        const int ButtonW = 72;
        const int ButtonH = 22;
        /// <summary>Seconds after a progress dialog opens during which the player's Cancel click is ignored.</summary>
        public const float CancelGrace = 0.5f;

        /// <param name="statusHeight">Phase N: room for <see cref="MessageBox.Status"/> under the text (0 = none).</param>
        public static MessageBox Message(GameServices g, string title, string text, string icon, string[] buttons,
            Action<string, CursorAgent> onResult, int defaultIndex = 0, Vector2? desktopTopLeft = null, int statusHeight = 0)
        {
            buttons = buttons == null || buttons.Length == 0 ? new[] { "OK" } : buttons;
            // Phase Q4 (A5): the body and the race line follow the Reading text size; the box grows with them.
            float f = DisplaySettings.ReadingFactor;
            int textMax = Mathf.RoundToInt(280 * f);
            var size = PixelFont.Measure(text, textMax, false, f);
            statusHeight = Mathf.RoundToInt(statusHeight * f);
            // Phase S (Large text: "Read easier" shrank to fit a 72 px button): each button is as wide as its label needs at the reading size.
            float bf = Mathf.Min(f, 2f);
            int buttonH = bf > 1f ? Mathf.CeilToInt(PixelFont.GlyphHeight * bf) + 12 : ButtonH;
            var widths = new int[buttons.Length];
            int buttonsTotal = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                widths[i] = Mathf.Max(ButtonW, PixelFont.MeasureLine(buttons[i], false, bf) + 16);
                buttonsTotal += widths[i] + (i > 0 ? 6 : 0);
            }
            // A status line needs room for "Session 209 is holding No for you. Click Yes." in bold.
            int clientW = Mathf.Max(size.x + 62, buttonsTotal + 20, statusHeight > 0 ? Mathf.RoundToInt(380 * f) : 200);
            int clientH = Mathf.Max(size.y, 32) + 22 + buttonH + 14 + (statusHeight > 0 ? statusHeight + 6 : 0);
            int w = clientW + 8;
            int h = clientH + OSWindow.CaptionHeight + 9;
            var pos = desktopTopLeft ?? (Vector2)WindowManager.Centered(w, h);

            var box = new MessageBox();
            var win = g.Windows.Create("dialog", title, null, (int)pos.x, (int)pos.y, w, h, WindowFlags.Dialog);
            box.Window = win;
            win.Owner = box;

            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIBuilder.Icon(win.Client, icon, 2);
                ic.rectTransform.anchoredPosition = new Vector2(10f, -10f);
            }
            var t = UIBuilder.Text(win.Client, text, Palette.Text);
            t.Wrap = true;
            t.Factor = f;
            t.rectTransform.At(52, 12, clientW - 60, Mathf.Max(size.y, 32) + 4);
            if (size.y < 32) t.VAlign = TextVAlign.Middle;
            if (statusHeight > 0) box.Status = UIBuilder.Rect("Status", win.Client).At(52, 12 + Mathf.Max(size.y, 32) + 10, clientW - 60, statusHeight);

            int bx = (clientW - buttonsTotal) / 2;
            for (int i = 0; i < buttons.Length; i++)
            {
                string label = buttons[i];
                var b = UiButton.Create(win.Client, label, a => box.Answer(label, a), "button:" + label);
                ((RectTransform)b.transform).At(bx, clientH - buttonH - 10, widths[i], buttonH);
                bx += widths[i] + 6;
                if (i == defaultIndex) b.IsDefault = true;
                box.Buttons[label] = b;
            }
            win.Closed += (x, a) => box.Answer("Close", a);
            if (onResult != null) box.Answered += onResult;
            // Phase M: the alarm bell is for warnings and refusals; a question or a notice opens with the window's own swell.
            Sfx.Play(icon == "icon_error" ? "sys_error" : icon == "icon_question" || icon == "icon_info" ? "ui_window" : "sys_warning");
            return box;
        }

        /// <param name="statusHeight">Phase S: room for <see cref="ProgressDialog.Status"/>, a live line under the bar (who can press Cancel), 0 = none.</param>
        public static ProgressDialog Progress(GameServices g, string title, string text, string icon = "icon_disposal_full", int statusHeight = 0)
        {
            // Phase S: the dialog follows the Reading text size like every other box (the Large text tester found "Shredding..." at 8 px).
            float f = DisplaySettings.ReadingFactor;
            int textW = Mathf.RoundToInt(250 * f);
            var textSize = PixelFont.Measure(text, textW, false, f);
            int textH = Mathf.Max(26, textSize.y + 2);
            int statusH = statusHeight > 0 ? Mathf.RoundToInt(statusHeight * f) + 4 : 0;
            int barY = 10 + textH + 8;
            int barH = Mathf.RoundToInt(18 * Mathf.Min(f, 1.5f));
            int statusY = barY + barH + 6;
            int cancelY = statusY + statusH + (statusH > 0 ? 2 : 0) + 4;
            int btnW = Mathf.RoundToInt(ButtonW * Mathf.Min(f, 1.5f)), btnH = Mathf.RoundToInt(ButtonH * Mathf.Min(f, 1.5f));
            int w = Mathf.Min(ScreenRig.Width - 20, Mathf.Max(320, textW + 70, statusHeight > 0 ? Mathf.RoundToInt(380 * f) + 40 : 0));
            int h = cancelY + btnH + 14 + OSWindow.CaptionHeight + 9 - 8;
            var pos = WindowManager.Centered(w, h);
            var dlg = new ProgressDialog();
            var win = g.Windows.Create("progress", title, null, pos.x, pos.y, w, h, WindowFlags.AlwaysOnTop | WindowFlags.NoTaskbar);
            dlg.Window = win;
            win.Owner = dlg;

            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIBuilder.Icon(win.Client, icon, 2);
                ic.rectTransform.anchoredPosition = new Vector2(10f, -8f);
            }
            dlg.Text = UIBuilder.Text(win.Client, text, Palette.Text);
            dlg.Text.Factor = f;
            dlg.Text.rectTransform.At(52, 10, w - 70, textH);
            dlg.Text.Wrap = true;

            var bar = UIBuilder.Bevel(win.Client, BevelStyle.Sunken, "Progress Bar");
            bar.rectTransform.At(10, barY, w - 28, barH);
            Canvas.ForceUpdateCanvases();
            dlg.BuildBar(bar.rectTransform);
            if (statusH > 0) dlg.Status = UIBuilder.Rect("Status", win.Client).At(10, statusY, w - 28, statusH);

            // Phase H: the dialog opens where Yes was, so Cancel can land under the pointer. A click of the player's in the
            // first moment is a reflex, not a decision: it is ignored (another cursor's click never is).
            float openedAt = Time.unscaledTime;
            dlg.CancelButton = UiButton.Create(win.Client, "Cancel", a =>
            {
                if (a != null && a.IsPlayer && Time.unscaledTime - openedAt < CancelGrace)
                {
                    Core.GameLog.Info(Core.LogChannel.Player, "Cancel ignored (clicked " + (Time.unscaledTime - openedAt).ToString("0.00") + " s after " + title + " opened)");
                    // Phase S (Cancel "did nothing"): say that a click this early is taken for a slip, and shake the box.
                    win.Shake(0.2f, 2f);
                    g.Notifications.Show(g.Content.Text("app.disposal"), g.Content.Text("shred.cancel.ignored"), "icon_info", null, "ui_select", false, null, Core.Game.NoticeKind.Plain, null, true);
                    return;
                }
                dlg.RaiseCancel(a);
            }, "button:Cancel");
            ((RectTransform)dlg.CancelButton.transform).At((w - 8 - btnW) / 2, cancelY, btnW, btnH);
            dlg.Progress = 0f;
            return dlg;
        }
    }
}
