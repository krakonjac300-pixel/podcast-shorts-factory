using System.Collections.Generic;
using SecondCursor.Core.Entity;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Optional designer asset: tune the second cursor without code. Create via
    /// Assets > Create > SECOND CURSOR > Entity Tuning, name it "EntityTuning" and put it in any Resources
    /// folder; it is picked up at startup. Without it the built-in defaults are used.
    /// </summary>
    [CreateAssetMenu(menuName = "SECOND CURSOR/Entity Tuning", fileName = "EntityTuning")]
    public sealed class EntityTuningAsset : ScriptableObject
    {
        public const string ResourcePath = "EntityTuning";

        [Header("Personality")]
        public EntityPersonality personality = new EntityPersonality();

        [Header("Tug-of-war feel")]
        public TugOfWarSettings tugOfWar = new TugOfWarSettings();

        [Header("Movement profiles (override built-ins by name)")]
        public List<MovementProfileData> movementOverrides = new List<MovementProfileData>
        {
            MovementProfiles.HumanLike,
            MovementProfiles.Hesitant,
            MovementProfiles.Aggressive,
            MovementProfiles.Panicked,
            MovementProfiles.Mechanical,
            MovementProfiles.Lurking,
            MovementProfiles.ImitatingPlayer,
        };

        [Tooltip("Radius (virtual px) within which your cursor physically blocks its clicks.")]
        public float blockRadius = 11f;

        public static EntityTuningAsset LoadOptional() => Resources.Load<EntityTuningAsset>(ResourcePath);

        /// <summary>
        /// Designers' Night 1 values win over the difficulty table (Night 1 Normal only): the tug-of-war
        /// settings, the grip base and the reaction scale (which also scales the race to No, as it always did).
        /// </summary>
        public void ApplyTo(DifficultyProfile profile)
        {
            if (profile == null) return;
            if (tugOfWar != null) profile.Tug = tugOfWar.Clone();
            if (personality == null) return;
            profile.GripBase = personality.grip;
            profile.ReactionScale = personality.reactionScale;
            profile.RaceToNoDelayMin *= personality.reactionScale;
            profile.RaceToNoDelayMax *= personality.reactionScale;
        }
    }
}
