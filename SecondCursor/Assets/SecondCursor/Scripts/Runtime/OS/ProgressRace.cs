using SecondCursor.Entity;
using SecondCursor.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Phase S (eleven blind testers: "Session 017 can still press Cancel, and nothing says what to do about it"): the live line of a raced
    /// shred's progress dialog. It says who can press Cancel and the one thing the player can do (hold the pointer on Cancel: a pointer on the
    /// button keeps another from clicking it), and confirms it while the player is doing it. It only describes the fight; nothing here changes it.
    /// </summary>
    public sealed class ProgressRace : MonoBehaviour
    {
        GameServices _g;
        ProgressDialog _dlg;
        PixelText _line;
        UnityEngine.UI.Image _icon;
        Core.Game.NoticeKind _iconKind = (Core.Game.NoticeKind)(-1);
        int _textH = 12;

        public static void Attach(GameServices g, ProgressDialog dlg)
        {
            if (dlg?.Status == null || dlg.Window == null) return;
            var r = dlg.Window.gameObject.AddComponent<ProgressRace>();
            r._g = g;
            r._dlg = dlg;
            float f = DisplaySettings.ReadingFactor;
            r._line = UIBuilder.Text(dlg.Status, "", Palette.Text, true, "Race Line");
            r._line.Wrap = true;
            r._line.Factor = f;
            r._textH = Mathf.RoundToInt(12 * f);
            r._line.rectTransform.Stretch(0, 0, 0, 0);
            r._icon = ActorSprites.TinyIcon(dlg.Status, Core.Game.NoticeKind.Entity);
            r._icon.rectTransform.anchoredPosition = new Vector2(0f, -2f);
            r._icon.enabled = false;
            r.LateUpdate();
        }

        void ShowIcon(Core.Game.NoticeKind kind)
        {
            bool show = Core.Game.ActorStyle.IsSession(kind);
            if (kind == _iconKind) return;
            _iconKind = kind;
            _icon.enabled = show;
            if (show) _icon.sprite = ActorSprites.For(ActorSprites.Tiny, kind);
            _line.rectTransform.Stretch(show ? 14 : 0, 0, 0, 0);
        }

        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        void LateUpdate()
        {
            if (_dlg == null || !_dlg.IsOpen || _dlg.CancelButton == null) return;
            var cancel = _dlg.CancelButton.Hit;
            var c = _g.Content;
            string text;
            var kind = Core.Game.NoticeKind.Plain;
            bool alarm = false;
            var e = _g.Entity;
            EntityController holder = null;
            foreach (var other in new[] { _g.Entity, _g.Gary })
                if (other != null && other.IsVisible && other.Guarding == cancel) holder = other;
            bool racerHere = e != null && e.IsVisible && e.Agent != null && e.Agent.Enabled;
            if (holder != null && holder != e)
            {
                text = c.Format("race.cancel.holding", Capital(SystemNotices.SessionOf(_g, holder.Agent)));
                kind = holder.Agent.Actor;
            }
            else if (racerHere && e.IsBlockedByPlayer(cancel))
                text = c.Text("race.cancel.blocked", "");
            else if (racerHere)
            {
                text = c.Format("race.cancel.reaching", Capital(SystemNotices.SessionOf(_g, e.Agent)));
                kind = e.Agent.Actor;
                alarm = true;
            }
            else
                text = c.Format("race.cancel.idle", Capital(SystemNotices.SessionOf(_g, e != null ? e.Agent : null)));
            _line.text = text;
            ShowIcon(kind);
            _line.color = alarm ? (Color)Palette.Red : (Color)Palette.Text;
        }
    }
}
