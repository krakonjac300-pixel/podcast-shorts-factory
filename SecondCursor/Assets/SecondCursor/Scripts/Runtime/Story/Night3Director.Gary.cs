// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 3's Gary (spec 2.5 and 5.3). Kept on Night 2: a faint, trembling amber hand that helps where he can
    /// (a code hint, looking away from the round, unlocking the log off). Finished: a clean, flat company pointer
    /// that does your work, forces the camera open and fights your log off.
    /// </summary>
    public sealed partial class Night3Director
    {
        const float GaryFaint = 0.3f;

        float GaryCps => GaryFinished ? 5f : 3f;
        MovementProfileData GaryMove => GaryFinished ? MovementProfiles.Mechanical : MovementProfiles.Tired;

        /// <summary>Held and incomplete: faint amber hand, flickering, trembling, typos.</summary>
        void GaryHeldLook()
        {
            Gary.MaxAlpha = 0.75f;
            Gary.BaseFlicker = 0.08f;
            Gary.View.Flicker = 0.08f;
            Gary.TypoRate = 0.08f;
            Gary.View.ForcedShape = CursorShape.Hand;
            _gary.MoveProfile = MovementProfiles.TiredName;
        }

        /// <summary>Complete: a solid arrow, no flicker, no tremor, no typos.</summary>
        void GaryFinishedLook()
        {
            Gary.MaxAlpha = 1f;
            Gary.BaseFlicker = 0f;
            Gary.View.Flicker = 0f;
            Gary.TypoRate = 0f;
            Gary.View.ForcedShape = CursorShape.Arrow;
            _gary.MoveProfile = MovementProfiles.MechanicalName;
        }

        /// <summary>Gary shows up (bottom edge if he was gone) and is fully there for a moment, then fades to his resting presence.</summary>
        IEnumerator GaryPresentFor(float seconds)
        {
            if (!Gary.IsVisible)
            {
                var at = new Vector2(ScreenRig.Width * 0.5f + UnityEngine.Random.Range(-80f, 80f), WindowManager.TaskbarHeight + 8f);
                Gary.Teleport(at);
                yield return Gary.Appear(at, GaryFinished ? 0.3f : 1f, false);
            }
            Gary.FadeTo(Gary.MaxAlpha, 0.3f);
            RunSide(GaryRestLater(seconds), "gary-rest");
        }

        IEnumerator GaryRestLater(float seconds)
        {
            yield return Wait(seconds);
            if (!GaryFinished && Gary.IsVisible && !Gary.Busy) Gary.FadeTo(GaryFaint, 1f);
        }

        // ------------------------------------------------------------------ work

        /// <summary>Kept: when Batch 47 is given he surfaces, faint, and says hello in his own Notepad.</summary>
        IEnumerator GaryKeptHello()
        {
            var bottom = new Vector2(ScreenRig.Width * 0.46f, WindowManager.TaskbarHeight + 6f);
            Gary.Teleport(bottom);
            _g.Taskbar.BlinkDevice(3, 2.5f);
            yield return Gary.Appear(bottom, 1.4f, false);
            yield return Wait(1f);
            yield return OpenNotepadAs(_gary);
            yield return TypeLines(_gary, Lines("g3_intro"), GaryCps);
            _gary.Direct = true;
            Gary.FadeTo(GaryFaint, 1.2f);
        }

        /// <summary>
        /// Finished: when Batch 47 is given he arrives solid from the left edge, opens File Manager on Intake if
        /// needed, drags one of the batch onto Archive for you, and says it is easier finished.
        /// </summary>
        IEnumerator GaryDoesBatch47()
        {
            yield return Wait(4f);
            var from = new Vector2(-10f, ScreenRig.Height * 0.55f);
            Gary.Teleport(from);
            yield return Gary.Appear(from, 0.3f, false);
            yield return GaryArchiveOne(Batch47);
            yield return OpenNotepadAs(_gary);
            yield return TypeLines(_gary, Lines("g3c_intro"), GaryCps);
            yield return TypeLines(_gary, Lines("g3c_easier"), GaryCps);
            _gary.Direct = true;
        }

        /// <summary>Finished: Batch 48 is archived by him within 20 s (a move task does not ask who moved the files).</summary>
        IEnumerator GaryDoesBatch48()
        {
            yield return Wait(6f);
            if (!Gary.IsVisible)
            {
                var from = new Vector2(-10f, ScreenRig.Height * 0.5f);
                Gary.Teleport(from);
                yield return Gary.Appear(from, 0.3f, false);
            }
            float until = Time.time + 20f;
            while (Time.time < until && !Done(ContentIds.TaskN3Batch48))
            {
                if (Gary.Busy) { yield return null; continue; }
                yield return GaryArchiveOne(Batch48);
                yield return Wait(0.4f);
            }
            // Whatever his hand did not finish, the batch is his by 20 s.
            foreach (var f in Batch48)
                if (_g.Files.Exists(f) && _g.Files.FolderOf(f) != ContentIds.FolderArchive) _g.Files.Move(f, ContentIds.FolderArchive, Actor.Entity);
        }

        /// <summary>Gary drags the next file of a batch onto the Archive folder row, opening File Manager where it is.</summary>
        IEnumerator GaryArchiveOne(string[] batch)
        {
            string next = null;
            foreach (var f in batch)
                if (_g.Files.Exists(f) && _g.Files.FolderOf(f) != ContentIds.FolderArchive) { next = f; break; }
            if (next == null) yield break;
            if (_g.Player.Payload != null && _g.Player.Payload.FileId == next) yield break;
            bool onDesktop = _g.Files.FolderOf(next) == ContentIds.FolderDesktop;
            var fm = _g.Apps.Find<FilesApp>();
            if (fm == null || fm.Window.IsMinimized || (!onDesktop && fm.FolderId != _g.Files.FolderOf(next)))
            {
                fm = _g.Apps.OpenFolder(onDesktop ? ContentIds.FolderIntake : _g.Files.FolderOf(next), Gary.Agent);
                yield return Wait(0.6f);
            }
            if (fm == null || !fm.IsOpen) yield break;
            fm.Window.Focus(Gary.Agent);
            yield return Wait(0.2f);
            var source = onDesktop ? _g.Desktop.IconForFile(next)?.Hit : fm.RowFor(next)?.Hit;
            var target = fm.FolderRowFor(ContentIds.FolderArchive);
            if (source == null || target == null) yield break;
            yield return Gary.DragTo(source, () => target != null && target.isActiveAndEnabled ? target.Center : (Vector2?)null, GaryMove);
            yield return Wait(0.3f);
            if (_g.Files.FolderOf(next) == ContentIds.FolderArchive) GameLog.Info(LogChannel.Entity, "Gary archived " + next);
        }

        // ------------------------------------------------------------------ rounds

        /// <summary>
        /// Finished Gary performs Security's forced opens himself: double-clicks the Camera Viewer icon and
        /// clicks the camera. If he cannot (busy, the icon is covered) the viewer opens by itself.
        /// </summary>
        void GaryForcedOpen(string camera)
        {
            if (Gary.Busy || _g.Conflict.IsFighting)
            {
                _g.Rounds.ShowOnViewer(camera, null);
                return;
            }
            Gary.Run(GaryOpensViewer(camera), "gary-forced-open");
        }

        IEnumerator GaryOpensViewer(string camera)
        {
            if (!Gary.IsVisible)
            {
                var from = new Vector2(-10f, ScreenRig.Height * 0.4f);
                Gary.Teleport(from);
                yield return Gary.Appear(from, 0.2f, false);
            }
            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null || cam.Window.IsMinimized)
            {
                if (cam == null) yield return Gary.OpenApp(AppIds.Camera, MovementProfiles.Mechanical);
                else cam.Window.Restore(Gary.Agent);
                yield return Wait(0.3f);
                cam = _g.Apps.Find<CameraApp>();
            }
            if (cam != null && cam.CurrentCamera != camera)
            {
                var button = cam.Window.Element("camera:" + camera);
                if (button != null) yield return Gary.ClickElement(button, MovementProfiles.Mechanical, null, 1.5f);
            }
            // Whatever his hand managed, the round counts the open (and the viewer is on the right camera).
            _g.Rounds.ShowOnViewer(camera, Gary.Agent);
        }

        /// <summary>
        /// Kept Gary, 1.0 to 1.5 s after a forced open, clicks a camera that does not show the figure ("look
        /// where it isn't").
        /// </summary>
        IEnumerator GaryLooksAway()
        {
            yield return Wait(UnityEngine.Random.Range(1.0f, 1.5f));
            var cam = _g.Apps.Find<CameraApp>();
            var model = _g.Rounds.Model;
            if (cam == null || !cam.IsOpen || cam.Window.IsMinimized || model == null || model.Finished) yield break;
            string away = model.FigureCamera == ContentIds.Cam02 ? ContentIds.Cam01 : ContentIds.Cam02;
            if (model.FigureCamera == ContentIds.Cam01) away = ContentIds.Cam03;
            var button = cam.Window.Element("camera:" + away);
            if (button == null) yield break;
            Gary.FadeTo(Gary.MaxAlpha, 0.2f);
            var fast = MovementProfiles.Tired;
            fast.speed *= 4f;
            fast.reactionDelay = 0.02f;
            fast.pauseProbability = 0f;
            fast.microCorrections = 1;
            fast.maxDuration = 0.8f;
            var clicked = new bool[1];
            yield return Gary.ClickElement(button, fast, clicked, 1.2f);
            if (!clicked[0] && cam.IsOpen) cam.Select(away, Gary.Agent);
            GameLog.Info(LogChannel.Entity, "Gary switched the viewer to " + away);
            Gary.FadeTo(GaryFaint, 1f);
        }

        // ------------------------------------------------------------------ finale

        /// <summary>
        /// Kept Gary at 6:48, if log off is still disabled: he opens Restricted (a cursor needs no code), opens
        /// session.cfg, takes the 0 back, types 1 and saves it. Restricted stays open for the player.
        /// </summary>
        IEnumerator GaryEnablesLogOff()
        {
            var g = _g;
            yield return GaryPresentFor(30f);
            yield return Say(_gary, Lines("g3_logoff"), GaryCps);
            var fm = g.Apps.OpenFolder(ContentIds.FolderRestricted, Gary.Agent);
            yield return Wait(0.8f);
            var row = fm != null && fm.IsOpen ? fm.RowFor(ContentIds.FileSessionCfg) : null;
            if (row != null) yield return Gary.ClickElement(row.Hit, GaryMove, null, 2f, true);
            yield return Wait(0.5f);
            NotepadApp pad = null;
            foreach (var app in g.Apps.OpenApps)
                if (app is NotepadApp n && n.IsOpen && n.FileId == ContentIds.FileSessionCfg) pad = n;
            if (pad == null) pad = g.Apps.OpenFile(ContentIds.FileSessionCfg, Gary.Agent) as NotepadApp;
            if (pad == null) yield break;
            pad.Window.Focus(Gary.Agent);
            yield return Gary.MoveTo(pad.Window.Client.WorldCenter(), GaryMove, 40f);
            if (!pad.Text.EndsWith("ALLOW_LOGOFF=1", StringComparison.Ordinal))
            {
                if (pad.Text.EndsWith("ALLOW_LOGOFF=0", StringComparison.Ordinal)) yield return pad.TypeAsEntity("\b", 2f, Gary.Agent, 0f);
                yield return Wait(0.6f);
                yield return pad.TypeAsEntity("1", 2f, Gary.Agent, 0f);
            }
            yield return Wait(0.8f);
            var menu = pad.Window.Element("menu:File");
            if (menu != null) yield return Gary.ClickElement(menu, GaryMove, null, 1.5f);
            yield return Wait(0.4f);
            PopupMenu.CloseAll();
            pad.Save(Gary.Agent);
            g.Flags.Set(MemoryFlags.N3GaryEnabledLogoff);
            g.Flags.Set(MemoryFlags.N3LogoffEnabled);
            g.Files.SetFolderLocked(ContentIds.FolderRestricted, false);
            GameLog.Info(LogChannel.Story, "Gary enabled log off");
            yield return Say(_gary, Lines("g3_logoff_done"), GaryCps);
            Gary.FadeTo(GaryFaint, 1f);
        }

        /// <summary>
        /// Gary covers a button for a few seconds so the second cursor cannot click it (No, Cancel), then slips off.
        /// </summary>
        IEnumerator GaryGuard(Func<Interactable> target, float seconds, bool slipOff = true)
        {
            var el = target();
            if (el == null) yield break;
            Gary.FadeTo(Gary.MaxAlpha, 0.1f);
            var fast = GaryMove.Clone();
            fast.speed *= 4f;
            fast.reactionDelay = 0.02f;
            fast.pauseProbability = 0f;
            fast.microCorrections = 1;
            fast.maxDuration = 0.8f;
            yield return Gary.MoveToElement(el, fast);
            GameLog.Info(LogChannel.Entity, "Gary guards " + (el != null ? el.elementId : "?"));
            float end = Time.time + seconds;
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
            if (!slipOff) yield break;
            var off = ScreenRig.ClampToScreen(Gary.Agent.Position + new Vector2(UnityEngine.Random.Range(-60f, 60f), -50f));
            yield return Gary.MoveTo(off, GaryMove, 30f);
            if (!GaryFinished) Gary.FadeTo(GaryFaint, 0.8f);
        }

        /// <summary>
        /// Kept Gary during a 017 shred: No for 3 s, then (the moment Yes is clicked) Cancel for 3 s. The first
        /// time he says so.
        /// </summary>
        IEnumerator GaryGuardsShred(MessageBox box, bool say)
        {
            if (say) RunSide(Say(_gary, Lines("g3_guard"), GaryCps), "gary-guard-line");
            yield return GaryGuard(() => box.IsOpen ? box.Button("No")?.Hit : null, 3f, false);
            ProgressDialog progress;
            float wait = Time.time + 4f;
            while ((progress = _g.Shred.Progress) == null && (box.IsOpen || box.Result == "Yes") && Time.time < wait) yield return null;
            if (progress != null && progress.IsOpen) yield return GaryGuard(() => progress.IsOpen && progress.CancelButton != null ? progress.CancelButton.Hit : null, 3f);
            else if (!GaryFinished) Gary.FadeTo(GaryFaint, 0.8f);
        }

        /// <summary>Finished Gary on the log off confirm: he races you to No (Night 3 race timing plus the assist delay).</summary>
        IEnumerator GaryRacesToNo(MessageBox box)
        {
            var no = box.Button("No");
            if (no == null) yield break;
            if (!Gary.IsVisible)
            {
                var from = new Vector2(-10f, no.Hit.Center.y);
                Gary.Teleport(from);
                yield return Gary.Appear(from, 0.15f, false);
            }
            float delay = _g.Difficulty.RaceToNoDelay(UnityEngine.Random.value, _g.Assist);
            var result = new bool[1];
            yield return RaceTo(Gary, no.Hit, delay, result, 1.5f);
            GameLog.Info(LogChannel.Entity, "Gary raced to No: " + (result[0] ? "clicked" : "blocked"));
            // A lost race counts like one of her defenses (the assist eases the next try), and he goes back to
            // the edge of the screen, so every try starts the same race again.
            if (result[0] && box.Result == "No") _g.Assist?.ReportDefense("no");
            yield return Gary.MoveTo(new Vector2(12f, Mathf.Clamp(Gary.Agent.Position.y, 120f, ScreenRig.Height - 40f)), MovementProfiles.Mechanical, 30f);
        }

        /// <summary>Finished Gary during the log off progress: he goes for Cancel (the player can block it with the cursor).</summary>
        IEnumerator GaryCancelsLogOff(ProgressDialog progress)
        {
            if (progress == null || progress.CancelButton == null) yield break;
            yield return Wait(0.4f);
            var result = new bool[1];
            yield return Gary.ClickElement(progress.CancelButton.Hit, MovementProfiles.Mechanical, result, 6f);
            GameLog.Info(LogChannel.Entity, "Gary went for Cancel: " + (result[0] ? "clicked" : "blocked"));
        }
    }
}
#endif
