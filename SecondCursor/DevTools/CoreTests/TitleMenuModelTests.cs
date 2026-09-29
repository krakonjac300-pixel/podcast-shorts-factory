using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>The title menu's items for each save state (expansion spec 8.1, Phase E plan item 4).</summary>
    public class TitleMenuModelTests
    {
        [Fact]
        public void AFirstLaunchShowsNewGameOptionsCreditsQuit()
        {
            var items = TitleMenuModel.Items(new SaveData(), false, false);
            Assert.Equal(new[] { TitleItem.NewGame, TitleItem.Options, TitleItem.Credits, TitleItem.Quit }, items);
            Assert.Equal(TitleItem.NewGame, TitleMenuModel.DefaultFocus(items));
        }

        [Fact]
        public void MidNightTwoShowsContinueNightSelectAndRecords()
        {
            var d = new SaveData { nightUnlocked = 2, currentNight = 2, endingsSeen = new[] { "n1_blackout" } };
            d.SetCheckpoint(new Checkpoint { valid = true, night = 2, beat = "asks" });
            var items = TitleMenuModel.Items(d, false, false);
            Assert.Equal(new[] { TitleItem.Continue, TitleItem.NewGame, TitleItem.NightSelect, TitleItem.Records, TitleItem.Options, TitleItem.Credits, TitleItem.Quit }, items);
            Assert.Equal(TitleItem.Continue, TitleMenuModel.DefaultFocus(items, d));
        }

        [Fact]
        public void AFinishedGameFocusesNightSelect()
        {
            var d = new SaveData { nightUnlocked = 4, currentNight = 3, lastCompletedNight = 3, endingsSeen = new[] { "n3_keep" } };
            var items = TitleMenuModel.Items(d, false, true);
            Assert.DoesNotContain(TitleItem.Continue, items);
            Assert.DoesNotContain(TitleItem.Wishlist, items);
            Assert.Contains(TitleItem.NightSelect, items);
            Assert.Equal(TitleItem.NightSelect, TitleMenuModel.DefaultFocus(items, d));
        }

        [Fact]
        public void TheDemoHasNoNightSelectOrRecordsAndAWishlistWhenTheStoreOpens()
        {
            var d = new SaveData();
            d.RecordNightStart(1, new FlagSnapshot());
            var before = TitleMenuModel.Items(d, true, false);
            Assert.Equal(new[] { TitleItem.Continue, TitleItem.NewGame, TitleItem.Options, TitleItem.Credits, TitleItem.Quit }, before);

            d.RecordNightComplete(new NightResult { Night = 1, EndingId = "n1_blackout" });
            var after = TitleMenuModel.Items(d, true, true);
            Assert.Equal(new[] { TitleItem.NewGame, TitleItem.Options, TitleItem.Credits, TitleItem.Wishlist, TitleItem.Quit }, after);
        }

        [Fact]
        public void DurationsFormatLikeAClock()
        {
            Assert.Equal("0:00", TitleMenuModel.FormatDuration(0f));
            Assert.Equal("12:05", TitleMenuModel.FormatDuration(725f));
            Assert.Equal("1:02:11", TitleMenuModel.FormatDuration(3731f));
            Assert.Equal("0:00", TitleMenuModel.FormatDuration(-5f));
        }
    }
}
