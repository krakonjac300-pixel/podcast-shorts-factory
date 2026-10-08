using System;
using System.Collections;
using System.Linq;
using SecondCursor.Apps;
using SecondCursor.Core.Game;
using SecondCursor.Core.Story;
using SecondCursor.Game;
using SecondCursor.OS;

namespace SecondCursor.EditorTools
{
    public static partial class SecondCursorTestBridge
    {
        static IEnumerator RegressionWaitFor(Func<bool> condition, float timeout, string what)
        {
            yield return WaitFor(condition, timeout, what);
            ExperienceAssert(condition(), "required state reached: " + what);
        }

        static IEnumerator BridgeWaitCheck()
        {
            double start = UnityEditor.EditorApplication.timeSinceStartup;
            yield return WaitSeconds(0.25f);
            ExperienceAssert(UnityEditor.EditorApplication.timeSinceStartup - start >= 0.24,
                "editor bridge executes nested waiters before the following assertion");
        }

        static void FinalChoiceCheck()
        {
            var engine = new DialogueEngine(G.Content);
            foreach (string id in new[] { "ex3_final", "ex3_confirm" })
            {
                var exchange = engine.Get(id);
                ExperienceAssert(exchange != null, "final exchange exists: " + id);
                foreach (string input in new[] { "I don't want to stay", "I will not stay", "No, I want to leave", "I want to stay but I am not sure", "yes, but not stay", "yesterday", "don't go unless I say so" })
                {
                    var reply = engine.Respond(exchange, input);
                    ExperienceAssert(reply.Tag != "stay" && reply.Tag != "confirm", "refusal or uncertain reply cannot commit " + id + ": " + input);
                }
                foreach (string input in new[] { "stay", "I want to stay", "Yes, stay", "I will stay", "not leaving" })
                {
                    var reply = engine.Respond(exchange, input);
                    ExperienceAssert(reply.Tag == (id == "ex3_final" ? "stay" : "confirm"), "explicit stay is recognized " + id + ": " + input);
                }
            }
            ExperienceAssert(engine.Respond(engine.Get("ex3_confirm"), "yes").Tag == "confirm", "short confirmation remains recognized");
            ExperienceAssert(engine.Respond(engine.Get("ex3_confirm"), "no").Tag == "cancel", "short refusal cancels confirmation");
            Say("PASS final choice regression checks complete");
        }

        static IEnumerator FinalReplayCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("finalreplaycheck requires savedir PATH first");
            var fixture = new SaveData();
            var original = new Checkpoint
            {
                valid = true, night = 3, beat = "finale", clockMinutes = 401, trust = 0.55f,
                elapsed = 900f, armed = true,
                flags = new FlagSnapshot { flags = new[] { "m.n1.review.choice", "m.n2.review.choice" } },
            };
            fixture.SetCheckpoint(original);
            ExperienceAssert(fixture.FinalDecisionForReplay() == null, "final replay is not offered before completing the encounter");
            fixture.RecordNightComplete(new NightResult { Night = 3, EndingId = "n3_keep", Seconds = 1000f });
            ExperienceAssert(fixture.CheckpointFor(3) == null && fixture.FinalDecisionForReplay() == original, "completion clears Continue but retains final decision");
            fixture.RecordNightComplete(new NightResult { Night = 3, EndingId = "n3_logoff", Seconds = 30f, FinalDecisionReplay = true });
            ExperienceAssert(fixture.bestNightSeconds[2] == 1000f, "a short final replay cannot replace the full-night best time");
            ExperienceAssert(Array.IndexOf(fixture.endingsSeen, "n3_logoff") >= 0, "final replays still record new endings");
            fixture.RecordNightStart(1, new FlagSnapshot());
            ExperienceAssert(fixture.FinalDecisionForReplay() == null, "starting an earlier night invalidates the old final replay");
            fixture.SetCheckpoint(original);
            fixture.NewGame();
            ExperienceAssert(!fixture.finalDecision.valid, "New Game clears final replay state");

            var previous = G;
            GameBootstrap.Restart(3, "work");
            yield return RegressionWaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "work", 30f, "fresh final replay fixture");
            var g = G;
            g.Flags.Set("m.n1.review.choice");
            g.Flags.Set("m.n2.review.choice");
            g.Flags.Set(MemoryFlags.N3LogoffEnabled);
            SaveSystem.SaveCheckpoint(g, "finale");
            var saved = SaveSystem.Load();
            saved.checkpoint.elapsed = 900f;
            saved.checkpoint.trust = 0.55f;
            saved.finalDecision.elapsed = 900f;
            saved.finalDecision.trust = 0.55f;
            SaveSystem.Save(saved);
            SaveSystem.RecordNightComplete(g, "n3_keep", null, 1000f);
            for (int i = 0; i < 2; i++)
            {
                previous = G;
                GameBootstrap.ReturnToFinalDecision(false, false);
                yield return RegressionWaitFor(() => G != null && G != previous && G.Director.CurrentBeat == "finale", 30f, "return to final decision " + (i + 1));
                g = G;
                ExperienceAssert(g.Flags.Has("m.n1.review.choice") && g.Flags.Has("m.n2.review.choice"), "earlier night choices survive final replay");
                ExperienceAssert(g.Flags.Has(MemoryFlags.N3LogoffEnabled), "earlier config choice survives final replay");
                ExperienceAssert(!g.Flags.Has(MemoryFlags.N3SaidStay), "the prior ending's stay decision is not carried into the new choice");
                ExperienceAssert(SaveSystem.Load().CheckpointFor(3)?.finalDecisionReplay == true, "final replay remains marked after automatic checkpoint save");
                ExperienceAssert(SaveSystem.Load().FinalDecisionForReplay().elapsed == 900f, "repeated final replays do not mutate the original snapshot");
                ExperienceAssert(!g.RecordsArmed, "an unarmed review remains excluded from records");
                SaveSystem.RecordNightComplete(g, "n3_keep", null, 10f);
            }
            Say("PASS final replay integration checks complete");
            GameBootstrap.ToTitle();
        }

        static NotepadApp FinalReplyPad()
        {
            return G.Apps.OpenApps.OfType<NotepadApp>().FirstOrDefault(p => p.WaitsForPlayer && p.Text.Contains("STAY WITH ME"));
        }

        static MessageBox StayChoiceBox()
        {
            return G.Windows.Windows.Select(w => w.Owner as MessageBox).FirstOrDefault(b => b != null && b.IsOpen
                && b.Button("Stay") != null && b.Button("Back") != null);
        }

        static IEnumerator FinalConsentCheck()
        {
            if (string.IsNullOrEmpty(SaveSystem.DirOverride))
                throw new InvalidOperationException("finalconsentcheck requires savedir PATH first");
            var previous = G;
            GameBootstrap.Restart(3, "finale");
            yield return RegressionWaitFor(() => G != null && G != previous && FinalReplyPad() != null, 100f, "final offer ready for refusal");
            var g = G;
            var pad = FinalReplyPad();
            pad.OnTyped("I don't want to stay\n", g.Player);
            yield return RegressionWaitFor(() => pad.WaitsForPlayer, 100f, "refusal receives another turn");
            ExperienceAssert(!g.Flags.Has(MemoryFlags.N3SaidStay) && StayChoiceBox() == null, "live explicit refusal preserves ending choices");
            pad.OnTyped("stay\n", g.Player);
            yield return RegressionWaitFor(() => pad.WaitsForPlayer && pad.Text.Contains("STAY UNTIL SEVEN"), 100f, "stay intent asks for confirmation");
            pad.OnTyped("I will not stay\n", g.Player);
            yield return RegressionWaitFor(() => pad.WaitsForPlayer, 100f, "confirmation refusal returns to the choice");
            ExperienceAssert(!g.Flags.Has(MemoryFlags.N3SaidStay) && StayChoiceBox() == null, "live confirmation refusal cannot commit KEEP");

            for (int i = 0; i < 2; i++)
            {
                previous = G;
                GameBootstrap.Restart(3, "finale");
                yield return RegressionWaitFor(() => G != null && G != previous && FinalReplyPad() != null, 100f, "fresh explicit consent fixture");
                g = G;
                pad = FinalReplyPad();
                pad.OnTyped("stay\n", g.Player);
                yield return RegressionWaitFor(() => pad.WaitsForPlayer && pad.Text.Contains("STAY UNTIL SEVEN"), 100f, "second stay confirmation ready");
                pad.OnTyped("yes\n", g.Player);
                yield return RegressionWaitFor(() => StayChoiceBox() != null, 100f, "explicit Back and Stay buttons visible");
                var box = StayChoiceBox();
                ExperienceAssert(box.Button("Back").IsDefault && !box.Button("Stay").IsDefault, "stay dialog focuses the reversible action");
                ExperienceAssert(!g.Flags.Has(MemoryFlags.N3SaidStay) && g.Clock.Frozen, "free text alone cannot commit and the choice can be read");
                float roundsElapsed = g.Rounds.Elapsed;
                yield return WaitSeconds(1f);
                ExperienceAssert(g.Rounds.Elapsed == roundsElapsed, "camera danger pauses while explicit choice is being read");
                box.Button(i == 0 ? "Back" : "Stay").Press(g.Player);
                yield return WaitSeconds(0.2f);
                ExperienceAssert(!g.Clock.Frozen, "closing the consent dialog releases the clock");
                ExperienceAssert(g.Flags.Has(MemoryFlags.N3SaidStay) == (i == 1), i == 0 ? "Back keeps final choices open" : "player click explicitly commits the intended stay choice");
            }
            Say("PASS final consent integration checks complete");
            GameBootstrap.ToTitle();
        }
    }
}
