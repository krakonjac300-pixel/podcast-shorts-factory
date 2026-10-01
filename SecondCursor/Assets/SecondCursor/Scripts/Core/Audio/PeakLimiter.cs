using System;

namespace SecondCursor.Core.Audio
{
    /// <summary>
    /// The master safety limiter's math (Phase M): the mix never leaves above the bank's peak ceiling (-1 dBFS). Instant attack,
    /// 120 ms release, stereo-linked. It should not engage in normal play; the counters let tests prove it.
    /// </summary>
    public sealed class PeakLimiter
    {
        public const float Ceiling = 0.89f;
        readonly float _release;
        float _gain = 1f;

        /// <summary>Samples (frames) where the gain had to drop, and the loudest input seen.</summary>
        public int EngagedSamples { get; private set; }
        public float MaxInput { get; private set; }

        public PeakLimiter(int sampleRate, float releaseSeconds = 0.12f) => _release = 1f - MathF.Exp(-1f / (releaseSeconds * sampleRate));

        /// <summary>Limits interleaved samples in place.</summary>
        public void Process(float[] data, int channels)
        {
            for (int i = 0; i + channels <= data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) peak = Math.Max(peak, Math.Abs(data[i + c]));
                // Review M8: a broken sample (NaN, infinity or absurdly loud) is silenced instead of passed on or poisoning the gain.
                if (!(peak <= 16f))
                {
                    for (int c = 0; c < channels; c++) data[i + c] = 0f;
                    continue;
                }
                if (peak > MaxInput) MaxInput = peak;
                // Release first, then clamp: the gain applied to this frame never lets it pass the ceiling.
                float g = _gain + (1f - _gain) * _release;
                if (peak * g > Ceiling)
                {
                    g = Ceiling / peak;
                    EngagedSamples++;
                }
                _gain = g;
                for (int c = 0; c < channels; c++) data[i + c] *= g;
            }
        }

        public void ResetCounters()
        {
            EngagedSamples = 0;
            MaxInput = 0f;
        }
    }
}
