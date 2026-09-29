using System.Text.Json;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using Xunit;

namespace SecondCursor.Tests
{
    /// <summary>progress.json: migration, checkpoints, night results and New Game (expansion spec 8.2).</summary>
    public class SaveDataTests
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };

        static SaveData RoundTrip(SaveData d) => JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(d, Options), Options);

        [Fact]
        public void AFinishedVersionTwoSaveMigratesToNightTwo()
        {
            var old = JsonSerializer.Deserialize<SaveData>(
                "{\"version\":2,\"endingsSeen\":[\"night1_blackout\"],\"shiftsCompleted\":1,\"nightUnlocked\":2,\"entityTrust\":-0.3," +
                "\"lastShiftFlags\":{\"flags\":[\"file017_shredded_once\",\"player_refused\",\"logged_in\"],\"counterKeys\":[\"player_wins\"],\"counterValues\":[2],\"choiceKeys\":[],\"choiceValues\":[]}}",
                Options);
            Assert.True(old.Migrate());
            Assert.Equal(SaveData.CurrentVersion, old.version);
            Assert.Equal(2, old.nightUnlocked);
            Assert.Equal(2, old.currentNight);
            Assert.Equal(new[] { "n1_blackout" }, old.endingsSeen);
            var mem = new NarrativeFlags();
            mem.Restore(old.memory);
            Assert.True(mem.Has(MemoryFlags.N1Shredded017));
            Assert.True(mem.Has(MemoryFlags.N1Refused));
            Assert.False(mem.Has(MemoryFlags.N1Agreed));
            Assert.False(mem.Has("logged_in"));
            Assert.Equal(2, mem.Get(MemoryFlags.N1TugWins));
            Assert.Empty(old.playerLines);
            Assert.Equal(3, old.nightSeconds.Length);
            // Migrating again changes nothing.
            Assert.False(old.Migrate());
        }

        [Fact]
        public void AFreshSaveNeedsNoMigration()
        {
            var d = new SaveData();
            Assert.False(d.Migrate());
            Assert.Equal(1, d.currentNight);
            Assert.Equal("normal", d.difficulty);
            Assert.False(d.IsStory);
        }

        [Fact]
        public void CheckpointsRoundTrip()
        {
            var flags = new NarrativeFlags();
            flags.Set("conflict_started");
            flags.SetCounter("player_wins", 1);
            var d = new SaveData();
            d.SetCheckpoint(new Checkpoint { valid = true, night = 1, beat = "conflict", clockMinutes = 140, trust = -0.15f, assistLevel = 1, flags = flags.Snapshot() });
            var back = RoundTrip(d);
            var cp = back.CheckpointFor(1);
            Assert.NotNull(cp);
            Assert.Equal("conflict", cp.beat);
            Assert.Equal(140, cp.clockMinutes);
            Assert.Equal(1, cp.assistLevel);
            var restored = new NarrativeFlags();
            restored.Restore(cp.flags);
            Assert.True(restored.Has("conflict_started"));
            Assert.Equal(1, restored.Get("player_wins"));
            Assert.Null(back.CheckpointFor(2));
        }

        [Fact]
        public void FinishingANightSavesMemoryAndUnlocksTheNext()
        {
            var d = new SaveData();
            d.RecordNightStart(1, new FlagSnapshot());
            d.SetCheckpoint(new Checkpoint { valid = true, night = 1, beat = "escalation" });
            var night = new NarrativeFlags();
            night.Set(MemoryFlags.N1Agreed);
            night.SetCounter(MemoryFlags.N1TugWins, 2);
            d.RecordNightComplete(new NightResult
            {
                Night = 1, EndingId = "n1_blackout", Memory = night.Snapshot(MemoryFlags.Prefix), Trust = 0.4f, AssistLevel = 2,
                TugWins = 2, TugLosses = 3, Seconds = 800f,
                PlayerLines = new[] { "who are you?", "  no   {way}  ", "caf" + (char)233 + " at 3", "fourth line is dropped" },
            });
            Assert.Equal(2, d.nightUnlocked);
            Assert.Equal(2, d.currentNight);
            Assert.False(d.checkpoint.valid);
            Assert.Equal(2, d.assistCarry);
            Assert.Equal(0.4f, d.entityTrust);
            Assert.Equal(new[] { "n1_blackout" }, d.endingsSeen);
            Assert.Equal(new[] { "who are you?", "no way", "caf at 3" }, d.playerLines);
            Assert.Equal(2, d.tugWinsTotal);
            Assert.Equal(3, d.tugLossesTotal);
            Assert.Equal(800f, d.nightSeconds[0]);
            Assert.Equal(1, d.nightStarts[0]);
            var mem = new NarrativeFlags();
            mem.Restore(d.memory);
            Assert.True(mem.Has(MemoryFlags.N1Agreed));
            Assert.Equal(2, mem.Get(MemoryFlags.N1TugWins));

            // Replaying Night 1 replaces that memory instead of mixing both runs' choices.
            var replay = new NarrativeFlags();
            replay.Set(MemoryFlags.N1Refused);
            d.RecordNightComplete(new NightResult { Night = 1, EndingId = "n1_blackout", Memory = replay.Snapshot(MemoryFlags.Prefix), TugLosses = 1 });
            mem.Restore(d.memory);
            Assert.True(mem.Has(MemoryFlags.N1Refused));
            Assert.False(mem.Has(MemoryFlags.N1Agreed));
            Assert.Equal(new[] { "n1_blackout" }, d.endingsSeen);
            Assert.Equal(4, d.tugLossesTotal);
        }

        [Fact]
        public void NightStartMemoryIsKeptFromTheFirstStart()
        {
            var d = new SaveData();
            var first = new NarrativeFlags();
            first.Set(MemoryFlags.N1Agreed);
            d.RecordNightStart(2, first.Snapshot());
            d.RecordNightStart(2, new FlagSnapshot());
            Assert.Equal(2, d.nightStarts[1]);
            Assert.Contains(MemoryFlags.N1Agreed, d.nightStartMemory[1].flags);
            Assert.Equal(2, d.currentNight);
        }

        [Fact]
        public void NewGameKeepsRecordsAndSettings()
        {
            var d = new SaveData { difficulty = "story", nightUnlocked = 3, currentNight = 3, endingsSeen = new[] { "n1_blackout" }, achievements = new[] { "ACH_NIGHT_1" }, tugWinsTotal = 4 };
            d.NewGame();
            Assert.Equal(1, d.nightUnlocked);
            Assert.Equal(1, d.currentNight);
            Assert.Equal("story", d.difficulty);
            Assert.Equal(new[] { "n1_blackout" }, d.endingsSeen);
            Assert.Equal(new[] { "ACH_NIGHT_1" }, d.achievements);
            Assert.Equal(4, d.tugWinsTotal);
        }

        [Fact]
        public void PlayerLinesAreSanitized()
        {
            Assert.Equal("", SaveData.SanitizePlayerLine(null));
            Assert.Equal("hello there", SaveData.SanitizePlayerLine("  hello\t   there  "));
            Assert.Equal("line1", SaveData.SanitizePlayerLine("{line1}"));
            Assert.Equal(40, SaveData.SanitizePlayerLine(new string('x', 60)).Length);
        }

        [Fact]
        public void TrustDecaysTowardNeutralEachNight()
        {
            Assert.Equal(0f, SaveData.TrustAtNightStart(1, 0.8f));
            Assert.Equal(0.4f, SaveData.TrustAtNightStart(2, 0.8f), 4);
            Assert.Equal(-0.6f, SaveData.TrustAtNightStart(3, -0.8f), 4);
        }

        [Fact]
        public void FlagSnapshotsFilterAndMerge()
        {
            var f = new NarrativeFlags();
            f.Set("logged_in");
            f.Set(MemoryFlags.N1Agreed);
            f.SetCounter(MemoryFlags.N1TugLosses, 3);
            f.SetCounter("player_wins", 1);
            var mem = f.Snapshot(MemoryFlags.Prefix);
            Assert.Equal(new[] { MemoryFlags.N1Agreed }, mem.flags);
            Assert.Equal(new[] { MemoryFlags.N1TugLosses }, mem.counterKeys);
            var next = new NarrativeFlags();
            next.Set("n2_local");
            next.Merge(mem);
            Assert.True(next.Has("n2_local"));
            Assert.True(next.Has(MemoryFlags.N1Agreed));
            Assert.Equal(3, next.Get(MemoryFlags.N1TugLosses));
        }
    }
}
