using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SecondCursor.Core.Art;
using SecondCursor.Core.Util;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase Q4 (code health CH7, CH8): the one-pass word wrap gives the lines the old one did, and the background file writer coalesces,
    /// keeps order, keeps the backup, flushes and reports a failure without throwing.
    /// </summary>
    public class PhaseQ4PerfTests
    {
        // ------------------------------------------------------------------ CH7: the word wrap

        /// <summary>The pen advance the runtime font uses: a space is a fixed advance, a tab four of them, any other glyph its width and the spacing.</summary>
        static int Advance(char c, bool bold)
        {
            if (c == ' ') return PixelFontData.SpaceAdvance + (bold ? 1 : 0);
            if (c == '\t') return (PixelFontData.SpaceAdvance + 1) * 4;
            return PixelFontData.GetWidth(c) + PixelFontData.LetterSpacing + (bold ? 1 : 0);
        }

        static int MeasureLine(string line, bool bold, float scale)
        {
            if (string.IsNullOrEmpty(line)) return 0;
            int w = 0;
            foreach (char c in line)
            {
                if (c == '\r' || c == '\n') continue;
                w += Advance(c, bold);
            }
            return (int)Math.Ceiling((w - PixelFontData.LetterSpacing) * Math.Max(1f, scale) - 0.001f);
        }

        /// <summary>The algorithm as it was before Phase Q4 (candidate lines by concatenation, the whole candidate measured for each word).</summary>
        static List<string> OldWrap(string text, int maxWidth, bool bold, float scale)
        {
            var lines = new List<string>();
            if (text == null) return lines;
            scale = Math.Max(1f, scale);
            foreach (var para in text.Replace("\r", "").Split('\n'))
            {
                if (maxWidth <= 0 || MeasureLine(para, bold, scale) <= maxWidth)
                {
                    lines.Add(para);
                    continue;
                }
                string cur = "";
                foreach (var word in para.Split(' '))
                {
                    string candidate = cur.Length == 0 ? word : cur + " " + word;
                    if (MeasureLine(candidate, bold, scale) <= maxWidth)
                    {
                        cur = candidate;
                        continue;
                    }
                    if (cur.Length > 0) lines.Add(cur);
                    string rest = word;
                    while (MeasureLine(rest, bold, scale) > maxWidth && rest.Length > 1)
                    {
                        int n = rest.Length - 1;
                        while (n > 1 && MeasureLine(rest.Substring(0, n), bold, scale) > maxWidth) n--;
                        lines.Add(rest.Substring(0, n));
                        rest = rest.Substring(n);
                    }
                    cur = rest;
                }
                lines.Add(cur);
            }
            return lines;
        }

        static string RandomText(Random r)
        {
            const string letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.,:;-_/()WMiIl";
            var sb = new System.Text.StringBuilder();
            int words = r.Next(0, 60);
            for (int i = 0; i < words; i++)
            {
                int len = r.Next(0, 16);
                if (r.Next(25) == 0) len = r.Next(30, 70);   // a word longer than most lines
                for (int k = 0; k < len; k++) sb.Append(letters[r.Next(letters.Length)]);
                int gap = r.Next(12);
                sb.Append(gap == 0 ? "  " : gap == 1 ? "\n" : gap == 2 ? "\n\n" : gap == 3 ? "\r\n" : " ");
            }
            return sb.ToString();
        }

        [Fact]
        public void TheOnePassWrapMakesTheSameLinesAsTheOldOne()
        {
            var r = new Random(1998);
            int checks = 0;
            for (int i = 0; i < 50; i++)
            {
                string text = RandomText(r);
                foreach (int width in new[] { 0, 12, 97, 240, 600 })
                    foreach (float scale in new[] { 1f, 1.5f, 2f })
                        foreach (bool bold in new[] { false, true })
                        {
                            var expected = OldWrap(text, width, bold, scale);
                            var actual = TextWrap.Wrap(text, width, scale, c => Advance(c, bold));
                            Assert.Equal(expected, actual);
                            checks++;
                        }
            }
            Assert.Equal(50 * 5 * 3 * 2, checks);
        }

        [Fact]
        public void TheWrapEdgesAreTheSame()
        {
            foreach (var text in new[] { null, "", " ", "  ", "\n", "a", "a b", "a  b", " a", "a ", "supercalifragilisticexpialidocious and more", "x\n\ny", "WWWWWWWWWWWWWWWWWWWWWWWWWWWWWWWW" })
                foreach (int width in new[] { 0, 5, 30, 100 })
                {
                    var expected = OldWrap(text, width, false, 1f);
                    var actual = TextWrap.Wrap(text, width, 1f, c => Advance(c, false));
                    Assert.Equal(expected, actual);
                }
        }

        [Fact]
        public void TheWrapReusesTheListItIsGivenAndClearsIt()
        {
            var list = new List<string> { "old", "lines" };
            var result = TextWrap.Wrap("one two three four five six", 40, 1f, c => Advance(c, false), list);
            Assert.Same(list, result);
            Assert.DoesNotContain("old", result);
            Assert.True(result.Count > 1);
        }

        [Fact]
        public void LineWidthIsTheSumLessTheSpacingScaledAndRoundedUp()
        {
            Assert.Equal(0 - 1, TextWrap.LineWidth(0, 1f));
            Assert.Equal(9, TextWrap.LineWidth(10, 1f));
            Assert.Equal(18, TextWrap.LineWidth(10, 2f));
            Assert.Equal(14, TextWrap.LineWidth(10, 1.5f));   // 13.5 rounds up
        }

        // ------------------------------------------------------------------ CH8: the background writer

        static string TempDir()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "writer_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void ManyWritesToOneFileLeaveTheLastAndAreCoalesced()
        {
            string dir = TempDir();
            try
            {
                var w = new BackgroundFileWriter();
                string path = Path.Combine(dir, "progress.json");
                for (int i = 0; i < 300; i++) w.Enqueue(path, "version " + i);
                Assert.True(w.Flush());
                Assert.Equal("version 299", File.ReadAllText(path));
                Assert.True(w.WritesDone >= 1 && w.WritesDone <= 300);
                Assert.True(w.IsIdle);
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void EachFileGetsItsOwnLatestTextAndTheBackupKeepsThePreviousFile()
        {
            string dir = TempDir();
            try
            {
                var w = new BackgroundFileWriter();
                string a = Path.Combine(dir, "a.json"), b = Path.Combine(dir, "b.json");
                w.Enqueue(a, "a1");
                Assert.True(w.Flush());
                w.Enqueue(a, "a2");
                w.Enqueue(b, "b1");
                Assert.True(w.Flush());
                Assert.Equal("a2", File.ReadAllText(a));
                Assert.Equal("a1", File.ReadAllText(a + ".bak"));
                Assert.Equal("b1", File.ReadAllText(b));
                Assert.False(File.Exists(a + ".tmp"));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void AFolderThatDoesNotExistIsCreated()
        {
            string dir = TempDir();
            try
            {
                var w = new BackgroundFileWriter();
                string path = Path.Combine(dir, "deep", "er", "settings.json");
                w.Enqueue(path, "{}");
                Assert.True(w.Flush());
                Assert.Equal("{}", File.ReadAllText(path));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void AFailedWriteIsReportedNotThrownAndTheWriterGoesOn()
        {
            string dir = TempDir();
            try
            {
                var w = new BackgroundFileWriter();
                string blocker = Path.Combine(dir, "blocker");
                File.WriteAllText(blocker, "a file where a folder is needed");
                w.Enqueue(Path.Combine(blocker, "inside.json"), "x");
                string good = Path.Combine(dir, "good.json");
                w.Enqueue(good, "ok");
                Assert.True(w.Flush());
                var messages = w.TakeMessages();
                Assert.NotNull(messages);
                Assert.Single(messages);
                Assert.Contains("inside.json", messages[0]);
                Assert.Equal("ok", File.ReadAllText(good));
                Assert.Null(w.TakeMessages());
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void FlushWithNothingPendingReturnsAtOnce()
        {
            var w = new BackgroundFileWriter();
            Assert.True(w.IsIdle);
            Assert.True(w.Flush(10));
            Assert.Null(w.TakeMessages());
        }
    }
}
