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

        /// <summary>Only the flags, counters and choices whose name starts with <paramref name="prefix"/> (e.g. "m." memory).</summary>
        public FlagSnapshot Snapshot(string prefix)
        {
            var all = Snapshot();
            if (string.IsNullOrEmpty(prefix)) return all;
            bool Keep(string k) => k != null && k.StartsWith(prefix, StringComparison.Ordinal);
            var s = new FlagSnapshot();
            var flags = new List<string>();
            foreach (var f in all.flags) if (Keep(f)) flags.Add(f);
            s.flags = flags.ToArray();
            var ck = new List<string>();
            var cv = new List<int>();
            for (int i = 0; i < all.counterKeys.Length; i++)
                if (Keep(all.counterKeys[i])) { ck.Add(all.counterKeys[i]); cv.Add(all.counterValues[i]); }
            s.counterKeys = ck.ToArray();
            s.counterValues = cv.ToArray();
            var hk = new List<string>();
            var hv = new List<string>();
            for (int i = 0; i < all.choiceKeys.Length; i++)
                if (Keep(all.choiceKeys[i])) { hk.Add(all.choiceKeys[i]); hv.Add(all.choiceValues[i]); }
            s.choiceKeys = hk.ToArray();
            s.choiceValues = hv.ToArray();
            return s;
        }

        /// <summary>
        /// Adds a snapshot on top of the current state (flags are added, counters and choices overwritten).
        /// Used to carry saved memory into a new night. Raises no events, like <see cref="Restore"/>.
        /// </summary>
        public void Merge(FlagSnapshot s)
        {
            if (s == null) return;
            if (s.flags != null) foreach (var f in s.flags) if (!string.IsNullOrEmpty(f)) _flags.Add(f);
            if (s.counterKeys != null && s.counterValues != null)
                for (int i = 0; i < Math.Min(s.counterKeys.Length, s.counterValues.Length); i++)
                    if (!string.IsNullOrEmpty(s.counterKeys[i])) _counters[s.counterKeys[i]] = s.counterValues[i];
            if (s.choiceKeys != null && s.choiceValues != null)
                for (int i = 0; i < Math.Min(s.choiceKeys.Length, s.choiceValues.Length); i++)
                    if (!string.IsNullOrEmpty(s.choiceKeys[i])) _choices[s.choiceKeys[i]] = s.choiceValues[i] ?? "";
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
        /// <summary>Tugs-of-war the player lost this night (entity_wins also counts the other defenses).</summary>
        public const string CounterTugLosses = "tug_losses";
        public const string CounterCameraReopens = "camera_reopens";
        public const string CounterWrongOrders = "wrong_orders";

        // Counters only the player's own actions raise (entity tasks count these, not what a cursor did).
        public const string OpenedByPlayerPrefix = "opened_by_player:";
        public const string ViewedByPlayerPrefix = "viewed_by_player:";

        // Night 2 (this night only; memory lives in MemoryFlags)
        public const string N2EllenHelped = "n2.ellen_helped";
        public const string N2GaryArrived = "n2.gary_arrived";
        public const string N2RoundsDone = "n2.rounds_done";
    }

    /// <summary>
    /// Cross-night memory (expansion spec 2.3): flags and counters under the "m." prefix, saved at the end of
    /// each night and merged into the next night's fresh flags.
    /// </summary>
    public static class MemoryFlags
    {
        public const string Prefix = "m.";

        // Night 1
        public const string N1Shredded017 = "m.n1.shredded_017";
        public const string N1Agreed = "m.n1.agreed";
        public const string N1Refused = "m.n1.refused";
        public const string N1Swore = "m.n1.swore";
        public const string N1AskedWho = "m.n1.asked_who";
        public const string N1Read017 = "m.n1.read_017";
        public const string N1ReadNotes = "m.n1.read_notes";
        public const string N1ReopenedCamera = "m.n1.reopened_camera";
        public const string N1TugWins = "m.n1.tug_wins";     // counter
        public const string N1TugLosses = "m.n1.tug_losses"; // counter

        // Night 2
        public const string N2Obeyed = "m.n2.obeyed";        // counter 0-3
        public const string N2DoorLog = "m.n2.door_log";
        public const string N2Lookup163 = "m.n2.lookup_163";
        public const string N2Hid214 = "m.n2.hid_214";
        public const string N2TalkedGary = "m.n2.talked_gary";
        public const string N2Glasses = "m.n2.glasses";
        public const string N2WipedGary = "m.n2.wiped_gary";
        public const string N2FinishedGary = "m.n2.finished_gary";
        public const string N2KeptGary = "m.n2.kept_gary";
        public const string N2ArchivedGary = "m.n2.archived_gary";
        public const string N2WatchedToDoor = "m.n2.watched_to_door";

        // Night 3
        public const string N3RestrictedOpen = "m.n3.restricted_open";
        public const string N3LogoffEnabled = "m.n3.logoff_enabled";
        public const string N3GaryEnabledLogoff = "m.n3.gary_enabled_logoff";
        public const string N3Cam00 = "m.n3.cam00";
        public const string N3SeatCleared = "m.n3.seat_cleared";
        public const string N3MaxStage = "m.n3.max_stage";   // counter
        public const string N3OwnShelfRejected = "m.n3.own_shelf_rejected";
        public const string N3SaidStay = "m.n3.said_stay";

        // Any night
        public const string SaidName = "m.said_name";
    }
}
