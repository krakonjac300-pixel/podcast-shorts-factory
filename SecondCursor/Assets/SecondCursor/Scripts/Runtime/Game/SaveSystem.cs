using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>Per-machine preferences (never synced: a Steam Deck's display mode must not reach a PC).</summary>
    [Serializable]
    public class SettingsData
    {
        public float masterVolume = 0.9f;
        public bool crtEffects = true;
        /// <summary>Photosensitivity: softer glitches, flashes and shakes.</summary>
        public bool reduceFlashing;
        public bool fullscreen = true;
        /// <summary>False until the player has made the first-launch flashing choice.</summary>
        public bool flashingChosen;
        /// <summary>0 = VSync (default), else a frame cap (see <see cref="DisplaySettings.FrameRates"/>); an old save's -1 (unlimited) reads as 240.</summary>
        public int frameRate;
        /// <summary>Reading text at double size in Jotter and Mail (Steam Deck readability). Phase Q1: kept for older builds (= textSize Large).</summary>
        public bool largeText;
        /// <summary>Phase Q1: 0 Normal, 1 Medium, 2 Large; -1 = not written yet (read from <see cref="largeText"/>).</summary>
        public int textSize = -1;
        /// <summary>Phase Q1: CRT intensity 0 Off, 1 Low, 2 Full; -1 = not written yet (read from <see cref="crtEffects"/>).</summary>
        public int crtLevel = -1;
        /// <summary>False until the player (or the Deck's first launch) has picked a reading text size.</summary>
        public bool largeTextChosen;
        /// <summary>Phase P (A2): "off" or "hold" (holding the button wins a tug-of-war).</summary>
        public string tugAssist = "off";
        /// <summary>Phase P (A2): a drag stays held after the button comes up; the next press drops it.</summary>
        public bool clickLock;
    }

    /// <summary>
    /// JSON files in Application.persistentDataPath: progress.json and settings.json. Writes are atomic
    /// (temp file, then replace, keeping a .bak), an unreadable file is never overwritten (it is renamed
    /// .corrupt and the backup is tried), and nothing here ever throws into gameplay.
    /// </summary>
    public static class SaveSystem
    {
        const string ProgressFile = "progress.json";
        const string SettingsFile = "settings.json";
        const string LegacyFile = "second_cursor_save.json";

        /// <summary>
        /// Test runs keep their saves out of the player's folder: set by the bridge ("savedir") or by the
        /// "-scsavedir PATH" launch argument. It survives Play sessions (domain reload is off), so every boot logs it.
        /// </summary>
        internal static string DirOverride = CommandLinePath("-scsavedir");

        static string Dir => string.IsNullOrEmpty(DirOverride) ? Application.persistentDataPath : DirOverride;

        /// <summary>Where the saves are read and written.</summary>
        public static string Folder => Dir;

        static string CommandLinePath(string name)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                    if (args[i] == name) return args[i + 1];
            }
            catch (Exception)
            {
                // No command line (some platforms): the default folder is used.
            }
            return null;
        }

        /// <summary>True if the progress file was set aside as unreadable during this app launch.</summary>
        public static bool CorruptThisLaunch { get; private set; }

        /// <summary>
        /// A QA launch (-scnight / -scbeat): progress.json is read but never written for the rest of the launch (no night
        /// start, checkpoint, completion, tug total or achievement). Settings are still saved.
        /// </summary>
        internal static bool ProgressReadOnly;
        static bool _readOnlyLogged;

        /// <summary>A new launch (every Play press in the Editor, where statics survive): forget the last launch's notice.</summary>
        internal static void ResetLaunchState()
        {
            CorruptThisLaunch = false;
            LockedFiles.Clear();
            ProgressReadOnly = false;
            _readOnlyLogged = false;
        }
        static string PathOf(string name) => Path.Combine(Dir, name);

        /// <summary>Test runs only (the bridge's resetsave): delete progress.json and settings.json in the override folder.</summary>
        internal static bool DeleteAllInOverride()
        {
            if (string.IsNullOrEmpty(DirOverride)) return false;
            foreach (var name in new[] { ProgressFile, SettingsFile })
                foreach (var suffix in new[] { "", ".bak", ".tmp", ".corrupt" })
                {
                    string f = PathOf(name) + suffix;
                    try
                    {
                        if (File.Exists(f)) File.Delete(f);
                    }
                    catch (Exception e)
                    {
                        GameLog.Warn(LogChannel.System, "Could not delete " + f + ": " + e.Message);
                    }
                }
            CorruptThisLaunch = false;
            LockedFiles.Clear();
            return true;
        }

        public static bool HasAnyData => File.Exists(PathOf(ProgressFile)) || File.Exists(PathOf(SettingsFile)) || File.Exists(PathOf(LegacyFile));

        // ------------------------------------------------------------------ progress

        /// <summary>
        /// The progress file, migrated to the current version. Always read fresh before a change: other
        /// writers (achievements) may have saved since the game started.
        /// </summary>
        public static SaveData Load()
        {
            MigrateLegacy();
            var data = Read<SaveData>(ProgressFile);
            if (data == null) return new SaveData();
            int before = data.version;
            if (data.Migrate() && before < SaveData.CurrentVersion)
            {
                GameLog.Info(LogChannel.System, "Migrated progress.json from version " + before + " to " + SaveData.CurrentVersion);
                Save(data);
            }
            return data;
        }

        public static void Save(SaveData data)
        {
            if (ProgressReadOnly)
            {
                if (!_readOnlyLogged) GameLog.Info(LogChannel.System, "QA launch: progress.json is not written");
                _readOnlyLogged = true;
                return;
            }
            Write(ProgressFile, data);
        }

        /// <summary>A night starts fresh (not from a checkpoint): remember its starting memory and trust.</summary>
        public static void RecordNightStart(GameServices g)
        {
            var data = Load();
            // A debug start moves Continue's night but is never remembered as the night's first start.
            if (g.RecordsArmed) data.RecordNightStart(g.Night, g.Flags.Snapshot(MemoryFlags.Prefix), g.Memory.Trust);
            else data.NoteNightStarted(g.Night);
            Save(data);
        }

        /// <summary>A real tug-of-war ended in a run that counts: add it to the lifetime totals. Returns the total wins.</summary>
        public static int RecordTug(bool playerWon)
        {
            var data = Load();
            data.RecordTug(playerWon);
            Save(data);
            return data.tugWinsTotal;
        }

        /// <summary>Phase L: a one-time tip was shown; it never shows again on this save.</summary>
        public static void MarkTipShown(string id)
        {
            var data = Load();
            if (data.MarkTipShown(id)) Save(data);
        }

        /// <summary>Night Select replaced the saved checkpoint: Continue no longer resumes it.</summary>
        public static void ClearCheckpoint()
        {
            var data = Load();
            data.SetCheckpoint(new Checkpoint());
            Save(data);
        }

        /// <summary>New Game: progression starts over, records and settings stay.</summary>
        public static void NewGame()
        {
            var data = Load();
            data.NewGame();
            Save(data);
            GameLog.Info(LogChannel.System, "New Game: progression reset (records kept)");
        }

        /// <summary>A checkpoint beat starts: everything Continue needs to rebuild the world from here.</summary>
        public static void SaveCheckpoint(GameServices g, string beat)
        {
            var data = Load();
            data.SetCheckpoint(new Checkpoint
            {
                valid = true,
                night = g.Night,
                beat = beat ?? "",
                clockMinutes = g.Clock.TotalMinutes,
                trust = g.Memory.Trust,
                assistLevel = g.Assist != null ? g.Assist.Level : 0,
                flags = g.Flags.Snapshot(),
                elapsed = g.Director != null ? g.Director.NightElapsed : 0f,
                armed = g.RecordsArmed,
            });
            Save(data);
            GameLog.Info(LogChannel.System, (ProgressReadOnly ? "Checkpoint not saved (QA launch): night " : "Checkpoint saved: night ") + g.Night + ", " + beat
                                            + (g.RecordsArmed ? "" : " (debug run)"));
        }

        /// <summary>A night ends: memory, trust, assist carry, the ending, unlocks and totals; the checkpoint is cleared.</summary>
        public static void RecordNightComplete(GameServices g, string endingId, IList<string> playerLines, float seconds, IList<int> lineMinutes = null,
            CaptureStats capture = null, int[] ghostPath = null)
        {
            var data = Load();
            data.RecordNightComplete(new NightResult
            {
                Night = g.Night,
                EndingId = endingId ?? "",
                Memory = g.Flags.Snapshot(MemoryFlags.Prefix),
                Trust = g.Memory.Trust,
                AssistLevel = g.Assist != null ? g.Assist.Level : 0,
                Seconds = seconds,
                PlayerLines = playerLines,
                PlayerLineMinutes = lineMinutes,
                Records = g.RecordsArmed,
                Capture = capture,
                GhostPath = ghostPath,
            });
            Save(data);
            // The game's own copy too: tokens read g.Save (a later night of this root, the end card).
            g.Save = data;
            SaveSettings(g);
            GameLog.Info(LogChannel.System, "Night " + g.Night + " complete, ending '" + endingId + "'"
                                            + (ProgressReadOnly ? " (QA launch: not saved)" : " (unlocked: night " + data.nightUnlocked + ")"));
        }

        /// <summary>Phase Q2 (V3): a name the player gave is kept at once (and in this root's copy of the save). True if it changed.</summary>
        public static bool SetPlayerName(GameServices g, string name)
        {
            var data = Load();
            bool changed = data.SetPlayerName(name);
            if (changed) Save(data);
            if (g != null && g.Save != null) g.Save.SetPlayerName(name);
            return changed;
        }

        /// <summary>Phase Q2 (T2): the Retention Record just shown is kept for Records.</summary>
        public static void SaveRecord(IList<RecordRow> rows, string endingId)
        {
            var data = Load();
            var packed = new string[rows?.Count ?? 0];
            for (int i = 0; i < packed.Length; i++) packed[i] = rows[i].Pack();
            data.lastRecord = packed;
            data.lastRecordEnding = endingId ?? "";
            Save(data);
        }

        public static void SetDifficulty(DifficultyMode mode)
        {
            var data = Load();
            data.difficulty = DifficultyTable.ModeId(mode);
            Save(data);
            GameLog.Info(LogChannel.System, "Difficulty: " + data.difficulty);
        }

        // ------------------------------------------------------------------ settings

        public static SettingsData LoadSettings()
        {
            MigrateLegacy();
            return Read<SettingsData>(SettingsFile) ?? new SettingsData();
        }

        public static void SaveSettings(SettingsData data) => Write(SettingsFile, data);

        public static void SaveSettings(GameServices g)
        {
            var s = LoadSettings();
            s.masterVolume = g.Audio.MasterVolume;
            s.crtEffects = g.Fx.CrtEnabled;
            s.crtLevel = (int)g.Fx.Crt;
            s.reduceFlashing = g.Fx.ReduceFlashing;
            if (!Application.isEditor) s.fullscreen = Screen.fullScreen;
            s.frameRate = DisplaySettings.FrameRate;
            s.largeText = DisplaySettings.LargeText;
            s.textSize = (int)DisplaySettings.Size;
            s.largeTextChosen = s.largeTextChosen || DisplaySettings.LargeTextChosen;
            AccessSettings.Save(s);
            SaveSettings(s);
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// The file, else its .bak. Only a file that was read but does not parse is set aside as .corrupt; a file that is
        /// briefly locked (antivirus, cloud sync) is read again a few times and never quarantined.
        /// </summary>
        static T Read<T>(string name) where T : class
        {
            string path = PathOf(name);
            // A main file that exists but stays locked is never overwritten (not even with its older .bak) until it can
            // be read again: the next write would otherwise replace the newest progress.
            bool mainLocked = false;
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                string text = ReadWithRetry(candidate);
                if (text == null)
                {
                    if (candidate == path) mainLocked = true;
                    continue;
                }
                try
                {
                    var data = JsonUtility.FromJson<T>(text);
                    if (data != null)
                    {
                        MarkLocked(name, mainLocked);
                        return data;
                    }
                }
                catch (Exception e)
                {
                    GameLog.Warn(LogChannel.System, "Unreadable " + Path.GetFileName(candidate) + ": " + e.Message);
                    if (candidate == path) Quarantine(path);
                }
            }
            MarkLocked(name, mainLocked);
            return null;
        }

        /// <summary>Files on disk that could not be read this time: writes to them are skipped until a read works.</summary>
        static readonly HashSet<string> LockedFiles = new HashSet<string>();

        static void MarkLocked(string name, bool locked)
        {
            if (!locked)
            {
                LockedFiles.Remove(name);
                return;
            }
            if (LockedFiles.Add(name))
                GameLog.Warn(LogChannel.System, name + " could not be read: it is left untouched until it can be read again");
        }

        const int ReadAttempts = 4;
        const int ReadRetryMilliseconds = 40;

        /// <summary>The file's text, retrying IO and access errors with a short, growing sleep; null if it stays unreadable.</summary>
        static string ReadWithRetry(string file)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return File.ReadAllText(file);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    if (attempt >= ReadAttempts)
                    {
                        GameLog.Warn(LogChannel.System, "Could not read " + Path.GetFileName(file) + " (" + attempt + " attempts): " + e.Message);
                        return null;
                    }
                    Thread.Sleep(ReadRetryMilliseconds * attempt);
                }
            }
        }

        static void Write(string name, object data)
        {
            if (LockedFiles.Contains(name))
            {
                GameLog.Warn(LogChannel.System, "Not writing " + name + ": the file on disk could not be read");
                return;
            }
            string path = PathOf(name), tmp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(tmp, JsonUtility.ToJson(data, true));
                if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
                else File.Move(tmp, path);
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.System, "Could not write " + name + ": " + e.Message);
            }
        }

        /// <summary>Keep a damaged file for inspection instead of silently overwriting it.</summary>
        static void Quarantine(string path)
        {
            if (Path.GetFileName(path) == ProgressFile) CorruptThisLaunch = true;
            try
            {
                string bad = path + ".corrupt";
                if (File.Exists(bad)) File.Delete(bad);
                File.Move(path, bad);
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.System, "Could not set aside a damaged save: " + e.Message);
            }
        }

        /// <summary>Version 1 kept settings and progress in one file: split it once.</summary>
        static void MigrateLegacy()
        {
            string legacy = PathOf(LegacyFile);
            if (ProgressReadOnly || !File.Exists(legacy)) return;
            try
            {
                var old = JsonUtility.FromJson<SaveData>(File.ReadAllText(legacy));
                if (old != null)
                {
                    if (!File.Exists(PathOf(SettingsFile)))
                        Write(SettingsFile, new SettingsData
                        {
                            masterVolume = old.masterVolume >= 0f ? old.masterVolume : 0.9f,
                            crtEffects = old.crtEffects,
                            flashingChosen = false,
                        });
                    if (!File.Exists(PathOf(ProgressFile)))
                    {
                        old.version = 2;
                        old.masterVolume = -1f;
                        Write(ProgressFile, old);
                    }
                }
                File.Delete(legacy);
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.System, "Could not migrate the old save: " + e.Message);
            }
        }
    }
}
