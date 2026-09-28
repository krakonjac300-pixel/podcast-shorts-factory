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
        /// <summary>After this many seconds the entity starts getting stronger (no endless stalemates).</summary>
        public float rampDelay = 3f;
        public float rampPerSecond = 0.14f;
        /// <summary>Seconds of button-up tolerated before the player counts as having let go.</summary>
        public float releaseGrace = 0.06f;
    }

    /// <summary>
    /// Pure model of two cursors fighting over one dragged object (the core conflict mechanic).
    /// The player wins by physically yanking the mouse away from the entity; holding still or letting go
    /// loses. Positions are inputs; the model outputs who holds how much, where the object sits and a
    /// 0..1 strain value that drives shake/audio. Fully deterministic and unit-tested.
    /// </summary>
    public sealed class TugOfWar
    {
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
        public TugOutcome Outcome { get; private set; }
        public Vec2 ObjectPosition { get; private set; }
        public bool IsOver => Outcome != TugOutcome.None;

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

            PlayerStrength = Math.Min(_s.maxPlayerStrength, _s.playerBaseStrength + Math.Max(0f, _effort) + velocity.Length * _s.jiggleCredit);
            float ramp = Math.Max(0f, Elapsed - _s.rampDelay) * _s.rampPerSecond;
            EntityStrength = Math.Max(0f, entityPull) + ramp;

            if (!playerHolding)
            {
                _releasedFor += dt;
                PlayerStrength = 0f;
                if (_releasedFor >= _s.releaseGrace) return Finish(TugOutcome.EntityWins, playerPos, entityPos);
            }
            else
            {
                _releasedFor = 0f;
            }

            EntityShare = MathUtil.Clamp01(EntityShare + (EntityStrength - PlayerStrength) * _s.shareRate * dt);
            Tension = Vec2.Distance(playerPos, entityPos);
            ObjectPosition = Vec2.Lerp(playerPos, entityPos, EntityShare);

            float closeness = 1f - Math.Abs(EntityShare - 0.5f) * 2f; // 1 when evenly matched
            Strain = MathUtil.Clamp01(Tension / _s.maxTension * 0.65f + closeness * 0.2f + Math.Min(1f, (PlayerStrength + EntityStrength) * 0.25f) * 0.25f);

            if (EntityShare <= _s.playerWinShare) return Finish(TugOutcome.PlayerWins, playerPos, entityPos);
            if (EntityShare >= _s.entityWinShare) return Finish(TugOutcome.EntityWins, playerPos, entityPos);
            if (Tension > _s.maxTension) return Finish(EntityShare < 0.5f ? TugOutcome.PlayerWins : TugOutcome.EntityWins, playerPos, entityPos);
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
