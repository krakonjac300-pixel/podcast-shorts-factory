using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Story
{
    /// <summary>
    /// Story state: boolean flags, integer counters and named choices. Everything the story "remembers"
    /// lives here so it can be saved later (Snapshot/Restore) and inspected in the debug overlay.
    /// </summary>
    public sealed class NarrativeFlags
    {
        readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, int> _counters = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _choices = new Dictionary<string, string>(StringComparer.Ordinal);

        public event Action<string> FlagSet;
        public event Action<string, int> CounterChanged;

        public bool Has(string flag) => flag != null && _flags.Contains(flag);

        public void Set(string flag)
        {
            if (string.IsNullOrEmpty(flag) || !_flags.Add(flag)) return;
            GameLog.Info(LogChannel.Story, "Flag " + flag);
            FlagSet?.Invoke(flag);
        }

        public void Clear(string flag) => _flags.Remove(flag);

        public int Get(string counter) => counter != null && _counters.TryGetValue(counter, out var v) ? v : 0;

        public int Increment(string counter, int by = 1)
        {
            int v = Get(counter) + by;
            _counters[counter] = v;
            CounterChanged?.Invoke(counter, v);
            return v;
        }

        public void SetCounter(string counter, int value)
        {
            _counters[counter] = value;
            CounterChanged?.Invoke(counter, value);
        }

        public void SetChoice(string key, string value)
        {
            _choices[key] = value ?? "";
            GameLog.Info(LogChannel.Story, "Choice " + key + " = " + value);
        }

        public string GetChoice(string key) => key != null && _choices.TryGetValue(key, out var v) ? v : null;

        public IEnumerable<string> AllFlags => _flags;
        public IEnumerable<KeyValuePair<string, int>> AllCounters => _counters;

        public FlagSnapshot Snapshot()
        {
            var s = new FlagSnapshot();
            s.flags = new List<string>(_flags).ToArray();
            s.counterKeys = new string[_counters.Count];
            s.counterValues = new int[_counters.Count];
            int i = 0;
            foreach (var kv in _counters) { s.counterKeys[i] = kv.Key; s.counterValues[i] = kv.Value; i++; }
            s.choiceKeys = new string[_choices.Count];
            s.choiceValues = new string[_choices.Count];
            i = 0;
            foreach (var kv in _choices) { s.choiceKeys[i] = kv.Key; s.choiceValues[i] = kv.Value; i++; }
            return s;
        }

        public void Restore(FlagSnapshot s)
        {
            _flags.Clear();
            _counters.Clear();
            _choices.Clear();
            if (s == null) return;
            if (s.flags != null) foreach (var f in s.flags) if (!string.IsNullOrEmpty(f)) _flags.Add(f);
            if (s.counterKeys != null && s.counterValues != null)
                for (int i = 0; i < Math.Min(s.counterKeys.Length, s.counterValues.Length); i++) _counters[s.counterKeys[i]] = s.counterValues[i];
            if (s.choiceKeys != null && s.choiceValues != null)
                for (int i = 0; i < Math.Min(s.choiceKeys.Length, s.choiceValues.Length); i++) _choices[s.choiceKeys[i]] = s.choiceValues[i];
        }
    }

    [Serializable]
    public class FlagSnapshot
    {
        public string[] flags = Array.Empty<string>();
        public string[] counterKeys = Array.Empty<string>();
        public int[] counterValues = Array.Empty<int>();
        public string[] choiceKeys = Array.Empty<string>();
        public string[] choiceValues = Array.Empty<string>();
    }

    /// <summary>Flag and counter names used by code (content may define more).</summary>
    public static class Flags
    {
        public const string LoggedIn = "logged_in";
        public const string TutorialDone = "tutorial_done";
        public const string FirstAnomaly = "first_anomaly";
        public const string EntitySeen = "entity_seen";
        public const string UrgentOrderReceived = "urgent_order_received";
        public const string ConflictStarted = "conflict_started";
        public const string File017ShreddedOnce = "file017_shredded_once";
        public const string File017Returned = "file017_returned";
        public const string EntitySpoke = "entity_spoke";
        public const string CameraDeniedSeen = "camera_denied_seen";
        public const string CameraUnlocked = "camera_unlocked";
        public const string MimicShown = "mimic_shown";
        public const string FigureSeen = "figure_seen";
        public const string Staff017Revealed = "staff017_revealed";
        public const string Ending = "ending_reached";

        // Player's replies in the Notepad conversation (set by DialogueEngine categories)
        public const string PlayerSwore = "player_swore";
        public const string PlayerAskedWho = "player_asked_who";
        public const string PlayerRefused = "player_refused";
        public const string PlayerAgreed = "player_agreed";

        // Counters
        public const string CounterShredAttempts = "shred_attempts";
        public const string CounterEntityWins = "entity_wins";
        public const string CounterPlayerWins = "player_wins";
        public const string CounterCameraReopens = "camera_reopens";
        public const string CounterWrongOrders = "wrong_orders";
    }
}
