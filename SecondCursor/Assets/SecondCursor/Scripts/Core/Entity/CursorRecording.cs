using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Entity
{
    [Serializable]
    public struct CursorSample
    {
        public float t;
        public float x;
        public float y;
        public bool down;

        public CursorSample(float t, float x, float y, bool down)
        {
            this.t = t;
            this.x = x;
            this.y = y;
            this.down = down;
        }

        public Vec2 Position => new Vec2(x, y);
    }

    /// <summary>
    /// Continuously records the player's cursor (position + button) into a bounded buffer so the entity can
    /// later replay a distorted imitation of what the player did (the "it's copying me" moment).
    /// </summary>
    public sealed class CursorRecorder
    {
        readonly List<CursorSample> _samples = new List<CursorSample>();
        readonly float _maxSeconds;
        readonly float _interval;
        bool _lastDown;

        public CursorRecorder(float maxSeconds = 240f, float sampleInterval = 1f / 30f)
        {
            _maxSeconds = maxSeconds;
            _interval = sampleInterval;
        }

        public int Count => _samples.Count;

        public void Add(float time, Vec2 position, bool down)
        {
            if (_samples.Count > 0)
            {
                var last = _samples[_samples.Count - 1];
                bool buttonChanged = down != _lastDown;
                if (!buttonChanged && time - last.t < _interval) return;
                if (!buttonChanged && Math.Abs(last.x - position.x) < 0.5f && Math.Abs(last.y - position.y) < 0.5f && time - last.t < 0.5f) return;
            }
            _samples.Add(new CursorSample(time, position.x, position.y, down));
            _lastDown = down;

            // Trim in chunks to keep Add O(1) amortized.
            if (_samples.Count > 64 && time - _samples[0].t > _maxSeconds * 1.25f)
            {
                int cut = 0;
                while (cut < _samples.Count && time - _samples[cut].t > _maxSeconds) cut++;
                _samples.RemoveRange(0, cut);
            }
        }

        /// <summary>Copies the samples in [fromTime, toTime] into a recording that starts at t=0.</summary>
        public CursorRecording Extract(float fromTime, float toTime)
        {
            var list = new List<CursorSample>();
            foreach (var s in _samples)
                if (s.t >= fromTime && s.t <= toTime) list.Add(new CursorSample(s.t - fromTime, s.x, s.y, s.down));
            return new CursorRecording(list);
        }

        public CursorRecording Last(float seconds, float now) => Extract(now - seconds, now);

        /// <summary>The window of the given length with the most cursor travel (the most "interesting" footage).</summary>
        public CursorRecording MostActive(float seconds, float now, float lookback)
        {
            float bestStart = Math.Max(0f, now - seconds);
            float bestTravel = -1f;
            float from = now - lookback;
            for (float start = from; start + seconds <= now; start += seconds * 0.25f)
            {
                float travel = 0f;
                CursorSample? prev = null;
                foreach (var s in _samples)
                {
                    if (s.t < start || s.t > start + seconds) continue;
                    if (prev.HasValue) travel += Vec2.Distance(prev.Value.Position, s.Position) + (s.down != prev.Value.down ? 40f : 0f);
                    prev = s;
                }
                if (travel > bestTravel)
                {
                    bestTravel = travel;
                    bestStart = start;
                }
            }
            return Extract(bestStart, bestStart + seconds);
        }
    }

    public sealed class CursorRecording
    {
        readonly List<CursorSample> _samples;

        public CursorRecording(List<CursorSample> samples)
        {
            _samples = samples ?? new List<CursorSample>();
        }

        public IReadOnlyList<CursorSample> Samples => _samples;
        public bool IsEmpty => _samples.Count < 2;
        public float Duration => _samples.Count == 0 ? 0f : _samples[_samples.Count - 1].t;

        /// <summary>Linearly interpolated position; button state of the latest sample at or before t.</summary>
        public CursorSample Sample(float t)
        {
            if (_samples.Count == 0) return default;
            if (t <= _samples[0].t) return _samples[0];
            var lastS = _samples[_samples.Count - 1];
            if (t >= lastS.t) return lastS;
            int lo = 0, hi = _samples.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_samples[mid].t <= t) lo = mid; else hi = mid;
            }
            var a = _samples[lo];
            var b = _samples[hi];
            float k = b.t - a.t > 1e-5f ? (t - a.t) / (b.t - a.t) : 0f;
            return new CursorSample(t, a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k, a.down);
        }

        /// <summary>A new recording with every position mapped (mirror, offset, distortion...).</summary>
        public CursorRecording Transformed(Func<Vec2, float, Vec2> map)
        {
            var list = new List<CursorSample>(_samples.Count);
            foreach (var s in _samples)
            {
                var p = map(s.Position, s.t);
                list.Add(new CursorSample(s.t, p.x, p.y, s.down));
            }
            return new CursorRecording(list);
        }

        /// <summary>Time-stretched copy (factor &gt; 1 = slower).</summary>
        public CursorRecording Stretched(float factor)
        {
            var list = new List<CursorSample>(_samples.Count);
            foreach (var s in _samples) list.Add(new CursorSample(s.t * factor, s.x, s.y, s.down));
            return new CursorRecording(list);
        }
    }
}
