using System;
using System.IO;
using SecondCursor.Core;
using SecondCursor.Core.Story;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>Progress that follows the player between machines (the only file Steam Cloud should sync).</summary>
    [Serializable]
    public class SaveData
    {
        public int version = 2;
        public string[] endingsSeen = Array.Empty<string>();
        public int shiftsCompleted;
        /// <summary>Highest night the player may start (1 until Night 1 is finished).</summary>
        public int nightUnlocked = 1;
        /// <summary>Story flags at the end of the last completed shift (entity relationship, choices...).</summary>
        public FlagSnapshot lastShiftFlags = new FlagSnapshot();
        public float entityTrust;
        /// <summary>Unlocked achievement ids (mirrored to Steam when it is available).</summary>
        public string[] achievements = Array.Empty<string>();

        // Settings used to live here (save version 1); kept only so old files migrate.
        public float masterVolume = -1f;
        public bool crtEffects = true;
    }

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

        static string Dir => Application.persistentDataPath;
        static string PathOf(string name) => Path.Combine(Dir, name);

        public static bool HasAnyData => File.Exists(PathOf(ProgressFile)) || File.Exists(PathOf(SettingsFile)) || File.Exists(PathOf(LegacyFile));

        // ------------------------------------------------------------------ progress

        public static SaveData Load()
        {
            MigrateLegacy();
            return Read<SaveData>(ProgressFile) ?? new SaveData();
        }

        public static void Save(SaveData data) => Write(ProgressFile, data);

        /// <summary>Record a finished shift and its ending.</summary>
        public static void RecordEnding(GameServices g, string endingId, int nightFinished = 1)
        {
            var data = Load();
            if (Array.IndexOf(data.endingsSeen, endingId) < 0)
            {
                var list = new System.Collections.Generic.List<string>(data.endingsSeen) { endingId };
                data.endingsSeen = list.ToArray();
            }
            data.shiftsCompleted++;
            data.nightUnlocked = Mathf.Max(data.nightUnlocked, nightFinished + 1);
            data.lastShiftFlags = g.Flags.Snapshot();
            data.entityTrust = g.Memory.Trust;
            Save(data);
            SaveSettings(g);
            GameLog.Info(LogChannel.System, "Saved ending '" + endingId + "' (shifts completed: " + data.shiftsCompleted + ")");
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
            s.reduceFlashing = g.Fx.ReduceFlashing;
            if (!Application.isEditor) s.fullscreen = Screen.fullScreen;
            SaveSettings(s);
        }

        // ------------------------------------------------------------------ plumbing

        static T Read<T>(string name) where T : class
        {
            string path = PathOf(name);
            foreach (var candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    var data = JsonUtility.FromJson<T>(File.ReadAllText(candidate));
                    if (data != null) return data;
                }
                catch (Exception e)
                {
                    GameLog.Warn(LogChannel.System, "Unreadable " + Path.GetFileName(candidate) + ": " + e.Message);
                    if (candidate == path) Quarantine(path);
                }
            }
            return null;
        }

        static void Write(string name, object data)
        {
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
            if (!File.Exists(legacy)) return;
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
