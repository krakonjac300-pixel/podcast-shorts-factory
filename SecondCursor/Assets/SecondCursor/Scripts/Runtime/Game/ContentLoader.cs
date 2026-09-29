using System;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using UnityEngine;

namespace SecondCursor.Game
{
    /// <summary>
    /// Loads the authored JSON content from Resources/Content (strings, story, filesystem, emails,
    /// employees, workorders, tasks, dialogue). A missing or malformed file is logged and replaced by
    /// safe placeholders so the prototype still runs.
    /// </summary>
    public static class ContentLoader
    {
        public const string Folder = "Content/";

        public static ContentDatabase Load()
        {
            var db = new ContentDatabase(
                Read<StringTableData>("strings"),
                Read<StoryData>("story"),
                Read<FileSystemData>("filesystem"),
                Read<EmailsData>("emails"),
                Read<EmployeesData>("employees"),
                Read<WorkOrdersData>("workorders"),
                Read<TasksData>("tasks"),
                Read<DialogueData>("dialogue"));
            foreach (var p in db.Problems) GameLog.Warn(LogChannel.System, "Content: " + p);
            return db;
        }

        static T Read<T>(string name) where T : class, new()
        {
            var asset = Resources.Load<TextAsset>(Folder + name);
            if (asset == null)
            {
                GameLog.Warn(LogChannel.System, "Content file Resources/" + Folder + name + ".json not found");
                return new T();
            }
            try
            {
                var data = JsonUtility.FromJson<T>(asset.text);
                return data ?? new T();
            }
            catch (Exception e)
            {
                GameLog.Error(LogChannel.System, "Content file " + name + ".json could not be parsed: " + e.Message);
                return new T();
            }
        }
    }
}
