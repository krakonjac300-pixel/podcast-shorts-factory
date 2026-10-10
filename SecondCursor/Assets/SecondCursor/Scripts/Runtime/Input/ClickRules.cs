using SecondCursor.Core.Game;
using SecondCursor.Game;
using System.Text.RegularExpressions;

namespace SecondCursor.Input
{
    /// <summary>
    /// Phase Q4 (review board A7): what counts as "open". A double-click, or (with Double-click: Single click) a single click by the player.
    /// The icons, the file rows and the folder rows ask here, so the option changes them all and nothing else (a title bar still needs two
    /// clicks to maximize).
    /// </summary>
    public static class ClickRules
    {
        /// <summary>Keep player instructions consistent with the current open gesture.</summary>
        public static string Instructions(string text)
        {
            if (string.IsNullOrEmpty(text) || !AccessOptions.OpensOnSingleClick(AccessSettings.ClickSpeed)) return text;
            string adjusted = Regex.Replace(text,
                @"\bdouble-click (?=(?:an?|the|this|that|it|Workstation|Camera Viewer|Mail|File Manager|Jotter|Personnel|Disposal|Notepad)\b|[""']?[\w./\\-]+\.[a-z0-9]{1,8}\b)",
                match => match.Value == match.Value.ToUpperInvariant() ? "CLICK " : char.IsUpper(match.Value[0]) ? "Click " : "click ",
                RegexOptions.IgnoreCase);
            return adjusted.Replace("Press A twice", "Press A once").Replace("press A twice", "press A once")
                .Replace("tap it twice", "tap it once");
        }

        /// <summary><paramref name="count"/> is the click count the router reports (1 for a single click, 2 for the second of a double-click).</summary>
        public static bool Opens(int count, CursorAgent by)
        {
            if (by != null && by.IsPlayer && AccessOptions.OpensOnSingleClick(AccessSettings.ClickSpeed)) return count == 1;
            return count == 2;
        }
    }
}
