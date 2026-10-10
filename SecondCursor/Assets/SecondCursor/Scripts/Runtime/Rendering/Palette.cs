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
        // Phase Q4 (A8): inactive titles read (the old pale text on a pale bar was 1.9:1; this is 4.5:1 or better).
        public static readonly Color32 TitleInactiveA = new Color32(0x4E, 0x4C, 0x47, 0xFF);
        public static readonly Color32 TitleInactiveB = new Color32(0x6E, 0x6C, 0x66, 0xFF);
        public static readonly Color32 TitleText = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        public static readonly Color32 TitleTextInactive = new Color32(0xF0, 0xEE, 0xE6, 0xFF);
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
        /// <summary>Phase Q4 (A8): darker than before so "ACTIVE" and "THE BAR IS YOURS" pass 4.5:1 on cream (5.1:1).</summary>
        public static readonly Color32 Green = new Color32(0x2F, 0x7A, 0x3B, 0xFF);
        /// <summary>The same green for text and fills on the dark entity panels (the tug's panel).</summary>
        public static readonly Color32 GreenOnDark = new Color32(0x4F, 0xB0, 0x5E, 0xFF);
        /// <summary>Muted text that still passes 4.5:1 on the beige and cream surfaces (the pause note, a tip's title).</summary>
        public static readonly Color32 TextMuted = new Color32(0x4A, 0x48, 0x40, 0xFF);
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
        public static readonly Color32 EntityOutline = FromRgb(Core.Game.ActorStyle.EntityRim);
        public static readonly Color32 EntityFill = FromRgb(Core.Game.ActorStyle.EntityFill);
        /// <summary>Gary, the third pointer: a tired amber outline on a dark brown body (a hand, never an arrow while held).</summary>
        public static readonly Color32 GaryOutline = FromRgb(Core.Game.ActorStyle.GaryRim);
        public static readonly Color32 GaryFill = FromRgb(Core.Game.ActorStyle.GaryFill);

        /// <summary>A 0xRRGGBB colour (the engine-free Core.Game.ActorStyle numbers) as an opaque Color32.</summary>
        public static Color32 FromRgb(uint rgb)
        {
            Core.Game.ActorStyle.Split(rgb, out byte r, out byte g, out byte b);
            return new Color32(r, g, b, 0xFF);
        }

        /// <summary>The colour of a notice kind's stripe (clear for a plain notice).</summary>
        public static Color32 StripeOf(Core.Game.NoticeKind kind)
        {
            uint rgb = Core.Game.ActorStyle.StripeColor(kind);
            return rgb == 0u ? new Color32(0, 0, 0, 0) : FromRgb(rgb);
        }

        public static Color32 WithAlpha(Color32 c, byte a) => new Color32(c.r, c.g, c.b, a);

        public static Color32 Lerp(Color32 a, Color32 b, float t) => Color32.Lerp(a, b, t);
    }
}
