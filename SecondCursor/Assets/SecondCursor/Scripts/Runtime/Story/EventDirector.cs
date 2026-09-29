using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Controls escalation across the vertical slice (brief sections 11 and 16):
    /// boot -> ordinary work (tutorial) -> subtle anomalies -> the second cursor appears -> conflict over
    /// employee_017.dat -> Notepad communication -> escalation (mimicry, camera unlocked) -> reveal on
    /// Camera 03 -> blackout ending. Beats wait on what the PLAYER does (tasks, attempts, camera use)
    /// rather than fixed timestamps, with timeouts so nothing can soft-lock. Any beat can be jumped to from
    /// the debug overlay; each beat knows how to set the world up for itself.
    /// </summary>
    public sealed class EventDirector : MonoBehaviour
    {
        public static readonly string[] Beats =
        {
            "boot", "work", "anomaly", "presence", "conflict", "communication", "escalation", "reveal", "ending",
        };

        GameServices _g;
        Routine _flow;
        CursorRecording _ledgerClip;
        NotepadApp _notepad;
        int _cameraReopens;
        BootSequence _boot;
        EndingSequence _ending;
        readonly List<Routine> _side = new List<Routine>();

        /// <summary>Run a background story routine alongside the current beat (stopped on jumps).</summary>
        void RunSide(IEnumerator routine, string name)
        {
            var r = new Routine(routine, name);
            r.Tick();
            if (!r.Done) _side.Add(r);
        }

        public string CurrentBeat { get; private set; } = "";
        public float BeatStartedAt { get; private set; }

        public static EventDirector Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Event Director");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<EventDirector>();
            d._g = g;
            g.Tasks.TaskCompleted += t =>
            {
                // Keep the footage of the player archiving the ledger: the entity replays it later.
                if (t.Id == ContentIds.TaskArchiveLedger) d._ledgerClip = g.Recorder.Extract(Time.time - 6.5f, Time.time + 0.1f);
                Sfx.Play("ui_select");
            };
            g.Apps.CanLaunch = d.CanLaunch;
            g.Apps.Launched += (appId, a) =>
            {
                if (appId == AppIds.Camera && a != null && a.IsPlayer && g.Flags.Has(Flags.CameraUnlocked)) d._cameraReopens++;
            };
            g.Windows.Restored += (w, a) =>
            {
                // Restoring a minimized feed counts as looking again.
                if (w.AppId == AppIds.Camera && a != null && a.IsPlayer && g.Flags.Has(Flags.CameraUnlocked)) d._cameraReopens++;
            };
            // employee_017.dat cannot be shredded outside the conflict (it is "in use by another user").
            g.Shred.IsInUse = id => id == ContentIds.File017;
            return d;
        }

        bool CanLaunch(string appId, CursorAgent by)
        {
            if (appId != AppIds.Camera || _g.Flags.Has(Flags.CameraUnlocked)) return true;
            if (by != null && by.IsEntity) return true;
            _g.Flags.Set(Flags.CameraDeniedSeen);
            var c = _g.Content;
            Dialogs.Message(_g, c.Text("camera.denied.title"), c.Text("camera.denied.body"), "icon_lock", new[] { "OK" }, null);
            return false;
        }

        // ------------------------------------------------------------------ flow control

        public void Begin() => JumpTo("boot");

        public void JumpTo(string beat)
        {
            int index = Array.IndexOf(Beats, beat);
            if (index < 0) return;
            if (_flow != null) _flow.Stop(); // stops the whole chain, including nested beat coroutines
            foreach (var r in _side) r.Stop();
            _side.Clear();
            CleanUpForJump();
            _flow = new Routine(Flow(index), "story");
            _flow.Tick();
        }

        void Update()
        {
            for (int i = _side.Count - 1; i >= 0; i--)
            {
                _side[i].Tick();
                if (_side[i].Done) _side.RemoveAt(i);
            }
            if (_flow == null) return;
            _flow.Tick();
            if (_flow.Done) _flow = null;
        }

        /// <summary>Undo whatever a half-finished beat left on screen before starting another one.</summary>
        void CleanUpForJump()
        {
            var g = _g;
            g.Entity.Interrupt();
            g.Entity.Brain.Enabled = false;
            g.Entity.Urgency = 1f;
            _boot?.Clear();
            _boot = null;
            _ending?.Clear();
            _ending = null;
            g.Player.ShapeOverride = null;
            g.Player.Enabled = true;
            g.Player.Visible = true;
            g.Audio.StopLoop("drone_tension", 0.3f);
            g.Shred.Abort();
            g.Shred.SpeedMultiplier = 1f;
            if (g.Fx.IsPoweredOff) g.Fx.PowerOn();
            g.Fx.SetBlack(false);
            if (_notepad != null && _notepad.IsOpen)
            {
                _notepad.ConversationMode = false;
                _notepad.PlayerCanType = true;
            }
        }

        public void SkipBeat()
        {
            int index = Array.IndexOf(Beats, CurrentBeat);
            if (index >= 0 && index < Beats.Length - 1) JumpTo(Beats[index + 1]);
        }

        IEnumerator Flow(int start)
        {
            if (start > 0) Prepare(start);
            for (int i = start; i < Beats.Length; i++)
            {
                CurrentBeat = Beats[i];
                BeatStartedAt = Time.time;
                GameLog.Info(LogChannel.Story, "Beat: " + CurrentBeat);
                yield return RunBeat(Beats[i]);
            }
        }

        IEnumerator RunBeat(string beat)
        {
            switch (beat)
            {
                case "boot": return Boot();
                case "work": return Work();
                case "anomaly": return Anomaly();
                case "presence": return Presence();
                case "conflict": return Conflict();
                case "communication": return Communication();
                case "escalation": return Escalation();
                case "reveal": return Reveal();
                default:
                    _ending = new EndingSequence(_g);
                    return _ending.Run();
            }
        }

        /// <summary>Put the world in the state a beat expects when jumping straight to it (debug).</summary>
        void Prepare(int beatIndex)
        {
            var g = _g;
            ShowDesktop();
            // Complete earlier tasks and deliver their mail.
            if (beatIndex > 1)
            {
                foreach (var t in new[] { ContentIds.TaskReadBriefing, ContentIds.TaskArchiveLedger, ContentIds.TaskVerify3317, ContentIds.TaskVerify3318, ContentIds.TaskShredCache })
                    g.Tasks.ForceComplete(t);
                g.Flags.Set(Flags.TutorialDone);
            }
            if (beatIndex > 2)
            {
                g.Mail.Deliver(ContentIds.MailIt, false);
                g.Tasks.ForceComplete(ContentIds.TaskArchiveBatch);
                g.Flags.Set(Flags.FirstAnomaly);
            }
            if (beatIndex > 3)
            {
                if (g.Files.FolderOf(ContentIds.File017) != ContentIds.FolderDesktop)
                    g.Files.Move(ContentIds.File017, ContentIds.FolderDesktop, Actor.Entity);
                g.Mail.Deliver(ContentIds.MailUrgent, false);
                g.Tasks.Activate(ContentIds.TaskShred017);
                g.Flags.Set(Flags.EntitySeen);
                g.Flags.Set(Flags.UrgentOrderReceived);
                g.Taskbar.PointingDevices = 2;
            }
            if (beatIndex > 4)
            {
                g.Flags.Set(Flags.ConflictStarted);
                g.Shred.IsInUse = id => id == ContentIds.File017;
            }
            if (beatIndex > 5) g.Flags.Set(Flags.EntitySpoke);
            if (beatIndex > 6)
            {
                g.Flags.Set(Flags.CameraUnlocked);
                g.Flags.Set(Flags.MimicShown);
                g.Mail.Deliver(ContentIds.MailNoSender, false);
                g.Files.SetFolderLocked(ContentIds.FolderRestricted, false);
            }
        }

        void ShowDesktop()
        {
            var g = _g;
            g.Player.Enabled = true;
            g.Fx.SetBlack(false);
            if (!g.Flags.Has(Flags.LoggedIn))
            {
                g.Flags.Set(Flags.LoggedIn);
                g.Audio.SetAmbience(true, 2f);
            }
        }

        // ------------------------------------------------------------------ helpers

        static IEnumerator Wait(float seconds)
        {
            yield return Waits.Seconds(seconds);
        }

        static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float end = Time.time + timeout;
            while (!condition() && Time.time < end) yield return null;
        }

        /// <summary>Waits for a task; after <paramref name="hintAfter"/> seconds shows its hint as a toast (once).</summary>
        IEnumerator WaitTask(string taskId, float hintAfter = 45f)
        {
            float start = Time.time;
            bool hinted = false;
            while (!_g.Tasks.IsCompleted(taskId))
            {
                // Safety nets: a needed file must never be lost, and no task may block the shift forever.
                var task = _g.Tasks.Get(taskId);
                if (task != null && task.Type == TaskType.MoveFile)
                {
                    foreach (var target in task.Data.targets)
                    {
                        var f = _g.Files.GetFile(target);
                        if (f != null && f.Shredded) _g.Files.Restore(target, ContentIds.FolderIntake, Actor.System);
                    }
                }
                if (Time.time - start > hintAfter + 240f)
                {
                    GameLog.Warn(LogChannel.Story, "Task " + taskId + " force-completed after timeout");
                    _g.Tasks.ForceComplete(taskId);
                    break;
                }
                if (!hinted && Time.time - start > hintAfter)
                {
                    hinted = true;
                    var t = _g.Tasks.Get(taskId);
                    if (t != null && !string.IsNullOrEmpty(t.Data.hint))
                        _g.Notifications.Show(_g.Content.Text("app.workqueue"), t.Data.hint, "icon_info", a => _g.Apps.Launch(AppIds.WorkQueue, a), "ui_select");
                }
                yield return null;
            }
            yield return Wait(0.8f);
        }

        void GiveTask(string taskId)
        {
            _g.Tasks.Activate(taskId);
            if (!_g.Tasks.IsCompleted(taskId))
                _g.Notifications.Show(_g.Content.Text("app.workqueue"), _g.Tasks.Get(taskId)?.Title ?? "", "icon_task_active",
                    a => _g.Apps.Launch(AppIds.WorkQueue, a), "notify_mail");
        }

        void PhantomClick(Vector2 at)
        {
            _g.Audio.Play("mouse_click", 0.85f, UnityEngine.Random.Range(0.96f, 1.04f), Audio.AudioManager.PanFor(at.x));
        }

        EntityController E => _g.Entity;

        NotepadApp EnsureNotepad()
        {
            if (_notepad != null && _notepad.IsOpen) return _notepad;
            _notepad = null;
            return null;
        }

        IEnumerator OpenNotepadAsEntity()
        {
            if (EnsureNotepad() != null) { _notepad.Window.Restore(E.Agent); yield break; }
            var before = new HashSet<App>(_g.Apps.OpenApps);
            yield return E.OpenApp(AppIds.Notepad, MovementProfiles.HumanLike);
            foreach (var app in _g.Apps.OpenApps)
                if (app is NotepadApp n && !before.Contains(app) && n.FileId == null) _notepad = n;
            if (_notepad == null)
                _notepad = (NotepadApp)_g.Apps.Launch(AppIds.Notepad, E.Agent);
            _notepad.ConversationMode = true;
            _notepad.PlayerCanType = false;
            _notepad.Window.Focus(E.Agent);
        }

        IEnumerator TypeLines(IEnumerable<string> lines, float cps = 4.5f)
        {
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                if (EnsureNotepad() == null) yield return OpenNotepadAsEntity();
                if (_notepad.Text.Length > 0 && !_notepad.Text.EndsWith("\n")) _notepad.Append("\n");
                yield return E.Type(_notepad, line, cps);
                yield return Wait(0.5f);
            }
            if (_notepad != null && _notepad.IsOpen && !_notepad.Text.EndsWith("\n")) _notepad.Append("\n");
        }

        // ------------------------------------------------------------------ BOOT

        IEnumerator Boot()
        {
            _g.Player.Enabled = true;
            _boot = new BootSequence(_g);
            yield return _boot.Run(false);
            _boot = null;
            _g.Flags.Set(Flags.LoggedIn);
            _g.Audio.Play("sys_startup");
            _g.Audio.SetAmbience(true, 3f);
            yield return Wait(1.2f);
        }

        // ------------------------------------------------------------------ PHASE 0: work

        IEnumerator Work()
        {
            E.Phase = EntityPhase.Invisible;
            _g.Apps.Launch(AppIds.WorkQueue, null);
            yield return Wait(1.5f);
            GiveTask(ContentIds.TaskReadBriefing);
            yield return Wait(1.5f);
            if (_g.Mail.UnreadCount > 0)
                _g.Notifications.Show(_g.Content.Text("app.mail"), _g.Content.Format("notify.newmail", _g.Mail.UnreadCount), "icon_mail_unread",
                    a => _g.Apps.Launch(AppIds.Mail, a));
            yield return WaitTask(ContentIds.TaskReadBriefing, 35f);

            GiveTask(ContentIds.TaskArchiveLedger);
            yield return WaitTask(ContentIds.TaskArchiveLedger);

            GiveTask(ContentIds.TaskVerify3317);
            RunSide(TinyNudge(), "tiny-nudge");
            yield return WaitTask(ContentIds.TaskVerify3317);
            GiveTask(ContentIds.TaskVerify3318);
            yield return WaitTask(ContentIds.TaskVerify3318);

            GiveTask(ContentIds.TaskShredCache);
            yield return WaitTask(ContentIds.TaskShredCache);
            _g.Flags.Set(Flags.TutorialDone);
        }

        /// <summary>Phase 0's single ambiguous oddity: the Staff Directory window shifts a few pixels.</summary>
        IEnumerator TinyNudge()
        {
            yield return WaitUntil(() => _g.Apps.FindById(AppIds.Staff) != null, 90f);
            yield return Wait(UnityEngine.Random.Range(6f, 9f));
            var staff = _g.Apps.FindById(AppIds.Staff);
            if (staff == null || !staff.IsOpen || staff.Window.DraggedBy != null) yield break;
            staff.Window.MoveBy(new Vector2(6f, 2f));
            GameLog.Info(LogChannel.Story, "Anomaly: window nudged (tiny)");
        }

        // ------------------------------------------------------------------ PHASE 1: ambiguous

        IEnumerator Anomaly()
        {
            E.Phase = EntityPhase.Ambiguous;
            yield return Wait(4f);
            _g.Mail.Deliver(ContentIds.MailIt);
            yield return Wait(3f);
            GiveTask(ContentIds.TaskArchiveBatch);

            // Anomaly 0: your own cursor twitches a few pixels, once.
            yield return Wait(6f);
            _g.PlayerView.Flinch(new Vector2(UnityEngine.Random.Range(3f, 5f), UnityEngine.Random.Range(-4f, -2f)));
            GameLog.Info(LogChannel.Story, "Anomaly: player cursor flinched");

            // Anomaly A: employee_017.dat becomes selected by itself (with a click you didn't make).
            yield return WaitUntil(() =>
            {
                var files = _g.Apps.Find<FilesApp>();
                if (files == null || files.Window.IsMinimized) return false;
                var row = files.RowFor(ContentIds.File017);
                return row != null && Vector2.Distance(_g.Player.Position, row.Hit.Center) > 140f && _g.Player.Payload == null;
            }, 120f);
            var fm = _g.Apps.Find<FilesApp>();
            var r = fm != null ? fm.RowFor(ContentIds.File017) : null;
            if (r != null)
            {
                fm.SelectFile(ContentIds.File017, E.Agent);
                PhantomClick(r.Hit.Center);
                _g.Flags.Set(Flags.FirstAnomaly);
                GameLog.Info(LogChannel.Story, "Anomaly: employee_017 selected itself");
            }

            // Anomaly B: a window drifts 10 px while you're looking elsewhere.
            yield return Wait(UnityEngine.Random.Range(9f, 14f));
            var target = _g.Windows.Active;
            if (target != null && target.DraggedBy == null && !target.IsMaximized)
            {
                target.MoveBy(new Vector2(10f, -4f));
                _g.Audio.Play("mouse_release", 0.5f, 1f, Audio.AudioManager.PanFor(target.CaptionCenter.x));
                GameLog.Info(LogChannel.Story, "Anomaly: window moved 10px");
            }
            var notes = _g.Content.Story.anomalyNotes;
            if (notes.Length > 0) _g.Notifications.Show(_g.Content.Text("os.name"), notes[0], "icon_info", null, "ui_select");

            yield return WaitTask(ContentIds.TaskArchiveBatch, 60f);
        }

        // ------------------------------------------------------------------ PHASE 2: presence

        IEnumerator Presence()
        {
            E.Phase = EntityPhase.Presence;
            E.State = EntityState.Curious;
            yield return Wait(3f);

            // The second cursor enters from the right edge, carrying employee_017.dat out of nowhere.
            var start = new Vector2(ScreenRig.Width + 6f, 330f);
            Vector2 spot = new Vector2(600f, 300f);
            if (!E.IsBareDesktop(spot))
            {
                // Find visible desktop so the player actually sees the file arrive.
                for (float y = 440f; y > 120f && !E.IsBareDesktop(spot); y -= 40f)
                    for (float x = 820f; x > 120f; x -= 60f)
                        if (E.IsBareDesktop(new Vector2(x, y))) { spot = new Vector2(x, y); break; }
            }
            E.Teleport(start);
            yield return E.Appear(start, 0.1f, true);
            _g.Taskbar.PointingDevices = 2;
            _g.Notifications.Show(_g.Content.Text("os.name"), "New pointing device detected.", "icon_info", null, "ui_select");
            _g.Flags.Set(Flags.EntitySeen);

            bool carried = false;
            if (_g.Files.Exists(ContentIds.File017) && _g.Files.FolderOf(ContentIds.File017) != ContentIds.FolderDesktop)
            {
                var file = _g.Files.GetFile(ContentIds.File017);
                E.Agent.SetButton(true);
                yield return null;
                _g.DragDrop.BeginFileDrag(E.Agent, file.Id, file.Name, FileIcons.SpriteFor(file), null, E.Agent.Position + new Vector2(-16f, 16f));
                carried = true;
            }
            yield return E.MoveTo(spot, MovementProfiles.Hesitant, 40f);
            if (carried)
            {
                yield return Wait(0.3f);
                E.Agent.SetButton(false);
                yield return null;
                yield return null;
                if (_g.Files.Exists(ContentIds.File017))
                {
                    // Whatever was under the cursor, the file ends up visibly on the desktop where it let go.
                    if (_g.Files.FolderOf(ContentIds.File017) != ContentIds.FolderDesktop)
                        _g.Files.Move(ContentIds.File017, ContentIds.FolderDesktop, Actor.Entity);
                    _g.Desktop.SetFilePosition(ContentIds.File017, OSLayers.WorldToDesktop(spot) - new Vector2(37f, 16f));
                }
            }
            // It lingers over the file, as if checking on it... then leaves.
            var icon = _g.Desktop.IconForFile(ContentIds.File017);
            if (icon != null) yield return E.Loiter(icon.Hit.Center, 14f, 2.2f, MovementProfiles.Hesitant);
            yield return E.MoveTo(new Vector2(-10f, 380f), MovementProfiles.HumanLike, 40f);
            yield return E.Vanish(0.3f);
            E.State = EntityState.Observing;

            yield return Wait(4f);
            _g.Mail.Deliver(ContentIds.MailUrgent);
            _g.Flags.Set(Flags.UrgentOrderReceived);
            yield return Wait(1f);
            GiveTask(ContentIds.TaskShred017);
        }

        // ------------------------------------------------------------------ PHASE 3: interference (key fun test)

        IEnumerator Conflict()
        {
            E.Phase = EntityPhase.Interference;
            E.State = EntityState.Defensive;
            var brain = E.Brain;
            brain.ProtectedFileId = ContentIds.File017;
            brain.AllowIdleLurk = false;
            brain.Enabled = true;
            _g.Shred.IsInUse = null;
            float start = Time.time;
            bool followedUp = false;
            int attemptsAtStart = _g.Memory.Count(MemoryKind.ShredAttempt, ContentIds.File017);

            while (true)
            {
                // After the first defence it stays on screen, watching.
                if (brain.Defenses >= 1) brain.AllowIdleLurk = true;
                bool shredded = _g.Files.GetFile(ContentIds.File017)?.Shredded ?? false;
                if (shredded) break;
                if (brain.Defenses >= 4 && !_g.Conflict.IsFighting && !_g.Shred.Busy) break;
                float elapsed = Time.time - start;
                int attempts = _g.Memory.Count(MemoryKind.ShredAttempt, ContentIds.File017) - attemptsAtStart;
                if (!followedUp && elapsed > 45f && attempts == 0)
                {
                    followedUp = true;
                    _g.Mail.Deliver(ContentIds.MailSupervisorCheck);
                }
                if (elapsed > 150f && !_g.Conflict.IsFighting && !_g.Shred.Busy) break;
                if (elapsed > 175f)
                {
                    // Hard cap: never let an abandoned dialog stall the shift.
                    _g.Shred.Abort();
                    break;
                }
                yield return null;
            }

            if (_g.Files.GetFile(ContentIds.File017)?.Shredded ?? false)
            {
                // The player won the fight... and it comes back anyway.
                _g.Flags.Set(Flags.File017ShreddedOnce);
                brain.Enabled = false;
                E.Interrupt();
                yield return Wait(1.8f);
                _g.Fx.Glitch(0.35f, 1f);
                _g.Audio.Play("glitch_burst", 0.8f);
                _g.Files.Restore(ContentIds.File017, ContentIds.FolderDesktop, Actor.Entity);
                _g.Desktop.SetFilePosition(ContentIds.File017, new Vector2(400f, 180f));
                _g.Flags.Set(Flags.File017Returned);
                yield return Wait(0.6f);
                _g.Notifications.Show(_g.Content.Text("app.disposal"), _g.Content.Format("error.inuse.body", "employee_017.dat"), "icon_error", null, "sys_error");
            }
            else if (!_g.Flags.Has(Flags.File017Returned))
            {
                _g.Mail.Deliver(ContentIds.MailSupervisorCheck);
            }
            _g.Shred.IsInUse = id => id == ContentIds.File017;
            brain.Enabled = false;
            E.Urgency = 1f;
            yield return E.WaitIdle();
        }

        // ------------------------------------------------------------------ PHASE 4: communication

        IEnumerator Communication()
        {
            E.Brain.Enabled = false;
            E.Interrupt();
            E.Phase = EntityPhase.Communication;
            E.State = EntityState.Communicating;
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.6f, ScreenRig.Height * 0.55f), 0.6f, true);
            // Stillness before it speaks.
            yield return Wait(1.6f);
            yield return OpenNotepadAsEntity();
            _g.Flags.Set(Flags.EntitySpoke);

            var exchange = _g.Dialogue.Get(ContentIds.ExchangeStop);
            bool first = true;
            int guard = 0;
            while (exchange != null && guard++ < 6)
            {
                yield return TypeLines(exchange.entityLines, first ? 2.2f : 4f);
                first = false;
                if (EnsureNotepad() == null) yield return OpenNotepadAsEntity();
                _notepad.PlayerCanType = true;
                _notepad.Window.Focus(null);

                string said = null;
                Action<string, CursorAgent> handler = (line, a) => said = line;
                _notepad.LineSubmitted += handler;
                float waitStart = Time.time;
                int reopened = 0;
                while (said == null)
                {
                    // Silence: 25s after the last keystroke (or since it finished typing).
                    float lastActivity = Mathf.Max(waitStart, _notepad != null ? _notepad.LastPlayerKeyTime : 0f);
                    if (Time.time - lastActivity > 25f) break;
                    if (EnsureNotepad() == null && reopened >= 2) break; // keeps closing it: treat as silence
                    if (EnsureNotepad() == null)
                    {
                        reopened++;
                        // Closing it doesn't make it go away.
                        yield return OpenNotepadAsEntity();
                        yield return TypeLines(new[] { "DONT" }, 3f);
                        _notepad.PlayerCanType = true;
                        _notepad.LineSubmitted += handler;
                        waitStart = Time.time;
                    }
                    yield return null;
                }
                if (_notepad != null) _notepad.LineSubmitted -= handler;
                if (_notepad != null) _notepad.PlayerCanType = false;

                string[] reply;
                if (said == null)
                {
                    reply = exchange.silence;
                }
                else
                {
                    var r = _g.Dialogue.Respond(exchange, said);
                    reply = r.Lines;
                    _g.Memory.Record(MemoryKind.TypedMessage, r.Category, Time.time);
                    switch (r.Category)
                    {
                        case "swear": _g.Flags.Set(Flags.PlayerSwore); break;
                        case "who": _g.Flags.Set(Flags.PlayerAskedWho); break;
                        case "refuse": _g.Flags.Set(Flags.PlayerRefused); _g.Memory.Record(MemoryKind.ResistedEntity, "notepad", Time.time); break;
                        case "agree": _g.Flags.Set(Flags.PlayerAgreed); _g.Memory.Record(MemoryKind.ObeyedEntity, "notepad", Time.time); break;
                    }
                    GameLog.Info(LogChannel.Player, "Typed \"" + said + "\" (" + r.Category + ")");
                }
                yield return Wait(1.1f);
                yield return TypeLines(reply, 4f);
                exchange = string.IsNullOrEmpty(exchange.next) ? null : _g.Dialogue.Get(exchange.next);
            }
        }

        // ------------------------------------------------------------------ PHASE 5: escalation

        IEnumerator Escalation()
        {
            E.Phase = EntityPhase.Escalation;
            E.State = EntityState.Curious;
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.5f, ScreenRig.Height * 0.5f), 0.5f, true);
            yield return Wait(1.5f);

            // It copies you: a replay of your own cursor from when you archived the ledger.
            CursorRecording rec = _ledgerClip;
            if (rec == null || rec.IsEmpty || rec.Duration < 2f) rec = _g.Recorder.MostActive(6f, Time.time - 20f, 120f);
            if (rec != null && !rec.IsEmpty)
            {
                E.State = EntityState.Observing;
                yield return E.Replay(rec, false);
                _g.Flags.Set(Flags.MimicShown);
                GameLog.Info(LogChannel.Entity, "Replayed the player's recorded movement (" + rec.Duration.ToString("0.0") + "s)");
                yield return Wait(1f);
            }
            yield return TypeLines(_g.Content.Dialogue.recordLines, 4f);
            yield return Wait(1.5f);
            // A message from your own account... dated eleven years ago.
            _g.Mail.Deliver(ContentIds.MailNoSender);
            yield return Wait(3f);
            yield return TypeLines(_g.Content.Dialogue.cameraLines, 3.5f);

            // It opens what you were not allowed to open: the Restricted folder, then the cameras.
            _g.Files.SetFolderLocked(ContentIds.FolderRestricted, false);
            _g.Notifications.Show(_g.Content.Text("os.name"), "Permissions on Restricted changed by a remote session.", "icon_lock", a => _g.Apps.OpenFolder(ContentIds.FolderRestricted, a), "ui_select");
            _g.Flags.Set(Flags.CameraUnlocked);
            _g.Notifications.Show(_g.Content.Text("app.camera"), "Clearance override accepted: remote session.", "icon_lock", null, "sys_warning");
            yield return Wait(0.8f);
            yield return E.OpenApp(AppIds.Camera, MovementProfiles.HumanLike);
            yield return Wait(0.8f);
            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null) cam = (CameraApp)_g.Apps.Launch(AppIds.Camera, E.Agent);
            if (cam != null)
            {
                var button = cam.Window.Element("camera:" + ContentIds.Cam03);
                if (button != null) yield return E.ClickElement(button, MovementProfiles.HumanLike, null, 3f);
                if (cam.CurrentCamera != ContentIds.Cam03) cam.Select(ContentIds.Cam03, E.Agent);
            }
        }

        // ------------------------------------------------------------------ PHASE 6: reveal

        IEnumerator Reveal()
        {
            E.Phase = EntityPhase.Reveal;
            var rig = _g.CameraRig;
            _g.Flags.Set(Flags.CameraUnlocked);
            rig.SeatedMimicsPlayer = true;
            rig.DoorOpen = 0f;
            rig.Figure = FigureStage.None;
            rig.LightFlicker = 0.1f;
            rig.LightsOn = true;
            _cameraReopens = 0;

            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null)
            {
                cam = (CameraApp)_g.Apps.Launch(AppIds.Camera, E.Agent);
                cam?.Select(ContentIds.Cam03, E.Agent);
            }
            // It moves aside and waits, still, while you watch yourself.
            if (cam != null)
            {
                var r = cam.Window.WorldRect;
                yield return E.MoveTo(ScreenRig.ClampToScreen(new Vector2(r.xMax + 40f, r.center.y + 30f)), MovementProfiles.Hesitant, 30f);
            }
            E.State = EntityState.Observing;
            float revealStart = Time.time;

            // 1) The door: it's ajar after a moment of static.
            yield return WaitWatching(8f, 25f);
            yield return StaticCut(() => rig.DoorOpen = 0.3f);

            // 2) Someone is standing in the doorway. The office goes quiet.
            yield return WaitWatching(8f, 30f);
            yield return StaticCut(() => { rig.DoorOpen = 0.85f; rig.Figure = FigureStage.Doorway; });
            _g.Flags.Set(Flags.FigureSeen);
            _g.Audio.SetAmbience(false, 4f);
            _g.Audio.PlayLoop("drone_tension", 0.25f, 6f);
            _g.Audio.Play("door_distant", 0.5f, 1f, -0.3f);
            yield return WaitWatching(4f, 12f);

            // 3) The entity panics and fights to shut the feed. Each time you reopen it, it is closer.
            E.State = EntityState.Panicked;
            var panic = _g.Content.Dialogue.panicLines;
            int panicLine = 0;
            float lastOpen = Time.time;
            int seenReopens = _cameraReopens;
            while (Time.time - revealStart < 150f)
            {
                // Reopened (or restored) since we last looked - even mid-typing: it advances.
                if (_cameraReopens > seenReopens)
                {
                    seenReopens = _cameraReopens;
                    rig.Figure = rig.Figure == FigureStage.Doorway ? FigureStage.Middle : FigureStage.BehindChair;
                    _g.Audio.Play("footstep_distant", 0.4f, 0.9f, 0.2f);
                    var reopened = _g.Apps.Find<CameraApp>();
                    if (reopened != null) reopened.Select(ContentIds.Cam03, null);
                    yield return WaitWatching(3f, 6f);
                    continue;
                }
                cam = _g.Apps.Find<CameraApp>();
                if (cam != null && cam.IsOpen && !cam.Window.IsMinimized)
                {
                    lastOpen = Time.time;
                    if (rig.Figure == FigureStage.BehindChair && cam.CurrentCamera == ContentIds.Cam03)
                    {
                        yield return WaitWatching(5f, 8f);
                        break;
                    }
                    var close = cam.Window.CloseButton;
                    if (close != null) yield return E.ClickElement(close.Hit, MovementProfiles.Panicked, null, 4f);
                    if (panicLine < panic.Length) yield return TypeLines(new[] { panic[panicLine++] }, 6f);
                }
                else
                {
                    // Closed: wait to see whether you look again (handled at the top of the loop).
                    yield return WaitUntil(() => _cameraReopens > seenReopens || Time.time - lastOpen > 12f, 13f);
                    if (_cameraReopens <= seenReopens)
                    {
                        // You obeyed. It shows you why instead - and then the feed opens by itself.
                        yield return ShowEmployee017();
                        rig.Figure = FigureStage.BehindChair;
                        rig.DoorOpen = 1f;
                        var self = (CameraApp)_g.Apps.Launch(AppIds.Camera, null);
                        self?.Select(ContentIds.Cam03, null);
                        _g.Audio.Play("low_thump", 0.8f);
                        if (panicLine < panic.Length) yield return TypeLines(new[] { panic[panicLine++] }, 7f);
                        yield return WaitWatching(4f, 8f);
                        break;
                    }
                }
                yield return null;
            }

            // Final image: it's right behind you, and "you" turn to look at the camera.
            cam = _g.Apps.Find<CameraApp>();
            if (cam == null || !cam.IsOpen)
            {
                cam = (CameraApp)_g.Apps.Launch(AppIds.Camera, null);
                cam?.Select(ContentIds.Cam03, null);
            }
            rig.Figure = FigureStage.BehindChair;
            rig.SeatedMimicsPlayer = false;
            rig.LightFlicker = 1f;
            float t = 0f;
            while (t < 3.2f)
            {
                t += Time.deltaTime;
                rig.SeatedHeadTurn = Mathf.SmoothStep(0f, 1f, t / 3.2f);
                rig.ExtraNoise = t / 3.2f * 0.5f;
                if (UnityEngine.Random.value < 0.05f) _g.Fx.Glitch(0.05f, 0.6f);
                yield return null;
            }
            yield return Wait(1.2f);
        }

        /// <summary>Waits until the player has watched Camera 03 for <paramref name="watchSeconds"/> (or timeout).</summary>
        IEnumerator WaitWatching(float watchSeconds, float timeout)
        {
            float watched = 0f, end = Time.time + timeout;
            while (watched < watchSeconds && Time.time < end)
            {
                var cam = _g.Apps.Find<CameraApp>();
                if (cam != null && !cam.Window.IsMinimized && cam.CurrentCamera == ContentIds.Cam03) watched += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>A burst of feed static that hides a change in the scene (things only move when you blink).</summary>
        IEnumerator StaticCut(Action change)
        {
            var rig = _g.CameraRig;
            _g.Audio.Play("camera_static", 0.6f);
            float t = 0f;
            bool changed = false;
            while (t < 0.45f)
            {
                t += Time.deltaTime;
                rig.ExtraNoise = 0.9f;
                if (!changed && t > 0.15f) { change(); changed = true; }
                yield return null;
            }
            rig.ExtraNoise = 0f;
        }

        IEnumerator ShowEmployee017()
        {
            _g.Flags.Set(Flags.Staff017Revealed);
            yield return E.OpenApp(AppIds.Staff, MovementProfiles.Panicked);
            yield return Wait(0.5f);
            var staff = _g.Apps.Find<StaffApp>();
            if (staff != null)
            {
                staff.RefreshNames();
                var row = staff.Window.Element("employee:" + ContentIds.Employee017);
                if (row != null) yield return E.ClickElement(row, MovementProfiles.Hesitant, null, 3f);
                staff.ShowById(ContentIds.Employee017, E.Agent);
            }
            yield return E.Loiter(E.Agent.Position, 10f, 4f, MovementProfiles.Hesitant);
        }
    }
}
