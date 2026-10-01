using System;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.FX;
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
    /// Runs the tug-of-war when both cursors grip the same dragged file: feeds real cursor motion into the engine-free model, places
    /// the entity's end and the straining file ghost, draws the "rubber band", drives the strain sound, shake and glitches, and hands
    /// the file to the winner. Each contest gets fresh settings from the night's difficulty and the adaptive assist, and reports its
    /// outcome back to the assist (which may make the next contest a mercy contest: low grip, and the entity lets go by itself once
    /// the player pulls for a while). Phase P: the model is chosen per contest (<see cref="TugOfWarSettings.model"/>): the Phase N
    /// speed model (ConflictSystem.Speed.cs) or the reel, "haul it to the bin" (ConflictSystem.Reel.cs).
    /// </summary>
    public sealed partial class ConflictSystem : MonoBehaviour
    {
        const int BandDots = 15;

        GameServices _g;
        ITugContest _model;
        DragPayload _payload;
        readonly List<Image> _band = new List<Image>();
        float _glitchCooldown;
        /// <summary>Strain glitches are at least this far apart (0.4 s: the flash budget's 0.34 s gap would refuse a closer one).</summary>
        const float GlitchCooldownSeconds = 0.4f;

        bool _mercy;
        MercyRelease _mercyRelease;
        /// <summary>
        /// Phase I read grace: the next contest starts with a standoff (Difficulty.ReadGraceSeconds) so the label can be read.
        /// Armed for a night's first contest (each root starts armed) and again when Story is chosen mid-night.
        /// Phase P, the reel: the same contests get her long fade-in.
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
        public float EntityShare => IsFighting && _model is TugOfWar speed ? speed.EntityShare : 0f;
        /// <summary>The pull meter's value: 0 = the second cursor is about to take the file, 1 = the player is about to keep it.</summary>
        public float PlayerLead => _model != null ? _model.PlayerLead : 0.5f;
        /// <summary>Phase J: letting go now would keep the file (the meter is past its line): the release is an ordinary drop.</summary>
        public bool PlayerKeepsOnRelease => IsFighting && _model.KeepsOnRelease;
        /// <summary>Phase P: the reel of the contest in progress or the last one (null when it was the speed model).</summary>
        public TugReel Reel => _model as TugReel;
        public bool IsReel => _model is TugReel;
        /// <summary>Phase P: the meter reading past which letting go keeps the file (the reel's half way is 0.75 on the meter).</summary>
        public float KeepLead => IsReel ? 0.5f + 0.5f * CurrentSettings.reel.keepFraction : TugOfWar.ReleaseKeepLead;
        /// <summary>Phase P: the player let go below the keep point and can still grab it again (the router swallows that press).</summary>
        public bool InRegrip => IsFighting && IsReel && Reel.InRegrip;
        /// <summary>
        /// Phase J: the way the player should drag. Speed: away from her, turned toward open screen by <see cref="TugGeometry"/>. Phase P,
        /// the reel: toward the Disposal bin.
        /// </summary>
        public Vector2 PullDirection => IsReel ? Reel.Axis.ToUnity() : -_escapeDir;
        /// <summary>Where the fought-over file sits (virtual px).</summary>
        public Vector2 ObjectPosition => _model != null && IsFighting ? _model.ObjectPosition.ToUnity() : _lastObject;
        /// <summary>The last contest was lost because the player let go of the button (not by being out-pulled).</summary>
        public bool LastLostByRelease { get; private set; }
        /// <summary>Phase K: what the player's pointer did in the last lost contest (the result text says what to change).</summary>
        public TugLossReason LastLossReason { get; private set; }
        /// <summary>Phase K: where the player's pointer went in the last contest ("LEFT"), and where the arrow pointed ("DOWN").</summary>
        public string LastPlayerDirection { get; private set; } = "";
        public string LastArrowDirection { get; private set; } = "";
        /// <summary>Phase K: the meter just before the last contest was decided (the result keeps it on screen).</summary>
        public float LastFinalLead { get; private set; } = 0.5f;
        /// <summary>Phase K: where the file was when the last contest started (a file she wins is set down near here).</summary>
        public Vector2 LastGrabPoint { get; private set; }
        /// <summary>Phase P: the last contest the player won ended with the file in the bin (the reel's finish was the bin).</summary>
        public bool LastWonIntoBin { get; private set; }
        /// <summary>Phase P: the last contest the player won ended by letting go past the keep line (the file dropped where the pointer was).</summary>
        public bool LastKeptOnRelease { get; private set; }
        /// <summary>Phase K: the way the arrow pointed, for the arrow's own name while the fight runs ("DOWN-LEFT").</summary>
        public string ArrowDirection => TugCoach.DirectionName(PullDirection.ToCore());
        /// <summary>Phase K: seconds into the current contest (the big arrow at the pointer shows at its start).</summary>
        public float Elapsed => _model != null && IsFighting ? _model.Elapsed : 0f;
        TugCoach _coach;
        /// <summary>
        /// Phase N (fifth blind playtest, finding 6): every contest opens with a GET READY beat this long, before anything is scored (the
        /// label says it and the big arrow shows the way); the hitch or the night's read grace follows it.
        /// </summary>
        public const float ReadySeconds = 0.4f;
        /// <summary>The GET READY beat is still running.</summary>
        public bool InReady => IsFighting && _model.InReady;
        /// <summary>Where the player's cursor was when the last contest ended.</summary>
        public Vector2 LastEndPlayerPosition { get; private set; }
        Vector2 _lastObject;
        bool _playerGripsNow = true;

        public event Action<DragPayload> TugStarted;
        public event Action<DragPayload, TugOutcome> TugEnded;
        /// <summary>Phase P, the reel: her pointer twitches back (a surge comes in 0.15 s), and the surge itself.</summary>
        public event Action SurgeWarned, Surged;

        /// <summary>Phase P (T1): changes a contest's settings as it starts (the Night 3 finale's LetGo hold over 017). Null = as the night says.</summary>
        public Func<DragPayload, TugOfWarSettings, TugOfWarSettings> Customize;
        /// <summary>Phase P (T6): the cap on contests over one file in a beat is off (the Night 3 finale, where T1 decides instead).</summary>
        public bool ContestCapOff;
        /// <summary>Phase P (T6): contests over each file since the beat began (<see cref="ResetBeat"/>).</summary>
        readonly Dictionary<string, int> _contestsThisBeat = new Dictionary<string, int>();
        /// <summary>Phase P: the contest in progress (or the last one) is the hold assist's (its wins never lower the assist level).</summary>
        public bool IsAssisted { get; private set; }
        /// <summary>Phase P: how the last contest ended (<see cref="TugOutcome.Released"/> for the finale's hold let go early).</summary>
        public TugOutcome LastOutcome { get; private set; }
        /// <summary>Phase P (R1 item 4): the rest of the screen dims while a fight runs.</summary>
        public FocusDim Dim { get; private set; }
        HaulView _haul;

        /// <summary>Phase P (T6): a new story beat: the contests counted per file start again.</summary>
        public void ResetBeat() => _contestsThisBeat.Clear();

        public static ConflictSystem Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Conflict");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<ConflictSystem>();
            c._g = g;
            c._model = new TugOfWar(c.CurrentSettings);
            // Phase P: the focus dim sits under everything on the Effects layer; the rope and track under the panel and arrows.
            c.Dim = FocusDim.Create(g.Layers.Effects);
            c._haul = HaulView.Create(g);
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
            // Phase P: a press inside the re-grip window grabs the file again; nothing under the pointer receives it.
            g.Router.ContestRegrip = a => a == g.Player && c.InRegrip;
            // Phase H: the fight explains itself above the file (label, pull meter, who kept it).
            c.Hud = TugHud.Create(g, c);
            // Phase P (R1 item 5): no new notice appears during a fight; the ones up draw faint.
            g.Notifications.Hold = () => c.IsFighting;
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
            // Phase P (T6): the fourth contest over one file in a beat is a mercy contest (Nights 2 and 3 before the finale).
            int before = p.FileId != null && _contestsThisBeat.TryGetValue(p.FileId, out int n) ? n : 0;
            if (p.FileId != null) _contestsThisBeat[p.FileId] = before + 1;
            if (!_mercy && !ContestCapOff && _g.Difficulty != null && _g.Difficulty.MercyByCap(before))
            {
                _mercy = true;
                _mercyRelease = new MercyRelease();
                GameLog.Info(LogChannel.Entity, "Contest cap: contest " + (before + 1) + " over " + p.FileId + " this beat is a mercy contest");
            }
            CurrentSettings = _g.Difficulty != null ? _g.Difficulty.TugFor(_g.Assist, _mercy, _graceContest) : new TugOfWarSettings();
            CurrentSettings.readySeconds = ReadySeconds;
            // Phase P (A2): Tug assist: Hold, read at every contest so Options can switch it mid-night.
            IsAssisted = Game.AccessSettings.TugAssistHold && !_mercy;
            if (IsAssisted) DifficultyTable.HoldAssist(CurrentSettings);
            if (Customize != null) CurrentSettings = Customize(p, CurrentSettings) ?? CurrentSettings;
            if (CurrentSettings.reel.holdSeconds > 0f && CurrentSettings.reel.releaseNeverLoses) IsAssisted = false;
            LastGrabPoint = p.GhostPosition + new Vector2(16f, -14f);
            if (CurrentSettings.model == TugModel.Reel) BeginReel(p);
            else BeginSpeed(p);

            _g.Flags.Set(Flags.ConflictStarted);
            _g.Audio?.Play("grab_snap", 0.7f, 0.8f, Audio.AudioManager.PanFor(_g.EntityAgent.Position.x));
            if (IsAssisted) GameLog.Info(LogChannel.Entity, "Tug assist: Hold");
            _g.Audio?.PlayLoop("tug_strain", 0.2f, 0.1f);
            _g.Fx?.Glitch(0.12f, 0.6f);
            GameLog.Info(LogChannel.Entity, "Tug-of-war started over " + p.FileId);
            if (_mercy) GameLog.Info(LogChannel.Entity, "Mercy contest");
            if (_graceContest) GameLog.Info(LogChannel.Entity, IsReel ? "Her long fade-in: " + CurrentSettings.reel.fade.ToString("0.0") + " s"
                : "Read grace: " + CurrentSettings.readGrace.ToString("0.0") + " s standoff");
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
            var outcome = IsReel ? StepReel(dt, playerGrips, grip) : StepSpeed(dt, playerGrips, grip);
            if (outcome == TugOutcome.None) outcome = Overrule(dt, playerGrips);
            _playerGripsNow = playerGrips;
            Feel(dt, _model.Strain);
            if (outcome != TugOutcome.None) End(outcome, true);
        }

        /// <summary>The strain heard and seen: the strain loop, the grain, a shake at high strain and now and then a strain glitch.</summary>
        void Feel(float dt, float strain)
        {
            if (_g.Audio != null)
            {
                _g.Audio.SetLoopVolume("tug_strain", 0.25f + strain * 0.75f, 0.05f);
                _g.Audio.SetLoopPitch("tug_strain", 0.85f + strain * 0.9f);
            }
            if (_g.Fx == null) return;
            _g.Fx.ExtraGrain = strain * 0.4f;
            if (strain > 0.7f) _g.Fx.Shake(0.05f, 1f);
            _glitchCooldown -= dt;
            if (strain > 0.55f && _glitchCooldown <= 0f && UnityEngine.Random.value < MathUtil.ChanceAt60(0.08f, dt))
            {
                _g.Fx.Glitch(0.08f, strain);
                _g.Audio?.Play("glitch_burst", 0.3f + strain * 0.4f);
                _glitchCooldown = GlitchCooldownSeconds;
            }
        }

        /// <summary>
        /// Phase J: the player lets go during a fight. Clearly ahead, the player keeps the file and the router drops it where the pointer
        /// is (a folder, the desktop, the bin); otherwise the fight goes on and she takes it (<see cref="Tick"/>). Phase P: letting go of
        /// the finale's LetGo hold below the keep point ends it with nobody winning, and the router drops the file the same way.
        /// </summary>
        void OnPlayerRelease(CursorAgent a)
        {
            var p = _payload;
            if (p == null || a != _g.Player || (p.Holder != a && p.Contender != a)) return;
            if (_model.KeepsOnRelease)
            {
                GameLog.Info(LogChannel.Entity, "Tug-of-war: let go ahead (meter " + _model.PlayerLead.ToString("0.00") + ")");
                End(TugOutcome.PlayerWins, true, true);
            }
            else if (IsReel && Reel.Variant == TugVariant.LetGo)
            {
                End(TugOutcome.Released, true, true);
            }
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
            LastOutcome = TugOutcome.None;
            // Paused inside the standoff, the player never got to read it: the next fight has its standoff.
            if (_graceContest && _model.InReadGrace) _readGraceArmed = true;
            _haul.Hide();
            Dim.Hide();
            ClearFeel();
            _g.Audio?.StopLoop("tug_strain", 0.08f);
            _g.DragDrop.Cancel(p);
            GameLog.Info(LogChannel.Entity, "Tug-of-war interrupted: no winner");
        }

        /// <summary>Phase P (R1): the dim fades once the result has been seen (the result's notice posts 0.6 s after it).</summary>
        System.Collections.IEnumerator DimAfterResult()
        {
            float until = Time.unscaledTime + ResultDimSeconds;
            while (Time.unscaledTime < until)
            {
                if (IsFighting) yield break;
                yield return null;
            }
            if (!IsFighting) Dim.Hide();
        }

        /// <summary>The dim holds this long after a result, then fades out over 0.2 s.</summary>
        public const float ResultDimSeconds = 0.4f;

        static System.Collections.IEnumerator FlickerFor(CursorView view, float amount, float seconds)
        {
            float before = view.Flicker;
            view.Flicker = Mathf.Max(before, amount);
            yield return new WaitForSecondsRealtime(seconds);
            if (view != null) view.Flicker = before;
        }

        void ClearFeel()
        {
            foreach (var d in _band) d.enabled = false;
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = Vector2.zero;
            if (_g.EntityView != null) _g.EntityView.Jitter = 0f;
            if (_g.Fx != null) _g.Fx.ExtraGrain = 0f;
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

        /// <param name="released">The player let go: the file is theirs and the release in progress drops it.</param>
        void End(TugOutcome outcome, bool transfer, bool released = false)
        {
            var p = _payload;
            var reel = Reel;
            LastLostByRelease = outcome == TugOutcome.EntityWins && (reel != null && reel.IsOver ? reel.EndReason == TugLossReason.LetGo : !_playerGripsNow);
            LastEndPlayerPosition = _g.Player.Position;
            LastFinalLead = _model.IsOver ? _model.FinalLead : _model.PlayerLead;
            LastWonIntoBin = outcome == TugOutcome.PlayerWins && transfer && !released && reel != null && reel.IsOver && reel.FinishIsBin && reel.S >= reel.Finish;
            LastKeptOnRelease = outcome == TugOutcome.PlayerWins && released;
            if (_coach != null)
            {
                LastLossReason = reel != null ? _coach.ClassifyReel(LastLostByRelease, reel.MeanReel, reel.MeanPull)
                    : _coach.Classify(LastLostByRelease, CurrentSettings.pullSpeedForFullStrength);
                LastArrowDirection = ArrowDirection;
                LastPlayerDirection = TugCoach.DirectionName(_coach.Net);
                if (outcome == TugOutcome.EntityWins)
                    GameLog.Info(LogChannel.Entity, "Tug lost: " + LastLossReason + " (pulled " + LastPlayerDirection + " " + _coach.Along.ToString("0") + " px along of "
                        + _coach.Path.ToString("0") + " px in " + _coach.HeldSeconds.ToString("0.00") + " s; " + (reel != null ? "bin " : "arrow ") + LastArrowDirection + ")");
            }
            _playerGripsNow = true;
            _payload = null;
            LastOutcome = outcome;
            // Phase P, the reel: a lost fight's rope whips out of the player's hand; any other end takes the rope away at once.
            if (reel != null && outcome == TugOutcome.EntityWins) _haul.Whip(LastEndPlayerPosition, reel.ObjectPosition.ToUnity(), reel.Strain);
            else _haul.Hide();
            ClearFeel();
            StartCoroutine(DimAfterResult());
            if (outcome == TugOutcome.EntityWins && _g.Audio != null)
            {
                // M5: a loss sounds like a punchline: the strain sags two semitones as it dies away.
                StartCoroutine(StrainSag());
            }
            else _g.Audio?.StopLoop("tug_strain", 0.08f);
            if (outcome == TugOutcome.Released)
            {
                // Phase P: the finale's LetGo hold let go early. Nobody won and nothing was taken: the release in progress drops the file.
                if (p != null)
                {
                    _g.DragDrop.TransferTo(p, _g.Player);
                    if (!released && !_g.Player.Held) _g.DragDrop.Cancel(p);
                }
                GameLog.Info(LogChannel.Entity, "Tug-of-war ended: Released (the hold was let go early)");
                _graceContest = false;
                _mercy = false;
                TugEnded?.Invoke(p, outcome);
                return;
            }
            if (reel != null) EndBeatsReel(p, outcome, transfer, released);
            _g.Audio?.Play("grab_snap", 1f, outcome == TugOutcome.PlayerWins ? 1.3f : 0.9f);
            // M5: the second cursor wins politely: one small nod before it leaves with the file.
            if (outcome == TugOutcome.EntityWins && transfer && _g.EntityView != null) StartCoroutine(Nod(_g.EntityView));
            if (outcome == TugOutcome.PlayerWins && transfer)
            {
                // A win lands as a punch: a sliver of hit-stop and the second cursor thrown back (Phase P, the reel: away from the bin), shuddering.
                StartCoroutine(HitStop());
                Vector2 back = reel != null ? -PullDirection : (_g.EntityAgent.Position - _g.Player.Position).normalized;
                if (_g.EntityView != null) _g.EntityView.Flinch(back * 40f, 0.45f);
                if (reel != null && _g.EntityView != null) StartCoroutine(FlickerFor(_g.EntityView, 0.5f, 0.3f));
            }
            if (_g.Fx != null) _g.Fx.Glitch(0.1f, 0.8f);
            if (p == null) return;

            if (transfer)
            {
                var winner = outcome == TugOutcome.PlayerWins ? _g.Player : _g.EntityAgent;
                Vector2 ghostWas = p.GhostPosition;
                _g.DragDrop.TransferTo(p, winner);
                if (!winner.Held && !released)
                {
                    // The winner isn't holding the button any more: the file just drops back where it came from.
                    _g.DragDrop.Cancel(p);
                }
                else if (reel != null && !released && !LastWonIntoBin)
                {
                    // Phase P: the file was out on the track: it flies to the winner's hand (a torn-loose win, or hers).
                    _g.DragDrop.SnapGhost(p, ghostWas, TearSnapSeconds);
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
            _g.Assist?.ReportTug(outcome == TugOutcome.PlayerWins, _model.ActiveElapsed, _model.PeakEffort, CurrentSettings.model, IsAssisted);
            _graceContest = false;
            _mercy = false;
            TugEnded?.Invoke(p, outcome);
            if (LastWonIntoBin && _g.Player.Payload == p)
            {
                // Phase P: hauled all the way: once the fight is recorded, the file drops into the bin as the player's own drop (Confirm Shred follows).
                GameLog.Info(LogChannel.Entity, "Tug-of-war: hauled into the bin");
                StartCoroutine(JoltBin());
                _g.DragDrop.DropInto(p, _g.Player, _g.Desktop.DisposalIcon.Hit);
            }
        }
    }
}
