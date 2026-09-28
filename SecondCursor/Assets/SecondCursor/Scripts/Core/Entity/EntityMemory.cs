using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Entity
{
    public enum MemoryKind
    {
        OpenedApp,
        OpenedFile,
        MovedFile,
        ReadEmail,
        DecidedOrder,
        ShredAttempt,
        ShredSucceeded,
        ResistedEntity,
        ObeyedEntity,
        TypedMessage,
        ClosedWindow,
        ReopenedWindow,
        WatchedCamera,
    }

    public readonly struct MemoryEvent
    {
        public readonly float Time;
        public readonly MemoryKind Kind;
        public readonly string Subject;

        public MemoryEvent(float time, MemoryKind kind, string subject)
        {
            Time = time;
            Kind = kind;
            Subject = subject ?? "";
        }

        public override string ToString() => Kind + ":" + Subject;
    }

    /// <summary>
    /// What the entity knows about the player. Lightweight on purpose: counts per (kind, subject), a ring
    /// buffer of recent events, and a trust value. Designed to grow later (mimicry targets, saved relationship).
    /// </summary>
    public sealed class EntityMemory
    {
        const int Capacity = 256;
        readonly MemoryEvent[] _ring = new MemoryEvent[Capacity];
        int _next;
        int _count;
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>-1 = the player fights it at every turn, +1 = the player does what it asks.</summary>
        public float Trust { get; private set; }

        public event Action<MemoryEvent> Recorded;

        public void Record(MemoryKind kind, string subject, float time)
        {
            var e = new MemoryEvent(time, kind, subject);
            _ring[_next] = e;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            Bump(Key(kind, null));
            if (!string.IsNullOrEmpty(subject)) Bump(Key(kind, subject));

            if (kind == MemoryKind.ResistedEntity) Trust = MathUtil.Clamp(Trust - 0.15f, -1f, 1f);
            else if (kind == MemoryKind.ObeyedEntity) Trust = MathUtil.Clamp(Trust + 0.2f, -1f, 1f);
            Recorded?.Invoke(e);
        }

        public int Count(MemoryKind kind, string subject = null)
        {
            return _counts.TryGetValue(Key(kind, subject), out var n) ? n : 0;
        }

        public int Resistance => Count(MemoryKind.ResistedEntity);

        /// <summary>Most frequent subject recorded for a kind (e.g. the app the player opens most).</summary>
        public string MostFrequent(MemoryKind kind)
        {
            string best = null;
            int bestN = 0;
            string prefix = ((int)kind).ToString() + "|";
            foreach (var kv in _counts)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal) || kv.Key.Length == prefix.Length) continue;
                if (kv.Value > bestN)
                {
                    bestN = kv.Value;
                    best = kv.Key.Substring(prefix.Length);
                }
            }
            return best;
        }

        public List<MemoryEvent> Recent(int max)
        {
            var list = new List<MemoryEvent>();
            int n = Math.Min(max, _count);
            int start = (_next - n + Capacity) % Capacity;
            for (int i = 0; i < n; i++) list.Add(_ring[(start + i) % Capacity]);
            return list;
        }

        public MemoryEvent? Last(MemoryKind kind)
        {
            for (int i = 1; i <= _count; i++)
            {
                var e = _ring[(_next - i + Capacity) % Capacity];
                if (e.Kind == kind) return e;
            }
            return null;
        }

        static string Key(MemoryKind kind, string subject) => ((int)kind).ToString() + "|" + (subject ?? "");

        void Bump(string key)
        {
            _counts.TryGetValue(key, out var n);
            _counts[key] = n + 1;
        }
    }
}
