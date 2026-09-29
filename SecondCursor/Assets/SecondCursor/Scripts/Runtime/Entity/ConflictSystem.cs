using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Runs the tug-of-war when both cursors grip the same dragged file: feeds real cursor motion into the
    /// engine-free <see cref="TugOfWar"/> model, drags the entity's end away, positions the straining file
    /// ghost between the cursors, draws the "rubber band", drives the strain sound, shake and glitches,
    /// and hands the file to the winner.
    /// </summary>
    public sealed class ConflictSystem : MonoBehaviour
    {
        const int BandDots = 9;

        GameServices _g;
        TugOfWar _model;
        DragPayload _payload;
        Vector2 _escapeDir;
        readonly List<Image> _band = new List<Image>();
        float _glitchCooldown;

        public TugOfWarSettings Settings = new TugOfWarSettings();
        public bool IsFighting => _payload != null;
        public float Strain => _model != null && IsFighting ? _model.Strain : 0f;
        public float EntityShare => _model != null && IsFighting ? _model.EntityShare : 0f;

        public event Action<DragPayload> TugStarted;
        public event Action<DragPayload, TugOutcome> TugEnded;

        public static ConflictSystem Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Conflict");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ConflictSystem>();
            c._g = g;
            c._model = new TugOfWar(c.Settings);
            for (int i = 0; i < BandDots; i++)
            {
                var dot = UIBuilder.Solid(g.Layers.Effects, new Color(0.95f, 0.95f, 0.9f, 0f), "Tension " + i);
                var rt = dot.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2f, 2f);
                dot.enabled = false;
                c._band.Add(dot);
            }
            g.DragDrop.ContestStarted += c.OnContestStarted;
            g.DragDrop.PayloadFinished += (p, accepted) =>
            {
                if (p == c._payload) c.End(accepted ? TugOutcome.PlayerWins : TugOutcome.EntityWins, false);
            };
            return c;
        }

        void OnContestStarted(DragPayload p, CursorAgent contender)
        {
            if (_payload != null) return;
            bool pair = (p.Holder == _g.Player && contender == _g.EntityAgent) || (p.Holder == _g.EntityAgent && contender == _g.Player);
            if (!pair) return;
            _payload = p;
            _model = new TugOfWar(Settings);
            Vector2 away = (_g.EntityAgent.Position - _g.Player.Position);
            Vector2 fromBin = (_g.EntityAgent.Position - _g.Desktop.DisposalIcon.Hit.Center);
            _escapeDir = (away.normalized * 0.7f + fromBin.normalized * 0.3f).normalized;
            if (_escapeDir.sqrMagnitude < 0.1f) _escapeDir = Vector2.up;

            _g.Flags.Set(Flags.ConflictStarted);
            _g.Audio?.Play("grab_snap", 0.7f, 0.8f, Audio.AudioManager.PanFor(_g.EntityAgent.Position.x));
            _g.Audio?.PlayLoop("tug_strain", 0.2f, 0.1f);
            _g.Fx?.Glitch(0.12f, 0.6f);
            GameLog.Info(LogChannel.Entity, "Tug-of-war started over " + p.FileId);
            TugStarted?.Invoke(p);
        }

        public void Tick(float dt)
        {
            if (_payload == null || dt <= 0f) return;
            var player = _g.Player;
            var entity = _g.EntityAgent;
            bool playerGrips = (_payload.Holder == player || _payload.Contender == player) && player.Held;
            float grip = _g.Entity != null ? _g.Entity.Brain.Grip : 0.62f;

            // The entity's end drags away (strength-dependent), with a nervous tremble.
            Vector2 drift = _escapeDir * (55f + 70f * grip) * dt + UnityEngine.Random.insideUnitCircle * (1.5f + _model.Strain * 3f);
            entity.Position = ScreenRig.ClampToScreen(entity.Position + drift);
            // Bounce the escape direction off the screen edges so it doesn't get pinned.
            if (entity.Position.x <= 1f || entity.Position.x >= ScreenRig.Width - 2f) _escapeDir.x = -_escapeDir.x;
            if (entity.Position.y <= WindowManager.TaskbarHeight + 1f || entity.Position.y >= ScreenRig.Height - 2f) _escapeDir.y = -_escapeDir.y;

            var outcome = _model.Step(dt, player.Position.ToCore(), playerGrips, entity.Position.ToCore(), grip);
            float strain = _model.Strain;

            Vector2 obj = _model.ObjectPosition.ToUnity();
            Vector2 shake = UnityEngine.Random.insideUnitCircle * (strain * 4f);
            _payload.GhostPosition = obj + new Vector2(-16f, 14f) + shake;

            UpdateBand(player.Position, obj, entity.Position, strain);

            if (_g.Audio != null)
            {
                _g.Audio.SetLoopVolume("tug_strain", 0.25f + strain * 0.75f, 0.05f);
                _g.Audio.SetLoopPitch("tug_strain", 0.85f + strain * 0.9f);
            }
            if (_g.Fx != null)
            {
                _g.Fx.ExtraGrain = strain * 0.4f;
                if (strain > 0.7f) _g.Fx.Shake(0.05f, 1f);
                _glitchCooldown -= dt;
                if (strain > 0.55f && _glitchCooldown <= 0f && UnityEngine.Random.value < 0.08f)
                {
                    _g.Fx.Glitch(0.08f, strain);
                    _g.Audio?.Play("glitch_burst", 0.3f + strain * 0.4f);
                    _glitchCooldown = 0.3f;
                }
            }

            if (outcome != TugOutcome.None) End(outcome, true);
        }

        void UpdateBand(Vector2 a, Vector2 mid, Vector2 b, float strain)
        {
            for (int i = 0; i < _band.Count; i++)
            {
                float t = (i + 1f) / (_band.Count + 1f);
                Vector2 p = t < 0.5f ? Vector2.Lerp(a, mid, t * 2f) : Vector2.Lerp(mid, b, (t - 0.5f) * 2f);
                Vector2 n = new Vector2(-(b - a).y, (b - a).x).normalized;
                p += n * Mathf.Sin(Time.time * 60f + i * 1.7f) * strain * 3f;
                var img = _band[i];
                img.enabled = true;
                img.rectTransform.anchoredPosition = new Vector2(Mathf.Round(p.x), Mathf.Round(p.y));
                img.color = new Color(0.95f, 0.95f, 0.9f, 0.25f + strain * 0.6f);
            }
        }

        void End(TugOutcome outcome, bool transfer)
        {
            var p = _payload;
            _payload = null;
            foreach (var d in _band) d.enabled = false;
            _g.Audio?.StopLoop("tug_strain", 0.08f);
            _g.Audio?.Play("grab_snap", 1f, outcome == TugOutcome.PlayerWins ? 1.15f : 0.9f);
            if (_g.Fx != null)
            {
                _g.Fx.ExtraGrain = 0f;
                _g.Fx.Glitch(0.1f, 0.8f);
            }
            if (p == null) return;

            if (transfer)
            {
                var winner = outcome == TugOutcome.PlayerWins ? _g.Player : _g.EntityAgent;
                _g.DragDrop.TransferTo(p, winner);
                if (winner == _g.Player && !_g.Player.Held)
                {
                    // Player won but already let go: the file just drops back where it came from.
                    _g.DragDrop.Cancel(p);
                }
            }
            if (outcome == TugOutcome.PlayerWins)
            {
                _g.Memory.Record(MemoryKind.ResistedEntity, p.FileId, _g.Now);
                _g.Flags.Increment(Flags.CounterPlayerWins);
            }
            GameLog.Info(LogChannel.Entity, "Tug-of-war ended: " + outcome);
            TugEnded?.Invoke(p, outcome);
        }
    }
}
