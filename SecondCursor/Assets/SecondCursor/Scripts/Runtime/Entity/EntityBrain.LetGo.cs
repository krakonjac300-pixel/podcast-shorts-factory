using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Input;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase P (T1, plan 1.8): the Night 3 finale when she lets 017 go. Her hand still grabs the file, but once the hold is won her pointer walks
    /// with it about 40 px behind (FollowFile), and at Confirm Shred it rests trembling on No for 0.8 s, then slides to Yes and stops 8 px right
    /// of the player's pointer (RestOnYes). She never clicks and never guards; the player clicks Yes.
    /// </summary>
    public sealed partial class EntityBrain
    {
        /// <summary>Her pointer walks this far behind the carried file (away from the bin).</summary>
        const float FollowGap = 40f;
        /// <summary>She rests on No this long before sliding to Yes.</summary>
        const float RestOnNoSeconds = 0.8f;
        const float RestTremble = 1.5f;

        void AddLetGo()
        {
            Add("FollowFile", ScoreFollowFile, RunFollowFile, 0.2f);
            Add("RestOnYes", ScoreRestOnYes, RunRestOnYes, 0.5f);
        }

        float ScoreFollowFile()
        {
            var p = Player.Payload;
            if (!LetsGo || p == null || !IsProtected(p.FileId) || p.Contested || p != _wonByPlayer) return 0f;
            return 60f;
        }

        IEnumerator RunFollowFile()
        {
            var p = Player.Payload;
            if (p == null) yield break;
            Vector2 File() => p.GhostPosition + new Vector2(16f, -16f);
            yield return EnsurePresent(File() + Vector2.left * FollowGap);
            _c.State = Core.Entity.EntityState.Observing;
            GameLog.Info(LogChannel.Entity, "Following " + p.FileId + " to the bin");
            float jitter = _c.View != null ? _c.View.Jitter : 0f;
            if (_c.View != null) _c.View.Jitter = RestTremble;
            while (LetsGo && Player.Payload == p && !p.Dropped && !p.Contested)
            {
                Vector2 file = File(), bin = _g.Desktop.DisposalIcon.Hit.Center;
                Vector2 back = file - bin;
                back = back.sqrMagnitude > 1f ? back.normalized : Vector2.left;
                Vector2 target = ScreenRig.ClampToScreen(file + back * FollowGap);
                _c.Agent.Position = Vector2.Lerp(_c.Agent.Position, target, MathUtil.LerpAt60(0.15f, Time.deltaTime));
                yield return null;
            }
            if (_c.View != null) _c.View.Jitter = jitter;
        }

        float ScoreRestOnYes()
        {
            var box = _g.Shred.Confirm;
            if (!LetsGo || box == null || !box.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            return 90f;
        }

        /// <summary>To No, a trembling rest, then to Yes beside the player's pointer; she stays there until the dialog closes.</summary>
        IEnumerator RunRestOnYes()
        {
            var box = _g.Shred.Confirm;
            var no = box?.Button("No");
            var yes = box?.Button("Yes");
            if (no == null || yes == null) yield break;
            yield return EnsurePresent(EntryPointNear(no.Hit.Center));
            _c.State = Core.Entity.EntityState.Observing;
            float jitter = _c.View != null ? _c.View.Jitter : 0f;
            if (_c.View != null) _c.View.Jitter = RestTremble;
            GameLog.Info(LogChannel.Entity, "Resting on No");
            yield return _c.MoveToDynamic(() => box.IsOpen ? no.Hit.Center : (Vector2?)null, MovementProfiles.Hesitant, 4f);
            float until = Time.time + RestOnNoSeconds;
            while (box.IsOpen && Time.time < until)
            {
                _c.Agent.Position = Vector2.Lerp(_c.Agent.Position, no.Hit.Center, MathUtil.LerpAt60(0.2f, Time.deltaTime));
                yield return null;
            }
            if (box.IsOpen) GameLog.Info(LogChannel.Entity, "Resting on Yes");
            while (box.IsOpen)
            {
                // Beside the player's pointer when it is on Yes (8 px to its right), else on Yes itself. Never a click.
                Vector2 target = yes.Hit.WorldRect.Contains(Player.Position) ? Player.Position + new Vector2(8f, 0f) : yes.Hit.Center;
                _c.Agent.Position = Vector2.Lerp(_c.Agent.Position, target, MathUtil.LerpAt60(0.12f, Time.deltaTime));
                yield return null;
            }
            if (_c.View != null) _c.View.Jitter = jitter;
        }

        /// <summary>After a released hold she hovers near the file where it was let go, then the brain thinks again (she may grab it again).</summary>
        IEnumerator HoverAfterRelease()
        {
            _c.State = Core.Entity.EntityState.Observing;
            Vector2 at = _g.Conflict.ObjectPosition;
            yield return _c.Loiter(at + new Vector2(-30f, 24f), 20f, 0.8f, MovementProfiles.Hesitant);
        }
    }
}
