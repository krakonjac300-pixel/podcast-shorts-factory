// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 2, second half: Gary (the third pointer) talks and brings his own record in, the order to shred
    /// employee_209.dat is fought over (finished, kept by archive, kept by the deadline), the first Custodial
    /// round runs on the Camera Viewer, and the night ends.
    /// </summary>
    public sealed partial class Night2Director
    {
        const float FinishDeadline = 150f;
        const float FinishHardCap = 175f;
        /// <summary>Ellen grabs a dragged 209 this close to the bin, whatever the drag's direction.</summary>
        const float Finish209Radius = 150f;
        /// <summary>...and this close while the drag heads for the bin (Phase F; 230 px any way before).</summary>
        const float Finish209HeadingRadius = 420f;
        /// <summary>The round beat's guard (the round itself lasts 80 s).</summary>
        const float RoundsGuard = 85f;
        const float GaryFaint = 0.3f;
        static readonly Vector2 Gary209Spot = new Vector2(480f, 200f);

        // finish beat state (events arrive from the shred pipeline and the file system)
        bool _finishOver;
        bool _triedShred209;
        bool _archivedByPlayer;
        bool _saidHelpYes;
        int _shredRequests209;
        Routine _garyRoutine;
        Action<string, CursorAgent> _onShredRequested;
        Action<string, MessageBox> _onConfirmShown;
        Action<string, ProgressDialog> _onProgressStarted;
        Action<VFile, string, string, Actor> _onFileMoved;
        Action<DragPayload> _onTugStarted;

        // rounds beat state
        Action<int> _onForcedOpen;
        Action _onPlayerReopen, _onReachedFinal, _onTimeUp;

        // ------------------------------------------------------------------ Gary's look

        /// <summary>Held and incomplete: faint amber hand, flickering, trembling (Tired), typos.</summary>
        void GaryHeldLook()
        {
            Gary.MaxAlpha = 0.75f;
            Gary.BaseFlicker = 0.08f;
            Gary.View.Flicker = 0.08f;
            Gary.TypoRate = 0.08f;
            Gary.View.ForcedShape = CursorShape.Hand;
        }

        /// <summary>Complete: a solid arrow, no flicker, no typos.</summary>
        void GaryFinishedLook()
        {
            Gary.MaxAlpha = 1f;
            Gary.BaseFlicker = 0f;
            Gary.View.Flicker = 0f;
            Gary.TypoRate = 0f;
            Gary.View.ForcedShape = CursorShape.Arrow;
        }

        void GaryPresentAt(Vector2 at, float alpha)
        {
            Gary.Teleport(at);
            Gary.SetPresent(true, 0.4f);
            Gary.FadeTo(alpha, 0.6f);
        }

        void PrepareGaryArrived()
        {
            var g = _g;
            g.Flags.Set(Flags.N2GaryArrived);
            GaryHeldLook();
            g.Taskbar.PointingDevices = 3;
            g.Files.SetHidden(ContentIds.File209, false);
            if (g.Files.Exists(ContentIds.File209) && g.Files.FolderOf(ContentIds.File209) != ContentIds.FolderDesktop)
                g.Files.Move(ContentIds.File209, ContentIds.FolderDesktop, Actor.Entity);
            g.Desktop.SetFilePosition(ContentIds.File209, OSLayers.WorldToDesktop(Gary209Spot) - new Vector2(37f, 16f));
            GaryPresentAt(Gary209Spot + new Vector2(40f, 30f), GaryFaint);
        }

        void PrepareFinishResolved()
        {
            var g = _g;
            g.Mail.Deliver(ContentIds.MailN2Urgent209, false);
            g.Flags.Set(Flags.UrgentOrderReceived);
            bool finished = g.Flags.Has(MemoryFlags.N2FinishedGary);
            if (!finished && !g.Flags.Has(MemoryFlags.N2KeptGary)) g.Flags.Set(MemoryFlags.N2KeptGary); // a jump past the choice keeps him
            if (finished)
            {
                if (g.Files.Exists(ContentIds.File209) && g.Files.Shred(ContentIds.File209, Actor.System)) g.Shred.MarkShredded();
                g.Tasks.ForceComplete(ContentIds.TaskN2Shred209);
                g.Tasks.Withdraw(ContentIds.TaskE2Archive209);
                GaryFinishedLook();
                Gary.SetPresent(false, 0f);
                g.Taskbar.PointingDevices = 2;
            }
            else
            {
                if (g.Flags.Has(MemoryFlags.N2ArchivedGary))
                {
                    if (g.Files.Exists(ContentIds.File209)) g.Files.Move(ContentIds.File209, ContentIds.FolderArchive, Actor.System);
                    g.Tasks.ForceComplete(ContentIds.TaskE2Archive209);
                }
                else
                {
                    g.Tasks.Withdraw(ContentIds.TaskE2Archive209);
                }
                g.Tasks.Withdraw(ContentIds.TaskN2Shred209);
            }
        }

        // ------------------------------------------------------------------ THIRD

        IEnumerator Third()
        {
            var g = _g;
            E.Brain.Enabled = false;
            yield return FlushEllenQueue();
            yield return Wait(4f);
            // Ellen steps back (her Notepad stays open). A third pointer wakes up at the bottom of the screen.
            yield return E.Vanish(0.8f);
            GaryHeldLook();
            g.Taskbar.PointingDevices = 3;
            g.Taskbar.BlinkDevice(3, 2.5f);
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("notify.pointer3"), "icon_info", null, "ui_select");
            g.Flags.Set(Flags.N2GaryArrived);
            var bottom = new Vector2(ScreenRig.Width * 0.5f, WindowManager.TaskbarHeight + 6f);
            Gary.Teleport(bottom);
            // Phase M (N2-3): the third pointer is a held person: he arrives as a breath, not with her swell.
            Scare("breath_near", 0.45f, 0f, 0.3f, 3f, ScareRules.IgnoreAllButStory);
            yield return Gary.Appear(bottom, 1.4f, false);
            yield return Wait(1.2f);

            // He opens his own Notepad (double-click on its icon, slowly) and talks.
            yield return OpenNotepadAs(_gary);
            yield return RunExchangeChain(_gary, ContentIds.ExchangeN2GaryOne, OnGaryReply, null, 3f, 3f, 25f, "hey",
                ex => ex.id == ContentIds.ExchangeN2GaryOne && g.Flags.Has(MemoryFlags.N1ReadNotes) ? Lines("g2_intro_mem_notes") : null);
            if (g.Flags.Has(MemoryFlags.N2WipedGary)) yield return TypeLines(_gary, Lines("g2_wiped"), 3f);

            // Ellen comes back and shuts him up.
            E.State = EntityState.Aggressive;
            yield return E.Appear(new Vector2(ScreenRig.Width + 6f, ScreenRig.Height * 0.6f), 0.3f, true);
            yield return CloseGaryPad();
            yield return TypeLines(_ellen, Lines("n2_gary_shut"), 4f);
            E.State = EntityState.Observing;

            // Gary drops back to the bottom edge, then brings his record in and loses his grip on the way.
            yield return Gary.MoveTo(new Vector2(Gary.Agent.Position.x, WindowManager.TaskbarHeight + 2f), MovementProfiles.Tired, 40f);
            yield return Gary.Vanish(0.6f);
            yield return Wait(3f);
            yield return GaryBrings209();
        }

        void OnGaryReply(DialogueReply r, string said)
        {
            _g.Flags.Set(MemoryFlags.N2TalkedGary);
            if (r.Tag == "glasses") _g.Flags.Set(MemoryFlags.N2Glasses);
        }

        /// <summary>
        /// Ellen clicks the close box of Gary's Notepad. Whatever covers it she pushes past; if the player
        /// guards it she jostles for 3 s, then closes it anyway with a glitch.
        /// </summary>
        IEnumerator CloseGaryPad()
        {
            var pad = EnsurePad(_gary);
            if (pad == null) yield break;
            var close = pad.Window.CloseButton;
            if (close == null) yield break;
            var closed = new bool[1];
            if (_g.Router.HitTest(close.Hit.Center, E.Agent) != close.Hit && !E.IsBlockedByOthers(close.Hit)) pad.Window.Focus(E.Agent);
            yield return E.ClickElement(close.Hit, MovementProfiles.Aggressive, closed, 3f);
            if (!closed[0] && pad.IsOpen && close != null && !E.IsBlockedByOthers(close.Hit))
            {
                pad.Window.Focus(E.Agent);
                yield return Wait(0.2f);
                if (close != null && pad.IsOpen) yield return E.ClickElement(close.Hit, MovementProfiles.Aggressive, closed, 1.5f);
            }
            if (!closed[0] && pad.IsOpen)
            {
                pad.Window.Close(E.Agent);
                _g.Fx.Glitch(0.15f, 0.7f);
                GameLog.Info(LogChannel.Entity, "Entity forced Gary's Notepad shut");
            }
            _gary.Pad = null;
        }

        /// <summary>Gary carries employee_209.dat in from the bottom edge and loses his grip 60% of the way.</summary>
        IEnumerator GaryBrings209()
        {
            var g = _g;
            if (!g.Files.Exists(ContentIds.File209)) yield break;
            g.Files.SetHidden(ContentIds.File209, false);
            var from = new Vector2(ScreenRig.Width * 0.5f, WindowManager.TaskbarHeight + 4f);
            var preferred = new Vector2(560f, 220f);
            yield return FindDropSpot(Vector2.Lerp(from, preferred, 0.6f));
            Vector2 spot = _dropSpot;
            Gary.Teleport(from);
            yield return Gary.Appear(from, 0.5f, false);
            yield return CarryFileIn(Gary, ContentIds.File209, spot, MovementProfiles.Tired);
            g.Audio.Play("mouse_release", 0.7f, 0.85f, Audio.AudioManager.PanFor(spot.x));
            Gary.View.Flinch(new Vector2(-3f, -4f), 0.5f);
            if (g.Files.Exists(ContentIds.File209) && g.Files.FolderOf(ContentIds.File209) != ContentIds.FolderDesktop)
                g.Files.Move(ContentIds.File209, ContentIds.FolderDesktop, Actor.Entity);
            g.Desktop.Attention(ContentIds.File209);
            GameLog.Info(LogChannel.Entity, "Gary dropped employee_209.dat on the desktop");
            var icon = g.Desktop.IconForFile(ContentIds.File209);
            if (icon != null) yield return Gary.Loiter(icon.Hit.Center + new Vector2(20f, 16f), 12f, 2f, MovementProfiles.Tired);
            Gary.FadeTo(GaryFaint, 1.2f);
            yield return Wait(1.2f);
        }

        // ------------------------------------------------------------------ FINISH

        IEnumerator Finish()
        {
            var g = _g;
            _freezeClock = false;
            g.Clock.Frozen = false;
            E.Phase = EntityPhase.Interference;
            _finishOver = false;
            _triedShred209 = _archivedByPlayer = _saidHelpYes = false;
            _shredRequests209 = 0;
            if (!Gary.IsVisible && g.Files.Exists(ContentIds.File209)) GaryPresentAt(Gary209Spot + new Vector2(40f, 30f), GaryFaint);

            g.Mail.Deliver(ContentIds.MailN2Urgent209);
            g.Flags.Set(Flags.UrgentOrderReceived);
            yield return Wait(1f);
            float orderAt = Time.time;
            // 3:00 lands on the deadline.
            g.Clock.Rate = Mathf.Max(0.01f, (float)((180.0 - g.Clock.ExactMinutes) / FinishDeadline));
            GiveTask(ContentIds.TaskN2Shred209);
            RunSide(TaskHints(ContentIds.TaskN2Shred209), "hints-209");
            HookFinish();

            // Ellen will not let it go in the bin. employee_017.dat stays "in use".
            var brain = E.Brain;
            brain.ProtectedFileId = ContentIds.File209;
            brain.AllowIdleLurk = false;
            brain.AllowKeepAway = true;
            brain.AllowCloseCamera = false;
            // She lets 209 go to Archive, never to the bin: she lunges once a drag is near the bin, or early
            // (Phase F) when it is clearly headed there, so the fight has room.
            brain.InterceptRadius = Finish209Radius;
            brain.InterceptRadiusHeading = Finish209HeadingRadius;
            brain.Enabled = true;
            E.State = EntityState.Defensive;
            g.Shred.IsInUse = id => id == ContentIds.File017;
            _garyRoutine = RunSide(GaryDuringFinish(orderAt), "gary-finish");

            string outcome = null;
            bool askedArchive = false;
            while (outcome == null)
            {
                if (brain.Defenses >= 1) brain.AllowIdleLurk = true;
                var file = g.Files.GetFile(ContentIds.File209);
                float elapsed = Time.time - orderAt;
                if (file == null || file.Shredded) outcome = "finished";
                else if (_archivedByPlayer) outcome = "archived";
                else if (g.Clock.TotalMinutes >= LateShredMinute && !g.Conflict.IsFighting && !g.Shred.Busy) outcome = "deadline";
                else if (elapsed > FinishHardCap)
                {
                    GameLog.Warn(LogChannel.Story, "Finish beat hit its hard cap");
                    g.Conflict.Interrupt();
                    g.Shred.Abort();
                    outcome = "deadline";
                }
                else if (!askedArchive && elapsed > 40f && !_triedShred209)
                {
                    askedArchive = true;
                    GiveEntityTask(ContentIds.TaskE2Archive209);
                    RunSide(SayDirect(_ellen, Lines("n2_archive_ask"), 4f), "archive-ask");
                }
                if (outcome == null) yield return null;
            }
            GameLog.Info(LogChannel.Story, "Gary: " + outcome);

            _finishOver = true;
            // No shred dialog outlives the decision (a confirm left open could still shred a kept 209).
            g.Shred.Abort();
            UnhookFinish();
            _garyRoutine?.Stop();
            _gary.Typing = false;
            Gary.Interrupt();
            brain.Enabled = false;
            brain.InterceptRadius = 0f;
            brain.InterceptRadiusHeading = 0f;
            E.Interrupt();
            E.Urgency = 1f;
            yield return Wait(1.2f);
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.6f, ScreenRig.Height * 0.55f), 0.4f, false);
            if (outcome == "finished") yield return GaryFinished();
            else yield return GaryKept(outcome == "archived");
        }

        void HookFinish()
        {
            UnhookFinish();
            var g = _g;
            _onShredRequested = (id, by) =>
            {
                if (id != ContentIds.File209 || by == null || !by.IsPlayer || _finishOver) return;
                _triedShred209 = true;
                _shredRequests209++;
                if (_shredRequests209 <= 2) RunSide(SayDirect(_ellen, Lines(_shredRequests209 == 1 ? "n2_protect" : "n2_protect_more"), 4.5f), "protect");
            };
            _onConfirmShown = (id, box) =>
            {
                if (id != ContentIds.File209 || _finishOver || !Gary.Agent.Enabled) return;
                bool say = !_saidHelpYes;
                _saidHelpYes = true;
                _gary.Typing = false;
                Gary.Run(GaryGuard(() => box.IsOpen ? box.Button("No")?.Hit : null, say), "gary-guard-no");
            };
            _onProgressStarted = (id, progress) =>
            {
                if (id != ContentIds.File209 || _finishOver || !Gary.Agent.Enabled) return;
                _gary.Typing = false;
                Gary.Run(GaryGuard(() => progress.IsOpen && progress.CancelButton != null ? progress.CancelButton.Hit : null, false), "gary-guard-cancel");
            };
            _onFileMoved = (f, from, to, actor) =>
            {
                if (f.Id == ContentIds.File209 && to == ContentIds.FolderArchive && actor == Actor.Player) _archivedByPlayer = true;
            };
            _onTugStarted = p => { if (p.FileId == ContentIds.File209) _triedShred209 = true; };
            g.Shred.Requested += _onShredRequested;
            g.Shred.ConfirmShown += _onConfirmShown;
            g.Shred.ProgressStarted += _onProgressStarted;
            g.Files.FileMoved += _onFileMoved;
            g.Conflict.TugStarted += _onTugStarted;
        }

        void UnhookFinish()
        {
            var g = _g;
            if (_onShredRequested != null) g.Shred.Requested -= _onShredRequested;
            if (_onConfirmShown != null) g.Shred.ConfirmShown -= _onConfirmShown;
            if (_onProgressStarted != null) g.Shred.ProgressStarted -= _onProgressStarted;
            if (_onFileMoved != null) g.Files.FileMoved -= _onFileMoved;
            if (_onTugStarted != null) g.Conflict.TugStarted -= _onTugStarted;
            _onShredRequested = null;
            _onConfirmShown = null;
            _onProgressStarted = null;
            _onFileMoved = null;
            _onTugStarted = null;
        }

        /// <summary>
        /// Gary during the order: he begs in his Notepad, and at +20 s and +50 s tries to drag himself to the bin
        /// (he never gets past 45% of the way). His guards (No, Cancel) interrupt whatever he is doing.
        /// </summary>
        IEnumerator GaryDuringFinish(float orderAt)
        {
            Gary.Run(TypeLines(_gary, Lines("g2_plea"), 3f), "gary-plea");
            bool try20 = false, try50 = false, saidDrop = false;
            int checks = 0;
            float nextShow = 1.5f;
            while (!_finishOver)
            {
                // The order is about a file you must be able to see. His Notepad (which opens 3 s in) and the player's own windows
                // can land on it, so the check repeats every 2 s for the first 24 s (Phase I: it used to run once, before his
                // Notepad covered the icon). It only moves the icon when something covers it.
                float sinceOrder = Time.time - orderAt;
                if (checks < 12 && sinceOrder > nextShow && !_triedShred209)
                {
                    checks++;
                    nextShow = sinceOrder + 2f;
                    yield return Ensure209Visible();
                }
                if (Gary.Busy) { yield return null; continue; }
                float elapsed = Time.time - orderAt;
                bool due = (!try20 && elapsed > 20f) || (!try50 && elapsed > 50f);
                if (due)
                {
                    if (elapsed > 50f) try50 = true;
                    try20 = true;
                    if (CanGaryDrag())
                    {
                        Gary.Run(GaryWeakDrag(!saidDrop), "gary-drag");
                        saidDrop = true;
                    }
                }
                yield return null;
            }
        }

        /// <summary>
        /// The order is about a file you must be able to see: if windows cover employee_209.dat, it is pulled
        /// out onto the nearest bare stretch of desktop (Gary's doing) and blinks.
        /// </summary>
        IEnumerator Ensure209Visible() => BringIntoView(ContentIds.File209, new Vector2(560f, 220f));

        bool CanGaryDrag()
        {
            var p = _g.Player.Payload;
            return _g.Files.Exists(ContentIds.File209) && _g.Files.FolderOf(ContentIds.File209) == ContentIds.FolderDesktop
                   && (p == null || p.FileId != ContentIds.File209) && !_g.Shred.Busy && !_g.Conflict.IsFighting;
        }

        /// <summary>
        /// He tries to drag his own record to the bin: presses, pulls toward the Disposal bin, and loses his
        /// grip after 0.6 to 0.9 s, never past 45% of the way. The file stays on the desktop.
        /// </summary>
        IEnumerator GaryWeakDrag(bool sayDrop)
        {
            var g = _g;
            yield return Ensure209Visible();
            var icon = g.Desktop.IconForFile(ContentIds.File209);
            if (icon == null) yield break;
            Gary.FadeTo(Gary.MaxAlpha, 0.3f);
            yield return Gary.MoveToElement(icon.Hit, MovementProfiles.Tired);
            icon = g.Desktop.IconForFile(ContentIds.File209);
            if (icon == null || !CanGaryDrag()) { Gary.FadeTo(GaryFaint, 0.5f); yield break; }
            Vector2 from = Gary.Agent.Position, bin = g.Desktop.DisposalIcon.Hit.Center;
            Vector2 stop = Vector2.Lerp(from, bin, UnityEngine.Random.Range(0.28f, 0.45f));
            GameLog.Info(LogChannel.Entity, "Gary tries to drag employee_209.dat to Disposal");
            Gary.Agent.SetButton(true);
            yield return null;
            yield return Wait(0.12f);
            Gary.Agent.Position += new Vector2(6f, -4f);
            yield return null;
            float hold = UnityEngine.Random.Range(0.6f, 0.9f), t = 0f;
            Vector2 start = Gary.Agent.Position;
            while (t < hold)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, t / hold);
                Gary.Agent.Position = Vector2.Lerp(start, stop, k) + UnityEngine.Random.insideUnitCircle * 1.6f;
                yield return null;
            }
            Gary.Agent.SetButton(false);
            yield return null;
            yield return null;
            g.Audio.Play("mouse_release", 0.6f, 0.85f, Audio.AudioManager.PanFor(stop.x));
            Gary.View.Flinch(new Vector2(-4f, 3f), 0.5f);
            if (g.Files.Exists(ContentIds.File209) && g.Files.FolderOf(ContentIds.File209) != ContentIds.FolderDesktop && g.Player.Payload == null)
            {
                // It fell into a window on the way: it is back where he dropped it.
                g.Files.Move(ContentIds.File209, ContentIds.FolderDesktop, Actor.Entity);
                g.Desktop.SetFilePosition(ContentIds.File209, OSLayers.WorldToDesktop(stop) - new Vector2(37f, 16f));
            }
            if (sayDrop) yield return TypeLines(_gary, Lines("g2_drop"), 3f);
            Gary.FadeTo(GaryFaint, 0.8f);
        }

        /// <summary>
        /// Gary covers a button for 3 s so the second cursor cannot click it (No on the confirm, then Cancel),
        /// then flickers out for 1.5 s. The first time he tells you to click Yes.
        /// </summary>
        IEnumerator GaryGuard(Func<Interactable> target, bool sayYes)
        {
            var el = target();
            if (el == null) yield break;
            Gary.FadeTo(Gary.MaxAlpha, 0.1f);
            var fast = MovementProfiles.Tired;
            fast.speed *= 4f;
            fast.reactionDelay = 0.02f;
            fast.pauseProbability = 0f;
            fast.microCorrections = 1;
            fast.maxDuration = 0.8f;
            yield return Gary.MoveToElement(el, fast);
            GameLog.Info(LogChannel.Entity, "Gary guards " + (el != null ? el.elementId : "?"));
            float end = Time.time + 3f;
            while (Time.time < end && (el = target()) != null && el.isActiveAndEnabled)
            {
                Gary.Guarding = el;
                Vector2 c = el.Center;
                if (Vector2.Distance(Gary.Agent.Position, c) > 14f)
                {
                    Gary.Guarding = null;
                    yield return Gary.MoveToDynamic(() => target() != null ? target().Center : (Vector2?)null, fast, 20f);
                }
                else
                {
                    Gary.Agent.Position = Vector2.Lerp(Gary.Agent.Position, c + UnityEngine.Random.insideUnitCircle * 1.5f, 0.2f);
                }
                yield return null;
            }
            Gary.Guarding = null;
            Gary.FadeTo(0.1f, 0.15f);
            // He slips off the button as he fades (a faint cursor never blocks anything).
            var off = ScreenRig.ClampToScreen(Gary.Agent.Position + new Vector2(UnityEngine.Random.Range(-60f, 60f), -50f));
            yield return Gary.MoveTo(off, MovementProfiles.Tired, 30f);
            yield return Wait(1f);
            Gary.FadeTo(GaryFaint, 0.5f);
            if (sayYes) yield return TypeLines(_gary, Lines("g2_help_yes"), 3f);
        }

        const float ThanksSilence = 1.2f, ThanksCaretHold = 2f;

        /// <summary>Finished: Gary thanks you mid-word, turns into a clean arrow and leaves in a straight line.</summary>
        IEnumerator GaryFinished()
        {
            var g = _g;
            g.Flags.Set(MemoryFlags.N2FinishedGary);
            g.Memory.Record(MemoryKind.ResistedEntity, "gary", Time.time);
            if (g.Tasks.IsActive(ContentIds.TaskE2Archive209)) g.Tasks.Withdraw(ContentIds.TaskE2Archive209, Expired);
            Gary.FadeTo(Gary.MaxAlpha, 0.3f);
            yield return TypeLines(_gary, Lines("g2_thanks"), 3f);
            // M7: the room goes silent for 1.2 s where the word stops, and his caret keeps blinking for 2 s before he goes.
            g.Audio.SetAmbience(false, 0.05f);
            if (_gary.Pad != null) _gary.Pad.ThinkingCaret = true;
            yield return Wait(ThanksSilence);
            g.Audio.SetAmbience(true, 0.6f);
            yield return Wait(ThanksCaretHold - ThanksSilence);
            if (_gary.Pad != null) _gary.Pad.ThinkingCaret = false;
            GaryFinishedLook();
            Gary.SetPresent(true, 0.2f);
            yield return Wait(0.9f);
            yield return Gary.MoveTo(new Vector2(-14f, Gary.Agent.Position.y), MovementProfiles.Mechanical, 30f);
            Gary.SetPresent(false, 0.1f);
            g.Taskbar.PointingDevices = 2;
            yield return Wait(1.2f);
            yield return TypeLines(_ellen, Lines("n2_finished"), 3.5f);
        }

        /// <summary>Kept: the order is suspended and Gary stays, faint (archived: where they put the quiet ones).</summary>
        IEnumerator GaryKept(bool archived)
        {
            var g = _g;
            g.Flags.Set(MemoryFlags.N2KeptGary);
            if (archived)
            {
                g.Flags.Set(MemoryFlags.N2ArchivedGary);
                g.Memory.Record(MemoryKind.ObeyedEntity, "gary", Time.time);
            }
            else if (g.Tasks.IsActive(ContentIds.TaskE2Archive209))
            {
                g.Tasks.Withdraw(ContentIds.TaskE2Archive209, Expired);
            }
            // Phase H: the order stays in the queue, struck through with why, and the notice says what was missed.
            if (!Done(ContentIds.TaskN2Shred209))
                g.Tasks.Withdraw(ContentIds.TaskN2Shred209, archived ? g.Content.Text("workqueue.withdrawn.suspended")
                    : g.Content.Format("workqueue.withdrawn.missed", g.Content.Task(ContentIds.TaskN2Shred209)?.deadline ?? "3:00 AM"));
            g.Notifications.Show(g.Content.Text("app.workqueue"), g.Content.Text(archived ? "notify.order.suspended" : "notify.order.missed"), "icon_info", null, "sys_warning");
            if (Gary.Agent.Enabled)
            {
                Gary.FadeTo(Gary.MaxAlpha * 0.8f, 0.4f);
                yield return TypeLines(_gary, Lines(archived ? "g2_archived" : "g2_kept"), 3f);
                Gary.FadeTo(GaryFaint, 1f);
            }
            yield return TypeLines(_ellen, Lines("n2_kept"), 3.5f);
        }

        // ------------------------------------------------------------------ ROUNDS

        IEnumerator RoundsBeat()
        {
            var g = _g;
            yield return EnsureClockAtLeast(3, 0, 5f);
            g.Clock.Rate = Rate;
            g.Flags.Set(Flags.CameraUnlocked);
            g.Mail.Deliver(ContentIds.MailN2SecurityRounds);
            yield return Wait(2f);
            bool finished = g.Flags.Has(MemoryFlags.N2FinishedGary);

            // Ellen now closes the viewer for you whenever it shows Custodial.
            var brain = E.Brain;
            brain.ProtectedFileId = null;
            brain.AllowIdleLurk = false; // a slow lurk would keep her from the close box
            brain.AllowCloseCamera = true;
            brain.Enabled = true;
            E.State = EntityState.Observing;

            bool reachedDoor = false, timeUp = false, saidRounds = false, saidAgain = false;
            UnhookRounds();
            int forcedOpens = 0;
            _onForcedOpen = i =>
            {
                forcedOpens++;
                // Phase M (N2-bed): the first Custodial round finally has air: a low drone from Security's first open.
                if (forcedOpens == 1) g.Audio.PlayLoop("drone_tension", RoundsDrone, 4f);
                if (!saidRounds)
                {
                    saidRounds = true;
                    RunSide(SayDirect(_ellen, Lines("n2_rounds"), 4.5f), "rounds-line");
                    return;
                }
                if (forcedOpens != 2 || saidAgain) return;
                // Nobody reopened the viewer: the one lever that matters on Night 3 (look elsewhere) is said anyway.
                saidAgain = true;
                var again = Lines("n2_rounds_again");
                if (again != null && again.Length > 1) RunSide(SayDirect(_ellen, new[] { again[again.Length - 1] }, 4.5f), "rounds-look-away");
            };
            _onPlayerReopen = () =>
            {
                if (saidAgain) return;
                saidAgain = true;
                RunSide(SayDirect(_ellen, Lines("n2_rounds_again"), 4.5f), "rounds-again");
            };
            _onReachedFinal = () => reachedDoor = true;
            _onTimeUp = () => timeUp = true;
            g.Rounds.ForcedOpen += _onForcedOpen;
            g.Rounds.PlayerReopened += _onPlayerReopen;
            g.Rounds.ReachedFinal += _onReachedFinal;
            g.Rounds.TimeUp += _onTimeUp;
            g.Rounds.ForcedBy = null;
            // Phase I: Personnel 000 says where Custodial is, as on Night 3 (the rounds hint sends you there).
            g.Rounds.PatchPersonnel = true;
            g.Rounds.Begin(RoundsConfig.Night2(g.Difficulty.Mode, g.Memory.Trust));
            // Phase H: what Security asks for during the round is a line in the queue, not only a mail.
            GiveTask(ContentIds.TaskN2RoundsWatch);

            float start = Time.time;
            bool emptied = false;
            while (!reachedDoor && !timeUp && Time.time - start < RoundsGuard)
            {
                if (finished && !emptied && Time.time - start >= 20f)
                {
                    // Custodial takes what was finished.
                    emptied = true;
                    g.Shred.ResetBin();
                    g.Notifications.Show(g.Content.Text("app.disposal"), g.Content.Text("notify.disposal.emptied"), "icon_disposal_empty", null, "sys_warning");
                }
                yield return null;
            }
            g.Rounds.Stop();
            UnhookRounds();
            if (!reachedDoor) g.Audio.StopLoop("drone_tension", 2f);
            if (g.Tasks.IsActive(ContentIds.TaskN2RoundsWatch)) g.Tasks.ForceComplete(ContentIds.TaskN2RoundsWatch);
            brain.Enabled = false;
            brain.AllowCloseCamera = false;
            E.Interrupt();
            var rig = g.CameraRig;
            if (reachedDoor)
            {
                // It reached the doorway and stood there long enough: it knocks, the door is flung wide and the feed dies (Phase M, 5.4).
                GameLog.Info(LogChannel.Story, "Rounds: Custodial reached the doorway");
                yield return DoorClimax(rig);
                var cam = g.Apps.Find<CameraApp>();
                if (cam != null) cam.Window.Close(null);
                rig.SignalLost = false;
                g.Audio.SetAmbience(true, 3f);
                EndClimax();
                g.Flags.Set(MemoryFlags.N2WatchedToDoor);
                yield return TypeLines(_ellen, Lines("n2_rounds_door"), 4f);
            }
            else
            {
                g.Notifications.Show(g.Content.Text("app.camera"), g.Content.Text("rounds.end"), "icon_camera", null, "ui_select");
            }
            rig.Figure = FigureStage.None;
            g.Flags.Clear(Flags.CameraUnlocked);
            g.Flags.Set(Flags.N2RoundsDone);
            yield return Wait(1f);
        }

        const float RoundsDrone = 0.12f;
        /// <summary>The door climax: the knocks start this long after it reaches the doorway; the hit lands this long after that.</summary>
        const float KnockAfter = 0.5f, KnockToHit = 1.6f;

        /// <summary>
        /// Night 2's door (5.4), only for a player who watched it to the doorway: the room and the drone drop out, three knocks shake the
        /// door, the hit, and the door is flung wide under NO SIGNAL for two silent seconds.
        /// </summary>
        IEnumerator DoorClimax(SecurityCameraRig rig)
        {
            var g = _g;
            BeginClimax();
            g.Audio.SetAmbienceLevel(0f, 0.8f);
            g.Audio.StopLoop("drone_tension", 0.8f);
            yield return Wait(KnockAfter);
            g.Audio.Play("knock_door", 0.9f, 1f, -0.1f);
            float t = 0f, door = rig.DoorOpen;
            int knocks = 0;
            float[] knockAt = { 0.05f, 0.58f, 1.12f };
            while (t < KnockToHit - StingerPreRoll)
            {
                t += Time.deltaTime;
                // The door jolts with each knock.
                if (knocks < knockAt.Length && t >= knockAt[knocks]) rig.DoorOpen = door + 0.03f * ++knocks;
                yield return null;
            }
            rig.Quiet = true;
            yield return Hit(0.6f, 0.8f, 3f, 0f, "static_burst");
            g.Audio.Play("static_burst", 0.9f);
            rig.DoorOpen = 1f;
            rig.SignalLost = true;
            yield return Wait(2f);
        }

        void UnhookRounds()
        {
            var r = _g.Rounds;
            if (r == null) return;
            if (_onForcedOpen != null) r.ForcedOpen -= _onForcedOpen;
            if (_onPlayerReopen != null) r.PlayerReopened -= _onPlayerReopen;
            if (_onReachedFinal != null) r.ReachedFinal -= _onReachedFinal;
            if (_onTimeUp != null) r.TimeUp -= _onTimeUp;
            _onForcedOpen = null;
            _onPlayerReopen = _onReachedFinal = _onTimeUp = null;
        }

        // ------------------------------------------------------------------ ENDING

        IEnumerator EndingBeat()
        {
            var g = _g;
            StopSideRoutines();
            yield return Wait(3f);
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("session.suspended"), "icon_warning", null, "sys_warning");
            yield return Wait(2.5f);
            bool finished = g.Flags.Has(MemoryFlags.N2FinishedGary);
            int late = g.Flags.Get(LateShredFlag);
            string id = finished ? ContentIds.EndingN2Finished : ContentIds.EndingN2Kept;
            CompleteNight(id);
            var spec = new EndingSpec
            {
                Id = id,
                Lines = Lines(finished ? "n2_end_finished" : "n2_end_kept"),
                GaryLines = finished ? null : Lines("g2_goodnight"),
                TitleKey = "end.n2.title",
                SubtitleKey = finished ? "end.n2.subtitle.finished" : "end.n2.subtitle.kept",
                // Phase J: like Night 1's card, one line says what the player's choice about 209 was (Phase K: a late shred too).
                Outcome = finished ? g.Content.Text("end.n2.outcome.finished")
                    : g.Flags.Has(MemoryFlags.N2ArchivedGary) ? g.Content.Text("end.n2.outcome.archived")
                    : late > 0 ? g.Content.Format(late > LateShredMinute ? "end.n2.outcome.late" : "end.n2.outcome.late0", GameClock.Format12(late), late - LateShredMinute)
                    : g.Content.Text("end.n2.outcome.missed"),
                DemoCard = false,
                ContinueNight = 3,
            };
            _ending = new EndingSequence(g, spec);
            yield return _ending.Run();
        }
    }
}
#endif
