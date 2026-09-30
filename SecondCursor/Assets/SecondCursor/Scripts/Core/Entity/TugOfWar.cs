using System;

namespace SecondCursor.Core.Entity
{
    public enum TugOutcome { None, PlayerWins, EntityWins }

    /// <summary>Designer-tunable feel parameters for the file tug-of-war.</summary>
    [Serializable]
    public class TugOfWarSettings
    {
        /// <summary>Share of the object the entity holds when it first grabs (0 = player, 1 = entity).</summary>
        public float startShare = 0.5f;
        public float playerWinShare = 0.12f;
        public float entityWinShare = 0.88f;
        /// <summary>How fast the share moves per second per unit of strength difference.</summary>
        public float shareRate = 0.85f;
        /// <summary>Grip the player has just by holding the button still.</summary>
        public float playerBaseStrength = 0.2f;
        /// <summary>Sustained pull speed (virtual px/s, directed away from the entity) that adds +1 strength.</summary>
        public float pullSpeedForFullStrength = 400f;
        /// <summary>Tiny credit for frantic motion (shaking), per px/s. Kept small so jiggling is not a strategy.</summary>
        public float jiggleCredit = 0.0001f;
        public float maxPlayerStrength = 2.2f;
        /// <summary>Smoothing time constant for the SIGNED pull (seconds). Back-and-forth shaking averages out.</summary>
        public float effortSmoothing = 0.12f;
        /// <summary>If the cursors get this far apart the grip snaps to whoever is ahead.</summary>
        public float maxTension = 280f;
        /// <summary>Distance at which the strain (band, shake, audio) reads as full; 0 = <see cref="maxTension"/>. Story sets it because its snap is off.</summary>
        public float strainTension;
        /// <summary>After this many seconds the entity starts getting stronger (no endless stalemates).</summary>
        public float rampDelay = 3f;
        public float rampPerSecond = 0.14f;
        /// <summary>Seconds of button-up tolerated before the player counts as having let go.</summary>
        public float releaseGrace = 0.06f;
        /// <summary>
        /// Phase I read grace: for this many seconds after the grab the entity's pull cannot move the share toward it, it
        /// cannot snap the file away and its ramp has not started, so a first-time player can read the label. The
        /// player's own pull counts from the first frame. 0 = no grace (every contest but a night's first).
        /// </summary>
        public float readGrace;

        public TugOfWarSettings Clone() => (TugOfWarSettings)MemberwiseClone();
    }

    /// <summary>
    /// Which way the entity drags its end of the file when a tug starts. By default 0.7 away from the player and
    /// 0.3 away from the Disposal bin. The player wins by pulling the opposite way, so when that pull would run into
    /// a screen edge within <see cref="MinPlayerRoom"/> px (a grab next to the bin in the bottom-right corner), the
    /// direction turns by the smallest angle that gives the player's pull room (Phase F: no corner traps).
    /// </summary>
    public static class TugGeometry
    {
        public const float MinPlayerRoom = 200f;
        static readonly float[] Turns = { 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f, 150f, -150f, 180f };

        /// <param name="bottom">Lowest y the cursors can use (the taskbar's top).</param>
        public static Vec2 EscapeDirection(Vec2 player, Vec2 entity, Vec2 bin, float width, float height, float bottom)
        {
            Vec2 away = (entity - player).Normalized;
            Vec2 fromBin = (entity - bin).Normalized;
            Vec2 dir = (away * 0.7f + fromBin * 0.3f).Normalized;
            if (dir.SqrLength < 0.1f) dir = new Vec2(0f, 1f);
            float room = RoomAlong(player, dir * -1f, width, height, bottom);
            if (room >= MinPlayerRoom) return dir;
            Vec2 best = dir;
            float bestRoom = room;
            foreach (float deg in Turns)
            {
                Vec2 d = Rotate(dir, deg);
                float r = RoomAlong(player, d * -1f, width, height, bottom);
                if (r >= MinPlayerRoom) return d;
                if (r > bestRoom) { bestRoom = r; best = d; }
            }
            return best;
        }

        /// <summary>Distance from <paramref name="p"/> along the unit direction <paramref name="d"/> to the edge of the usable screen.</summary>
        public static float RoomAlong(Vec2 p, Vec2 d, float width, float height, float bottom)
        {
            float rx = d.x > 1e-4f ? (width - p.x) / d.x : d.x < -1e-4f ? p.x / -d.x : float.MaxValue;
            float ry = d.y > 1e-4f ? (height - p.y) / d.y : d.y < -1e-4f ? (p.y - bottom) / -d.y : float.MaxValue;
            return Math.Max(0f, Math.Min(rx, ry));
        }

        static Vec2 Rotate(Vec2 v, float degrees)
        {
            double r = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vec2(v.x * c - v.y * s, v.x * s + v.y * c);
        }
    }

    /// <summary>
    /// Pure model of two cursors fighting over one dragged object (the core conflict mechanic).
    /// The player wins by physically yanking the mouse away from the entity; holding still or letting go
    /// loses. Positions are inputs; the model outputs who holds how much, where the object sits and a
    /// 0..1 strain value that drives shake/audio. Fully deterministic and unit-tested.
    /// </summary>
    public sealed class TugOfWar
    {
        /// <summary>
        /// Phase J: the meter reading (<see cref="PlayerLead"/>) from which letting go keeps the file (the release is an ordinary
        /// drop). Below it she takes the file once the button has been up for <see cref="TugOfWarSettings.releaseGrace"/>.
        /// </summary>
        public const float ReleaseKeepLead = 0.6f;

        readonly TugOfWarSettings _s;
        Vec2 _prevPlayer;
        bool _hasPrev;
        float _effort;
        float _releasedFor;

        public float EntityShare { get; private set; }
        public float PlayerStrength { get; private set; }
        public float EntityStrength { get; private set; }
        public float Tension { get; private set; }
        public float Strain { get; private set; }
        public float Elapsed { get; private set; }
        /// <summary>Seconds of the contest after the read grace (all of it when there is none): what the ramp and the assist count.</summary>
        public float ActiveElapsed => Math.Max(0f, Elapsed - _s.readGrace);
        /// <summary>The read grace is still running (the entity's pull is held back).</summary>
        public bool InReadGrace => !IsOver && _s.readGrace > 0f && Elapsed < _s.readGrace;
        public TugOutcome Outcome { get; private set; }
        public Vec2 ObjectPosition { get; private set; }
        public bool IsOver => Outcome != TugOutcome.None;
        /// <summary>The player's smoothed pull right now (1 = pulling at pullSpeedForFullStrength).</summary>
        public float Effort => _effort;
        /// <summary>Highest smoothed pull reached in this contest (the adaptive assist reads it).</summary>
        public float PeakEffort { get; private set; }

        /// <summary>
        /// How close the player is to winning, for the on-screen pull meter: 0 = the entity is about to take the file
        /// (its win share), 1 = the player is about to keep it (the player's win share), 0.5 = even.
        /// </summary>
        public float PlayerLead
        {
            get
            {
                float span = _s.entityWinShare - _s.playerWinShare;
                if (span <= 1e-4f) return 0.5f;
                return 1f - MathUtil.Clamp01((EntityShare - _s.playerWinShare) / span);
            }
        }

        /// <summary>Letting go now would keep the file (the meter is at <see cref="ReleaseKeepLead"/> or more).</summary>
        public bool KeepsOnRelease => !IsOver && PlayerLead >= ReleaseKeepLead;

        /// <summary>
        /// How fast the entity drags its end of the file away during a fight (virtual px/s). Shared by the
        /// runtime conflict and the difficulty tests so both use the same model.
        /// </summary>
        public static float EntityDriftSpeed(float grip) => 55f + 70f * grip;

        public TugOfWar(TugOfWarSettings settings = null)
        {
            _s = settings ?? new TugOfWarSettings();
            Reset();
        }

        public TugOfWarSettings Settings => _s;

        public void Reset()
        {
            EntityShare = _s.startShare;
            Outcome = TugOutcome.None;
            Elapsed = 0f;
            _hasPrev = false;
            _effort = 0f;
            PeakEffort = 0f;
            _releasedFor = 0f;
            Strain = 0f;
        }

        /// <param name="entityPull">The entity's current pulling strength (0..~1.5), set by its AI/personality.</param>
        public TugOutcome Step(float dt, Vec2 playerPos, bool playerHolding, Vec2 entityPos, float entityPull)
        {
            if (IsOver || dt <= 0f) return Outcome;
            Elapsed += dt;

            // --- player effort: SIGNED speed along the axis away from the entity, smoothed, so only a
            // committed yank counts (shaking back and forth averages to ~0).
            Vec2 velocity = _hasPrev ? (playerPos - _prevPlayer) / dt : Vec2.Zero;
            _prevPlayer = playerPos;
            _hasPrev = true;
            Vec2 away = (playerPos - entityPos).Normalized;
            if (away.SqrLength < 0.5f) away = new Vec2(-1f, 0f);
            float signedPull = Vec2.Dot(velocity, away) / _s.pullSpeedForFullStrength;
            _effort += (signedPull - _effort) * MathUtil.Damp(1f / Math.Max(0.01f, _s.effortSmoothing), dt);
            if (_effort > PeakEffort) PeakEffort = _effort;

            PlayerStrength = Math.Min(_s.maxPlayerStrength, _s.playerBaseStrength + Math.Max(0f, _effort) + velocity.Length * _s.jiggleCredit);
            bool grace = _s.readGrace > 0f && Elapsed <= _s.readGrace;
            float ramp = Math.Max(0f, ActiveElapsed - _s.rampDelay) * _s.rampPerSecond;
            EntityStrength = Math.Max(0f, entityPull) + ramp;

            if (!playerHolding)
            {
                // Phase J: letting go while clearly ahead keeps the file (the runtime drops it where the pointer is).
                if (KeepsOnRelease) return Finish(TugOutcome.PlayerWins, playerPos, entityPos);
                _releasedFor += dt;
                PlayerStrength = 0f;
                if (_releasedFor >= _s.releaseGrace) return Finish(TugOutcome.EntityWins, playerPos, entityPos);
            }
            else
            {
                _releasedFor = 0f;
            }

            float shift = (EntityStrength - PlayerStrength) * _s.shareRate * dt;
            // Read grace: her pull waits; only the player's pull can move the share.
            if (grace && shift > 0f) shift = 0f;
            EntityShare = MathUtil.Clamp01(EntityShare + shift);
            Tension = Vec2.Distance(playerPos, entityPos);
            ObjectPosition = Vec2.Lerp(playerPos, entityPos, EntityShare);

            float closeness = 1f - Math.Abs(EntityShare - 0.5f) * 2f; // 1 when evenly matched
            float strainAt = _s.strainTension > 0f ? _s.strainTension : _s.maxTension;
            Strain = MathUtil.Clamp01(Tension / strainAt * 0.65f + closeness * 0.2f + Math.Min(1f, (PlayerStrength + EntityStrength) * 0.25f) * 0.25f);

            if (EntityShare <= _s.playerWinShare) return Finish(TugOutcome.PlayerWins, playerPos, entityPos);
            if (EntityShare >= _s.entityWinShare) return Finish(TugOutcome.EntityWins, playerPos, entityPos);
            if (Tension > _s.maxTension)
            {
                // Read grace: the cursors coming apart cannot hand her the file, only the player can win by it.
                if (EntityShare < 0.5f) return Finish(TugOutcome.PlayerWins, playerPos, entityPos);
                if (!grace) return Finish(TugOutcome.EntityWins, playerPos, entityPos);
            }
            return TugOutcome.None;
        }

        TugOutcome Finish(TugOutcome outcome, Vec2 playerPos, Vec2 entityPos)
        {
            Outcome = outcome;
            EntityShare = outcome == TugOutcome.PlayerWins ? 0f : 1f;
            ObjectPosition = outcome == TugOutcome.PlayerWins ? playerPos : entityPos;
            Strain = 0f;
            GameLog.Info(LogChannel.Entity, "Tug-of-war over after " + Elapsed.ToString("0.00") + "s: " + outcome);
            return outcome;
        }
    }

}
