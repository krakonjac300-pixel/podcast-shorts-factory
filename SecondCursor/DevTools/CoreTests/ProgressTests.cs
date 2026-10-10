using System.Text.Json;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>Phase E progression: Continue's target, Night Select start state, records gating, tug totals.</summary>
    public class ProgressTests
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

        static SaveData RoundTrip(SaveData d) => JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(d, Options), Options);

        [Fact]
        public void AFreshSaveHasNothingToContinue()
        {
            var d = new SaveData();
            Assert.False(d.HasProgress);
            Assert.False(d.HasRecords);
            Assert.Null(d.ContinueTarget(3));
        }

        [Fact]
        public void StartingNightOneGivesContinueFromItsStart()
        {
            var d = new SaveData();
            d.RecordNightStart(1, new FlagSnapshot());
            var c = d.ContinueTarget(3);
            Assert.NotNull(c);
            Assert.Equal(1, c.Night);
            Assert.Null(c.Checkpoint);
        }

        [Fact]
        public void ACheckpointIsContinuedFirst()
        {
            var d = new SaveData { nightUnlocked = 2, currentNight = 2 };
            d.SetCheckpoint(new Checkpoint { valid = true, night = 2, beat = "asks", clockMinutes = 150 });
            var c = d.ContinueTarget(3);
            Assert.Equal(2, c.Night);
            Assert.Equal("asks", c.Checkpoint.beat);
        }

        [Fact]
        public void AFinishedGameHidesContinueUnlessACheckpointExists()
        {
            var d = new SaveData();
            for (int n = 1; n <= 3; n++)
            {
                d.RecordNightStart(n, new FlagSnapshot());
                d.RecordNightComplete(new NightResult { Night = n, EndingId = n == 3 ? "n3_keep" : "n" + n + "_x" });
            }
            Assert.Equal(3, d.lastCompletedNight);
            Assert.Equal(4, d.nightUnlocked);
            Assert.Null(d.ContinueTarget(3));
            // A replay that saved a checkpoint can be continued.
            d.SetCheckpoint(new Checkpoint { valid = true, night = 2, beat = "work" });
            Assert.Equal(2, d.ContinueTarget(3).Night);
        }

        [Fact]
        public void TheDemoHasNoContinueAfterNightOne()
        {
            var d = new SaveData();
            d.RecordNightStart(1, new FlagSnapshot());
            Assert.Equal(1, d.ContinueTarget(1).Night);
            d.RecordNightComplete(new NightResult { Night = 1, EndingId = "n1_blackout" });
            Assert.Equal(2, d.currentNight);
            Assert.Null(d.ContinueTarget(1));
            // A Night 2 checkpoint (full game save) is not continued by the demo either.
            d.SetCheckpoint(new Checkpoint { valid = true, night = 2, beat = "work" });
            Assert.Null(d.ContinueTarget(1));
        }

        [Fact]
        public void NightSelectAfterANightOneReplayKeepsTheNightTwoChoices()
        {
            var d = new SaveData();
            var n1 = new NarrativeFlags();
            n1.Set(MemoryFlags.N1Agreed);
            d.RecordNightStart(1, new FlagSnapshot(), 0f);
            d.RecordNightComplete(new NightResult { Night = 1, EndingId = "n1_blackout", Memory = n1.Snapshot(MemoryFlags.Prefix), Trust = 0.6f });
            d.StartStateFor(2, false, out var m2, out float t2);
            d.RecordNightStart(2, m2, t2);
            var n2 = new NarrativeFlags();
            n2.Restore(m2);
            n2.Set(MemoryFlags.N2KeptGary);
            d.RecordNightComplete(new NightResult { Night = 2, EndingId = "n2_kept", Memory = n2.Snapshot(MemoryFlags.Prefix), Trust = 0.2f });
            d.StartStateFor(3, false, out var m3, out float t3);
            d.RecordNightStart(3, m3, t3);
            float firstNight3Trust = d.nightStartTrust[2];

            // Replaying Night 1 drops the later nights' keys from the saved memory...
            d.RecordNightComplete(new NightResult { Night = 1, EndingId = "n1_blackout", Memory = new FlagSnapshot(), Trust = -0.5f });
            var mem = new NarrativeFlags();
            mem.Restore(d.memory);
            Assert.False(mem.Has(MemoryFlags.N2KeptGary));

            // ...but Night Select starts Night 3 from its first start, Gary branch included.
            d.StartStateFor(3, true, out var select3, out float selectTrust);
            var s3 = new NarrativeFlags();
            s3.Restore(select3);
            Assert.True(s3.Has(MemoryFlags.N2KeptGary));
            Assert.True(s3.Has(MemoryFlags.N1Agreed));
            Assert.Equal(firstNight3Trust, selectTrust);

            // Continue (not Night Select) uses the saved memory.
            d.StartStateFor(3, false, out var cont3, out _);
            var c3 = new NarrativeFlags();
            c3.Restore(cont3);
            Assert.False(c3.Has(MemoryFlags.N2KeptGary));
        }

        [Fact]
        public void NightSelectOfANeverStartedNightUsesTheSavedMemory()
        {
            var d = new SaveData { nightUnlocked = 2, entityTrust = 0.8f };
            var n1 = new NarrativeFlags();
            n1.Set(MemoryFlags.N1Refused);
            d.memory = n1.Snapshot(MemoryFlags.Prefix);
            d.StartStateFor(2, true, out var m, out float t);
            Assert.Contains(MemoryFlags.N1Refused, m.flags);
            Assert.Equal(0.4f, t, 4);
            d.StartStateFor(1, true, out var m1, out float t1);
            Assert.Empty(m1.flags);
            Assert.Equal(0f, t1);
        }

        [Fact]
        public void RecordsAreOnlyKeptForRunsThatCount()
        {
            var d = new SaveData();
            d.RecordNightStart(1, new FlagSnapshot());
            d.RecordNightComplete(new NightResult { Night = 1, EndingId = "n1_blackout", Seconds = 600f, Records = false });
            // Progression still moves on (debug runs unlock nights)...
            Assert.Equal(2, d.nightUnlocked);
            Assert.Equal(1, d.lastCompletedNight);
            // ...records do not.
            Assert.Empty(d.endingsSeen);
            Assert.Equal(0f, d.nightSeconds[0]);
            Assert.Equal(0f, d.bestNightSeconds[0]);
            Assert.False(d.HasRecords);
        }

        [Fact]
        public void ADebugStartIsNeverANightsFirstStart()
        {
            var d = new SaveData { nightUnlocked = 3 };
            d.NoteNightStarted(2);
            Assert.Equal(2, d.currentNight);
            Assert.Equal(0, d.nightStarts[1]);
            // The first real start still records the memory Night Select replays from.
            var mem = new NarrativeFlags();
            mem.Set(MemoryFlags.N1Agreed);
            d.RecordNightStart(2, mem.Snapshot(), 0.3f);
            Assert.Contains(MemoryFlags.N1Agreed, d.nightStartMemory[1].flags);
            Assert.Equal(0.3f, d.nightStartTrust[1]);
        }

        [Fact]
        public void TugTotalsCountLive()
        {
            var d = new SaveData();
            d.RecordTug(true);
            d.RecordTug(true);
            d.RecordTug(false);
            Assert.Equal(2, d.tugWinsTotal);
            Assert.Equal(1, d.tugLossesTotal);
            Assert.True(d.HasRecords);
        }

        [Fact]
        public void CheckpointElapsedAndArmedRoundTrip()
        {
            var d = new SaveData();
            d.SetCheckpoint(new Checkpoint { valid = true, night = 1, beat = "work", elapsed = 312.5f, armed = false });
            var back = RoundTrip(d);
            Assert.Equal(312.5f, back.checkpoint.elapsed);
            Assert.False(back.checkpoint.armed);
            // A checkpoint written before the field existed counts as armed.
            var old = JsonSerializer.Deserialize<SaveData>("{\"version\":3,\"checkpoint\":{\"valid\":true,\"night\":1,\"beat\":\"work\"}}", Options);
            Assert.True(old.checkpoint.armed);
        }

        [Fact]
        public void NewGameKeepsBestTimesAndTotals()
        {
            var d = new SaveData { tugWinsTotal = 3, lastCompletedNight = 3, nightUnlocked = 4 };
            d.bestNightSeconds[1] = 1200f;
            d.nightStartTrust[2] = 0.5f;
            d.nightStarts[2] = 1;
            d.NewGame();
            Assert.Equal(1200f, d.bestNightSeconds[1]);
            Assert.Equal(3, d.tugWinsTotal);
            Assert.Equal(0, d.lastCompletedNight);
            Assert.Equal(0f, d.nightStartTrust[2]);
            Assert.Equal(0, d.nightStarts[2]);
        }

        [Fact]
        public void OldSavesGetTheNewArrays()
        {
            var old = JsonSerializer.Deserialize<SaveData>("{\"version\":3,\"nightSeconds\":[1,2,3]}", Options);
            old.bestNightSeconds = null;
            old.nightStartTrust = new float[1];
            old.Migrate();
            Assert.Equal(3, old.bestNightSeconds.Length);
            Assert.Equal(3, old.nightStartTrust.Length);
        }

        [Fact]
        public void HerNameIsRememberedPerNightSoAReplayForgetsIt()
        {
            var mem = new NarrativeFlags();
            mem.Set(MemoryFlags.N3SaidName);
            var d = new SaveData { memory = mem.Snapshot(MemoryFlags.Prefix) };
            // Replaying Night 3 starts without the name said on the first run...
            var replay = new NarrativeFlags();
            replay.Merge(d.MemoryForNight(3));
            Assert.False(MemoryFlags.SaidNameAny(replay));
            // ...but a name said on Night 2 is still remembered on Night 3.
            mem.Set(MemoryFlags.N2SaidName);
            d.memory = mem.Snapshot(MemoryFlags.Prefix);
            var n3 = new NarrativeFlags();
            n3.Merge(d.MemoryForNight(3));
            Assert.True(MemoryFlags.SaidNameAny(n3));
            // The Phase D key of no night is no longer read.
            var legacy = new NarrativeFlags();
            legacy.Set(MemoryFlags.SaidName);
            Assert.False(MemoryFlags.SaidNameAny(legacy));
        }

        [Fact]
        public void ClockFormatsAnyMinute()
        {
            Assert.Equal("2:47 AM", GameClock.Format12(2 * 60 + 47));
            Assert.Equal("12:05 AM", GameClock.Format12(5));
            Assert.Equal("1:00 PM", GameClock.Format12(13 * 60));
            Assert.Equal("11:59 PM", GameClock.Format12(-1));
        }
    }
}
