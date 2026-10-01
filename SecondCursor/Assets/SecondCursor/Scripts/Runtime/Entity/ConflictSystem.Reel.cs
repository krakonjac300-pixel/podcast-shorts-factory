using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase P, the reel (<see cref="TugModel.Reel"/>, review board E1 to E6): "haul it to the bin". The track runs from the grabbed file to
    /// the Disposal bin; each stroke toward the bin reels the file along it and her pointer rides the far end, dragging it back. The file
    /// at the bin drops in as the player's own drop; at a tear line it snaps to the player's pointer. The player's pointer is never moved.
    /// </summary>
    public sealed partial class ConflictSystem
    {
        /// <summary>A file torn loose (or lost) flies from the track to the winner's hand in this long.</summary>
        const float TearSnapSeconds = 0.12f;
        /// <summary>Her pointer's tremble and the file's shake are re-rolled every 1/60 s, so they look the same at any frame rate.</summary>
        StepTimer _feelStep;
        Vector2 _tremble, _shake;

        void BeginReel(DragPayload p)
        {
            CurrentSettings.reel.seed = UnityEngine.Random.Range(1, int.MaxValue);
            var reel = new TugReel(CurrentSettings);
            reel.Begin((p.GhostPosition + new Vector2(16f, -16f)).ToCore(), _g.EntityAgent.Position.ToCore(), _g.Desktop.DisposalIcon.Hit.Center.ToCore(),
                new ScreenBounds(ScreenRig.Width, ScreenRig.Height, WindowManager.TaskbarHeight));
            _model = reel;
            _coach = new TugCoach(reel.Axis);
            _feelStep.Prime();
            GameLog.Info(LogChannel.Entity, "Tug haul " + ArrowDirection + " " + reel.Finish.ToString("0") + " px to " + (reel.FinishIsBin ? "the bin" : "a tear line")
                + ", her line " + reel.HerLine.ToString("0") + " px" + (reel.Origin != reel.GrabOrigin ? " (grabbed by the bin: slid back)" : "") + ", " + reel.Variant);
        }

        TugOutcome StepReel(float dt, bool playerGrips, float grip)
        {
            var reel = (TugReel)_model;
            var player = _g.Player;
            var entity = _g.EntityAgent;
            // The coach judges from the end of GET READY on, on the reel's smoothed stroke (hand over hand is not stopping).
            if (!reel.InReady) _coach.Step(dt, player.Position.ToCore(), playerGrips, reel.ReelSpeed);
            var outcome = reel.Step(dt, player.Position.ToCore(), playerGrips, Vec2.Zero, grip);
            float strain = reel.Strain;
            if (_feelStep.Tick(dt))
            {
                _tremble = UnityEngine.Random.insideUnitCircle * (1.5f + strain * 3f);
                _shake = UnityEngine.Random.insideUnitCircle * (strain * 4f);
            }
            // Her pointer rides the far end of the file: hauled toward the bin when the player gains, walking it back when she does.
            entity.Position = ScreenRig.ClampToScreen(reel.EntityPosition.ToUnity() + _tremble);
            Vector2 obj = reel.ObjectPosition.ToUnity();
            _lastObject = obj;
            _payload.GhostPosition = obj + new Vector2(-16f, 16f) + _shake;
            UpdateBand(player.Position, obj, entity.Position, strain);
            // The player's arrow leans toward her by at most 3 px (the hotspot never moves); hers shakes with effort.
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = (entity.Position - player.Position).normalized * Mathf.Min(3f, strain * 5f);
            if (_g.EntityView != null) _g.EntityView.Jitter = 0.5f + strain * 2f;
            return outcome;
        }
    }
}
