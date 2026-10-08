using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using Xunit;

namespace SecondCursor.Tests
{
    public class StoryClarityTests
    {
        static readonly string Content = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));

        static string Text(string folder, string key)
        {
            // Missing fixtures fail: these checks must not silently pass without content.
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, folder, "strings.json")));
            return doc.RootElement.GetProperty("entries").EnumerateArray()
                .Single(e => e.GetProperty("key").GetString() == key).GetProperty("value").GetString();
        }

        [Theory]
        [InlineData(ContentIds.EndingN1Blackout, EndingResultKind.ChapterComplete)]
        [InlineData(ContentIds.EndingN2Finished, EndingResultKind.ChapterComplete)]
        [InlineData(ContentIds.EndingN2Kept, EndingResultKind.ChapterComplete)]
        [InlineData(ContentIds.EndingN3Keep, EndingResultKind.Lost)]
        [InlineData(ContentIds.EndingN3Shred, EndingResultKind.Lost)]
        [InlineData(ContentIds.EndingN3LogOff, EndingResultKind.Escaped)]
        [InlineData(EndingResult.CameraCapture, EndingResultKind.Lost)]
        public void ResultsDescribeThePlayerNotTheFile(string id, EndingResultKind expected)
        {
            Assert.Equal(expected, EndingResult.For(id));
            string folder = id == ContentIds.EndingN1Blackout || id == EndingResult.CameraCapture ? "" : "full";
            Assert.False(string.IsNullOrWhiteSpace(Text(folder, EndingResult.ExplanationKey(id))));
            string headingFolder = expected == EndingResultKind.Escaped ? "full" : "";
            Assert.False(string.IsNullOrWhiteSpace(Text(headingFolder, EndingResult.HeadingKey(id))));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-an-ending")]
        public void UnknownIdsCannotClaimAVictory(string id)
        {
            Assert.Equal(EndingResultKind.Unknown, EndingResult.For(id));
            Assert.Null(EndingResult.HeadingKey(id));
            Assert.Null(EndingResult.ExplanationKey(id));
        }

        [Fact]
        public void LosingOneFileContestIsNotAGameOver()
        {
            Assert.Equal("FILE CONTEST LOST", Text("", "contest.card.lost"));
            Assert.Equal("FILE CONTEST WON", Text("", "contest.card.won"));
            Assert.Equal("YOU LOST", Text("", "result.lost"));
            Assert.Contains("not a game over", Text("", "result.n1_blackout"));
        }

        [Theory]
        [InlineData(-1f)]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(30f)]
        public void SharedCursorScenesKeepYouIdentified(float left)
        {
            Assert.Equal(1f, PointerTagRules.VisibleYouAlpha(left, true));
            Assert.Equal(PointerTagRules.YouAlpha(left), PointerTagRules.VisibleYouAlpha(left, false));
        }

        [Fact]
        public void LaterHandoverEntriesRequireTheRestrictedFolder()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, "filesystem.json")));
            var files = doc.RootElement.GetProperty("files").EnumerateArray().ToArray();
            var early = files.Single(f => f.GetProperty("id").GetString() == "prev_operator_notes");
            var later = files.Single(f => f.GetProperty("id").GetString() == "prev_operator_continuation");
            Assert.Equal("documents", early.GetProperty("folder").GetString());
            Assert.DoesNotContain("stopping me, not fighting me", early.GetProperty("content").GetString());
            Assert.DoesNotContain("Something dragged it back", early.GetProperty("content").GetString());
            Assert.Contains("handover_recovered.txt", early.GetProperty("content").GetString());
            Assert.Contains("stopping me, not fighting me", later.GetProperty("content").GetString());
            var data = JsonSerializer.Deserialize<FileSystemData>(doc.RootElement.GetRawText(),
                new JsonSerializerOptions { IncludeFields = true });
            var fs = new SecondCursor.Core.FileSystem.VirtualFileSystem(data);
            Assert.True(fs.IsInsideLocked(later.GetProperty("folder").GetString()));
            fs.SetFolderLocked("restricted", false);
            Assert.False(fs.IsInsideLocked(later.GetProperty("folder").GetString()));
        }

        [Fact]
        public void ClickLockHelpDescribesTheInitialHeldDrag()
        {
            foreach (var variant in new[] { "", ".deck" })
            {
                string help = Text("", "help.body" + variant);
                Assert.Contains("held for half a second", help);
                Assert.Contains("again to drop", help);
                Assert.DoesNotContain("once to pick up", help);
            }
        }

        [Fact]
        public void OrientationIdentifiesTheOperatorAndAssistance()
        {
            foreach (var variant in new[] { "", ".deck" })
            {
                string intro = Text("", "quickstart.body" + variant);
                Assert.Contains("Casey Rourke", intro);
                Assert.Contains("WS-04", intro);
                Assert.Contains("WHITE arrow", intro);
                Assert.DoesNotContain("own intentions", intro);
                Assert.DoesNotContain("previous session", intro);
                Assert.Contains("accept its offer", intro);
            }
            Assert.Contains("YOU", Text("", "camera.operator.you"));
            Assert.Contains("EMPTY CHAIR", Text("", "camera.operator.empty"));
            Assert.Contains("Your Work Queue will record who completed it", Text("", "assist.offer.body"));
        }
    }
}
