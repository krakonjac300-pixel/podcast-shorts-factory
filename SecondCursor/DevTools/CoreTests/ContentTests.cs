using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SecondCursor.Core.Art;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Validates the authored JSON in Resources/Content the way the game loads it (field names must match
    /// the Core data classes exactly, like Unity's JsonUtility), plus cross-references and font coverage.
    /// </summary>
    public class ContentTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };

        static T Load<T>(string name) where T : new()
        {
            string path = Path.Combine(Dir, name + ".json");
            Assert.True(File.Exists(path), "missing " + path);
            var obj = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            Assert.NotNull(obj);
            return obj;
        }

        static ContentDatabase LoadAll() => new ContentDatabase(
            Load<StringTableData>("strings"), Load<StoryData>("story"), Load<FileSystemData>("filesystem"),
            Load<EmailsData>("emails"), Load<EmployeesData>("employees"), Load<WorkOrdersData>("workorders"),
            Load<TasksData>("tasks"), Load<DialogueData>("dialogue"));

        /// <summary>An optional overlay file (Content/nightN/NAME.json), or null.</summary>
        static T LoadOptional<T>(string folder, string name) where T : class
        {
            string path = Path.Combine(Dir, folder, name + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }

        static ContentPack Pack(string folder) => new ContentPack
        {
            Strings = LoadOptional<StringTableData>(folder, "strings"), Story = LoadOptional<StoryData>(folder, "story"),
            FileSystem = LoadOptional<FileSystemData>(folder, "filesystem"), Emails = LoadOptional<EmailsData>(folder, "emails"),
            Employees = LoadOptional<EmployeesData>(folder, "employees"), WorkOrders = LoadOptional<WorkOrdersData>(folder, "workorders"),
            Tasks = LoadOptional<TasksData>(folder, "tasks"), Dialogue = LoadOptional<DialogueData>(folder, "dialogue"),
        };

        /// <summary>The content of a night the way ContentLoader.Load(night) builds it (full game: base, full, nights).</summary>
        static ContentDatabase LoadNight(int night)
        {
            var pack = Pack("").Overlay(Pack("full"));
            for (int n = 2; n <= night; n++) pack = pack.Overlay(Pack("night" + n));
            return pack.Build();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryNightLoadsWithoutProblems(int night)
        {
            if (!ContentPresent) return;
            var db = LoadNight(night);
            Assert.True(db.Problems.Count == 0, "Night " + night + " content problems:\n" + string.Join("\n", db.Problems));
        }

        static bool ContentPresent => File.Exists(Path.Combine(Dir, "dialogue.json")) && File.Exists(Path.Combine(Dir, "strings.json"));

        [Fact]
        public void ContentFilesArePresent() => Assert.True(ContentPresent, "content not found in " + Dir);

        [Fact]
        public void AllRequiredContentExistsWithoutPlaceholders()
        {
            if (!ContentPresent) return;
            var db = LoadAll();
            Assert.True(db.Problems.Count == 0, "Content problems:\n" + string.Join("\n", db.Problems));
        }

        [Fact]
        public void EveryStringIsRenderableWithThePixelFont()
        {
            if (!ContentPresent) return;
            var bad = new List<string>();
            foreach (var file in Directory.GetFiles(Dir, "*.json", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                // Decode JSON escapes by round-tripping every string value.
                using var doc = JsonDocument.Parse(text);
                Walk(doc.RootElement, Path.GetFileName(file), bad);
            }
            Assert.True(bad.Count == 0, "Unrenderable characters:\n" + string.Join("\n", bad));
        }

        static void Walk(JsonElement e, string where, List<string> bad)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Walk(p.Value, where + "." + p.Name, bad);
                    break;
                case JsonValueKind.Array:
                    int i = 0;
                    foreach (var x in e.EnumerateArray()) Walk(x, where + "[" + i++ + "]", bad);
                    break;
                case JsonValueKind.String:
                    foreach (char c in e.GetString())
                    {
                        if (c == '\n') continue;
                        if (c < 32 || c > 126 || !PixelFontData.Glyphs.ContainsKey(c) && c != ' ')
                        {
                            bad.Add(where + ": U+" + ((int)c).ToString("X4"));
                            break;
                        }
                    }
                    break;
            }
        }

        [Fact]
        public void CrossReferencesResolve()
        {
            if (!ContentPresent) return;
            var db = LoadAll();
            var fs = new VirtualFileSystem(db.FileSystem);
            foreach (var f in db.FileSystem.files)
                Assert.True(fs.GetFolder(f.folder) != null, "file " + f.id + " in unknown folder " + f.folder);
            foreach (var f in db.FileSystem.folders)
                Assert.True(f.parent == "" || fs.GetFolder(f.parent) != null, "folder " + f.id + " has unknown parent " + f.parent);
            foreach (var o in db.WorkOrders.orders)
                Assert.True(db.Employee(o.employeeRef) != null, "order " + o.id + " references unknown employee " + o.employeeRef);
            foreach (var t in db.Tasks.tasks)
            {
                foreach (var target in t.targets)
                {
                    bool ok = db.Email(target) != null || fs.GetFile(target) != null || db.Order(target) != null;
                    Assert.True(ok, "task " + t.id + " targets unknown id " + target);
                }
            }
            Assert.Equal(ContentIds.FolderIntake, fs.FolderOf(ContentIds.File017));
            Assert.Equal("employee_017.dat", fs.GetFile(ContentIds.File017).Name);
        }

        [Fact]
        public void WorkOrdersMatchTheStaffDirectoryRule()
        {
            if (!ContentPresent) return;
            var db = LoadAll();
            foreach (var id in new[] { ContentIds.Order3317, ContentIds.Order3318 })
            {
                var o = db.Order(id);
                var e = db.Employee(o.employeeRef);
                bool terminated = string.Equals(e.status, "TERMINATED", StringComparison.OrdinalIgnoreCase);
                Assert.Equal(terminated ? "approve" : "reject", o.correct);
            }
        }

        [Fact]
        public void DialogueChainTerminatesAndCoversKeywordGroups()
        {
            if (!ContentPresent) return;
            var db = LoadAll();
            var engine = new DialogueEngine(db);
            var seen = new HashSet<string>();
            var ex = engine.Get(ContentIds.ExchangeStop);
            Assert.NotNull(ex);
            Assert.StartsWith("STOP", ex.entityLines[0]);
            int n = 0;
            while (ex != null)
            {
                Assert.True(seen.Add(ex.id), "dialogue loop at " + ex.id);
                Assert.NotEmpty(ex.fallback);
                Assert.NotEmpty(ex.silence);
                foreach (var probe in new[] { "who are you", "why", "help me", "no", "yes", "fuck", "what is your name", "017", "are you real", "hello", "stop" })
                {
                    var r = engine.Respond(ex, probe);
                    Assert.NotEmpty(r.Lines);
                }
                ex = string.IsNullOrEmpty(ex.next) ? null : engine.Get(ex.next);
                Assert.True(++n < 10);
            }
            Assert.NotEmpty(db.Dialogue.panicLines);
            Assert.NotEmpty(db.Dialogue.cameraLines);
            Assert.NotEmpty(db.Dialogue.recordLines);
        }

        [Fact]
        public void StringsReferencedByCodeExist()
        {
            if (!ContentPresent) return;
            var strings = Load<StringTableData>("strings");
            var keys = new HashSet<string>();
            foreach (var e in strings.entries) keys.Add(e.key);
            // Keys with format placeholders must keep them.
            var db = LoadAll();
            foreach (var k in new[] { "shred.confirm.body", "shred.progress.body", "error.inuse.body", "notify.newmail" })
                Assert.Contains("{0}", db.Text(k));
            Assert.Equal("NEXUS OS", db.Text("os.name"));
        }
    }
}
