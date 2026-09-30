// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Entity;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 3, the Ruth beat (spec 5.3 N3.3): a missed call from ext. 2118 at 2:17, the Intake files flicker
    /// into 0217.dat, Ruth's mail about the comment she finally wrote, Batch 48, and the Restricted code with
    /// its hints. The clock then runs on to 3:00.
    /// </summary>
    public sealed partial class Night3Director
    {
        const float RuthMinSeconds = 150f;
        const float RuthMaxSeconds = 300f;
        /// <summary>
        /// Phase K (finding 6): once the queue says "Nothing to do until then", the wait is at most this long before the clock runs
        /// to 3:00 (over <see cref="ToRoundsSeconds"/>), however quickly the chores were done; the fourth tester waited 20 s on one
        /// pass and four minutes on the replay.
        /// </summary>
        const float WaitRoundsCap = 25f, ToRoundsSeconds = 12f;
        float _waitRoundsSince = -1f;

        /// <summary>Ellen present and lurking with her brain on (after a jump past her arrival).</summary>
        IEnumerator EnsureEllenLurking(bool keepAway = false)
        {
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width + 6f, ScreenRig.Height * 0.6f), 0.5f, false);
            _ellen.Direct = true;
            E.State = EntityState.Observing;
            var brain = E.Brain;
            brain.ProtectedFileId = ContentIds.File017;
            brain.AllowKeepAway = keepAway;
            brain.AllowIdleLurk = true;
            brain.InterceptRadius = 230f;
            brain.Enabled = true;
        }

        IEnumerator Ruth()
        {
            var g = _g;
            yield return EnsureEllenLurking();
            _holdAt = -1;
            if (g.Clock.TotalMinutes < Night3Rules.RuthCall) yield return EnsureClockAtLeast(2, 17, 3f);
            g.Clock.Frozen = false;
            g.Clock.Rate = Rate;
            _holdAt = RuthHold;
            yield return Wait(2f);

            // 2:17: the desk phone rings twice (there is no desk phone), and the files in Intake flicker into the code.
            RunSide(ZeroTwoSeventeen(), "0217");
            g.Audio.Play("phone_ring", 0.9f);
            yield return Wait(3f);
            g.Audio.Play("phone_ring", 0.9f);
            yield return Wait(2f);
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("notify.missedcall"), "icon_info", null, "sys_warning");
            GameLog.Info(LogChannel.Story, "Missed call: ext. 2118");
            yield return Wait(8f);
            g.Mail.Deliver(ContentIds.MailN3RuthComment);
            float mailAt = Time.time;
            yield return Wait(2f);

            GiveTask(ContentIds.TaskN3Batch48);
            RunSide(BatchHints(ContentIds.TaskN3Batch48), "hints-48");
            if (GaryFinished) RunSide(GaryDoesBatch48(), "gary-48");
            RunSide(RuthExchange(), "ruth-exchange");
            RunSide(CodeHints(), "code-hints");
            RunSide(WaitForRoundsLine(), "wait-rounds");

            // At least 150 s after the mail (or the wait line's cap), and the batch done; at 300 s the batch is done for you.
            _waitRoundsSince = -1f;
            while (!(Done(ContentIds.TaskN3Batch48) && (Time.time - mailAt >= RuthMinSeconds || (_waitRoundsSince > 0f && Time.time - _waitRoundsSince >= WaitRoundsCap))))
            {
                if (Time.time - mailAt >= RuthMaxSeconds)
                {
                    GameLog.Warn(LogChannel.Story, "Batch 48 archived by the safety net");
                    FileTheRest(ContentIds.TaskN3Batch48);
                    break;
                }
                yield return null;
            }
            // Let her finish what she is saying, then the clock runs on to 3:00.
            float talk = Time.time + 20f;
            while (_ellen.Typing && Time.time < talk) yield return null;
            _holdAt = -1;
            yield return EnsureClockAtLeast(3, 0, ToRoundsSeconds);
        }

        /// <summary>
        /// Phase H: once Batch 48 is archived the queue says what comes next (Custodial at 3:00), so the quiet stretch
        /// before the round reads as intended. The round beat ticks it.
        /// </summary>
        IEnumerator WaitForRoundsLine()
        {
            yield return WaitUntil(() => Done(ContentIds.TaskN3Batch48), 900f);
            if (!Done(ContentIds.TaskN3Batch48) || CurrentBeat != "ruth") yield break;
            yield return Wait(1.5f);
            if (CurrentBeat != "ruth") yield break;
            GiveTask(ContentIds.TaskN3WaitRounds);
            _waitRoundsSince = Time.time;
        }

        /// <summary>Every .dat in Intake is called 0217.dat for 2.5 s (a glitch and a click you did not make).</summary>
        IEnumerator ZeroTwoSeventeen()
        {
            var g = _g;
            var renamed = new List<(string id, string name)>();
            foreach (var f in g.Files.FilesIn(ContentIds.FolderIntake))
            {
                if (f.Extension != "dat") continue;
                renamed.Add((f.Id, f.Name));
                g.Files.Rename(f.Id, "0217.dat");
            }
            if (renamed.Count == 0) yield break;
            g.Fx.Glitch(0.2f, 0.8f);
            g.Audio.Play("glitch_burst", 0.5f);
            var fm = g.Apps.Find<FilesApp>();
            var row = fm != null && !fm.Window.IsMinimized ? fm.RowFor(renamed[0].id) : null;
            PhantomClick(row != null ? row.Hit.Center : new Vector2(ScreenRig.Width * 0.5f, ScreenRig.Height * 0.5f));
            GameLog.Info(LogChannel.Story, "Anomaly: " + renamed.Count + " Intake file(s) renamed 0217.dat");
            yield return Wait(2.5f);
            foreach (var (id, name) in renamed)
                if (g.Files.GetFile(id) != null) g.Files.Rename(id, name);
            g.Fx.Glitch(0.1f, 0.4f);
        }

        /// <summary>The company hint toasts for a task, without forcing it (the beat decides what happens).</summary>
        IEnumerator BatchHints(string taskId)
        {
            var d = _g.Difficulty;
            float next = Time.time + d.TaskHintFirst;
            while (!Done(taskId) && !_g.Tasks.IsWithdrawn(taskId))
            {
                if (Time.time > next)
                {
                    next = Time.time + d.TaskHintRepeat;
                    ShowTaskHint(taskId);
                }
                yield return null;
            }
        }

        /// <summary>When the player reads Ruth's mail, Ellen talks about her (one exchange).</summary>
        IEnumerator RuthExchange()
        {
            yield return WaitUntil(() => _g.Mail.IsRead(ContentIds.MailN3RuthComment), 900f);
            if (!_g.Mail.IsRead(ContentIds.MailN3RuthComment)) yield break;
            yield return Wait(2.5f);
            _ellen.Direct = true;
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeN3Ruth, OnEllenReply, null, 2.6f, 4f, 25f, "DONT");
        }

        void OnEllenReply(DialogueReply r, string said)
        {
            // Phase J: her name counts in any reply ("goodbye ellen, i will let you go" is answered as a goodbye).
            string norm = DialogueEngine.Normalize(said);
            if (r.Tag == "name" || DialogueEngine.Matches(norm, "=ellen") || DialogueEngine.Matches(norm, "=marsh")) _g.Flags.Set(MemoryFlags.N3SaidName);
        }

        bool CodeSolved => !_g.Files.IsInsideLocked(ContentIds.FolderRestricted) || _g.Flags.Has(MemoryFlags.N3RestrictedOpen);

        /// <summary>
        /// The Restricted code's hints (spec 7.6), timed from reading Ruth's mail: Ellen at 60 s (or right after
        /// the first wrong code, whichever is later), kept Gary at 120 s, Ellen again at 180 s. Nothing is said
        /// once the folder is open, and nothing during the round.
        /// </summary>
        IEnumerator CodeHints()
        {
            var g = _g;
            var d = g.Difficulty;
            yield return WaitUntil(() => g.Mail.IsRead(ContentIds.MailN3RuthComment) || CodeSolved, 1200f);
            if (CodeSolved) yield break;
            float t0 = Time.time;
            bool Due(float at) => Time.time - t0 >= at;
            // A player who has the code prompt open is left to try first (at most 20 s more).
            yield return WaitUntil(() => CodeSolved || Due(d.CodeHint1Delay), 1200f);
            if (!CodeSolved && g.Apps.Find<AuthPromptApp>() != null)
                yield return WaitUntil(() => CodeSolved || g.Apps.Find<AuthPromptApp>() == null || Due(d.CodeHint1Delay + 20f), 30f);
            if (CodeSolved) yield break;
            yield return QuietHint(_ellen, "n3_code_hint1");
            if (!GaryFinished)
            {
                yield return WaitUntil(() => CodeSolved || Due(d.CodeGaryHint), 1200f);
                if (CodeSolved) yield break;
                yield return QuietHint(_gary, "g3_code");
            }
            yield return WaitUntil(() => CodeSolved || Due(d.CodeHint2Delay), 1200f);
            if (CodeSolved) yield break;
            yield return QuietHint(_ellen, "n3_code_hint2");
        }

        /// <summary>A hint typed when nothing else is going on (never during the round or the ending).</summary>
        IEnumerator QuietHint(Speaker s, string lineSet)
        {
            yield return WaitUntil(() => CurrentBeat != "rounds" && !s.Typing, 900f);
            if (CurrentBeat == "ending" || CodeSolved) yield break;
            if (s == _gary) yield return GaryPresentFor(3f);
            yield return Say(s, Lines(lineSet), s == _gary ? GaryCps : 4f);
            GameLog.Info(LogChannel.Story, "Code hint: " + lineSet);
        }
    }
}
#endif
