using SecondCursor.Core.Game;
using SecondCursor.Game;

namespace SecondCursor.Input
{
    /// <summary>
    /// Phase Q4 (review board A7): what counts as "open". A double-click, or (with Double-click: Single click) a single click by the player.
    /// The icons, the file rows and the folder rows ask here, so the option changes them all and nothing else (a title bar still needs two
    /// clicks to maximize).
    /// </summary>
    public static class ClickRules
    {
        /// <summary><paramref name="count"/> is the click count the router reports (1 for a single click, 2 for the second of a double-click).</summary>
        public static bool Opens(int count, CursorAgent by)
        {
            if (by != null && by.IsPlayer && AccessOptions.OpensOnSingleClick(AccessSettings.ClickSpeed)) return count == 1;
            return count == 2;
        }
    }
}
