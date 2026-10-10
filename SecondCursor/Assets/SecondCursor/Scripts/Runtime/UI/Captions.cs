using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Game;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.UI
{
    /// <summary>
    /// Phase Q4 (review board A6): sound captions. With the option on, a short bracketed line stands at the bottom of the screen for each
    /// story or scare sound as it plays ("[knock at a door]"), with a "&lt;" or "&gt;" mark for the side it came from. Which sounds, and how
    /// often, is <see cref="CaptionRules"/>; the wording is strings.json (<c>caption.&lt;sound id&gt;</c>), so a sound with no string is not
    /// captioned. Off by default; offered on the first-launch screen and in Options > Accessibility. A caption appears on the frame its sound
    /// starts, never before, and never names anything the sound has not already done.
    /// </summary>
    public sealed class Captions : MonoBehaviour
    {
        sealed class Line
        {
            public RectTransform Rect;
            public float Left;
            public float Height;
        }

        GameServices _g;
        RectTransform _layer;
        readonly CaptionRules _rules = new CaptionRules();
        readonly List<Line> _lines = new List<Line>();
        const int Margin = 8, Pad = 4;

        public static Captions Create(GameServices g, RectTransform layer)
        {
            var go = new GameObject("Captions");
            go.transform.SetParent(layer, false);
            var c = go.AddComponent<Captions>();
            c._g = g;
            c._layer = layer;
            g.Audio.SoundPlayed += c.OnSound;
            return c;
        }

        void OnSound(string id, float volume, float pan)
        {
            if (!AccessSettings.Captions || _g == null || _g.Content == null) return;
            if (!CaptionRules.IsCaptioned(id)) return;
            string key = CaptionRules.KeyFor(id);
            if (!_g.Content.HasText(key)) return;
            string text = _g.Content.Text(key);
            var cue = _rules.Select(id, volume, pan, Time.time, text.Length);
            if (!cue.HasValue) return;
            Show(CaptionRules.Format(text, cue.Value.Side), cue.Value.Seconds);
        }

        void Show(string text, float seconds)
        {
            int scale = Mathf.Clamp(DisplaySettings.ReadingScale, 1, 2);
            while (_lines.Count >= CaptionRules.MaxVisible) Drop(0);
            int width = Mathf.Min(ScreenRig.Width - 2 * Margin, PixelFont.MeasureLine(text, false, scale) + 2 * Pad + 2);
            int height = PixelFont.GlyphHeight * scale + 2 * Pad;
            var rt = UIBuilder.Rect("Caption", _layer);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(width, height);
            var back = rt.gameObject.AddComponent<Image>();
            back.color = new Color32(0x0B, 0x0E, 0x0D, 0xD8);
            back.raycastTarget = false;
            var label = UIBuilder.Text(rt, text, Palette.BiosBright);
            label.Scale = scale;
            label.Align = TextAlign.Center;
            label.VAlign = TextVAlign.Middle;
            label.rectTransform.Stretch(Pad, 0, Pad, 0);
            _lines.Add(new Line { Rect = rt, Left = seconds, Height = height });
            rt.SetAsLastSibling();   // above an end card or the pause panel: a deaf player needs the caption most then
            Layout();
        }

        void Drop(int index)
        {
            if (_lines[index].Rect != null) Destroy(_lines[index].Rect.gameObject);
            _lines.RemoveAt(index);
        }

        void Update()
        {
            if (_lines.Count == 0) return;
            float dt = Time.deltaTime;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                _lines[i].Left -= dt;
                if (_lines[i].Left <= 0f || _lines[i].Rect == null) Drop(i);
            }
            Layout();
        }

        /// <summary>The newest caption at the bottom, centred above the taskbar; older ones stand above it.</summary>
        void Layout()
        {
            float y = WindowManager.TaskbarHeight + Margin;
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var l = _lines[i];
                if (l.Rect == null) continue;
                float x = Mathf.Round((ScreenRig.Width - l.Rect.sizeDelta.x) * 0.5f);
                l.Rect.anchoredPosition = new Vector2(x, Mathf.Round(y));
                y += l.Height + 2;
            }
        }

        void OnDestroy()
        {
            if (_g != null && _g.Audio != null) _g.Audio.SoundPlayed -= OnSound;
        }
    }
}
