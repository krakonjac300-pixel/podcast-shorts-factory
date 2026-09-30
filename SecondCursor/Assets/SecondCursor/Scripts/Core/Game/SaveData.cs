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
        /// <summary>Seconds of the night played before this checkpoint (best times and totals carry on from here).</summary>
        public float elapsed;
        /// <summary>
        /// Saved in a run that counts for records and achievements. Field initializers survive JsonUtility, so a
        /// checkpoint saved before this field existed loads as armed.
        /// </summary>
        public bool armed = true;
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
        public float Seconds;
        /// <summary>
        /// The run counts for records (endings seen, play time, best time). Progression is saved either way: a debug
        /// run may unlock nights, never endings or achievements.
        /// </summary>
        public bool Records = true;
        /// <summary>What the player typed to the second cursor, in order (Night 1 keeps the first three).</summary>
        public IList<string> PlayerLines;
        /// <summary>The game clock (minutes since midnight) when each line was typed; same order, may be shorter.</summary>
        public IList<int> PlayerLineMinutes;
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
        /// <summary>Shorter "nights" (a test jump straight to an ending) never become a best time.</summary>
        public const float MinRecordedSeconds = 1f;

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
        /// <summary>Trust at the first start of each night (Night Select replays from here).</summary>
        public float[] nightStartTrust = new float[Nights];
        /// <summary>The last night finished (0 = none; 3 = the game was just finished, so Continue has nothing to resume).</summary>
        public int lastCompletedNight;
        /// <summary>All "m." flags and counters so far.</summary>
        public FlagSnapshot memory = new FlagSnapshot();
        /// <summary>Night 1 Notepad replies, sanitized.</summary>
        public string[] playerLines = Array.Empty<string>();
        /// <summary>The Night 1 clock (minutes since midnight) when each saved line was typed (-1: unknown).</summary>
        public int[] playerLineMinutes = Array.Empty<int>();
        /// <summary>Entity trust at the end of the last completed night.</summary>
        public float entityTrust;
        /// <summary>Final adaptive assist level of the last completed night.</summary>
        public int assistCarry;

        // records
        public string[] endingsSeen = Array.Empty<string>();
        /// <summary>Unlocked achievement ids (mirrored to Steam when it is available).</summary>
        public string[] achievements = Array.Empty<string>();
        public string[] secrets = Array.Empty<string>();
        /// <summary>Phase L: the one-time tips this save has shown (New Game keeps them: the player has been taught).</summary>
        public string[] tipsShown = Array.Empty<string>();
        public int tugWinsTotal;
        public int tugLossesTotal;
        /// <summary>Total play time per night (all armed runs added up).</summary>
        public float[] nightSeconds = new float[Nights];
        /// <summary>Fastest armed completion per night in seconds (0 = none yet).</summary>
        public float[] bestNightSeconds = new float[Nights];

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
            if (bestNightSeconds == null || bestNightSeconds.Length != Nights) { bestNightSeconds = Resize(bestNightSeconds); changed = true; }
            if (nightStartTrust == null || nightStartTrust.Length != Nights) { nightStartTrust = Resize(nightStartTrust); changed = true; }
            playerLines = playerLines ?? Array.Empty<string>();
            playerLineMinutes = playerLineMinutes ?? Array.Empty<int>();
            endingsSeen = endingsSeen ?? Array.Empty<string>();
            achievements = achievements ?? Array.Empty<string>();
            secrets = secrets ?? Array.Empty<string>();
            tipsShown = tipsShown ?? Array.Empty<string>();
            difficulty = string.IsNullOrEmpty(difficulty) ? "normal" : difficulty;
            nightUnlocked = Math.Max(1, Math.Min(Nights + 1, nightUnlocked));
            currentNight = Math.Max(1, Math.Min(Nights, currentNight));
            lastCompletedNight = Math.Max(0, Math.Min(Nights, lastCompletedNight));
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

        /// <summary>
        /// A night starts: remember the memory and trust it started from (first start only) and make it Continue's
        /// night.
        /// </summary>
        public void RecordNightStart(int night, FlagSnapshot memoryAtStart, float trustAtStart = 0f)
        {
            int i = Index(night);
            if (nightStarts[i] == 0)
            {
                nightStartMemory[i] = memoryAtStart ?? new FlagSnapshot();
                nightStartTrust[i] = MathUtil.Clamp(trustAtStart, -1f, 1f);
            }
            nightStarts[i]++;
            currentNight = i + 1;
        }

        /// <summary>A night was started by a run that does not count (a debug start): only Continue's night follows it.</summary>
        public void NoteNightStarted(int night) => currentNight = Index(night) + 1;

        /// <summary>A real tug-of-war ended (never a forced one): the lifetime totals count it at once.</summary>
        public void RecordTug(bool playerWon)
        {
            if (playerWon) tugWinsTotal++;
            else tugLossesTotal++;
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
            if (r.Records && !string.IsNullOrEmpty(r.EndingId) && Array.IndexOf(endingsSeen, r.EndingId) < 0)
            {
                var list = new List<string>(endingsSeen) { r.EndingId };
                endingsSeen = list.ToArray();
            }
            // The keys of this night and later nights ("m.n2." and on for Night 2) come from this run only (a
            // replayed night never mixes two runs' choices, and later nights built on the old path are stale);
            // earlier nights' keys and keys of no night are kept.
            memory = MergeNightMemory(memory, r.Memory, r.Night);
            entityTrust = MathUtil.Clamp(r.Trust, -1f, 1f);
            assistCarry = r.AssistLevel;
            nightUnlocked = Math.Max(nightUnlocked, Math.Min(r.Night + 1, Nights + 1));
            currentNight = Math.Min(i + 2, Nights);
            checkpoint = new Checkpoint();
            lastCompletedNight = i + 1;
            if (r.Records && r.Seconds >= MinRecordedSeconds)
            {
                nightSeconds[i] += r.Seconds;
                if (bestNightSeconds[i] <= 0f || r.Seconds < bestNightSeconds[i]) bestNightSeconds[i] = r.Seconds;
            }
            if (r.Night == 1 && r.PlayerLines != null)
            {
                var lines = new List<string>();
                var minutes = new List<int>();
                for (int k = 0; k < r.PlayerLines.Count; k++)
                {
                    if (lines.Count >= MaxPlayerLines) break;
                    string clean = SanitizePlayerLine(r.PlayerLines[k]);
                    if (clean.Length == 0) continue;
                    lines.Add(clean);
                    minutes.Add(r.PlayerLineMinutes != null && k < r.PlayerLineMinutes.Count ? r.PlayerLineMinutes[k] : -1);
                }
                playerLines = lines.ToArray();
                playerLineMinutes = minutes.ToArray();
            }
        }

        /// <summary>
        /// New Game: progression starts over; settings, endings seen, achievements, secrets, totals and best times
        /// stay.
        /// </summary>
        public void NewGame()
        {
            nightUnlocked = 1;
            currentNight = 1;
            lastCompletedNight = 0;
            checkpoint = new Checkpoint();
            nightStartMemory = new[] { new FlagSnapshot(), new FlagSnapshot(), new FlagSnapshot() };
            nightStarts = new int[Nights];
            nightStartTrust = new float[Nights];
            memory = new FlagSnapshot();
            playerLines = Array.Empty<string>();
            playerLineMinutes = Array.Empty<int>();
            entityTrust = 0f;
            assistCarry = 0;
        }

        /// <summary>A tip is remembered as shown (true = it was new).</summary>
        public bool MarkTipShown(string id)
        {
            if (string.IsNullOrEmpty(id) || Array.IndexOf(tipsShown, id) >= 0) return false;
            var list = new List<string>(tipsShown) { id };
            tipsShown = list.ToArray();
            return true;
        }

        /// <summary>Anything to continue: a night was started or unlocked, or a checkpoint exists.</summary>
        public bool HasProgress
        {
            get
            {
                if (nightUnlocked > 1 || (checkpoint != null && checkpoint.valid)) return true;
                foreach (int n in nightStarts ?? Array.Empty<int>()) if (n > 0) return true;
                return false;
            }
        }

        /// <summary>Anything for the Records screen: achievements, endings, tug totals or play time.</summary>
        public bool HasRecords
        {
            get
            {
                if ((achievements?.Length ?? 0) > 0 || (endingsSeen?.Length ?? 0) > 0 || tugWinsTotal > 0 || tugLossesTotal > 0) return true;
                foreach (float s in nightSeconds ?? Array.Empty<float>()) if (s > 0f) return true;
                return false;
            }
        }

        /// <summary>Where the title's Continue goes.</summary>
        public sealed class ContinueInfo
        {
            public int Night;
            /// <summary>The checkpoint to resume, or null to start the night from its beginning.</summary>
            public Checkpoint Checkpoint;
        }

        /// <summary>
        /// Continue's target, or null to hide Continue: a valid checkpoint of a playable night first; nothing once
        /// the game was just finished or the next night is not in this build (the demo after Night 1); else the
        /// current night from its start when there is any progress.
        /// </summary>
        public ContinueInfo ContinueTarget(int maxNight)
        {
            var cp = checkpoint;
            if (cp != null && cp.valid && !string.IsNullOrEmpty(cp.beat) && cp.night >= 1 && cp.night <= maxNight)
                return new ContinueInfo { Night = cp.night, Checkpoint = cp };
            if (lastCompletedNight >= Nights || currentNight > maxNight) return null;
            return HasProgress ? new ContinueInfo { Night = currentNight } : null;
        }

        /// <summary>
        /// The memory and trust a night starts from. Night Select replays a night that was started before from its
        /// first start (so replaying Night 1 cannot take the Night 2 choices away from a Night 3 replay); every other
        /// start uses the saved memory up to that night, with trust decayed toward neutral. Night 1 starts empty.
        /// </summary>
        public void StartStateFor(int night, bool fromNightSelect, out FlagSnapshot memoryAtStart, out float trustAtStart)
        {
            int i = Index(night);
            if (fromNightSelect && nightStarts[i] > 0)
            {
                memoryAtStart = nightStartMemory[i] ?? new FlagSnapshot();
                trustAtStart = nightStartTrust[i];
                return;
            }
            if (i == 0)
            {
                memoryAtStart = new FlagSnapshot();
                trustAtStart = 0f;
                return;
            }
            memoryAtStart = MemoryForNight(i + 1);
            trustAtStart = TrustAtNightStart(i + 1, entityTrust);
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

        /// <summary>
        /// The saved memory after <paramref name="night"/> ends: the saved keys of earlier nights (and of no night,
        /// such as the legacy m.said_name) stay, the keys of this night and later nights are dropped, then everything the run
        /// remembered is added on top (its own keys, and the earlier memory it started from).
        /// </summary>
        public static FlagSnapshot MergeNightMemory(FlagSnapshot saved, FlagSnapshot run, int night)
        {
            int first = Math.Max(1, Math.Min(Nights, night));
            bool Keep(string key)
            {
                if (string.IsNullOrEmpty(key)) return false;
                for (int n = first; n <= Nights; n++)
                    if (key.StartsWith(MemoryFlags.Prefix + "n" + n + ".", StringComparison.Ordinal)) return false;
                return true;
            }
            saved = saved ?? new FlagSnapshot();
            var kept = new FlagSnapshot();
            var flags = new List<string>();
            foreach (var f in saved.flags ?? Array.Empty<string>()) if (Keep(f)) flags.Add(f);
            kept.flags = flags.ToArray();
            var ck = new List<string>();
            var cv = new List<int>();
            var keys = saved.counterKeys ?? Array.Empty<string>();
            var values = saved.counterValues ?? Array.Empty<int>();
            for (int i = 0; i < Math.Min(keys.Length, values.Length); i++)
                if (Keep(keys[i])) { ck.Add(keys[i]); cv.Add(values[i]); }
            kept.counterKeys = ck.ToArray();
            kept.counterValues = cv.ToArray();
            var hk = new List<string>();
            var hv = new List<string>();
            var choiceKeys = saved.choiceKeys ?? Array.Empty<string>();
            var choiceValues = saved.choiceValues ?? Array.Empty<string>();
            for (int i = 0; i < Math.Min(choiceKeys.Length, choiceValues.Length); i++)
                if (Keep(choiceKeys[i])) { hk.Add(choiceKeys[i]); hv.Add(choiceValues[i]); }
            kept.choiceKeys = hk.ToArray();
            kept.choiceValues = hv.ToArray();
            var merged = new NarrativeFlags();
            merged.Restore(kept);
            merged.Merge(run);
            return merged.Snapshot();
        }

        /// <summary>Trust decays toward neutral each night so the new night's choices weigh most (2.3).</summary>
        public static float TrustAtNightStart(int night, float savedTrust)
        {
            float k = night >= 3 ? 0.75f : night == 2 ? 0.5f : 0f;
            return MathUtil.Clamp(savedTrust * k, -1f, 1f);
        }
    }
}
