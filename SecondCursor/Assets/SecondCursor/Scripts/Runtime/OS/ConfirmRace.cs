using System;
using SecondCursor.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Names the pointer acting on a confirmation without obscuring its movement with a separate race display.
    /// This describes the interaction only; the pointers and buttons decide its outcome.
    /// </summary>
    public sealed class ConfirmRace : MonoBehaviour
    {
        GameServices _g;
        MessageBox _box;
        EntityController _racer;
        Func<bool> _racing;
        string _idleKey;
        PixelText _line;
        UnityEngine.UI.Image _icon;
        Core.Game.NoticeKind _iconKind = (Core.Game.NoticeKind)(-1);
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
            g.Router.PressRefused += r.OnRefused;
            r.LateUpdate();
        }

        float _refusedUntil = -1f;

        /// <summary>Phase S: the player's click on a covered Yes bounces: the line says so for a moment (nothing else told the player why).</summary>
        void OnRefused(CursorAgent a, Interactable hit)
        {
            if (a == null || !a.IsPlayer || _box == null || !_box.IsOpen) return;
            var yes = _box.Button("Yes");
            if (yes != null && hit == yes.Hit) _refusedUntil = Time.time + 2.5f;
        }

        void OnDestroy()
        {
            if (_g != null && _g.Router != null) _g.Router.PressRefused -= OnRefused;
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
            bool approaching = false;
            var kind = Core.Game.NoticeKind.Plain;
            foreach (var other in new[] { _g.Entity, _g.Gary })
            {
                if (other == null || !other.IsVisible || other.Agent == null || !other.Agent.Enabled) continue;
                if (other.Guarding == no.Hit && other != _racer) { text = _g.Content.Format("race.holding", Session(other)); kind = other.Agent.Actor; }
                else if (other.Guarding == yes.Hit) { text = _g.Content.Format("race.covering", Session(other)); kind = other.Agent.Actor; }
            }
            if (Time.time < _refusedUntil)
            {
                // The click was refused: say why and what to do (a pointer held on No keeps the other from clicking it).
                foreach (var other in new[] { _g.Entity, _g.Gary })
                    if (other != null && other.IsVisible && other.Guarding == yes.Hit)
                    {
                        text = _g.Content.Format("race.covered.click", Session(other));
                        kind = other.Agent.Actor;
                        approaching = true;
                    }
            }
            if (text == null && _racer != null && _racer.IsVisible && _racing() && _racer.Brain != null && _racer.Brain.DraggingDialog)
            {
                text = _g.Content.Format("race.dragging", Session(_racer));
                kind = _racer.Agent.Actor;
                approaching = true;
            }
            if (text == null && _racer != null && _racer.IsVisible && _racing())
            {
                approaching = true;
                text = _g.Content.Format("race.reaching", Session(_racer));
                kind = _racer.Agent.Actor;
            }
            _line.text = text ?? _g.Content.Text(_idleKey, "");
            ShowIcon(text != null ? kind : Core.Game.NoticeKind.Plain);
            _line.color = approaching ? (Color)Palette.Red : (Color)Palette.Text;
        }
    }
}
