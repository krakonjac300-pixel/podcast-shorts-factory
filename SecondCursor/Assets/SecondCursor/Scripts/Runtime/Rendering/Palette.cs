using UnityEngine;

namespace SecondCursor.Rendering
{
    /// <summary>
    /// NEXUS OS colour scheme: warm beige-grey 90s chrome, a deep institutional teal for focus and
    /// selection, and a dark green-slate desktop. Tweak here; everything reads from these.
    /// </summary>
    public static class Palette
    {
        // Chrome
        public static readonly Color32 Face = new Color32(0xC2, 0xBF, 0xB2, 0xFF);
        public static readonly Color32 Highlight = new Color32(0xF7, 0xF5, 0xEE, 0xFF);
        public static readonly Color32 Midlight = new Color32(0xDD, 0xDA, 0xD0, 0xFF);
        public static readonly Color32 Shadow = new Color32(0x86, 0x83, 0x7A, 0xFF);
        public static readonly Color32 Dark = new Color32(0x1C, 0x1B, 0x18, 0xFF);
        public static readonly Color32 Window = new Color32(0xFB, 0xFA, 0xF5, 0xFF);
        public static readonly Color32 Text = new Color32(0x14, 0x13, 0x11, 0xFF);
        public static readonly Color32 TextDisabled = new Color32(0x8A, 0x87, 0x7E, 0xFF);
        public static readonly Color32 TextEmboss = new Color32(0xF7, 0xF5, 0xEE, 0xFF);

        // Focus / selection
        public static readonly Color32 TitleActiveA = new Color32(0x18, 0x3F, 0x3A, 0xFF);
        public static readonly Color32 TitleActiveB = new Color32(0x3E, 0x7A, 0x6E, 0xFF);
        public static readonly Color32 TitleInactiveA = new Color32(0x6E, 0x6C, 0x66, 0xFF);
        public static readonly Color32 TitleInactiveB = new Color32(0x9C, 0x99, 0x8F, 0xFF);
        public static readonly Color32 TitleText = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly Color32 TitleTextInactive = new Color32(0xD4, 0xD1, 0xC6, 0xFF);
        public static readonly Color32 Selection = new Color32(0x18, 0x3F, 0x3A, 0xFF);
        public static readonly Color32 SelectionText = new Color32(0xF7, 0xF5, 0xEE, 0xFF);
        public static readonly Color32 SelectionInactive = new Color32(0xB0, 0xAD, 0xA2, 0xFF);

        // Desktop
        public static readonly Color32 DesktopA = new Color32(0x2B, 0x3D, 0x3A, 0xFF);
        public static readonly Color32 DesktopB = new Color32(0x24, 0x33, 0x30, 0xFF);
        public static readonly Color32 DesktopLabel = new Color32(0xF2, 0xF0, 0xE8, 0xFF);
        public static readonly Color32 DesktopLabelShadow = new Color32(0x0E, 0x14, 0x13, 0xFF);

        // Accents
        public static readonly Color32 Red = new Color32(0xB0, 0x33, 0x28, 0xFF);
        public static readonly Color32 Amber = new Color32(0xC9, 0x9A, 0x2E, 0xFF);
        public static readonly Color32 Green = new Color32(0x3E, 0x8A, 0x4A, 0xFF);
        public static readonly Color32 Link = new Color32(0x1F, 0x3E, 0x6B, 0xFF);
        public static readonly Color32 Tooltip = new Color32(0xFF, 0xFB, 0xDC, 0xFF);

        // Boot / terminal
        public static readonly Color32 Black = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color32 BiosText = new Color32(0xB8, 0xB8, 0xB0, 0xFF);
        public static readonly Color32 BiosBright = new Color32(0xFF, 0xFF, 0xF4, 0xFF);
        public static readonly Color32 EntityText = new Color32(0xE8, 0xE6, 0xDF, 0xFF);

        // Cursors
        public static readonly Color32 CursorOutline = new Color32(0x0A, 0x0A, 0x0A, 0xFF);
        public static readonly Color32 CursorFill = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        /// <summary>The second cursor is an inverted, slightly cold copy: dark body, pale outline.</summary>
        public static readonly Color32 EntityOutline = new Color32(0xE6, 0xEC, 0xEA, 0xFF);
        public static readonly Color32 EntityFill = new Color32(0x0B, 0x0E, 0x0D, 0xFF);

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
    }
}
