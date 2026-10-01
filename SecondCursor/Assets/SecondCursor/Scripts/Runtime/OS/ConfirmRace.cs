using System;
using SecondCursor.Entity;
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.OS
{
    /// <summary>
    /// Phase N (fifth blind playtest, findings 2 and 9): a confirm another pointer races you on says so inside the dialog. A line under
    /// the question names who is doing what ("Session 017 is reaching for No.", "Session 209 is holding No for you.", "Session 017 is
    /// covering Yes.") and a bar fills as the racing pointer closes in on No. It only describes the race: nothing here changes it.
    /// </summary>
    public sealed class ConfirmRace : MonoBehaviour
    {
        const int Blocks = 26, BarH = 8;
        /// <summary>The bar's full length in px of pointer travel at least.</summary>
        const float BarSpan = 240f;

        GameServices _g;
        MessageBox _box;
        EntityController _racer;
        Func<bool> _racing;
        string _idleKey;
        PixelText _line;
        UnityEngine.UI.Image _icon;
        Core.Game.NoticeKind _iconKind = (Core.Game.NoticeKind)(-1);
        RectTransform _bar;
        readonly Image[] _blocks = new Image[Blocks];
        float _farthest;
        int _textH = 12;

        /// <summary>
        /// Watches <paramref name="box"/> (which must have been made with a status strip). <paramref name="racer"/> goes for No while
        /// <paramref name="racing"/> says so; any other pointer sitting on No holds it for the player. <paramref name="idleKey"/>: the line
        /// before anyone moves.
        /// </summary>
        public static void Attach(GameServices g, MessageBox box, EntityController racer, Func<bool> racing, string idleKey)
        {
            if (box?.Status == null || box.Window == null) return;
            var r = box.Window.gameObject.AddComponent<ConfirmRace>();
            r._g = g;
            r._box = box;
            r._racer = racer;
            r._racing = racing;
            r._idleKey = idleKey;
            r._line = UIBuilder.Text(box.Status, "", Palette.Text, true, "Race Line");
            float f = DisplaySettings.ReadingFactor;
            r._line.Factor = f;
            r._textH = Mathf.RoundToInt(12 * f);
            r._line.rectTransform.TopStrip(0, r._textH);
            // Phase Q4 (R2): the racer's pointer, in its colours, stands 8 px left of the line (the same pointer the player sees move).
            r._icon = Rendering.ActorSprites.TinyIcon(box.Status, Core.Game.NoticeKind.Entity);
            r._icon.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            r._icon.enabled = false;
            var frame = UIBuilder.Bevel(box.Status, BevelStyle.Sunken, "Race Bar");
            r._bar = frame.rectTransform;
            r._bar.TopStrip(Mathf.RoundToInt(14 * f), BarH + 4, 0, 0);
            for (int i = 0; i < Blocks; i++)
            {
                r._blocks[i] = UIBuilder.Solid(r._bar, Palette.Red, "Block " + i);
                r._blocks[i].enabled = false;
            }
            r.LateUpdate();
        }

        /// <summary>The racer's pointer left of the line while someone is racing, else only the text (the line then starts at the edge).</summary>
        void ShowIcon(Core.Game.NoticeKind kind)
        {
            bool show = Core.Game.ActorStyle.IsSession(kind);
            if (kind == _iconKind) return;
            _iconKind = kind;
            _icon.enabled = show;
            if (show) _icon.sprite = Rendering.ActorSprites.For(Rendering.ActorSprites.Tiny, kind);
            _line.rectTransform.TopStrip(0, _textH, show ? 14 : 0, 0);
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        string Session(EntityController c) => Capital(SystemNotices.SessionOf(_g, c.Agent));

        void LateUpdate()
        {
            if (_box == null || !_box.IsOpen) return;
            var yes = _box.Button("Yes");
            var no = _box.Button("No");
            if (yes == null || no == null) return;
            string text = null;
            float fill = -1f;
            var kind = Core.Game.NoticeKind.Plain;
            foreach (var other in new[] { _g.Entity, _g.Gary })
            {
                if (other == null || !other.IsVisible || other.Agent == null || !other.Agent.Enabled) continue;
                if (other.Guarding == no.Hit && other != _racer) { text = _g.Content.Format("race.holding", Session(other)); kind = other.Agent.Actor; }
                else if (other.Guarding == yes.Hit) { text = _g.Content.Format("race.covering", Session(other)); kind = other.Agent.Actor; }
            }
            if (text == null && _racer != null && _racer.IsVisible && _racing())
            {
                // Her progress: how much of the way from the farthest she has been (since she came for it, and never less than
                // BarSpan) she has covered, so a pointer that starts close shows close.
                float d = Vector2.Distance(_racer.Agent.Position, no.Hit.Center);
                _farthest = Mathf.Max(_farthest, d, BarSpan);
                fill = no.Hit.WorldRect.Contains(_racer.Agent.Position) ? 1f : 1f - d / _farthest;
                text = _g.Content.Format("race.reaching", Session(_racer));
                kind = _racer.Agent.Actor;
            }
            _line.text = text ?? _g.Content.Text(_idleKey, "");
            ShowIcon(text != null ? kind : Core.Game.NoticeKind.Plain);
            _line.color = fill >= 0f ? (Color)Palette.Red : (Color)Palette.Text;
            _bar.gameObject.SetActive(fill >= 0f);
            if (fill < 0f) return;
            float w = _bar.rect.width - 4f, step = w / Blocks;
            int lit = Mathf.FloorToInt(Mathf.Clamp01(fill) * Blocks + 0.001f);
            for (int i = 0; i < Blocks; i++)
            {
                _blocks[i].rectTransform.At(2 + Mathf.Round(i * step), 2, Mathf.Max(1f, Mathf.Round(step) - 2f), BarH);
                _blocks[i].enabled = i < lit;
            }
        }
    }
}
