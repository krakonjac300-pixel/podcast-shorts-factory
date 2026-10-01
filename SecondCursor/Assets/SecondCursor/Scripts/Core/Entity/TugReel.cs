using System;

namespace SecondCursor.Core.Entity
{
    /// <summary>The usable screen for a fight (virtual px, y up): the cursors stay at or above <see cref="Bottom"/> (the taskbar's top).</summary>
    public readonly struct ScreenBounds
    {
        public readonly float Width, Height, Bottom;

        public ScreenBounds(float width, float height, float bottom)
        {
            Width = width;
            Height = height;
            Bottom = bottom;
        }

        public Vec2 Clamp(Vec2 p) => new Vec2(MathUtil.Clamp(p.x, 0f, Width - 1f), MathUtil.Clamp(p.y, Bottom, Height - 1f));
    }

    /// <summary>Phase P: a fight, the hold assist's hold, or the Night 3 finale's LetGo hold.</summary>
    public enum TugVariant { Fight, Hold, LetGo }

    /// <summary>Phase P: the track of a reel fight, from the grabbed file straight to the Disposal bin.</summary>
    public static class HaulGeometry
    {
        /// <summary>The bin's reach from its centre: a file this close is in.</summary>
        public const float BinRadius = 30f;
        /// <summary>Her line keeps this much screen behind it (her pointer rides 40 px behind the file, plus a margin).</summary>
        public const float HerLineMargin = 48f;

        /// <summary>The unit way from the file to the bin (fixed for the whole fight).</summary>
        public static Vec2 Axis(Vec2 file, Vec2 bin)
        {
            Vec2 d = bin - file;
            return d.SqrLength > 1e-6f ? d.Normalized : new Vec2(1f, 0f);
        }

        /// <summary>Distance from the file's centre to the bin's edge.</summary>
        public static float EdgeDistance(Vec2 file, Vec2 bin, float binRadius = BinRadius) => Math.Max(0f, Vec2.Distance(file, bin) - binRadius);

        /// <summary>Distance from <paramref name="p"/> along the unit direction <paramref name="d"/> to the edge of the usable screen.</summary>
        public static float RoomAlong(Vec2 p, Vec2 d, ScreenBounds b)
        {
            float rx = d.x > 1e-4f ? (b.Width - p.x) / d.x : d.x < -1e-4f ? p.x / -d.x : float.MaxValue;
            float ry = d.y > 1e-4f ? (b.Height - p.y) / d.y : d.y < -1e-4f ? (p.y - b.Bottom) / -d.y : float.MaxValue;
            return Math.Max(0f, Math.Min(rx, ry));
        }

        /// <summary>How far behind <paramref name="origin"/> her line sits: <paramref name="want"/>, less where the screen ends, never under <paramref name="min"/>.</summary>
        public static float HerLine(Vec2 origin, Vec2 axis, ScreenBounds b, float want, float min) =>
            MathUtil.Clamp(RoomAlong(origin, -axis, b) - HerLineMargin, min, want);
    }

    /// <summary>
    /// Phase P (review board E1 to E6), "haul it to the bin": every fight runs from the grabbed file to the Disposal bin. Each stroke toward
    /// the bin reels the file along the track (strokes away or across count nothing, so a return stroke is free: hand over hand); her pointer
    /// on the far end drags it back, fading in after GET READY, ramping after a delay, with warned surges. The file at the finish (the bin, or
    /// a tear line when the bin is far) is the player's; dragged back to her line it is hers. Letting go past half way keeps it; below, a
    /// press within the re-grip window goes on fighting. Engine-free and unit-tested; the runtime never moves the player's pointer.
    /// </summary>
    public sealed class TugReel : ITugContest
    {
        /// <summary>The stroke speed that reads as an <see cref="Effort"/> of 1 (the mercy release and the easy-win rule read it).</summary>
        public const float EffortSpeed = 300f;
        /// <summary>Her pointer rides this far behind the file, plus her lead in a surge (eased out and back) and the twitch that warns of it.</summary>
        public const float HerGap = 40f, SurgeLead = 12f, TelegraphTwitch = 3f;
        /// <summary>GET READY: the near-bin slide back, and her pointer easing onto the far end.</summary>
        public const float SlideSeconds = 0.30f, HerEaseSeconds = 0.25f;
        /// <summary>While the button is up in the re-grip window she pulls twice as hard, at least twice this.</summary>
        public const float RegripPullFloor = 40f;

        readonly TugOfWarSettings _s;
        readonly ReelSettings _r;
        readonly Rng _rng;
        Vec2 _prev, _herFrom;
        float _stroke, _peak, _releasedFor, _nextSurge, _surgeFrom = -1f, _held, _reeled, _pulled;

        public TugReel(TugOfWarSettings s)
        {
            _s = s ?? new TugOfWarSettings();
            _r = _s.reel ?? new ReelSettings();
            _rng = new Rng(_r.seed);
            Variant = _r.holdSeconds <= 0f ? TugVariant.Fight : _r.releaseNeverLoses ? TugVariant.LetGo : TugVariant.Hold;
        }

        public TugVariant Variant { get; }
        /// <summary>The unit way from the grab to the bin, fixed for the fight.</summary>
        public Vec2 Axis { get; private set; }
        /// <summary>Where the file was grabbed (its centre), and where the track starts (slid back from the bin when grabbed close to it).</summary>
        public Vec2 GrabOrigin { get; private set; }
        public Vec2 Origin { get; private set; }
        /// <summary>Where her pointer belongs: on the far end of the file, or easing there during GET READY.</summary>
        public Vec2 EntityPosition { get; private set; }
        public Vec2 ObjectPosition { get; private set; }
        /// <summary>The file's place on the track (px from the origin toward the bin).</summary>
        public float S { get; private set; }
        public float Finish { get; private set; }
        public bool FinishIsBin { get; private set; }
        public float HerLine { get; private set; }
        public float Progress => Finish > 0f ? S / Finish : 0f;
        /// <summary>Your smoothed stroke toward the bin (pointer px/s).</summary>
        public float ReelSpeed => _stroke;
        /// <summary>Her pull this frame (px/s of track; negative is the hold's creep toward the bin).</summary>
        public float Pull { get; private set; }
        public bool Surging { get; private set; }
        /// <summary>A surge comes in under <see cref="ReelSettings.telegraphSeconds"/>: her pointer twitches back.</summary>
        public bool Telegraph { get; private set; }
        /// <summary>
        /// The button is up below the keep point: a press within <see cref="RegripLeft"/> goes on fighting (during GET READY the night's
        /// short release grace, after it the re-grip window).
        /// </summary>
        public bool InRegrip => !IsOver && _releasedFor > 0f;
        public float RegripLeft => InRegrip ? Math.Max(0f, (Elapsed <= _s.readySeconds ? _s.releaseGrace : _r.regrip) - _releasedFor) : 0f;
        public int Regrips { get; private set; }
        /// <summary>Your reel and her pull (px/s of track) averaged over the held fight after GET READY (the coach reads them).</summary>
        public float MeanReel => _held > 0f ? _reeled / _held : 0f;
        public float MeanPull => _held > 0f ? _pulled / _held : 0f;
        /// <summary>LetGo when the button was up at the end (the window ran out, or she dragged it to her line meanwhile); null for any other end.</summary>
        public TugLossReason? EndReason { get; private set; }

        public TugOutcome Outcome { get; private set; }
        public bool IsOver => Outcome != TugOutcome.None;
        public float Elapsed { get; private set; }
        public float ActiveElapsed => Math.Max(0f, Elapsed - _s.readySeconds);
        public bool InReady => !IsOver && Elapsed < _s.readySeconds;
        /// <summary>GET READY or her fade-in is still running (a fight paused in it gets its long fade again).</summary>
        public bool InReadGrace => !IsOver && Elapsed < _s.readySeconds + _r.fade;
        /// <summary>The meter: 0.5 at the grab, 1 at the finish, 0 at her line.</summary>
        public float PlayerLead => MathUtil.Clamp01(S >= 0f ? 0.5f + 0.5f * S / Math.Max(1f, Finish) : 0.5f + 0.5f * S / Math.Max(1f, HerLine));
        public float FinalLead { get; private set; } = 0.5f;
        public bool KeepsOnRelease => !IsOver && S >= _r.keepFraction * Finish;
        public float Effort => _stroke / EffortSpeed;
        public float PeakEffort => _peak / EffortSpeed;
        public float Strain { get; private set; }

        /// <summary>
        /// The grab: the track runs from <paramref name="fileCentre"/> to the bin. A grab closer than <see cref="ReelSettings.finishMin"/> to the
        /// bin's edge starts that far back (GET READY slides the file there: her yank); else the finish is the bin, or a tear line at
        /// <see cref="ReelSettings.finishMax"/>.
        /// </summary>
        public void Begin(Vec2 fileCentre, Vec2 herPointer, Vec2 binCentre, ScreenBounds bounds)
        {
            GrabOrigin = ObjectPosition = fileCentre;
            Axis = HaulGeometry.Axis(fileCentre, binCentre);
            float d = HaulGeometry.EdgeDistance(fileCentre, binCentre);
            if (d < _r.finishMin)
            {
                Origin = bounds.Clamp(fileCentre - Axis * (_r.finishMin - d));
                Finish = _r.finishMin;
                FinishIsBin = true;
            }
            else
            {
                Origin = fileCentre;
                Finish = Math.Min(d, _r.finishMax);
                FinishIsBin = d <= _r.finishMax;
            }
            HerLine = HaulGeometry.HerLine(Origin, Axis, bounds, _r.herLine, _r.herLineMin);
            _herFrom = EntityPosition = herPointer;
            _nextSurge = _rng.Range(_r.firstSurgeMin, _r.firstSurgeMax);
        }

        /// <param name="entity">Unused: the reel places her pointer itself (<see cref="EntityPosition"/>).</param>
        /// <param name="grip">Her grip this contest; her pull is <see cref="ReelSettings.herPull"/> times grip / gripBase.</param>
        public TugOutcome Step(float dt, Vec2 player, bool holding, Vec2 entity, float grip)
        {
            if (IsOver || dt <= 0f) return Outcome;
            Elapsed += dt;
            if (Elapsed <= _s.readySeconds)
            {
                // GET READY: nothing is scored; the file slides back if it was grabbed by the bin, her pointer takes the far end.
                _prev = player;
                ObjectPosition = Vec2.Lerp(GrabOrigin, Origin, MathUtil.SmoothStep(Elapsed / SlideSeconds));
                EntityPosition = Vec2.Lerp(_herFrom, ObjectPosition - Axis * HerGap, MathUtil.SmoothStep(Elapsed / HerEaseSeconds));
                Strain = Math.Max(_r.strainFloor, 0.25f);
                if (holding)
                {
                    _releasedFor = 0f;
                    return TugOutcome.None;
                }
                if (Variant == TugVariant.LetGo) return End(TugOutcome.Released);
                _releasedFor += dt;
                return _releasedFor >= _s.releaseGrace ? End(TugOutcome.EntityWins, TugLossReason.LetGo) : TugOutcome.None;
            }

            float since = Elapsed - _s.readySeconds;
            float forward = Math.Max(0f, Vec2.Dot(player - _prev, Axis)) / dt;
            _prev = player;
            _stroke += (forward - _stroke) * MathUtil.Damp(1f / Math.Max(0.01f, _r.reelSmoothing), dt);
            if (_stroke > _peak) _peak = _stroke;
            float reel = Math.Min(_stroke, _r.reelCap) * _r.reelGain;
            float pull = HerPull(since, grip);
            if (!holding)
            {
                if (KeepsOnRelease) return End(TugOutcome.PlayerWins);
                if (Variant == TugVariant.LetGo) return End(TugOutcome.Released);
                _releasedFor += dt;
                if (_releasedFor >= _r.regrip - 1e-5f) return End(TugOutcome.EntityWins, TugLossReason.LetGo);
                reel = 0f;
                pull = 2f * Math.Max(pull, RegripPullFloor);
            }
            else
            {
                if (_releasedFor > 0f) Regrips++;
                _releasedFor = 0f;
                _held += dt;
                _reeled += reel * dt;
                _pulled += pull * dt;
            }
            Pull = pull;
            S += (reel - pull) * dt;
            ObjectPosition = Origin + Axis * S;
            float lead = Surging ? SurgeLead * (float)Math.Sin(Math.PI * MathUtil.Clamp01((since - _surgeFrom) / _r.surgeSeconds)) : Telegraph ? TelegraphTwitch : 0f;
            EntityPosition = ObjectPosition - Axis * (HerGap + lead);
            Strain = Math.Max(_r.strainFloor, MathUtil.Clamp01(0.25f + 0.35f * pull / 200f + 0.5f * Math.Abs(PlayerLead - 0.5f) + 0.15f * _stroke / EffortSpeed));
            if (S >= Finish) return End(TugOutcome.PlayerWins);
            // Dragged back to her line while the button was up is still letting go.
            if (S <= -HerLine) return End(TugOutcome.EntityWins, holding ? (TugLossReason?)null : TugLossReason.LetGo);
            return TugOutcome.None;
        }

        /// <summary>
        /// Her pull <paramref name="since"/> seconds after GET READY: in a fight, her grip-scaled pull faded in, the ramp after its delay and the
        /// seeded surges (faded in too); in a hold, the creep that brings the file to the finish in <see cref="ReelSettings.holdSeconds"/>.
        /// </summary>
        float HerPull(float since, float grip)
        {
            Surging = Telegraph = false;
            if (Variant != TugVariant.Fight) return -Finish / _r.holdSeconds;
            float k = _r.fade > 0f ? Math.Min(1f, since / _r.fade) : 1f;
            float steady = _r.herPull > 0f ? _r.herPull * grip / Math.Max(0.01f, _r.gripBase) : _r.herPull;
            float pull = steady * k + Math.Max(0f, since - _r.fade - _s.rampDelay) * _r.reelRampPerSecond;
            if (_r.surge <= 0f) return pull;
            if (since >= _nextSurge)
            {
                _surgeFrom = _nextSurge;
                _nextSurge += _rng.Range(_r.surgeEveryMin, _r.surgeEveryMax);
            }
            Surging = _surgeFrom >= 0f && since < _surgeFrom + _r.surgeSeconds;
            Telegraph = !Surging && since >= _nextSurge - _r.telegraphSeconds;
            return Surging ? pull + _r.surge * k : pull;
        }

        TugOutcome End(TugOutcome outcome, TugLossReason? reason = null)
        {
            FinalLead = PlayerLead;
            Outcome = outcome;
            EndReason = reason;
            Strain = 0f;
            GameLog.Info(LogChannel.Entity, "Tug-of-war over after " + Elapsed.ToString("0.00") + "s: " + outcome + " (reel " + Variant + ", "
                + S.ToString("0") + "/" + Finish.ToString("0") + (FinishIsBin ? " bin" : " tear") + ", regrips " + Regrips + ")");
            return outcome;
        }
    }
}
