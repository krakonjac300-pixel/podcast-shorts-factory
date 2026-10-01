using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// The Phase N speed model (<see cref="TugModel.Speed"/>): pull fast along an arrow until the bar is yours. Her end drifts away along
    /// her way, the file sits between the two pointers. Kept behind the switch until real hands decide (review board E8).
    /// </summary>
    public sealed partial class ConflictSystem
    {
        Vector2 _escapeDir;
        /// <summary>
        /// Phase K: every contest starts with this standoff (her pull and drift wait, the player's pull counts) while a big arrow at
        /// the pointer shows the way; the night's first contest keeps its longer read grace.
        /// </summary>
        public const float GrabHitchSeconds = 0.3f;
        /// <summary>Phase N: the way each file was fought over tonight: a retry over the same file keeps it (room permitting).</summary>
        readonly Dictionary<string, Vector2> _escapeByFile = new Dictionary<string, Vector2>();

        void BeginSpeed(DragPayload p)
        {
            CurrentSettings.readGrace = Mathf.Max(CurrentSettings.readGrace, GrabHitchSeconds);
            var model = new TugOfWar(CurrentSettings);
            _model = model;
            // Away from the player and the bin, turned if the player's pull would have no room (a grab by the bin). Phase N: a file fought
            // over before tonight keeps its way, turned only as far as the room at this grab needs.
            var player = _g.Player.Position.ToCore();
            _escapeDir = (_escapeByFile.TryGetValue(p.FileId ?? "", out var before)
                ? TugGeometry.WithRoom(before.ToCore(), player, ScreenRig.Width, ScreenRig.Height, WindowManager.TaskbarHeight)
                : TugGeometry.EscapeDirection(player, _g.EntityAgent.Position.ToCore(), _g.Desktop.DisposalIcon.Hit.Center.ToCore(),
                    ScreenRig.Width, ScreenRig.Height, WindowManager.TaskbarHeight, TugGeometry.MinPlayerRoom + TugGeometry.FirstArrowMargin)).ToUnity();
            if (_escapeDir.sqrMagnitude < 0.1f) _escapeDir = Vector2.up;
            _escapeDir.Normalize();
            if (p.FileId != null) _escapeByFile[p.FileId] = _escapeDir;
            // Phase K: the arrow is decided once per fight and it is exactly what counts: pulling along it is the pull.
            model.PullAxis = (-_escapeDir).ToCore();
            _coach = new TugCoach((-_escapeDir).ToCore());
            GameLog.Info(LogChannel.Entity, "Tug arrow " + ArrowDirection + (before != default ? " (same file as before)" : ""));
        }

        TugOutcome StepSpeed(float dt, bool playerGrips, float grip)
        {
            var model = (TugOfWar)_model;
            var player = _g.Player;
            var entity = _g.EntityAgent;
            // The entity's end drags away (strength-dependent), with a nervous tremble. During the read grace it holds
            // still (only the tremble), so the label can be read and the cursors do not drift apart.
            float driftScale = model.InReadGrace ? 0f : 1f;
            Vector2 drift = _escapeDir * TugOfWar.EntityDriftSpeed(grip) * dt * driftScale;
            // At a screen edge her end slides along it (Phase K: the arrow, which is her way reversed, never flips mid-fight).
            if ((entity.Position.x <= 1f && drift.x < 0f) || (entity.Position.x >= ScreenRig.Width - 2f && drift.x > 0f)) drift.x = 0f;
            if ((entity.Position.y <= WindowManager.TaskbarHeight + 1f && drift.y < 0f) || (entity.Position.y >= ScreenRig.Height - 2f && drift.y > 0f)) drift.y = 0f;
            // The tremble is a random walk: its step shrinks with the frame time, so it spreads the same per second at any frame rate.
            drift += UnityEngine.Random.insideUnitCircle * ((1.5f + model.Strain * 3f) * Mathf.Sqrt(dt * 60f));
            entity.Position = ScreenRig.ClampToScreen(entity.Position + drift);

            // Phase N: the coach judges the pull from the end of GET READY on.
            if (!model.InReady) _coach.Step(dt, player.Position.ToCore(), playerGrips);
            var outcome = model.Step(dt, player.Position.ToCore(), playerGrips, entity.Position.ToCore(), grip);
            float strain = model.Strain;

            Vector2 obj = model.ObjectPosition.ToUnity();
            _lastObject = obj;
            Vector2 shake = UnityEngine.Random.insideUnitCircle * (strain * 4f);
            _payload.GhostPosition = obj + new Vector2(-16f, 14f) + shake;

            UpdateBand(player.Position, obj, entity.Position, strain);
            // Phase P (R1): the rest of the screen dims around the player's pointer and the file.
            if (outcome == TugOutcome.None) Dim.Show((player.Position + obj) * 0.5f, 120f);
            // Feel: your cursor is dragged a little toward it; its cursor shakes with effort.
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = (entity.Position - player.Position).normalized * (strain * 5f);
            if (_g.EntityView != null) _g.EntityView.Jitter = 0.5f + strain * 2f;
            return outcome;
        }
    }
}
