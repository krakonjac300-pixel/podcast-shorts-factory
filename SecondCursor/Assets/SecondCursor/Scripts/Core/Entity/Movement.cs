using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Entity
{
    /// <summary>
    /// How the second cursor moves. Movement style IS its personality: the same "move to the Cancel button"
    /// reads as curious, hesitant, predatory or panicked depending on these numbers.
    /// </summary>
    [Serializable]
    public class MovementProfileData
    {
        public string name = "HumanLike";
        /// <summary>Nominal speed in virtual px/s (scales the Fitts'-law duration; literal speed when linear).</summary>
        public float speed = 900f;
        public float minDuration = 0.12f;
        public float maxDuration = 2.4f;
        /// <summary>Max sideways bow of the path as a fraction of the distance.</summary>
        public float curveRandomness = 0.16f;
        /// <summary>Overshoot as a fraction of the distance (0 = lands directly).</summary>
        public float overshoot = 0.05f;
        /// <summary>Small corrective sub-movements before settling on the target.</summary>
        public int microCorrections = 1;
        public float correctionSize = 3f;
        /// <summary>Chance of one hesitation (stop, then resume) in the middle of the path.</summary>
        public float pauseProbability = 0f;
        public float pauseDuration = 0.3f;
        /// <summary>Hand-tremor amplitude in px and frequency in Hz.</summary>
        public float tremor = 0.35f;
        public float tremorFrequency = 9f;
        /// <summary>Delay before the movement starts, seconds.</summary>
        public float reactionDelay = 0.06f;
        /// <summary>Constant-velocity straight lines (a machine, not a hand).</summary>
        public bool linear;
        /// <summary>Random +/- variation of the duration as a fraction.</summary>
        public float durationJitter = 0.12f;

        public MovementProfileData Clone() => (MovementProfileData)MemberwiseClone();
    }

    /// <summary>
    /// Built-in movement presets (brief section 20). Returned as fresh copies so callers may tweak them.
    /// Designers can override any preset by name at runtime (see EntityTuningAsset).
    /// </summary>
    public static class MovementProfiles
    {
        public const string HumanLikeName = "HumanLike";
        public const string HesitantName = "Hesitant";
        public const string AggressiveName = "Aggressive";
        public const string PanickedName = "Panicked";
        public const string MechanicalName = "Mechanical";
        public const string LurkingName = "Lurking";
        public const string ImitatingName = "ImitatingPlayer";
        public const string TiredName = "Tired";

        static readonly Dictionary<string, MovementProfileData> Overrides = new Dictionary<string, MovementProfileData>(StringComparer.Ordinal);

        /// <summary>Replace presets by name (null/empty clears all overrides).</summary>
        public static void SetOverrides(IEnumerable<MovementProfileData> profiles)
        {
            Overrides.Clear();
            if (profiles == null) return;
            foreach (var p in profiles)
                if (p != null && !string.IsNullOrEmpty(p.name)) Overrides[p.name] = p.Clone();
        }

        static MovementProfileData Resolve(string name, MovementProfileData builtIn) =>
            Overrides.TryGetValue(name, out var o) ? o.Clone() : builtIn;

        public static MovementProfileData HumanLike => Resolve(HumanLikeName, new MovementProfileData { name = HumanLikeName });

        public static MovementProfileData Hesitant => Resolve(HesitantName, new MovementProfileData
        {
            name = HesitantName, speed = 420f, maxDuration = 4f, curveRandomness = 0.22f, overshoot = 0f,
            microCorrections = 3, correctionSize = 6f, pauseProbability = 0.7f, pauseDuration = 0.6f,
            tremor = 0.8f, tremorFrequency = 7f, reactionDelay = 0.35f,
        });

        public static MovementProfileData Aggressive => Resolve(AggressiveName, new MovementProfileData
        {
            name = AggressiveName, speed = 2400f, minDuration = 0.07f, curveRandomness = 0.06f, overshoot = 0.08f,
            microCorrections = 0, tremor = 0.2f, reactionDelay = 0f, durationJitter = 0.05f,
        });

        public static MovementProfileData Panicked => Resolve(PanickedName, new MovementProfileData
        {
            name = PanickedName, speed = 1900f, minDuration = 0.08f, curveRandomness = 0.3f, overshoot = 0.15f,
            microCorrections = 2, correctionSize = 10f, pauseProbability = 0.15f, pauseDuration = 0.12f,
            tremor = 2.2f, tremorFrequency = 14f, reactionDelay = 0f,
        });

        public static MovementProfileData Mechanical => Resolve(MechanicalName, new MovementProfileData
        {
            name = MechanicalName, speed = 700f, curveRandomness = 0f, overshoot = 0f, microCorrections = 0,
            tremor = 0f, reactionDelay = 0.25f, linear = true, durationJitter = 0f, maxDuration = 5f,
        });

        public static MovementProfileData Lurking => Resolve(LurkingName, new MovementProfileData
        {
            name = LurkingName, speed = 160f, maxDuration = 8f, curveRandomness = 0.25f, overshoot = 0f,
            microCorrections = 0, tremor = 0.5f, pauseProbability = 0.4f, pauseDuration = 1.2f, reactionDelay = 0.5f,
        });

        public static MovementProfileData ImitatingPlayer => Resolve(ImitatingName, new MovementProfileData
        {
            name = ImitatingName, speed = 1000f, curveRandomness = 0.12f, overshoot = 0.04f, microCorrections = 1,
        });

        /// <summary>Gary, held and incomplete: slow, shaky, stops halfway, corrects three times before he lands.</summary>
        public static MovementProfileData Tired => Resolve(TiredName, new MovementProfileData
        {
            name = TiredName, speed = 300f, maxDuration = 6f, curveRandomness = 0.2f, overshoot = 0f,
            microCorrections = 3, correctionSize = 8f, pauseProbability = 0.5f, pauseDuration = 0.8f,
            tremor = 1.6f, tremorFrequency = 6f, reactionDelay = 0.6f,
        });

        public static MovementProfileData Get(string name)
        {
            switch (name)
            {
                case HesitantName: return Hesitant;
                case AggressiveName: return Aggressive;
                case PanickedName: return Panicked;
                case MechanicalName: return Mechanical;
                case LurkingName: return Lurking;
                case ImitatingName: return ImitatingPlayer;
                case TiredName: return Tired;
                default: return HumanLike;
            }
        }
    }

    /// <summary>
    /// A precomputed cursor trajectory: a chain of quadratic-bezier segments with minimum-jerk timing,
    /// optional hesitation holds, overshoot + correction, micro-corrections and tremor. Always ends exactly
    /// on the target so clicks land where intended.
    /// </summary>
    public sealed class MotionPlan
    {
        internal struct Segment
        {
            public Vec2 A;
            public Vec2 C;
            public Vec2 B;
            public float Start;
            public float Duration;
            public bool Linear;
        }

        readonly List<Segment> _segments;
        readonly float _tremor;
        readonly float _tremorFrequency;
        readonly int _seed;

        public readonly Vec2 Start;
        public readonly Vec2 End;
        public readonly float Duration;

        internal MotionPlan(Vec2 start, Vec2 end, List<Segment> segments, float tremor, float tremorFrequency, int seed)
        {
            Start = start;
            End = end;
            _segments = segments;
            _tremor = tremor;
            _tremorFrequency = tremorFrequency;
            _seed = seed;
            float d = 0f;
            foreach (var s in segments) d = Math.Max(d, s.Start + s.Duration);
            Duration = d;
        }

        public int SegmentCount => _segments.Count;

        public static MotionPlan Hold(Vec2 at, float duration)
        {
            var segs = new List<Segment> { new Segment { A = at, C = at, B = at, Start = 0f, Duration = Math.Max(0f, duration) } };
            return new MotionPlan(at, at, segs, 0f, 0f, 0);
        }

        public float Progress(float t) => Duration <= 0f ? 1f : MathUtil.Clamp01(t / Duration);

        public Vec2 Evaluate(float t)
        {
            if (t >= Duration) return End;
            if (t <= 0f || _segments.Count == 0) return Start;

            Vec2 pos = Start;
            for (int i = 0; i < _segments.Count; i++)
            {
                var s = _segments[i];
                if (t < s.Start) break;
                if (t <= s.Start + s.Duration || i == _segments.Count - 1)
                {
                    float u = s.Duration > 0f ? MathUtil.Clamp01((t - s.Start) / s.Duration) : 1f;
                    float e = s.Linear ? u : MathUtil.MinJerk(u);
                    pos = Vec2.Bezier(s.A, s.C, s.B, e);
                    break;
                }
                pos = s.B;
            }

            if (_tremor > 0f)
            {
                // Tremor fades out over the final 80 ms so the cursor settles precisely.
                float fade = MathUtil.Clamp01((Duration - t) / 0.08f);
                float f = _tremorFrequency;
                pos += new Vec2(MathUtil.Noise1(t * f, _seed), MathUtil.Noise1(t * f, _seed + 17)) * (_tremor * fade);
            }
            return pos;
        }

        /// <summary>
        /// Like Evaluate but pulls the path toward a target that has moved since planning (e.g. the player
        /// dragged the window away mid-flight), blending the correction in along the motion.
        /// </summary>
        public Vec2 EvaluateHoming(float t, Vec2 currentTarget)
        {
            Vec2 drift = currentTarget - End;
            return Evaluate(t) + drift * MathUtil.MinJerk(Progress(t));
        }
    }

    public static class MovementPlanner
    {
        /// <summary>Fitts'-law style duration: longer for far/small targets, scaled by profile speed.</summary>
        public static float FittsDuration(float distance, float targetSize, float speed)
        {
            float w = Math.Max(4f, targetSize);
            double id = Math.Log(distance / w + 1.0, 2.0);
            return (float)((0.08 + 0.11 * id) * (900.0 / Math.Max(1.0, speed)));
        }

        public static MotionPlan Plan(Vec2 from, Vec2 to, MovementProfileData p, Rng rng, float targetSize = 16f)
        {
            p = p ?? MovementProfiles.HumanLike;
            rng = rng ?? new Rng(1);
            var segs = new List<MotionPlan.Segment>();
            float t = Math.Max(0f, p.reactionDelay * (1f + rng.Range(-0.3f, 0.3f)));
            Vec2 d = to - from;
            float dist = d.Length;
            int seed = rng.Range(1, 100000);

            if (dist < 0.5f)
            {
                segs.Add(new MotionPlan.Segment { A = from, C = from, B = to, Start = t, Duration = 0.01f });
                return new MotionPlan(from, to, segs, 0f, 0f, seed);
            }

            float duration = p.linear ? dist / Math.Max(1f, p.speed) : FittsDuration(dist, targetSize, p.speed);
            duration *= 1f + rng.Range(-p.durationJitter, p.durationJitter);
            duration = MathUtil.Clamp(duration, p.minDuration, p.maxDuration);

            Vec2 dir = d / dist;
            Vec2 side = dir.Perpendicular;
            bool overshoot = p.overshoot > 0f && dist > 30f && !p.linear;
            Vec2 primaryEnd = to;
            if (overshoot)
                primaryEnd = to + dir * (dist * p.overshoot * rng.Range(0.5f, 1.2f)) + side * (rng.Gaussian() * dist * p.overshoot * 0.3f);

            Vec2 mid = Vec2.Lerp(from, primaryEnd, 0.5f);
            Vec2 control = p.linear ? mid : mid + side * (rng.Range(-1f, 1f) * p.curveRandomness * dist);
            float primaryDur = overshoot ? duration * 0.82f : duration;

            if (!p.linear && dist > 40f && rng.Chance(p.pauseProbability))
            {
                // Hesitation: split the bezier (de Casteljau) and hold at the split point.
                float f = rng.Range(0.3f, 0.7f);
                Vec2 p01 = Vec2.Lerp(from, control, f);
                Vec2 p12 = Vec2.Lerp(control, primaryEnd, f);
                Vec2 split = Vec2.Lerp(p01, p12, f);
                float d1 = primaryDur * f;
                float hold = p.pauseDuration * rng.Range(0.6f, 1.4f);
                segs.Add(new MotionPlan.Segment { A = from, C = p01, B = split, Start = t, Duration = d1 });
                t += d1;
                segs.Add(new MotionPlan.Segment { A = split, C = split, B = split, Start = t, Duration = hold });
                t += hold;
                segs.Add(new MotionPlan.Segment { A = split, C = p12, B = primaryEnd, Start = t, Duration = primaryDur - d1 });
                t += primaryDur - d1;
            }
            else
            {
                segs.Add(new MotionPlan.Segment { A = from, C = control, B = primaryEnd, Start = t, Duration = primaryDur, Linear = p.linear });
                t += primaryDur;
            }

            Vec2 cur = primaryEnd;
            if (overshoot)
            {
                float back = Vec2.Distance(primaryEnd, to);
                float cd = MathUtil.Clamp(0.1f + back * 0.002f, 0.1f, 0.3f);
                Vec2 c2 = Vec2.Lerp(primaryEnd, to, 0.5f) + side * rng.Range(-0.2f, 0.2f) * back;
                segs.Add(new MotionPlan.Segment { A = cur, C = c2, B = to, Start = t, Duration = cd });
                t += cd;
                cur = to;
            }

            for (int i = 0; i < p.microCorrections; i++)
            {
                float scale = p.correctionSize * (1f - i / (float)(p.microCorrections + 1));
                Vec2 target = to + rng.InsideUnitCircle() * scale;
                float cd = rng.Range(0.06f, 0.12f);
                segs.Add(new MotionPlan.Segment { A = cur, C = Vec2.Lerp(cur, target, 0.5f), B = target, Start = t, Duration = cd });
                t += cd;
                cur = target;
            }

            if (cur != to)
            {
                float cd = p.microCorrections > 0 ? 0.08f : 0.05f;
                segs.Add(new MotionPlan.Segment { A = cur, C = Vec2.Lerp(cur, to, 0.5f), B = to, Start = t, Duration = cd });
            }

            return new MotionPlan(from, to, segs, p.tremor, p.tremorFrequency, seed);
        }
    }
}
