using System;
using System.Collections.Generic;
using System.Text;
using SecondCursor.Core.Story;

// Engine-free save data (progress.json) so progression rules can be unit-tested. The runtime SaveSystem
// reads and writes it with Unity's JsonUtility: [Serializable], public fields, arrays, no dictionaries.
namespace SecondCursor.Core.Game
{
    /// <summary>Where Continue resumes: a checkpoint beat and the story state at its start.</summary>
    [Serializable]
    public class Checkpoint
    {
        public bool valid;
        public int night;
        public string beat = "";
        public int clockMinutes;
        public float trust;
        public int assistLevel;
        public FlagSnapshot flags = new FlagSnapshot();
    }

    /// <summary>What a finished night hands to the save (see <see cref="SaveData.RecordNightComplete"/>).</summary>
    public sealed class NightResult
    {
        public int Night = 1;
        public string EndingId = "";
        /// <summary>The night's "m." flags and counters (including the memory the night started with).</summary>
        public FlagSnapshot Memory = new FlagSnapshot();
        public float Trust;
        public int AssistLevel;
        public int TugWins;
        public int TugLosses;
        public float Seconds;
        /// <summary>What the player typed to the second cursor, in order (Night 1 keeps the first three).</summary>
        public IList<string> PlayerLines;
    }

    /// <summary>
    /// Progress that follows the player between machines (the only file Steam Cloud should sync).
    /// Version history: 1 = one file with settings, 2 = settings split out, 3 = three-night progression.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 3;
        public const int Nights = 3;
        public const int MaxPlayerLines = 3;
        public const int MaxPlayerLineLength = 40;

        public int version = CurrentVersion;

        // progression
        /// <summary>"normal" or "story".</summary>
        public string difficulty = "normal";
        /// <summary>Highest night the player may start: 1..3; 4 = the game was finished once.</summary>
        public int nightUnlocked = 1;
        /// <summary>The night Continue starts.</summary>
        public int currentNight = 1;
        public Checkpoint checkpoint = new Checkpoint();
        /// <summary>Memory at the first start of each night (Night Select replays from here).</summary>
        public FlagSnapshot[] nightStartMemory = { new FlagSnapshot(), new FlagSnapshot(), new FlagSnapshot() };
        /// <summary>How often each night was started (0 = never; tells an empty start memory from an unset one).</summary>
        public int[] nightStarts = new int[Nights];
        /// <summary>All "m." flags and counters so far.</summary>
        public FlagSnapshot memory = new FlagSnapshot();
        /// <summary>Night 1 Notepad replies, sanitized.</summary>
        public string[] playerLines = Array.Empty<string>();
        /// <summary>Entity trust at the end of the last completed night.</summary>
        public float entityTrust;
        /// <summary>Final adaptive assist level of the last completed night.</summary>
        public int assistCarry;

        // records
        public string[] endingsSeen = Array.Empty<string>();
        /// <summary>Unlocked achievement ids (mirrored to Steam when it is available).</summary>
        public string[] achievements = Array.Empty<string>();
        public string[] secrets = Array.Empty<string>();
        public int tugWinsTotal;
        public int tugLossesTotal;
        public float[] nightSeconds = new float[Nights];

        // legacy fields, kept so older files migrate
        public int shiftsCompleted;
        public FlagSnapshot lastShiftFlags = new FlagSnapshot();
        public float masterVolume = -1f;
        public bool crtEffects = true;

        public bool IsStory => string.Equals(difficulty, "story", StringComparison.OrdinalIgnoreCase);

        /// <summary>Night 1 flags that older saves kept in lastShiftFlags, and their memory names.</summary>
        static readonly string[,] LegacyMemory =
        {
            { Flags.File017ShreddedOnce, MemoryFlags.N1Shredded017 },
            { Flags.PlayerAgreed, MemoryFlags.N1Agreed },
            { Flags.PlayerRefused, MemoryFlags.N1Refused },
            { Flags.PlayerSwore, MemoryFlags.N1Swore },
            { Flags.PlayerAskedWho, MemoryFlags.N1AskedWho },
        };

        /// <summary>
        /// Brings a loaded file up to <see cref="CurrentVersion"/> and repairs missing arrays. Returns true if
        /// anything changed. Safe to call on every load.
        /// </summary>
        public bool Migrate()
        {
            bool changed = EnsureShape();
            if (version >= CurrentVersion) return changed;
            if (shiftsCompleted >= 1)
            {
                nightUnlocked = Math.Max(nightUnlocked, 2);
                currentNight = Math.Max(currentNight, 2);
                var old = new NarrativeFlags();
                old.Restore(lastShiftFlags);
                var mem = new NarrativeFlags();
                mem.Restore(memory);
                for (int i = 0; i < LegacyMemory.GetLength(0); i++)
                    if (old.Has(LegacyMemory[i, 0])) mem.Set(LegacyMemory[i, 1]);
                if (old.Get(Flags.CounterPlayerWins) > 0) mem.SetCounter(MemoryFlags.N1TugWins, old.Get(Flags.CounterPlayerWins));
                memory = mem.Snapshot();
                entityTrust = MathUtil.Clamp(entityTrust, -1f, 1f);
            }
            for (int i = 0; i < endingsSeen.Length; i++)
                if (endingsSeen[i] == "night1_blackout") endingsSeen[i] = "n1_blackout";
            version = CurrentVersion;
            return true;
        }

        bool EnsureShape()
        {
            bool changed = false;
            if (checkpoint == null) { checkpoint = new Checkpoint(); changed = true; }
            if (checkpoint.flags == null) { checkpoint.flags = new FlagSnapshot(); changed = true; }
            checkpoint.beat = checkpoint.beat ?? "";
            if (memory == null) { memory = new FlagSnapshot(); changed = true; }
            if (lastShiftFlags == null) lastShiftFlags = new FlagSnapshot();
            if (nightStartMemory == null || nightStartMemory.Length != Nights)
            {
                var fixedArray = new FlagSnapshot[Nights];
                for (int i = 0; i < Nights; i++)
                    fixedArray[i] = nightStartMemory != null && i < nightStartMemory.Length && nightStartMemory[i] != null ? nightStartMemory[i] : new FlagSnapshot();
                nightStartMemory = fixedArray;
                changed = true;
            }
            for (int i = 0; i < Nights; i++) if (nightStartMemory[i] == null) { nightStartMemory[i] = new FlagSnapshot(); changed = true; }
            if (nightStarts == null || nightStarts.Length != Nights) { nightStarts = Resize(nightStarts); changed = true; }
            if (nightSeconds == null || nightSeconds.Length != Nights) { nightSeconds = Resize(nightSeconds); changed = true; }
            playerLines = playerLines ?? Array.Empty<string>();
            endingsSeen = endingsSeen ?? Array.Empty<string>();
            achievements = achievements ?? Array.Empty<string>();
            secrets = secrets ?? Array.Empty<string>();
            difficulty = string.IsNullOrEmpty(difficulty) ? "normal" : difficulty;
            nightUnlocked = Math.Max(1, Math.Min(Nights + 1, nightUnlocked));
            currentNight = Math.Max(1, Math.Min(Nights, currentNight));
            return changed;
        }

        static int[] Resize(int[] a)
        {
            var r = new int[Nights];
            if (a != null) Array.Copy(a, r, Math.Min(a.Length, Nights));
            return r;
        }

        static float[] Resize(float[] a)
        {
            var r = new float[Nights];
            if (a != null) Array.Copy(a, r, Math.Min(a.Length, Nights));
            return r;
        }

        static int Index(int night) => Math.Max(1, Math.Min(Nights, night)) - 1;

        /// <summary>A night starts: remember the memory it started from (first start only) and make it Continue's night.</summary>
        public void RecordNightStart(int night, FlagSnapshot memoryAtStart)
        {
            int i = Index(night);
            if (nightStarts[i] == 0) nightStartMemory[i] = memoryAtStart ?? new FlagSnapshot();
            nightStarts[i]++;
            currentNight = i + 1;
        }

        public void SetCheckpoint(Checkpoint cp)
        {
            checkpoint = cp ?? new Checkpoint();
            if (checkpoint.valid) currentNight = Index(checkpoint.night) + 1;
        }

        /// <summary>The saved checkpoint if it belongs to <paramref name="night"/>, else null.</summary>
        public Checkpoint CheckpointFor(int night)
        {
            return checkpoint != null && checkpoint.valid && checkpoint.night == night && !string.IsNullOrEmpty(checkpoint.beat) ? checkpoint : null;
        }

        public void RecordNightComplete(NightResult r)
        {
            if (r == null) return;
            int i = Index(r.Night);
            if (!string.IsNullOrEmpty(r.EndingId) && Array.IndexOf(endingsSeen, r.EndingId) < 0)
            {
                var list = new List<string>(endingsSeen) { r.EndingId };
                endingsSeen = list.ToArray();
            }
            // The night's flags started from the memory it was given, so its "m." snapshot is the whole path
            // so far: it replaces the saved memory (a replayed night never mixes two runs' choices).
            memory = r.Memory ?? new FlagSnapshot();
            entityTrust = MathUtil.Clamp(r.Trust, -1f, 1f);
            assistCarry = r.AssistLevel;
            nightUnlocked = Math.Max(nightUnlocked, Math.Min(r.Night + 1, Nights + 1));
            currentNight = Math.Min(i + 2, Nights);
            checkpoint = new Checkpoint();
            tugWinsTotal += Math.Max(0, r.TugWins);
            tugLossesTotal += Math.Max(0, r.TugLosses);
            nightSeconds[i] += Math.Max(0f, r.Seconds);
            if (r.Night == 1 && r.PlayerLines != null)
            {
                var lines = new List<string>();
                foreach (var l in r.PlayerLines)
                {
                    if (lines.Count >= MaxPlayerLines) break;
                    string clean = SanitizePlayerLine(l);
                    if (clean.Length > 0) lines.Add(clean);
                }
                playerLines = lines.ToArray();
            }
        }

        /// <summary>New Game: progression starts over; settings, endings seen, achievements, secrets and totals stay.</summary>
        public void NewGame()
        {
            nightUnlocked = 1;
            currentNight = 1;
            checkpoint = new Checkpoint();
            nightStartMemory = new[] { new FlagSnapshot(), new FlagSnapshot(), new FlagSnapshot() };
            nightStarts = new int[Nights];
            memory = new FlagSnapshot();
            playerLines = Array.Empty<string>();
            entityTrust = 0f;
            assistCarry = 0;
        }

        /// <summary>
        /// A player's Notepad line made safe to show again in content: printable ASCII only, no braces
        /// (template tokens), runs of spaces collapsed, trimmed, at most 40 characters.
        /// </summary>
        public static string SanitizePlayerLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            bool lastSpace = true;
            foreach (char c in s)
            {
                if (c < 32 || c > 126 || c == '{' || c == '}') continue;
                if (c == ' ')
                {
                    if (lastSpace) continue;
                    lastSpace = true;
                }
                else
                {
                    lastSpace = false;
                }
                sb.Append(c);
            }
            string r = sb.ToString().Trim();
            if (r.Length > MaxPlayerLineLength) r = r.Substring(0, MaxPlayerLineLength).TrimEnd();
            return r;
        }

        /// <summary>
        /// The memory a night starts from: everything saved so far except what that night and later nights
        /// wrote ("m.n2." and on for Night 2). Replaying a finished night never starts with its own choices.
        /// </summary>
        public FlagSnapshot MemoryForNight(int night)
        {
            var src = memory ?? new FlagSnapshot();
            bool Keep(string key)
            {
                if (string.IsNullOrEmpty(key)) return false;
                for (int n = Math.Max(1, night); n <= Nights; n++)
                    if (key.StartsWith(MemoryFlags.Prefix + "n" + n + ".", StringComparison.Ordinal)) return false;
                return true;
            }
            var s = new FlagSnapshot();
            var flags = new List<string>();
            foreach (var f in src.flags ?? Array.Empty<string>()) if (Keep(f)) flags.Add(f);
            s.flags = flags.ToArray();
            var ck = new List<string>();
            var cv = new List<int>();
            var keys = src.counterKeys ?? Array.Empty<string>();
            var values = src.counterValues ?? Array.Empty<int>();
            for (int i = 0; i < Math.Min(keys.Length, values.Length); i++)
                if (Keep(keys[i])) { ck.Add(keys[i]); cv.Add(values[i]); }
            s.counterKeys = ck.ToArray();
            s.counterValues = cv.ToArray();
            var hk = new List<string>();
            var hv = new List<string>();
            var choiceKeys = src.choiceKeys ?? Array.Empty<string>();
            var choiceValues = src.choiceValues ?? Array.Empty<string>();
            for (int i = 0; i < Math.Min(choiceKeys.Length, choiceValues.Length); i++)
                if (Keep(choiceKeys[i])) { hk.Add(choiceKeys[i]); hv.Add(choiceValues[i]); }
            s.choiceKeys = hk.ToArray();
            s.choiceValues = hv.ToArray();
            return s;
        }

        /// <summary>Trust decays toward neutral each night so the new night's choices weigh most (2.3).</summary>
        public static float TrustAtNightStart(int night, float savedTrust)
        {
            float k = night >= 3 ? 0.75f : night == 2 ? 0.5f : 0f;
            return MathUtil.Clamp(savedTrust * k, -1f, 1f);
        }
    }
}
