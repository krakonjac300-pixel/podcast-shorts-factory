using System;

namespace SecondCursor.Core.Entity
{
    /// <summary>
    /// Phase P: the reel's values for one contest ("haul it to the bin", <see cref="TugReel"/>). Distances are px along the track from
    /// the grab toward the Disposal bin, speeds px/s. Defaults are Night 1 at its base grip; <see cref="DifficultyProfile.TugFor"/>
    /// resolves the fade, the assist and mercy per contest. The ramp's delay is the speed model's <see cref="TugOfWarSettings.rampDelay"/>.
    /// </summary>
    [Serializable]
    public class ReelSettings
    {
        /// <summary>Her steady pull at <see cref="gripBase"/> (scaled by grip / gripBase); negative (Story) creeps the file to the bin unscaled.</summary>
        public float herPull = 25f;
        public float gripBase = 0.62f;
        /// <summary>Extra pull during a surge, faded in like her pull.</summary>
        public float surge = 60f;
        public float surgeSeconds = 0.25f;
        public float surgeEveryMin = 0.9f, surgeEveryMax = 1.4f;
        public float firstSurgeMin = 0.6f, firstSurgeMax = 1.0f;
        /// <summary>Her pointer twitches back this long before each surge.</summary>
        public float telegraphSeconds = 0.15f;
        /// <summary>Her pull grows by this much per second once the ramp's delay after the fade has passed.</summary>
        public float reelRampPerSecond = 15f;
        /// <summary>The finish: the bin's edge, or a tear line this far along when the bin is further.</summary>
        public float finishMax = 140f;
        /// <summary>A grab closer to the bin is slid back this far from it during GET READY.</summary>
        public float finishMin = 110f;
        /// <summary>Her line behind the grab (less where the screen ends, never under <see cref="herLineMin"/>): the file there is hers.</summary>
        public float herLine = 90f;
        public float herLineMin = 50f;
        /// <summary>The smoothed stroke toward the bin counts up to this, times <see cref="reelGain"/> px of track per px.</summary>
        public float reelCap = 300f;
        public float reelGain = 1.5f;
        public float reelSmoothing = 0.10f;
        /// <summary>Her pull and surges fade in over <see cref="fade"/> after GET READY: the night's first fight, or any later one.</summary>
        public float fadeFirst = 1.2f, fadeLater = 0.5f;
        public float fade = 0.5f;
        /// <summary>After letting go below the keep point, a press within this long goes on fighting.</summary>
        public float regrip = 0.5f;
        /// <summary>Letting go with the file this far to the finish keeps it.</summary>
        public float keepFraction = 0.5f;
        /// <summary>0 = a fight; else the file creeps to the finish in this long while held (hold assist, the finale's LetGo).</summary>
        public float holdSeconds;
        /// <summary>The finale's LetGo: letting go below the keep point ends the hold with nobody winning (<see cref="TugOutcome.Released"/>).</summary>
        public bool releaseNeverLoses;
        public float strainFloor;
        /// <summary>The surge schedule's seed (the runtime picks one per fight).</summary>
        public int seed = 1;

        public ReelSettings Clone() => (ReelSettings)MemberwiseClone();

        /// <summary>Value equality, field by field (a contest set up like another compares equal, as the speed model's values do).</summary>
        public override bool Equals(object obj)
        {
            if (!(obj is ReelSettings other)) return false;
            foreach (var f in typeof(ReelSettings).GetFields())
                if (!Equals(f.GetValue(this), f.GetValue(other))) return false;
            return true;
        }

        public override int GetHashCode() => herPull.GetHashCode() ^ finishMax.GetHashCode() ^ seed;
    }
}
