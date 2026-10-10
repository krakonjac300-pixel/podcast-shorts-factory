namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase Q4 (review board R2): who a notice, a closing window or a taskbar button belongs to. The computer speaks in cream
    /// (<see cref="Plain"/>); session 017 is black with a pale rim; session 209 is amber on brown; a deadline notice carries a red stripe.
    /// Colour marks authorship only, never alarm: Security is the building, not a session, so it stays plain.
    /// </summary>
    public enum NoticeKind { Plain = 0, Entity = 1, Gary = 2, Deadline = 3 }

    /// <summary>
    /// The colour language of R2 as plain 0xRRGGBB numbers (engine-free, so a test can pin it). The runtime's Palette builds its
    /// session colours from these, so the two cannot drift apart.
    /// </summary>
    public static class ActorStyle
    {
        /// <summary>Session 017's body and rim.</summary>
        public const uint EntityFill = 0x0B0E0D, EntityRim = 0xE6ECEA;
        /// <summary>Session 209 (Gary): amber outline on a brown body.</summary>
        public const uint GaryFill = 0x2A2418, GaryRim = 0xD8A840;
        /// <summary>The stripe of a deadline notice (the 6:41 and 7:00 rules, a PRIORITY order).</summary>
        public const uint DeadlineStripe = 0xB03328;
        /// <summary>Width of a notice's left stripe in virtual pixels.</summary>
        public const int StripeWidth = 3;

        /// <summary>The actor kind of a pointer: the second cursor is 017, the third (Gary) is 209, the player and the system are plain.</summary>
        public static NoticeKind Of(bool isEntity, bool isGary) => !isEntity ? NoticeKind.Plain : isGary ? NoticeKind.Gary : NoticeKind.Entity;

        /// <summary>True for a session (017 or 209), false for plain and deadline notices.</summary>
        public static bool IsSession(NoticeKind kind) => kind == NoticeKind.Entity || kind == NoticeKind.Gary;

        /// <summary>The stripe's colour, or 0 for a notice with no stripe (plain).</summary>
        public static uint StripeColor(NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Entity: return EntityFill;
                case NoticeKind.Gary: return GaryRim;
                case NoticeKind.Deadline: return DeadlineStripe;
                default: return 0u;
            }
        }

        /// <summary>The 1 px line beside session 017's stripe (a dark stripe on cream needs a pale edge), or 0 for none.</summary>
        public static uint StripeLine(NoticeKind kind) => kind == NoticeKind.Entity ? EntityRim : 0u;

        /// <summary>The colour of the frame of a window zooming into a session's pointer (0 for a plain close).</summary>
        public static uint ZoomFrame(NoticeKind kind)
        {
            switch (kind)
            {
                case NoticeKind.Entity: return EntityRim;
                case NoticeKind.Gary: return GaryRim;
                default: return 0u;
            }
        }

        /// <summary>The bottom edge of a session's taskbar button (2 px in the actor colour), or 0.</summary>
        public static uint TaskbarEdge(NoticeKind kind) => IsSession(kind) ? ZoomFrame(kind) : 0u;

        /// <summary>The red, green and blue bytes of a 0xRRGGBB colour.</summary>
        public static void Split(uint rgb, out byte r, out byte g, out byte b)
        {
            r = (byte)((rgb >> 16) & 0xFF);
            g = (byte)((rgb >> 8) & 0xFF);
            b = (byte)(rgb & 0xFF);
        }
    }
}
