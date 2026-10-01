using System;
using System.Collections.Generic;
using System.Text;

namespace SecondCursor.Core.Art
{
    /// <summary>
    /// Phase Q4 (CH7): the pixel font's word wrap, engine-free and in one pass. The old version built every candidate line by string
    /// concatenation and measured the whole candidate again for each word, so its cost grew with the square of a paragraph's word count and it
    /// ran twice per typed character in a Jotter. This version keeps a running pixel width and builds each line once; its results are the same
    /// lines as before (a test replays the old algorithm against it on many strings).
    /// </summary>
    public static class TextWrap
    {
        [ThreadStatic] static StringBuilder _line;

        /// <summary>
        /// Width of a line of <paramref name="advanceSum"/> pen advances at <paramref name="scale"/>: the sum less the trailing letter spacing,
        /// scaled and rounded up to whole pixels (what <c>PixelFont.MeasureLine</c> returns).
        /// </summary>
        public static int LineWidth(int advanceSum, float scale)
            => (int)Math.Ceiling((advanceSum - PixelFontData.LetterSpacing) * Math.Max(1f, scale) - 0.001f);

        /// <summary>Total pen advance of a string (newlines are not drawn and take no room).</summary>
        public static int AdvanceSum(string s, Func<char, int> advance)
        {
            int sum = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\r' || c == '\n') continue;
                sum += advance(c);
            }
            return sum;
        }

        /// <summary>
        /// Splits on newlines and word-wraps to <paramref name="maxWidth"/> pixels (already scaled; 0 or less disables wrapping). A word longer than a
        /// whole line is broken. <paramref name="advance"/> is the pen advance of one character, letter spacing included.
        /// </summary>
        public static List<string> Wrap(string text, int maxWidth, float scale, Func<char, int> advance, List<string> into = null)
        {
            var lines = into ?? new List<string>();
            lines.Clear();
            if (text == null) return lines;
            scale = Math.Max(1f, scale);
            var cur = _line ?? (_line = new StringBuilder(128));
            int spaceAdvance = advance(' ');
            var paragraphs = text.Replace("\r", "").Split('\n');
            foreach (var para in paragraphs)
            {
                if (maxWidth <= 0 || LineWidth(AdvanceSum(para, advance), scale) <= maxWidth)
                {
                    lines.Add(para);
                    continue;
                }
                cur.Length = 0;
                int curSum = 0;
                foreach (var word in para.Split(' '))
                {
                    int wordSum = AdvanceSum(word, advance);
                    int candidate = cur.Length == 0 ? wordSum : curSum + spaceAdvance + wordSum;
                    if (LineWidth(candidate, scale) <= maxWidth)
                    {
                        if (cur.Length > 0) cur.Append(' ');
                        cur.Append(word);
                        curSum = candidate;
                        continue;
                    }
                    if (cur.Length > 0) lines.Add(cur.ToString());
                    // Break words longer than a whole line.
                    string rest = word;
                    while (LineWidth(AdvanceSum(rest, advance), scale) > maxWidth && rest.Length > 1)
                    {
                        int n = rest.Length - 1;
                        while (n > 1 && LineWidth(AdvanceSum(rest.Substring(0, n), advance), scale) > maxWidth) n--;
                        lines.Add(rest.Substring(0, n));
                        rest = rest.Substring(n);
                    }
                    cur.Length = 0;
                    cur.Append(rest);
                    curSum = AdvanceSum(rest, advance);
                }
                lines.Add(cur.ToString());
            }
            return lines;
        }
    }
}
