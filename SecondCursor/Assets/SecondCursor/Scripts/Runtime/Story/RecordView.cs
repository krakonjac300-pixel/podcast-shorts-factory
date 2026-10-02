using System.Collections.Generic;
using SecondCursor.Core.Game;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q2 (board A T2, C V5): draws the Retention Record. <see cref="CardRows"/> puts the four rows under a night's card (label
    /// right-aligned left of the centre, value left-aligned right of it); <see cref="Page"/> fills the screen with the whole record in
    /// the BIOS colours, one row per line with a dotted leader, sized to be one screenshot (960 x 540).
    /// </summary>
    public static class RecordView
    {
        static readonly Color32 Label = new Color32(0x8A, 0x8A, 0x84, 0xFF);
        const int RowStep = 12, PageRowStep = 16;

        /// <summary>The rows under a card from <paramref name="y"/> (top, virtual px). Returns the y under the last row.</summary>
        public static int CardRows(RectTransform parent, IList<RecordRow> rows, int y)
        {
            if (rows == null) return y;
            int mid = ScreenRig.Width / 2;
            foreach (var r in rows)
            {
                var label = UIBuilder.Text(parent, r.Label, Label);
                label.rectTransform.At(0, y, mid - 8, 12);
                label.Align = TextAlign.Right;
                var value = UIBuilder.Text(parent, r.Value, Palette.BiosText);
                value.rectTransform.At(mid + 8, y, mid - 16, 12);
                value.Align = TextAlign.Left;
                y += RowStep;
            }
            return y;
        }

        /// <summary>The full page on <paramref name="parent"/> (stretched over it); the caller adds the button under it.</summary>
        public static RectTransform Page(RectTransform parent, IList<RecordRow> rows, System.Func<string, string> text)
        {
            var page = UIBuilder.Rect("Retention Record", parent).Stretch();
            const int left = 150, right = 810, valueX = 470;
            Line(page, text("record.company"), Palette.BiosText, left, 56, right - left, TextAlign.Left, false);
            Line(page, text("record.title"), Palette.BiosBright, left, 56, right - left, TextAlign.Right, true);
            Line(page, text("record.subject"), Palette.BiosText, left, 72, right - left, TextAlign.Left, false);
            Line(page, text("record.dates"), Palette.BiosText, left, 72, right - left, TextAlign.Right, false);
            Rule(page, left, 90, right - left);
            int y = 100;
            foreach (var r in rows ?? new List<RecordRow>())
            {
                if (r.Label.Length == 0 && r.Value.Length == 0)
                {
                    Rule(page, left, y + 6, right - left);
                    y += PageRowStep;
                    continue;
                }
                if (r.Label.Length > 0)
                {
                    Line(page, r.Label, Palette.BiosText, left, y, valueX - left, TextAlign.Left, false);
                    int start = left + PixelFont.MeasureLine(r.Label + " ", false, 1);
                    Line(page, Dots(valueX - 8 - start), Label, start, y, valueX - start, TextAlign.Left, false);
                }
                Line(page, r.Value, Palette.BiosBright, r.Label.Length > 0 ? valueX : left + 24, y, right - valueX, TextAlign.Left, false);
                y += PageRowStep;
            }
            Rule(page, left, y + 4, right - left);
            Line(page, "SECOND CURSOR", Palette.BiosBright, left, y + 14, right - left, TextAlign.Right, true);
            return page;
        }

        /// <summary>As many dots as fit in <paramref name="width"/> pixels (the leader from a label to its value).</summary>
        static string Dots(int width)
        {
            // Measured over ten dots: one dot alone does not include the spacing between dots.
            float dot = Mathf.Max(1f, PixelFont.MeasureLine("..........", false, 1) / 10f);
            return new string('.', Mathf.Max(0, Mathf.FloorToInt(width / dot)));
        }

        static void Line(RectTransform parent, string s, Color32 color, int x, int y, int w, TextAlign align, bool bold)
        {
            var t = UIBuilder.Text(parent, s, color, bold);
            t.rectTransform.At(x, y, w, 12);
            t.Align = align;
        }

        static void Rule(RectTransform parent, int x, int y, int w)
        {
            var r = UIBuilder.Solid(parent, new Color32(0x5A, 0x5A, 0x56, 0xFF), "Rule");
            r.raycastTarget = false;
            r.rectTransform.At(x, y, w, 1);
        }
    }
}
