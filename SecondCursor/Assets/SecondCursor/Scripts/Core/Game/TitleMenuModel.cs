using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Game
{
    /// <summary>The title menu's buttons, top to bottom.</summary>
    public enum TitleItem { Continue, NewGame, NightSelect, Records, Options, Credits, Wishlist, Quit }

    /// <summary>
    /// Which title items a save shows (expansion spec 8.1): a first launch has New Game, Options, Credits and Quit;
    /// Continue appears with progress, Night Select once Night 2 is unlocked, Records once there is anything to show.
    /// The demo has no Night Select or Records, and a Wishlist button when the store page can be opened.
    /// </summary>
    public static class TitleMenuModel
    {
        public static List<TitleItem> Items(SaveData d, bool demo, bool storeAvailable)
        {
            d = d ?? new SaveData();
            int maxNight = demo ? 1 : SaveData.Nights;
            var items = new List<TitleItem>();
            if (d.ContinueTarget(maxNight) != null) items.Add(TitleItem.Continue);
            items.Add(TitleItem.NewGame);
            if (!demo && d.nightUnlocked >= 2) items.Add(TitleItem.NightSelect);
            if (!demo && d.HasRecords) items.Add(TitleItem.Records);
            items.Add(TitleItem.Options);
            items.Add(TitleItem.Credits);
            if (demo && storeAvailable) items.Add(TitleItem.Wishlist);
            items.Add(TitleItem.Quit);
            return items;
        }

        /// <summary>Continue when it exists; else Night Select when the game is finished; else New Game.</summary>
        public static TitleItem DefaultFocus(IList<TitleItem> items, SaveData d = null)
        {
            if (items == null || items.Count == 0) return TitleItem.NewGame;
            if (items.Contains(TitleItem.Continue)) return TitleItem.Continue;
            bool finished = d != null && d.lastCompletedNight >= SaveData.Nights;
            if (finished && items.Contains(TitleItem.NightSelect)) return TitleItem.NightSelect;
            return items.Contains(TitleItem.NewGame) ? TitleItem.NewGame : items[0];
        }

        /// <summary>"0:00" for none, "12:05", or "1:02:11" past an hour.</summary>
        public static string FormatDuration(float seconds)
        {
            int total = (int)Math.Round(Math.Max(0f, seconds));
            int h = total / 3600, m = total / 60 % 60, s = total % 60;
            return h > 0 ? h + ":" + m.ToString("00") + ":" + s.ToString("00") : m + ":" + s.ToString("00");
        }
    }
}
