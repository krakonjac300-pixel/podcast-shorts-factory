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
    /// and hands the file to the winner. Each contest gets fresh settings from the night's difficulty and
    /// the adaptive assist, and reports its outcome back to the assist (which may make the next contest a
    /// mercy contest: low grip, and the entity lets go by itself once the player pulls for a while).
    /// </summary>
    public sealed class ConflictSystem : MonoBehaviour
    {
        const int BandDots = 15;

        GameServices _g;
        TugOfWar _model;
        DragPayload _payload;
        Vector2 _escapeDir;
        readonly List<Image> _band = new List<Image>();
        float _glitchCooldown;

        bool _mercy;
        MercyRelease _mercyRelease;
        /// <summary>
        /// Phase I read grace: the next contest starts with a standoff (Difficulty.ReadGraceSeconds) so the label can be read.
        /// Armed for a night's first contest (each root starts armed) and again when Story is chosen mid-night.
        /// </summary>
        bool _readGraceArmed = true;
        bool _graceContest;

        /// <summary>Development builds only: decide the next contests (None = fight for real). Set by the debug panel and the test bridge.</summary>
        public static TugOutcome ForcedOutcome = TugOutcome.None;
        const float ForcedOutcomeAfter = 0.35f;

        /// <summary>Settings of the contest in progress (or the last one).</summary>
        public TugOfWarSettings CurrentSettings { get; private set; } = new TugOfWarSettings();
        public bool IsFighting => _payload != null;
        public bool IsMercyContest => IsFighting && _mercy;
        /// <summary>The explanation panel of the fight (label, meter, arrow).</summary>
        public TugHud Hud { get; private set; }
        /// <summary>The next contest starts with the read grace again (Story was chosen: its fights read differently).</summary>
        public void ArmReadGrace() => _readGraceArmed = true;
        /// <summary>The last contest's outcome was decided by a debug override (development builds): it never counts for records.</summary>
        public bool LastOutcomeForced { get; private set; }
        bool _forcedNow;
        public float Strain => _model != null && IsFighting ? _model.Strain : 0f;
        public float EntityShare => _model != null && IsFighting ? _model.EntityShare : 0f;
        /// <summary>The pull meter's value: 0 = the second cursor is about to take the file, 1 = the player is about to keep it.</summary>
        public float PlayerLead => _model != null ? _model.PlayerLead : 0.5f;
        /// <summary>Phase J: letting go now would keep the file (the meter is past its line): the release is an ordinary drop.</summary>
        public bool PlayerKeepsOnRelease => IsFighting && _model.KeepsOnRelease;
        /// <summary>Phase J: the way the player should drag (away from her, turned toward open screen by <see cref="TugGeometry"/>).</summary>
        public Vector2 PullDirection => -_escapeDir;
        /// <summary>Where the fought-over file sits between the two cursors (virtual px).</summary>
        public Vector2 ObjectPosition => _model != null && IsFighting ? _model.ObjectPosition.ToUnity() : _lastObject;
        /// <summary>The last contest was lost because the player let go of the button (not by being out-pulled).</summary>
        public bool LastLostByRelease { get; private set; }
        /// <summary>Where the player's cursor was when the last contest ended.</summary>
        public Vector2 LastEndPlayerPosition { get; private set; }
        Vector2 _lastObject;
        bool _playerGripsNow = true;

        public event Action<DragPayload> TugStarted;
        public event Action<DragPayload, TugOutcome> TugEnded;

        public static ConflictSystem Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Conflict");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ConflictSystem>();
            c._g = g;
            c._model = new TugOfWar(c.CurrentSettings);
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
            // Phase J: a player clearly ahead keeps the file on letting go, and drop targets light up for it.
            g.Router.ContestRelease = c.OnPlayerRelease;
            g.Router.ContestKeeps = a => a == g.Player && c.PlayerKeepsOnRelease;
            // Phase H: the fight explains itself above the file (label, pull meter, who kept it).
            c.Hud = TugHud.Create(g, c);
            g.DragDrop.PayloadFinished += (p, accepted, by) =>
            {
                if (p != c._payload) return;
                // Whoever managed to drop it somewhere won; a returned (refused) payload goes to the entity.
                bool playerWon = accepted && by != null && by.IsPlayer;
                c.End(playerWon ? TugOutcome.PlayerWins : TugOutcome.EntityWins, false);
            };
            return c;
        }

        void OnContestStarted(DragPayload p, CursorAgent contender)
        {
            if (_payload != null) return;
            bool pair = (p.Holder == _g.Player && contender == _g.EntityAgent) || (p.Holder == _g.EntityAgent && contender == _g.Player);
            if (!pair) return;
            _payload = p;
            _forcedNow = false;
            _mercy = _g.Assist != null && _g.Assist.BeginContest();
            _mercyRelease = _mercy ? new MercyRelease() : null;
            _graceContest = _readGraceArmed;
            _readGraceArmed = false;
            CurrentSettings = _g.Difficulty != null ? _g.Difficulty.TugFor(_g.Assist, _mercy, _graceContest) : new TugOfWarSettings();
            _model = new TugOfWar(CurrentSettings);
            // Away from the player and the bin, turned if the player's pull would have no room (a grab by the bin).
            _escapeDir = TugGeometry.EscapeDirection(_g.Player.Position.ToCore(), _g.EntityAgent.Position.ToCore(),
                _g.Desktop.DisposalIcon.Hit.Center.ToCore(), ScreenRig.Width, ScreenRig.Height, WindowManager.TaskbarHeight).ToUnity();
            if (_escapeDir.sqrMagnitude < 0.1f) _escapeDir = Vector2.up;

            _g.Flags.Set(Flags.ConflictStarted);
            _g.Audio?.Play("grab_snap", 0.7f, 0.8f, Audio.AudioManager.PanFor(_g.EntityAgent.Position.x));
            _g.Audio?.PlayLoop("tug_strain", 0.2f, 0.1f);
            _g.Fx?.Glitch(0.12f, 0.6f);
            GameLog.Info(LogChannel.Entity, "Tug-of-war started over " + p.FileId);
            if (_mercy) GameLog.Info(LogChannel.Entity, "Mercy contest");
            if (_graceContest) GameLog.Info(LogChannel.Entity, "Read grace: " + CurrentSettings.readGrace.ToString("0.0") + " s standoff");
            TugStarted?.Invoke(p);
        }

        public void Tick(float dt)
        {
            if (_payload == null || dt <= 0f) return;
            var player = _g.Player;
            var entity = _g.EntityAgent;
            bool playerGrips = (_payload.Holder == player || _payload.Contender == player) && player.Held;
            bool entityGrips = (_payload.Holder == entity || _payload.Contender == entity) && entity.Held;
            if (!entityGrips)
            {
                // It let go: the file is simply yours.
                End(TugOutcome.PlayerWins, true);
                return;
            }
            float grip = _mercy ? AdaptiveAssist.MercyGrip : _g.Entity != null ? _g.Entity.Brain.Grip : 0.62f;

            // The entity's end drags away (strength-dependent), with a nervous tremble. During the read grace it holds
            // still (only the tremble), so the label can be read and the cursors do not drift apart.
            float driftScale = _model.InReadGrace ? 0f : 1f;
            Vector2 drift = _escapeDir * TugOfWar.EntityDriftSpeed(grip) * dt * driftScale + UnityEngine.Random.insideUnitCircle * (1.5f + _model.Strain * 3f);
            entity.Position = ScreenRig.ClampToScreen(entity.Position + drift);
            // Bounce the escape direction off the screen edges so it doesn't get pinned.
            if (entity.Position.x <= 1f || entity.Position.x >= ScreenRig.Width - 2f) _escapeDir.x = -_escapeDir.x;
            if (entity.Position.y <= WindowManager.TaskbarHeight + 1f || entity.Position.y >= ScreenRig.Height - 2f) _escapeDir.y = -_escapeDir.y;

            var outcome = _model.Step(dt, player.Position.ToCore(), playerGrips, entity.Position.ToCore(), grip);
            if (outcome == TugOutcome.None) outcome = Overrule(dt, playerGrips);
            float strain = _model.Strain;
            _playerGripsNow = playerGrips;

            Vector2 obj = _model.ObjectPosition.ToUnity();
            _lastObject = obj;
            Vector2 shake = UnityEngine.Random.insideUnitCircle * (strain * 4f);
            _payload.GhostPosition = obj + new Vector2(-16f, 14f) + shake;

            UpdateBand(player.Position, obj, entity.Position, strain);
            // Feel: your cursor is dragged a little toward it; its cursor shakes with effort.
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = (entity.Position - player.Position).normalized * (strain * 5f);
            if (_g.EntityView != null) _g.EntityView.Jitter = 0.5f + strain * 2f;

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

        /// <summary>
        /// Phase J: the player lets go during a fight. Clearly ahead, the player keeps the file and the router drops it where
        /// the pointer is (a folder, the desktop, the bin); otherwise the fight goes on and she takes it (<see cref="Tick"/>).
        /// </summary>
        void OnPlayerRelease(CursorAgent a)
        {
            var p = _payload;
            if (p == null || a != _g.Player || (p.Holder != a && p.Contender != a) || !_model.KeepsOnRelease) return;
            GameLog.Info(LogChannel.Entity, "Tug-of-war: let go ahead (meter " + _model.PlayerLead.ToString("0.00") + ")");
            End(TugOutcome.PlayerWins, true, true);
        }

        /// <summary>
        /// Outcomes the model does not decide: in a mercy contest the entity lets go once the player has pulled
        /// for a moment, or has simply held on for a while; in development builds a forced outcome ends the
        /// contest early.
        /// </summary>
        TugOutcome Overrule(float dt, bool playerGrips)
        {
            if (_mercy && _mercyRelease != null && _mercyRelease.Step(dt, playerGrips, _model.Effort))
            {
                GameLog.Info(LogChannel.Entity, "Entity let go (mercy)");
                return TugOutcome.PlayerWins;
            }
            if (ForcedOutcome != TugOutcome.None && Debug.isDebugBuild && _model.Elapsed >= ForcedOutcomeAfter)
            {
                GameLog.Info(LogChannel.Debug, "Tug-of-war outcome forced: " + ForcedOutcome);
                _forcedNow = true;
                return ForcedOutcome;
            }
            return TugOutcome.None;
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
                // The band thickens and heats from pale to red as the fight strains.
                float size = strain > 0.6f ? 3f : 2f;
                img.rectTransform.sizeDelta = new Vector2(size, size);
                img.color = Color.Lerp(new Color(0.95f, 0.95f, 0.9f, 0.45f), new Color(0.95f, 0.22f, 0.2f, 0.95f), strain);
            }
        }

        /// <summary>
        /// Stops a fight with no winner (the game was paused mid-fight): the file flies back to where it was,
        /// and neither side's memory, flags or grip changes.
        /// </summary>
        public void Interrupt()
        {
            var p = _payload;
            if (p == null) return;
            _payload = null;   // so the PayloadFinished handler does not score the cancelled drag
            // Paused inside the standoff, the player never got to read it: the next fight has its standoff.
            if (_graceContest && _model.Elapsed < CurrentSettings.readGrace) _readGraceArmed = true;
            foreach (var d in _band) d.enabled = false;
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = Vector2.zero;
            if (_g.EntityView != null) _g.EntityView.Jitter = 0f;
            _g.Audio?.StopLoop("tug_strain", 0.08f);
            if (_g.Fx != null) _g.Fx.ExtraGrain = 0f;
            _g.DragDrop.Cancel(p);
            GameLog.Info(LogChannel.Entity, "Tug-of-war interrupted: no winner");
        }

        /// <summary>
        /// The time scale a running hit-stop will restore (below 0: no hit-stop). The pause menu saves this instead of
        /// the 0.03 slow-down, so a pause inside the 90 ms freeze never resumes the game at 3% speed.
        /// </summary>
        public float ScaleBeforeHitStop { get; private set; } = -1f;

        System.Collections.IEnumerator HitStop()
        {
            if (Game.PauseMenu.IsPaused || Time.timeScale <= 0.05f) yield break;
            float previous = Time.timeScale;
            ScaleBeforeHitStop = previous;
            Time.timeScale = 0.03f;
            yield return new WaitForSecondsRealtime(0.09f);
            if (!Game.PauseMenu.IsPaused && Mathf.Approximately(Time.timeScale, 0.03f)) Time.timeScale = previous;
            ScaleBeforeHitStop = -1f;
        }

        const float SagSeconds = 0.3f, NodSeconds = 0.22f, NodDepth = 4f;
        /// <summary>Two semitones down (2^(-2/12)).</summary>
        const float TwoSemitonesDown = 0.8909f;

        System.Collections.IEnumerator StrainSag()
        {
            var audio = _g.Audio;
            float from = audio.LoopPitch("tug_strain");
            audio.StopLoop("tug_strain", SagSeconds);
            float t = 0f;
            while (t < SagSeconds)
            {
                // A new fight takes the strain over again.
                if (IsFighting) yield break;
                t += Time.unscaledDeltaTime;
                audio.SetLoopPitch("tug_strain", Mathf.Lerp(from, from * TwoSemitonesDown, t / SagSeconds));
                yield return null;
            }
        }

        static System.Collections.IEnumerator Nod(CursorView view)
        {
            float t = 0f;
            while (t < NodSeconds && view != null)
            {
                t += Time.deltaTime;
                view.VisualOffset = new Vector2(0f, -NodDepth * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / NodSeconds)));
                yield return null;
            }
            if (view != null) view.VisualOffset = Vector2.zero;
        }

        /// <param name="released">The player let go ahead: the file is theirs and the release in progress drops it.</param>
        void End(TugOutcome outcome, bool transfer, bool released = false)
        {
            var p = _payload;
            LastLostByRelease = outcome == TugOutcome.EntityWins && !_playerGripsNow;
            LastEndPlayerPosition = _g.Player.Position;
            _playerGripsNow = true;
            _payload = null;
            foreach (var d in _band) d.enabled = false;
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = Vector2.zero;
            if (_g.EntityView != null) _g.EntityView.Jitter = 0f;
            if (outcome == TugOutcome.EntityWins && _g.Audio != null)
            {
                // M5: a loss sounds like a punchline: the strain sags two semitones as it dies away.
                StartCoroutine(StrainSag());
            }
            else _g.Audio?.StopLoop("tug_strain", 0.08f);
            _g.Audio?.Play("grab_snap", 1f, outcome == TugOutcome.PlayerWins ? 1.3f : 0.9f);
            // M5: the second cursor wins politely: one small nod before it leaves with the file.
            if (outcome == TugOutcome.EntityWins && transfer && _g.EntityView != null) StartCoroutine(Nod(_g.EntityView));
            if (outcome == TugOutcome.PlayerWins && transfer)
            {
                // A win lands as a punch: a sliver of hit-stop and the second cursor thrown back, shuddering.
                StartCoroutine(HitStop());
                if (_g.EntityView != null)
                    _g.EntityView.Flinch((_g.EntityAgent.Position - _g.Player.Position).normalized * 40f, 0.45f);
            }
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
                if (!winner.Held && !released)
                {
                    // The winner isn't holding the button any more: the file just drops back where it came from.
                    _g.DragDrop.Cancel(p);
                }
            }
            if (outcome == TugOutcome.PlayerWins)
            {
                _g.Memory.Record(MemoryKind.ResistedEntity, p.FileId, _g.Now);
                _g.Flags.Increment(Flags.CounterPlayerWins);
            }
            else
            {
                _g.Flags.Increment(Flags.CounterTugLosses);
            }
            LastOutcomeForced = _forcedNow;
            _forcedNow = false;
            if (LastOutcomeForced) _g.Disarm("forced tug outcome");
            GameLog.Info(LogChannel.Entity, "Tug-of-war ended: " + outcome);
            _g.Assist?.ReportTug(outcome == TugOutcome.PlayerWins, _model.ActiveElapsed, _model.PeakEffort);
            _graceContest = false;
            _mercy = false;
            TugEnded?.Invoke(p, outcome);
        }
    }
}
