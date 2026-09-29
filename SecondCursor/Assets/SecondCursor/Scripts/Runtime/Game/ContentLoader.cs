using System;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Loads the authored JSON content from Resources/Content (strings, story, filesystem, emails,
    /// employees, workorders, tasks, dialogue). Night 2 and 3 apply the optional overlay folders
    /// Content/night2/ and Content/night3/ on top, cumulatively (<see cref="ContentOverlay"/>). A missing or
    /// malformed base file is logged and replaced by safe placeholders so the game still runs; a missing
    /// overlay file is normal.
    /// </summary>
    public static class ContentLoader
    {
        public const string Folder = "Content/";

        public static ContentDatabase Load() => Load(1);

        public static ContentDatabase Load(int night)
        {
            var pack = ReadPack(Folder, true);
            for (int n = 2; n <= Mathf.Clamp(night, 1, 3); n++)
            {
                var overlay = ReadPack(Folder + "night" + n + "/", false);
                if (overlay.IsEmpty) continue;
                pack = pack.Overlay(overlay);
                GameLog.Info(LogChannel.System, "Content: applied the night " + n + " overlay");
            }
            var db = pack.Build();
            foreach (var p in db.Problems) GameLog.Warn(LogChannel.System, "Content: " + p);
            return db;
        }

        static ContentPack ReadPack(string folder, bool required)
        {
            return new ContentPack
            {
                Strings = Read<StringTableData>(folder, "strings", required),
                Story = Read<StoryData>(folder, "story", required),
                FileSystem = Read<FileSystemData>(folder, "filesystem", required),
                Emails = Read<EmailsData>(folder, "emails", required),
                Employees = Read<EmployeesData>(folder, "employees", required),
                WorkOrders = Read<WorkOrdersData>(folder, "workorders", required),
                Tasks = Read<TasksData>(folder, "tasks", required),
                Dialogue = Read<DialogueData>(folder, "dialogue", required),
            };
        }

        /// <summary>A content file, or (base: an empty object and a warning; overlay: null) if it is missing.</summary>
        static T Read<T>(string folder, string name, bool required) where T : class, new()
        {
            var asset = Resources.Load<TextAsset>(folder + name);
            if (asset == null)
            {
                if (!required) return null;
                GameLog.Warn(LogChannel.System, "Content file Resources/" + folder + name + ".json not found");
                return new T();
            }
            try
            {
                var data = JsonUtility.FromJson<T>(asset.text);
                return data ?? (required ? new T() : null);
            }
            catch (Exception e)
            {
                GameLog.Error(LogChannel.System, "Content file " + folder + name + ".json could not be parsed: " + e.Message);
                return required ? new T() : null;
            }
        }
    }
}
