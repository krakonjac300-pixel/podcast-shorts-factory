using System;
using System.Collections.Generic;

namespace SecondCursor.Core
{
    public enum LogChannel { System, Player, Entity, Story, Task, OS, Audio, Debug }

    public enum LogLevel { Info, Warning, Error }

    public readonly struct LogEntry
    {
        public readonly float Time;
        public readonly LogChannel Channel;
        public readonly LogLevel Level;
        public readonly string Message;

        public LogEntry(float time, LogChannel channel, LogLevel level, string message)
        {
            Time = time;
            Channel = channel;
            Level = level;
            Message = message;
        }

        public string Tag => "[" + Channel.ToString().ToUpperInvariant() + "]";
        public override string ToString() => Tag + " " + Message;
    }

    /// <summary>
    /// Game-wide action log ("[PLAYER] Opened Files", "[ENTITY] State Observing -> Defensive").
    /// Runtime wires <see cref="Output"/> to Debug.Log and <see cref="Clock"/> to Time.unscaledTime.
    /// Keeps a small ring buffer for the in-game debug overlay. Deliberately low-volume: log decisions and
    /// state changes, never per-frame data.
    /// </summary>
    public static class GameLog
    {
        const int Capacity = 80;
        static readonly LogEntry[] Ring = new LogEntry[Capacity];
        static int _next;
        static int _count;

        public static Action<LogEntry> Output;
        public static Func<float> Clock = () => 0f;

        public static void Info(LogChannel channel, string message) => Write(channel, LogLevel.Info, message);
        public static void Warn(LogChannel channel, string message) => Write(channel, LogLevel.Warning, message);
        public static void Error(LogChannel channel, string message) => Write(channel, LogLevel.Error, message);

        public static void Write(LogChannel channel, LogLevel level, string message)
        {
            var entry = new LogEntry(Clock != null ? Clock() : 0f, channel, level, message ?? string.Empty);
            lock (Ring)
            {
                Ring[_next] = entry;
                _next = (_next + 1) % Capacity;
                if (_count < Capacity) _count++;
            }
            Output?.Invoke(entry);
        }

        /// <summary>Most recent entries, oldest first.</summary>
        public static List<LogEntry> Recent(int max)
        {
            var list = new List<LogEntry>();
            lock (Ring)
            {
                int n = Math.Min(max, _count);
                int start = (_next - n + Capacity) % Capacity;
                for (int i = 0; i < n; i++) list.Add(Ring[(start + i) % Capacity]);
            }
            return list;
        }

        public static void ClearHistory()
        {
            lock (Ring)
            {
                _next = 0;
                _count = 0;
            }
        }
    }
}
