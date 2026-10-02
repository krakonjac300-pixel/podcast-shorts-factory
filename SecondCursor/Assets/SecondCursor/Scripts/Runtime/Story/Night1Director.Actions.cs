using SecondCursor.Core;
using SecondCursor.Core.Story;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q1 (owner 4, A T5): Night 1's scare born from the player's own action. A program icon the player dragged somewhere else on the
    /// desktop is back where it was the next time they look: it moves only while a window covers it, once a night, after the tutorial, and
    /// through the scheduler's action gate (never during a tug, a dialog, typing or a drag). Nothing a task needs is touched.
    /// </summary>
    public sealed partial class Night1Director
    {
        DesktopIcon _movedIcon;
        Vector2 _movedFrom, _movedTo;
        bool _iconBackArmed;

        /// <summary>The icon has to have really moved (a nudge of a few pixels is not "where it was").</summary>
        const float IconBackMinDistance = 40f;

        void OnAppIconMoved(DesktopIcon icon, Vector2 from)
        {
            if (icon == null) return;
            if (_movedIcon == null)
            {
                if (Vector2.Distance(from, icon.TopLeft) < IconBackMinDistance) return;
                _movedIcon = icon;
                _movedFrom = from;
            }
            if (icon == _movedIcon) _movedTo = icon.TopLeft;
            if (_g.Flags.Has(Flags.TutorialDone)) ArmIconBack();
        }

        void ArmIconBack()
        {
            if (_iconBackArmed || _movedIcon == null || IsStandIn) return;
            _iconBackArmed = true;
            _g.Scares.SlotAction("icon_back", 15f, 600f, IconCovered, () =>
            {
                if (_movedIcon == null) return;
                _movedIcon.TopLeft = _movedFrom;
                GameLog.Info(LogChannel.Story, "Anomaly: " + _movedIcon.Label + " icon back where it was (" + _movedFrom + ")");
            });
        }

        /// <summary>The moved icon is still where the player put it, and something covers it (they will see it back when they look).</summary>
        bool IconCovered()
        {
            if (_movedIcon == null || !_movedIcon.isActiveAndEnabled || Vector2.Distance(_movedIcon.TopLeft, _movedTo) > 1f) return false;
            if (Vector2.Distance(_movedTo, _movedFrom) < IconBackMinDistance || _g.Player.Payload != null) return false;
            var hit = _g.Router.HitTest(_movedIcon.Hit.Center);
            return hit != null && hit != _movedIcon.Hit;
        }
    }
}
