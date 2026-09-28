using System;

namespace SecondCursor.Core.Entity
{
    /// <summary>Emotional/behavioural state of the entity (brief section 12).</summary>
    public enum EntityState
    {
        Dormant,
        Observing,
        Curious,
        Helpful,
        Interfering,
        Defensive,
        Aggressive,
        Communicating,
        Panicked,
    }

    /// <summary>Story escalation phase (brief section 11).</summary>
    public enum EntityPhase
    {
        Invisible = 0,
        Ambiguous = 1,
        Presence = 2,
        Interference = 3,
        Communication = 4,
        Escalation = 5,
        Reveal = 6,
    }

    /// <summary>
    /// Slow-changing traits that bias the entity's choices and movement. Values 0..1. Kept deliberately
    /// small; future nights can author different personalities as data.
    /// </summary>
    [Serializable]
    public class EntityPersonality
    {
        public string name = "Default";
        /// <summary>How readily it acts in the open (vs. subtle nudges).</summary>
        public float boldness = 0.35f;
        /// <summary>How long it waits before intervening.</summary>
        public float patience = 0.6f;
        /// <summary>How strongly it protects its protected files.</summary>
        public float protectiveness = 0.9f;
        /// <summary>Baseline pulling strength in a tug-of-war.</summary>
        public float grip = 0.62f;
        /// <summary>Reaction time multiplier (lower = faster).</summary>
        public float reactionScale = 1f;

        public EntityPersonality Clone() => (EntityPersonality)MemberwiseClone();
    }

    /// <summary>Utility helpers for choosing an action: score = priority * weight, respecting cooldowns.</summary>
    public static class UtilityMath
    {
        /// <summary>Soft response curve: 0 at x=0, ~1 at x=1, never exceeds 1.</summary>
        public static float Saturate(float x, float steepness = 3f) => (float)(1.0 - Math.Exp(-Math.Max(0f, x) * steepness));

        /// <summary>1 near the point, falling to 0 at the radius.</summary>
        public static float Proximity(Vec2 a, Vec2 b, float radius)
        {
            if (radius <= 0f) return 0f;
            return MathUtil.Clamp01(1f - Vec2.Distance(a, b) / radius);
        }
    }
}
