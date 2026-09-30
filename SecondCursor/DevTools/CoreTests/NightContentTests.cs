using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecondCursor.Core.Content;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Content of every night the way ContentLoader.Load(night) builds it (base + overlays), checked against
    /// the expansion spec's content rules (12.2): cross-references, the Personnel rule, dialogue coverage,
    /// the two voices, banned words and template tokens.
    /// </summary>
    public class NightContentTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));

        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

        /// <summary>Long dashes are banned from all text (a house rule): U+2014 and U+2013.</summary>
        static readonly char[] Dashes = { (char)0x2014, (char)0x2013 };

        static T Read<T>(string folder, string name) where T : class
        {
            string path = Path.Combine(Dir, folder, name + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }

        static ContentPack Pack(string folder) => new ContentPack
        {
            Strings = Read<StringTableData>(folder, "strings"), Story = Read<StoryData>(folder, "story"),
            FileSystem = Read<FileSystemData>(folder, "filesystem"), Emails = Read<EmailsData>(folder, "emails"),
            Employees = Read<EmployeesData>(folder, "employees"), WorkOrders = Read<WorkOrdersData>(folder, "workorders"),
            Tasks = Read<TasksData>(folder, "tasks"), Dialogue = Read<DialogueData>(folder, "dialogue"),
        };

        static ContentDatabase Night(int night)
        {
            // As ContentLoader.Load(night) builds it in the full game: base, the full-game strings, then the nights.
            var pack = Pack("").Overlay(Pack("full"));
            for (int n = 2; n <= night; n++) pack = pack.Overlay(Pack("night" + n));
            return pack.Build();
        }

        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));
        static bool Has(int night) => night == 1 || Directory.Exists(Path.Combine(Dir, "night" + night));

        public static IEnumerable<object[]> Nights => new[] { new object[] { 1 }, new object[] { 2 }, new object[] { 3 } };

        [Theory]
        [MemberData(nameof(Nights))]
        public void TaskTargetsAndOrderOwnersResolve(int night)
        {
            if (!Present || !Has(night)) return;
            var db = Night(night);
            var fs = new VirtualFileSystem(db.FileSystem);
            foreach (var t in db.Tasks.tasks)
            {
                foreach (var target in t.targets)
                {
                    // An earlier night's task whose file a later night deleted is never given again.
                    if (Array.IndexOf(db.FileSystem.removedFiles, target) >= 0) continue;
                    bool ok = t.type == "ViewEmployee" ? db.Employee(target) != null
                        : t.type == "OpenFile" ? fs.GetFile(target) != null
                        : db.Email(target) != null || fs.GetFile(target) != null || db.Order(target) != null;
                    Assert.True(ok, "night " + night + ": task " + t.id + " targets unknown id " + target);
                }
                if (t.type == "MoveFile") Assert.True(fs.GetFolder(t.param) != null, "task " + t.id + " moves to unknown folder " + t.param);
                Assert.True(t.author == "" || t.author == "entity", "task " + t.id + " has unknown author " + t.author);
            }
            foreach (var o in db.WorkOrders.orders)
                Assert.True(db.Employee(o.employeeRef) != null, "night " + night + ": order " + o.id + " references unknown employee " + o.employeeRef);
            foreach (var f in db.FileSystem.files)
                Assert.True(fs.GetFolder(f.folder) != null, "night " + night + ": file " + f.id + " in unknown folder " + f.folder);
        }

        [Theory]
        [MemberData(nameof(Nights))]
        public void PersonnelRuleOrdersMatchThatNightsStatuses(int night)
        {
            if (!Present || !Has(night)) return;
            var db = Night(night);
            // Each night's own orders (an earlier night's orders are history: decided that night, by that night's records).
            var own = night == 1 ? Read<WorkOrdersData>("", "workorders") : Read<WorkOrdersData>("night" + night, "workorders");
            var ids = new HashSet<string>((own?.orders ?? Array.Empty<WorkOrderData>()).Select(o => o.id));
            foreach (var o in db.WorkOrders.orders)
            {
                if (o.rule != "" || !ids.Contains(o.id)) continue;
                var e = db.Employee(o.employeeRef);
                bool terminated = string.Equals(e.status, "TERMINATED", StringComparison.OrdinalIgnoreCase);
                Assert.True((terminated ? "approve" : "reject") == o.correct, "night " + night + ": " + o.id + " owner " + e.id + " is " + e.status + " but correct=" + o.correct);
            }
        }

        [Fact]
        public void Night2OverlayRemovesTheOldTempFileWithoutAPlaceholder()
        {
            if (!Present || !Has(2)) return;
            var db = Night(2);
            Assert.Empty(db.Problems);
            var fs = new VirtualFileSystem(db.FileSystem);
            Assert.Null(fs.GetFile(ContentIds.FileCache));
            Assert.NotNull(fs.GetFile(ContentIds.FileCacheN2));
            Assert.True(fs.GetFile(ContentIds.File209).Hidden);
            Assert.True(fs.GetFile(ContentIds.File214).Hidden);
            Assert.Equal(ContentIds.FolderDocuments, fs.FolderOf(ContentIds.FileDoorLog));
            Assert.Equal("DECEASED", db.Employee("017").status);
            Assert.Contains("NOT RESPONDING", string.Join("\n", db.Story.biosLines));
            // Night 1's content is untouched.
            var n1 = new VirtualFileSystem(Night(1).FileSystem);
            Assert.NotNull(n1.GetFile(ContentIds.FileCache));
            Assert.Null(n1.GetFile(ContentIds.File209));
        }

        static readonly string[] Probes = { "who are you", "why", "help me", "no", "yes", "fuck", "what is your name", "017", "are you real", "hello", "stop" };

        [Theory]
        [MemberData(nameof(Nights))]
        public void EveryExchangeAnswersTheProbesAndHasFallbackAndSilence(int night)
        {
            if (!Present || !Has(night)) return;
            var db = Night(night);
            var engine = new DialogueEngine(db);
            foreach (var ex in db.Dialogue.exchanges)
            {
                Assert.NotEmpty(ex.fallback);
                Assert.NotEmpty(ex.silence);
                Assert.NotEmpty(ex.entityLines);
                Assert.True(ex.next == "" || db.Exchange(ex.next) != null, ex.id + " -> unknown next " + ex.next);
                foreach (var probe in Probes) Assert.NotEmpty(engine.Respond(ex, probe).Lines);
            }
        }

        [Fact]
        public void Night2DialogueHitsItsTaggedKeywords()
        {
            if (!Present || !Has(2)) return;
            var db = Night(2);
            var engine = new DialogueEngine(db);
            var back = db.Exchange(ContentIds.ExchangeN2Back);
            var gary = db.Exchange(ContentIds.ExchangeN2GaryOne);
            Assert.Equal("name", engine.Respond(back, "Ellen?").Tag);
            Assert.NotEqual("name", engine.Respond(back, "excellent").Tag);
            var glasses = engine.Respond(gary, "your glasses");
            Assert.Equal("glasses", glasses.Tag);
            Assert.Equal("my glasses", glasses.Lines[0]);
            Assert.Equal("ex2_gary_two", gary.next);
            Assert.Equal("2 something", engine.Respond(db.Exchange("ex2_gary_two"), "its 3 oclock").Lines[0]);
        }

        static readonly Regex EllenLine = new Regex("^[A-Z0-9 ?]+$");

        static int Words(string line) => line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>Ellen and Gary lines of a night's overlay (Night 1's base content is the slice and keeps its own rules).</summary>
        static IEnumerable<(string where, string voice, string line)> VoiceLines(DialogueData d)
        {
            if (d == null) yield break;
            foreach (var ex in d.exchanges ?? Array.Empty<ExchangeData>())
            {
                string v = ex.voice ?? "";
                foreach (var l in ex.entityLines) yield return (ex.id, v, l);
                foreach (var l in ex.fallback) yield return (ex.id + ".fallback", v, l);
                foreach (var l in ex.silence) yield return (ex.id + ".silence", v, l);
                foreach (var r in ex.responses)
                    foreach (var l in r.reply) yield return (ex.id + ".reply", v, l);
            }
            foreach (var set in d.lineSets ?? Array.Empty<LineSetData>())
                foreach (var l in set.lines) yield return (set.id, set.voice ?? "", l);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        public void EllenAndGaryKeepTheirVoices(int night)
        {
            if (!Present || !Has(night)) return;
            var d = Read<DialogueData>("night" + night, "dialogue");
            var bad = new List<string>();
            foreach (var (where, voice, line) in VoiceLines(d))
            {
                int words = Words(line);
                if (words < 1 || words > 6) bad.Add(where + ": " + words + " words: " + line);
                if (voice == "")
                {
                    if (!EllenLine.IsMatch(line)) bad.Add(where + ": Ellen line not ALL CAPS or has punctuation: " + line);
                }
                else if (voice == "gary")
                {
                    if (where != "g3c_seated" && line.Any(char.IsUpper)) bad.Add(where + ": Gary line with capitals: " + line);
                }
                else if (voice != "system")
                {
                    bad.Add(where + ": unknown voice " + voice);
                }
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        static IEnumerable<string> AllStrings(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) foreach (var s in AllStrings(p.Value)) yield return s;
                    break;
                case JsonValueKind.Array:
                    foreach (var x in e.EnumerateArray()) foreach (var s in AllStrings(x)) yield return s;
                    break;
                case JsonValueKind.String:
                    yield return e.GetString();
                    break;
            }
        }

        static IEnumerable<(string file, string text)> EveryContentString()
        {
            foreach (var file in Directory.GetFiles(Dir, "*.json", SearchOption.AllDirectories))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var s in AllStrings(doc.RootElement).ToList()) yield return (Path.GetRelativePath(Dir, file), s);
            }
        }

        [Fact]
        public void NoBannedWordsAndNoLongDashesAnywhere()
        {
            if (!Present) return;
            string[] banned = { "Microsoft", "Windows", "Recycle Bin", "Explorer", "WordPad", "Minesweeper", "Solitaire" };
            var bad = new List<string>();
            foreach (var (file, text) in EveryContentString())
            {
                foreach (var b in banned)
                    if (text.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) bad.Add(file + ": '" + b + "' in " + text);
                if (text.IndexOfAny(Dashes) >= 0) bad.Add(file + ": long dash in " + text);
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Fact]
        public void EveryTemplateTokenIsKnown()
        {
            if (!Present) return;
            var known = new HashSet<string>(NightTemplates.Known) { "0", "1", "2", "3", "4" };   // Phase K: the shelf result lines take five arguments
            var bad = new List<string>();
            foreach (var (file, text) in EveryContentString())
                foreach (var token in NightTemplates.TokensIn(text))
                    if (!known.Contains(token)) bad.Add(file + ": {" + token + "}");
            Assert.True(bad.Count == 0, string.Join("\n", bad));
        }

        [Fact]
        public void TemplateFilesOfNight2FillCompletely()
        {
            if (!Present || !Has(2)) return;
            var db = Night(2);
            var tokens = NightTemplates.Tokens(new NarrativeFlags(), new[] { "who are you", "stop", "no" });
            foreach (var f in db.FileSystem.files.Where(f => f.tags.Contains("template")))
            {
                string filled = NightTemplates.Fill(f.content, tokens);
                Assert.DoesNotContain("{", filled);
                Assert.Contains("who are you", filled);
            }
        }

        [Fact]
        public void EntityTasksAreWrittenInHerVoice()
        {
            if (!Present || !Has(2)) return;
            foreach (var t in Night(2).Tasks.tasks.Where(t => t.author == "entity"))
            {
                Assert.Matches(EllenLine, t.title);
                Assert.InRange(Words(t.title), 1, 6);
            }
        }
    }
}
