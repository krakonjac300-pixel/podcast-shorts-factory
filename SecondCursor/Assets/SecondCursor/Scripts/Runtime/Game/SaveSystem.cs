using System;
using System.IO;
using SecondCursor.Core;
using SecondCursor.Core.Story;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>What persists between sessions. Small today; shaped for multiple shifts and endings later.</summary>
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public float masterVolume = 0.9f;
        public bool crtEffects = true;
        public string[] endingsSeen = Array.Empty<string>();
        public int shiftsCompleted;
        /// <summary>Story flags at the end of the last completed shift (entity relationship, choices...).</summary>
        public FlagSnapshot lastShiftFlags = new FlagSnapshot();
        public float entityTrust;
    }

    /// <summary>JSON save file in Application.persistentDataPath. Never throws into gameplay.</summary>
    public static class SaveSystem
    {
        const string FileName = "second_cursor_save.json";

        static string PathOnDisk => Path.Combine(Application.persistentDataPath, FileName);

        public static SaveData Load()
        {
            try
            {
                if (!File.Exists(PathOnDisk)) return new SaveData();
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(PathOnDisk));
                return data ?? new SaveData();
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.System, "Save file unreadable, starting fresh: " + e.Message);
                return new SaveData();
            }
        }

        public static void Save(SaveData data)
        {
            try
            {
                File.WriteAllText(PathOnDisk, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.System, "Could not write save file: " + e.Message);
            }
        }

        /// <summary>Record a finished shift and its ending.</summary>
        public static void RecordEnding(GameServices g, string endingId)
        {
            var data = Load();
            if (Array.IndexOf(data.endingsSeen, endingId) < 0)
            {
                var list = new System.Collections.Generic.List<string>(data.endingsSeen) { endingId };
                data.endingsSeen = list.ToArray();
            }
            data.shiftsCompleted++;
            data.lastShiftFlags = g.Flags.Snapshot();
            data.entityTrust = g.Memory.Trust;
            data.masterVolume = g.Audio.MasterVolume;
            data.crtEffects = g.Fx.CrtEnabled;
            Save(data);
            GameLog.Info(LogChannel.System, "Saved ending '" + endingId + "' (shifts completed: " + data.shiftsCompleted + ")");
        }

        public static void SaveSettings(GameServices g)
        {
            var data = Load();
            data.masterVolume = g.Audio.MasterVolume;
            data.crtEffects = g.Fx.CrtEnabled;
            Save(data);
        }
    }
}
