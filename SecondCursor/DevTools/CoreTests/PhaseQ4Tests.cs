using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.Game;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>
    /// Phase Q4 (readability and access): the actor colour language (R2), the deadline chip and the taskbar plan (R3, R10), readable notice
    /// durations and the Recent notices list (A3), relaxed timing (A4), sound captions (A6), the access options (A7, A9, A10), the scanline
    /// period (R6), and the strings and content they need.
    /// </summary>
    public class PhaseQ4Tests
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Assets/SecondCursor/Resources/Content"));
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        static bool Present => File.Exists(Path.Combine(Dir, "strings.json"));
        static readonly char[] LongDashes = { (char)0x2014, (char)0x2013 };

        static T Read<T>(string folder, string name) where T : class
        {
            string path = Path.Combine(Dir, folder, name + ".json");
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : null;
        }

        static ContentPack Pack(string folder) => new ContentPack
        {
            Strings = Read<StringTableData>(folder, "strings"), Story = Read<StoryData>(folder, "story"),
            FileSystem = Read<FileSystemData>(folder, "filesystem"), Emails = Read<EmailsData>(folder, "emails"),
            Employees = Read<EmployeesData>(folder, "employees"), WorkOrders = Read<WorkOrdersData>(folder, "workorders"),
            Tasks = Read<TasksData>(folder, "tasks"), Dialogue = Read<DialogueData>(folder, "dialogue"),
        };

        static ContentDatabase Demo() => Pack("").Build();

        static ContentDatabase Night3() => Pack("").Overlay(Pack("full")).Overlay(Pack("night2")).Overlay(Pack("night3")).Build();

        // ------------------------------------------------------------------ R2: one colour per actor

        [Fact]
        public void TheActorColoursAreTheBoardsAndNeverAlarms()
        {
            Assert.Equal(0x0B0E0Du, ActorStyle.EntityFill);
            Assert.Equal(0xE6ECEAu, ActorStyle.EntityRim);
            Assert.Equal(0x2A2418u, ActorStyle.GaryFill);
            Assert.Equal(0xD8A840u, ActorStyle.GaryRim);
            Assert.Equal(0u, ActorStyle.StripeColor(NoticeKind.Plain));
            Assert.Equal(ActorStyle.EntityFill, ActorStyle.StripeColor(NoticeKind.Entity));
            Assert.Equal(ActorStyle.EntityRim, ActorStyle.StripeLine(NoticeKind.Entity));
            Assert.Equal(0u, ActorStyle.StripeLine(NoticeKind.Gary));
            Assert.Equal(ActorStyle.GaryRim, ActorStyle.StripeColor(NoticeKind.Gary));
            Assert.Equal(0xB03328u, ActorStyle.StripeColor(NoticeKind.Deadline));
            // Only a session colours a closing window or a taskbar button: a deadline is a stripe, never a window frame.
            Assert.Equal(ActorStyle.EntityRim, ActorStyle.ZoomFrame(NoticeKind.Entity));
            Assert.Equal(ActorStyle.GaryRim, ActorStyle.ZoomFrame(NoticeKind.Gary));
            Assert.Equal(0u, ActorStyle.ZoomFrame(NoticeKind.Plain));
            Assert.Equal(0u, ActorStyle.ZoomFrame(NoticeKind.Deadline));
            Assert.Equal(0u, ActorStyle.TaskbarEdge(NoticeKind.Deadline));
            Assert.Equal(ActorStyle.GaryRim, ActorStyle.TaskbarEdge(NoticeKind.Gary));
        }

        [Fact]
        public void APointerMapsToItsSession()
        {
            Assert.Equal(NoticeKind.Plain, ActorStyle.Of(false, false));
            Assert.Equal(NoticeKind.Entity, ActorStyle.Of(true, false));
            Assert.Equal(NoticeKind.Gary, ActorStyle.Of(true, true));
            Assert.True(ActorStyle.IsSession(NoticeKind.Entity));
            Assert.True(ActorStyle.IsSession(NoticeKind.Gary));
            Assert.False(ActorStyle.IsSession(NoticeKind.Deadline));
            Assert.False(ActorStyle.IsSession(NoticeKind.Plain));
        }

        [Fact]
        public void ARgbNumberSplitsIntoBytes()
        {
            ActorStyle.Split(0xD8A840u, out byte r, out byte g, out byte b);
            Assert.Equal((byte)0xD8, r);
            Assert.Equal((byte)0xA8, g);
            Assert.Equal((byte)0x40, b);
        }

        // ------------------------------------------------------------------ A3: notices that can be read

        [Theory]
        [InlineData(0, 7f)]
        [InlineData(20, 7f)]
        [InlineData(65, 9.4166665f)]
        [InlineData(177, 18.75f)]
        [InlineData(160, 17.333334f)]
        [InlineData(400, 20f)]
        public void ANoticeStaysAsLongAsItTakesToRead(int characters, float seconds)
        {
            Assert.Equal(seconds, NoticeRules.Duration(characters, NoticeTime.Normal), 3);
        }

        [Fact]
        public void LongIsHalfAgainAndUntilClickedIsForever()
        {
            Assert.Equal(10.5f, NoticeRules.Duration(0, NoticeTime.Long), 3);
            Assert.Equal(28.125f, NoticeRules.Duration(177, NoticeTime.Long), 3);
            Assert.Equal(30f, NoticeRules.Duration(400, NoticeTime.Long), 3);
            Assert.True(NoticeRules.Duration(10, NoticeTime.UntilClicked) >= 3600f);
        }

        [Fact]
        public void TheLongestOldNoticesNowGetAboutTwelveCharactersASecond()
        {
            // The board's evidence: 177 characters in 7 s was 25 a second; now it is under 10.
            foreach (int chars in new[] { 140, 145, 160, 177, 243 })
                Assert.True(chars / NoticeRules.Duration(chars, NoticeTime.Normal) <= 12.5f, chars + " characters");
        }

        [Fact]
        public void TasksDeadlinesCamerasAndOrdersAreNeverDroppedUnseen()
        {
            Assert.True(NoticeRules.IsImportant("icon_info", true, false, NoticeKind.Plain));
            Assert.True(NoticeRules.IsImportant("icon_info", false, true, NoticeKind.Plain));
            Assert.True(NoticeRules.IsImportant("icon_info", false, false, NoticeKind.Deadline));
            Assert.False(NoticeRules.IsImportant("icon_info", false, false, NoticeKind.Entity));   // a burst of session notices goes stale like any other
            Assert.False(NoticeRules.IsImportant("icon_info", false, false, NoticeKind.Gary));
            Assert.True(NoticeRules.IsImportant("icon_camera", false, false, NoticeKind.Plain));
            Assert.True(NoticeRules.IsImportant("icon_task_active", false, false, NoticeKind.Plain));
            Assert.False(NoticeRules.IsImportant("icon_info", false, false, NoticeKind.Plain));
            Assert.False(NoticeRules.IsImportant("icon_mail_unread", false, false, NoticeKind.Plain));
        }

        [Fact]
        public void TheNoticeTimeSettingRoundTripsAndOldFilesReadAsNormal()
        {
            foreach (NoticeTime mode in Enum.GetValues(typeof(NoticeTime)))
                Assert.Equal(mode, NoticeRules.Parse(NoticeRules.Id(mode)));
            Assert.Equal(NoticeTime.Normal, NoticeRules.Parse(null));
            Assert.Equal(NoticeTime.Normal, NoticeRules.Parse(""));
            Assert.Equal(NoticeTime.Normal, NoticeRules.Parse("whatever"));
            Assert.Equal(NoticeTime.Long, NoticeRules.Next(NoticeTime.Normal));
            Assert.Equal(NoticeTime.UntilClicked, NoticeRules.Next(NoticeTime.Long));
            Assert.Equal(NoticeTime.Normal, NoticeRules.Next(NoticeTime.UntilClicked));
        }

        [Fact]
        public void ThreeNoticesShowAtOnceAndTheHistoryKeepsTwenty()
        {
            Assert.Equal(3, NoticeRules.VisibleCap);
            Assert.Equal(20, NoticeRules.HistoryCap);
            var h = new NoticeHistory();
            for (int i = 0; i < 27; i++) h.Add("1:" + i.ToString("00") + " AM", "NEXUS OS", "notice " + i, NoticeKind.Plain);
            Assert.Equal(20, h.Count);
            Assert.Equal("notice 26", h.Entries[0].Body);      // newest first
            Assert.Equal("notice 7", h.Entries[19].Body);      // the oldest seven are gone
            Assert.Equal("1:26 AM", h.Entries[0].Stamp);
        }

        [Fact]
        public void TheHistoryKeepsWhoSaidItAndIgnoresAnExactRepeat()
        {
            var h = new NoticeHistory();
            h.Add("2:00 AM", "Camera Viewer", "Camera Viewer closed by session 017.", NoticeKind.Entity);
            int revision = h.Revision;
            h.Add("2:00 AM", "Camera Viewer", "Camera Viewer closed by session 017.", NoticeKind.Entity);   // a fight can close it every few seconds
            Assert.Equal(1, h.Count);
            Assert.Equal(revision, h.Revision);
            h.Add("2:02 AM", "Work Queue", "New task.", NoticeKind.Deadline);
            h.Add("2:03 AM", "Camera Viewer", "Camera Viewer closed by session 017.", NoticeKind.Entity);   // after a different one it counts again
            Assert.Equal(3, h.Count);
            Assert.Equal(NoticeKind.Entity, h.Entries[0].Kind);
            Assert.Equal(NoticeKind.Deadline, h.Entries[1].Kind);
            Assert.True(h.Revision > revision);
            h.Clear();
            Assert.Equal(0, h.Count);
        }

        // ------------------------------------------------------------------ A4: relaxed timing

        [Fact]
        public void RelaxedTimingIsOnInStoryAndAnOptionForNormal()
        {
            Assert.Equal(1f, RelaxedTiming.Scale(false, false));
            Assert.Equal(2f, RelaxedTiming.Scale(true, false));
            Assert.Equal(2f, RelaxedTiming.Scale(false, true));
            Assert.Equal(2f, RelaxedTiming.Scale(true, true));
            Assert.True(RelaxedTiming.Parse("on"));
            Assert.False(RelaxedTiming.Parse("off"));
            Assert.False(RelaxedTiming.Parse(null));
        }

        [Fact]
        public void NormalTimingIsExactlyAsItWas()
        {
            Assert.Equal(150f, RelaxedTiming.Seconds(150f, 1f));
            Assert.Equal(20f, RelaxedTiming.Seconds(20f, 1f));
            Assert.Equal(1f / 12f, RelaxedTiming.Rate(1f / 12f, 1f));
            Assert.Equal(0f, RelaxedTiming.RaceDelayAdd(1f));
            Assert.Equal(1.4f, RelaxedTiming.CloseReaction(1.4f, 1f));
        }

        [Fact]
        public void RelaxedDoublesTheRealTimeWindows()
        {
            Assert.Equal(300f, RelaxedTiming.Seconds(150f, 2f));    // Night 2's priority shred
            Assert.Equal(350f, RelaxedTiming.Seconds(175f, 2f));    // its hard cap
            Assert.Equal(40f, RelaxedTiming.Seconds(20f, 2f));      // the grace after a log off or shred has started
            Assert.Equal(0.4f, RelaxedTiming.RaceDelayAdd(2f), 4);
            Assert.Equal(2.1f, RelaxedTiming.CloseReaction(1.4f, 2f), 4);
        }

        [Fact]
        public void TheLastFiveMinutesLastTwiceAsLongInRealSeconds()
        {
            // 7:00 to 7:05 is five game minutes: at 1/12 a minute a second that is 60 s; relaxed it is 120 s.
            float normal = 5f / RelaxedTiming.Rate(1f / 12f, 1f), relaxed = 5f / RelaxedTiming.Rate(1f / 12f, 2f);
            Assert.Equal(60f, normal, 2);
            Assert.Equal(120f, relaxed, 2);
            // Night 2's 3:00 deadline: the same game minutes, spread over twice the seconds.
            float minutes = 30f;
            Assert.Equal(300f, minutes / (minutes / RelaxedTiming.Seconds(150f, 2f)), 2);
        }

        [Fact]
        public void AScaleBelowOneNeverSpeedsTheClockUp()
        {
            Assert.Equal(1f / 12f, RelaxedTiming.Rate(1f / 12f, 0.5f));
            Assert.Equal(150f, RelaxedTiming.Seconds(150f, 0.25f));
            Assert.Equal(0f, RelaxedTiming.RaceDelayAdd(0f));
        }

        // ------------------------------------------------------------------ R3 and R10: the chip and the taskbar

        [Theory]
        [InlineData(-1f, false)]
        [InlineData(0f, true)]
        [InlineData(41f, true)]
        [InlineData(119.9f, true)]
        [InlineData(120f, false)]
        [InlineData(400f, false)]
        public void TheChipShowsUnderTwoRealMinutes(float seconds, bool shows)
        {
            Assert.Equal(shows, DeadlineChip.Shows(seconds));
        }

        [Theory]
        [InlineData(41f, "0:41")]
        [InlineData(40.2f, "0:41")]
        [InlineData(0f, "0:00")]
        [InlineData(61f, "1:01")]
        [InlineData(119.5f, "2:00")]
        [InlineData(-3f, "0:00")]
        public void TheChipCountsDownInRealSeconds(float seconds, string text)
        {
            Assert.Equal(text, DeadlineChip.Text(seconds));
        }

        [Fact]
        public void AChipThatIsUpStaysFiveSecondsPastTheLine()
        {
            Assert.False(DeadlineChip.Shows(122f, false));
            Assert.True(DeadlineChip.Shows(122f, true));    // the clock changing speed at the line cannot flicker it
            Assert.False(DeadlineChip.Shows(125f, true));
            Assert.True(DeadlineChip.Shows(119.9f, false));
            Assert.False(DeadlineChip.Shows(-1f, true));
        }

        [Fact]
        public void TheChipBlinksOnlyInTheLastFifteenSecondsAndNeverWithReduceFlashing()
        {
            Assert.False(DeadlineChip.BlinkDark(30f, false, 0.3f));
            Assert.False(DeadlineChip.BlinkDark(10f, false, 0.1f));     // the red half of the beat
            Assert.True(DeadlineChip.BlinkDark(10f, false, 0.3f));      // the dark half
            Assert.False(DeadlineChip.BlinkDark(10f, true, 0.3f));      // steady red
            Assert.False(DeadlineChip.BlinkDark(-1f, false, 0.3f));
            Assert.True(DeadlineChip.BlinkHz < 3f);                     // under the photosensitivity line
        }

        [Fact]
        public void TheTaskbarKeepsNamedButtonsWhileTheyFit()
        {
            var plan = TaskbarLayout.Plan(756, 3, false);
            Assert.False(plan.IconOnly);
            Assert.Equal(TaskbarLayout.TaskDefault, plan.TaskWidth);
            Assert.Equal(TaskbarLayout.ButtonMax, plan.ButtonWidth);
            plan = TaskbarLayout.Plan(756, 6, false);
            Assert.False(plan.IconOnly);
            Assert.True(plan.ButtonWidth >= TaskbarLayout.IconOnlyBelow);
        }

        [Fact]
        public void TenWindowsBecomeIconsAndTheTaskLineNeverShrinks()
        {
            // Night 3 under load: ten buttons used to read "Wo..", "Ses..", "inci..".
            var plan = TaskbarLayout.Plan(756, 10, false);
            Assert.True(plan.IconOnly);
            Assert.Equal(TaskbarLayout.IconButton, plan.ButtonWidth);
            Assert.Equal(TaskbarLayout.TaskDefault, plan.TaskWidth);
            Assert.True(10 * plan.ButtonWidth + 9 * TaskbarLayout.ButtonGap + plan.TaskWidth <= 756);
        }

        [Fact]
        public void TheChipCollapsesTheButtonsAndWidensTheTaskButton()
        {
            var plan = TaskbarLayout.Plan(756, 3, true);
            Assert.True(plan.IconOnly);
            Assert.True(plan.TaskWidth > TaskbarLayout.TaskDefault);
            Assert.True(plan.TaskWidth <= TaskbarLayout.TaskMax);
            Assert.True(3 * plan.ButtonWidth + 2 * TaskbarLayout.ButtonGap + plan.TaskWidth <= 756);
            // With a crowd the task button still never goes under its default.
            Assert.True(TaskbarLayout.Plan(756, 12, true).TaskWidth >= TaskbarLayout.TaskDefault);
            Assert.Equal(TaskbarLayout.TaskMax, TaskbarLayout.Plan(756, 1, true).TaskWidth);
        }

        // ------------------------------------------------------------------ A6: captions

        [Fact]
        public void OnlyStoryAndScareSoundsAreCaptioned()
        {
            foreach (var id in new[] { "knock_door", "chair_creak", "step_near", "scare_hit", "phone_ring", "ear_ring", "whisper_burst" })
                Assert.True(CaptionRules.IsCaptioned(id), id);
            foreach (var id in new[] { "ui_click", "ui_select", "key_tap", "mouse_click", "notify_mail", "amb_room", "drone_tension", "camera_static", "shred_loop", "sys_error", null })
                Assert.False(CaptionRules.IsCaptioned(id), id ?? "null");
            foreach (var id in CaptionRules.CaptionedIds) Assert.True(ProceduralSoundBank.Has(id), id + " is a real sound");
            Assert.DoesNotContain(CaptionRules.CaptionedIds, ProceduralSoundBank.IsLoop);
        }

        [Fact]
        public void ACaptionComesWithTheSoundAndNamesItsSide()
        {
            var rules = new CaptionRules();
            var cue = rules.Select("knock_door", 0.8f, -0.6f, 10f, 17);
            Assert.True(cue.HasValue);
            Assert.Equal(CaptionSide.Left, cue.Value.Side);
            Assert.Equal("caption.knock_door", cue.Value.Key);
            Assert.Equal(CaptionSide.Right, rules.Select("step_near", 0.6f, 0.5f, 11f, 10).Value.Side);
            Assert.Equal(CaptionSide.Centre, rules.Select("scare_hit", 1f, 0f, 12f, 10).Value.Side);
            Assert.Equal(CaptionSide.Centre, CaptionRules.SideFor(0.19f));
            Assert.Equal(CaptionSide.Left, CaptionRules.SideFor(-0.21f));
        }

        [Fact]
        public void ARepeatedOrNearlySilentSoundIsNotCaptionedAgain()
        {
            var rules = new CaptionRules();
            Assert.True(rules.Select("footstep_distant", 0.5f, 0.3f, 5f, 20).HasValue);
            Assert.False(rules.Select("footstep_distant", 0.5f, 0.3f, 5.5f, 20).HasValue);   // the same sound too soon
            Assert.True(rules.Select("footstep_distant", 0.5f, 0.3f, 6.3f, 20).HasValue);    // after the gap
            Assert.False(rules.Select("knock_door", 0.9f, 0f, 6.35f, 17).HasValue);          // too close behind another caption
            Assert.True(rules.Select("knock_door", 0.9f, 0f, 6.6f, 17).HasValue);
            Assert.False(rules.Select("ear_ring", 0.05f, 0f, 20f, 9).HasValue);             // too quiet to be a cue
            Assert.False(rules.Select("ui_click", 1f, 0f, 30f, 9).HasValue);
            rules.Reset();
            Assert.True(rules.Select("knock_door", 0.9f, 0f, 6.7f, 17).HasValue);
        }

        [Fact]
        public void CaptionsAreMarkedByTheirSideAndStayLongEnoughToRead()
        {
            Assert.Equal("< [knock at a door]", CaptionRules.Format("[knock at a door]", CaptionSide.Left));
            Assert.Equal("[knock at a door] >", CaptionRules.Format("[knock at a door]", CaptionSide.Right));
            Assert.Equal("[knock at a door]", CaptionRules.Format("[knock at a door]", CaptionSide.Centre));
            Assert.Equal("", CaptionRules.Format("", CaptionSide.Left));
            Assert.Equal(2.4f, CaptionRules.SecondsFor(3), 3);
            Assert.True(CaptionRules.SecondsFor(40) > CaptionRules.SecondsFor(10));
            Assert.Equal(4.5f, CaptionRules.SecondsFor(500), 3);
            Assert.Equal(3, CaptionRules.MaxVisible);
        }

        /// <summary>Sounds only Nights 2 and 3 play (checked against the directors): their captions live in the full game's strings, not the demo's.</summary>
        static readonly string[] FullOnly = { "phone_ring", "whisper_burst", "breath_near", "metal_scrape", "sub_swell", "footstep_distant" };

        [Fact]
        public void EveryCaptionHasWordingAndTheNightTwoAndThreeSoundsAreOnlyInTheFullGame()
        {
            if (!Present) return;
            var demo = Demo();
            var full = Night3();
            foreach (var id in CaptionRules.CaptionedIds)
            {
                string key = CaptionRules.KeyFor(id);
                if (FullOnly.Contains(id))
                {
                    Assert.False(demo.HasText(key), "the demo has no caption for " + id);
                    Assert.True(full.HasText(key), key);
                    continue;
                }
                Assert.True(demo.HasText(key), key);
                string text = demo.Text(key);
                Assert.StartsWith("[", text);
                Assert.EndsWith("]", text);
                Assert.True(text.Length <= 34, key + " is short");
            }
        }

        [Fact]
        public void CaptionsNameWhatWasHeardNotWhatItMeans()
        {
            if (!Present) return;
            var db = Night3();
            string[] forbidden = { "017", "ellen", "casey", "entity", "ghost", "demon", "figure", "custodial", "danger", "scare", "jump", "she " };
            foreach (var id in CaptionRules.CaptionedIds)
            {
                string text = db.Text(CaptionRules.KeyFor(id), "").ToLowerInvariant();
                foreach (var word in forbidden) Assert.DoesNotContain(word, text);
            }
        }

        // ------------------------------------------------------------------ A7, A9, A10: the options

        [Fact]
        public void ShakeAndSuddenSoundsFollowReduceFlashingUntilTheyAreSet()
        {
            Assert.Equal(ShakeLevel.Full, AccessOptions.ShakeFrom(-1, false));
            Assert.Equal(ShakeLevel.Reduced, AccessOptions.ShakeFrom(-1, true));
            Assert.Equal(ShakeLevel.Off, AccessOptions.ShakeFrom(2, false));
            Assert.Equal(ShakeLevel.Full, AccessOptions.ShakeFrom(0, true));     // an explicit choice wins over Reduce flashing
            Assert.Equal(1f, AccessOptions.ShakeFactor(ShakeLevel.Full));
            Assert.Equal(0.3f, AccessOptions.ShakeFactor(ShakeLevel.Reduced), 4);   // what Reduce flashing always did
            Assert.Equal(0f, AccessOptions.ShakeFactor(ShakeLevel.Off));
            Assert.Equal(ShakeLevel.Reduced, AccessOptions.Next(ShakeLevel.Full));
            Assert.Equal(ShakeLevel.Full, AccessOptions.Next(ShakeLevel.Off));
            Assert.False(AccessOptions.SoftSoundsFrom(-1, false));
            Assert.True(AccessOptions.SoftSoundsFrom(-1, true));
            Assert.True(AccessOptions.SoftSoundsFrom(1, false));     // soft sounds without reducing the flashing
            Assert.False(AccessOptions.SoftSoundsFrom(0, true));     // and the other way round
        }

        [Fact]
        public void TheDoubleClickIsAdjustable()
        {
            Assert.Equal(0.45f, AccessOptions.DoubleClickSeconds(ClickSpeed.Normal));
            Assert.Equal(5f, AccessOptions.DoubleClickDistance(ClickSpeed.Normal));
            Assert.Equal(0.9f, AccessOptions.DoubleClickSeconds(ClickSpeed.Slow));
            Assert.True(AccessOptions.DoubleClickDistance(ClickSpeed.Slow) > 5f);
            Assert.True(AccessOptions.OpensOnSingleClick(ClickSpeed.Single));
            Assert.False(AccessOptions.OpensOnSingleClick(ClickSpeed.Normal));
            Assert.False(AccessOptions.OpensOnSingleClick(ClickSpeed.Slow));
            Assert.Equal(ClickSpeed.Slow, AccessOptions.Next(ClickSpeed.Normal));
            Assert.Equal(ClickSpeed.Normal, AccessOptions.Next(ClickSpeed.Single));
            Assert.Equal(ClickSpeed.Normal, AccessOptions.ClickFrom(-5));
            Assert.Equal(ClickSpeed.Single, AccessOptions.ClickFrom(2));
            Assert.Equal(1, AccessOptions.CursorScale(false));
            Assert.Equal(2, AccessOptions.CursorScale(true));
        }

        // ------------------------------------------------------------------ R6: scanlines without a moire

        [Theory]
        [InlineData(1f, 2)]
        [InlineData(1.333f, 2)]   // the Steam Deck
        [InlineData(1.5f, 2)]
        [InlineData(2f, 2)]       // 1080p: as it always was
        [InlineData(3f, 3)]
        [InlineData(4f, 4)]       // 4K: as it always was
        public void ScanlinesRepeatEveryWholeNumberOfRealPixels(float scale, int period)
        {
            Assert.Equal(period, ScanlinePlan.Period(scale));
        }

        [Fact]
        public void AtTheOldScalesTheScanlinesAreUnchangedAndAtTheDeckTheyAreRegular()
        {
            // At 2x one dark real row in two, at 4x two in four: the old picture.
            Assert.Equal(1, ScanlinePlan.DarkRows(ScanlinePlan.Period(2f)));
            Assert.Equal(2, ScanlinePlan.DarkRows(ScanlinePlan.Period(4f)));
            // At 1.333x the repeat is a whole number of real rows over the whole height, so it cannot beat against the 10 px glyphs.
            int period = ScanlinePlan.Period(1.333f);
            float height = 540f * 1.333f;
            Assert.Equal(height / period, ScanlinePlan.Repeats(height, period), 3);
            Assert.True(ScanlinePlan.DarkRows(period) >= 1 && ScanlinePlan.DarkRows(period) < period);
        }

        // ------------------------------------------------------------------ content

        [Fact]
        public void TheAccessOptionsHaveTheirWordsInTheBaseGame()
        {
            if (!Present) return;
            var db = Demo();
            foreach (var key in new[]
            {
                "pause.access", "pause.access.title", "pause.noticetime", "pause.noticetime.normal", "pause.noticetime.long", "pause.noticetime.clicked",
                "pause.relaxed", "pause.captions", "pause.on", "pause.off", "pause.sudden", "pause.sudden.normal", "pause.sudden.soft", "pause.shake",
                "pause.shake.full", "pause.shake.reduced", "pause.shake.off", "pause.mono", "pause.bigcursor", "pause.clickspeed",
                "pause.clickspeed.normal", "pause.clickspeed.slow", "pause.clickspeed.single", "disclaimer.keys", "start.recent", "app.recent", "recent.empty",
            })
                Assert.True(db.HasText(key), key);
            // The row labels take their value as {0}.
            foreach (var key in new[] { "pause.noticetime", "pause.relaxed", "pause.captions", "pause.sudden", "pause.shake", "pause.mono", "pause.bigcursor", "pause.clickspeed" })
                Assert.Contains("{0}", db.Text(key));
        }

        [Fact]
        public void TheLastFiveMinutesTaskHasThreeShortScanLinesAndTheDemoHasNone()
        {
            if (!Present) return;
            var task = Night3().Tasks.tasks.First(t => t.id == ContentIds.TaskN3LogOffBy);
            Assert.Equal(3, task.summary.Length);
            foreach (var line in task.summary) Assert.True(line.Length < 40, line);
            Assert.Contains("LOG OFF", task.summary[0]);
            Assert.Contains("Custodial OFF your camera", task.summary[1]);
            Assert.Contains("keeps you", task.summary[2]);
            // The paragraph is unchanged (the scan lines go on top of it).
            Assert.StartsWith("1. Log off between 7:00 and 7:05 AM", task.description);
            Assert.All(Demo().Tasks.tasks, t => Assert.Empty(t.summary));
            Assert.All(Pack("night2").Tasks.tasks, t => Assert.Empty(t.summary));
        }

        [Fact]
        public void NoNewTextUsesALongDash()
        {
            if (!Present) return;
            foreach (var folder in new[] { "", "full", "night2", "night3" })
            {
                foreach (var file in new[] { "strings.json", "tasks.json" })
                {
                    string path = Path.Combine(Dir, folder, file);
                    if (!File.Exists(path)) continue;
                    Assert.True(File.ReadAllText(path).IndexOfAny(LongDashes) < 0, path);
                }
            }
        }
    }
}
