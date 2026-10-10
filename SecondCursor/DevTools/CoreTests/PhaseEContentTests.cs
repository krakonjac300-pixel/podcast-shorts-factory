using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase E content: every achievement, title, Records and pause key the code uses is in the strings the title root
    /// loads (base, plus the full-game strings since Phase G), and the Steam Deck wording variant.
    /// </summary>
    public class PhaseEContentTests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));

        static T Load<T>(string name) where T : new() => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Dir, name + ".json")), Options);

        /// <summary>The strings a full-game title root has: base, then Content/full (Phase G moved Night 2/3 keys there).</summary>
        static Dictionary<string, string> BaseStrings()
        {
            var d = new Dictionary<string, string>();
            foreach (var e in Load<StringTableData>("strings").entries) d[e.key] = e.value;
            string full = Path.Combine(Dir, "full", "strings.json");
            if (File.Exists(full))
                foreach (var e in JsonSerializer.Deserialize<StringTableData>(File.ReadAllText(full), Options).entries) d[e.key] = e.value;
            return d;
        }

        static ContentDatabase Base() => new ContentDatabase(
            Load<StringTableData>("strings"), Load<StoryData>("story"), Load<FileSystemData>("filesystem"),
            Load<EmailsData>("emails"), Load<EmployeesData>("employees"), Load<WorkOrdersData>("workorders"),
            Load<TasksData>("tasks"), Load<DialogueData>("dialogue"));

        [Fact]
        public void EveryAchievementHasANameAndDescription()
        {
            if (!Present) return;
            var s = BaseStrings();
            foreach (var a in AchievementIds.All)
            {
                Assert.True(s.TryGetValue(a.NameKey, out var name) && name.Length > 0, "missing " + a.NameKey);
                Assert.True(s.TryGetValue(a.DescKey, out var desc) && desc.Length > 0, "missing " + a.DescKey);
            }
        }

        public static readonly string[] KeysUsedByCode =
        {
            "title.continue", "title.continue.at", "title.new", "title.new.confirm", "title.select", "title.records", "title.settings",
            "title.credits", "title.quit", "title.tagline", "title.headphones", "title.back", "title.wishlist", "title.yes", "title.no",
            "title.normal", "title.story", "title.difficulty.choose", "title.difficulty.normal.body", "title.difficulty.story.body",
            "select.title", "select.night1", "select.night2", "select.night3", "select.locked", "select.best", "select.nobest", "select.endings",
            "select.replace.confirm", "select.seen",
            "records.title", "records.endings", "records.ending.n3_shred", "records.ending.n3_keep", "records.ending.n3_logoff", "records.unknown",
            "records.stats", "records.tugs", "records.night", "records.notime", "records.achievements", "records.hidden",
            "credits.title", "credits.body", "credits.notices",
            "pause.title", "pause.resume", "pause.quit", "pause.totitle", "pause.totitle.confirm", "pause.restart", "pause.restart.night",
            "pause.yes", "pause.no", "pause.back", "pause.difficulty", "pause.difficulty.note", "pause.difficulty.next", "pause.framerate",
            "pause.framerate.vsync", "os.app.stopped", "pause.textsize", "pause.textsize.normal", "pause.textsize.large",
            "end.card.continue", "end.card.menu", "end.card.select", "end.card.wishlist", "end.card.quit", "end.card.cta", "end.card.thanks",
            "end.n1.title", "end.n1.subtitle",
        };

        [Fact]
        public void TitleRecordsAndPauseKeysAreInTheBaseStrings()
        {
            if (!Present) return;
            var s = BaseStrings();
            var missing = new List<string>();
            foreach (var k in KeysUsedByCode) if (!s.ContainsKey(k)) missing.Add(k);
            Assert.True(missing.Count == 0, "missing base strings: " + string.Join(", ", missing));
            Assert.Equal("Options", s["title.settings"]);
            foreach (var k in new[] { "select.best", "records.tugs", "records.night", "records.achievements", "pause.framerate", "pause.textsize" })
                Assert.Contains("{0}", s[k]);
        }

        [Fact]
        public void TheDeckVariantPrefersItsOwnWording()
        {
            if (!Present) return;
            var db = Base();
            string plain = db.Text("disclaimer.body");
            Assert.Contains("Esc", plain);
            db.Variant = "deck";
            string deck = db.Text("disclaimer.body");
            Assert.DoesNotContain("Esc", deck);
            Assert.Contains("Menu button", deck);
            Assert.DoesNotContain("ight-click", db.Text("quickstart.body"));
            Assert.DoesNotContain("Esc", db.Text("help.body"));
            // Keys without a deck variant fall back to the plain text; an unknown variant changes nothing.
            Assert.Equal("NEXUS OS", db.Text("os.name"));
            db.Variant = "tv";
            Assert.Equal(plain, db.Text("disclaimer.body"));
        }

        [Fact]
        public void DeckHintsReplaceMouseHintsOnTheDeck()
        {
            if (!Present) return;
            var db = Base();
            Assert.Contains("Double-click", db.Task("t_read_briefing").hint);
            db.Variant = "deck";
            Assert.DoesNotContain("click", db.Task("t_read_briefing").hint, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("right-click", db.Task("t_shred_cache").hint, StringComparison.OrdinalIgnoreCase);
            // Tasks without a Deck hint keep theirs (Phase G gave every drag hint a Deck version).
            Assert.Contains("click Approve", db.Task("t_verify_3317").hint);
            Assert.DoesNotContain("drag", db.Task("t_archive_ledger").hint, StringComparison.OrdinalIgnoreCase);
        }
    }
}
