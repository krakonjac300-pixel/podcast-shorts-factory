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
using SecondCursor.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 1 (Wed 11/18/98), the vertical slice: boot -> ordinary work (tutorial) -> subtle anomalies ->
    /// the second cursor appears -> conflict over employee_017.dat -> Notepad communication -> escalation
    /// (mimicry, camera unlocked) -> reveal on Camera 03 -> blackout ending. Beats wait on what the PLAYER
    /// does (tasks, attempts, camera use) rather than fixed timestamps, with timeouts so nothing can
    /// soft-lock. Any beat can be jumped to; <see cref="Prepare"/> sets the world up for it.
    /// </summary>
    public sealed class Night1Director : NightDirector
    {
        static readonly string[] BeatList =
        {
            "boot", "work", "anomaly", "presence", "conflict", "communication", "escalation", "reveal", "ending",
        };

        static readonly string[] Checkpoints = { "work", "conflict", "escalation" };

        CursorRecording _ledgerClip;
        Speaker _ellen;
        int _cameraReopens;

        public override string[] Beats => BeatList;
        public override int Night => 1;
        protected override string[] CheckpointBeats => Checkpoints;

        protected override void Init()
        {
            base.Init();
            var g = _g;
            _ellen = AddSpeaker(g.Entity);
            g.Tasks.TaskCompleted += t =>
            {
                // Keep the footage of the player archiving the ledger: the entity replays it later.
                if (t.Id == ContentIds.TaskArchiveLedger) _ledgerClip = g.Recorder.Extract(Time.time - 6.5f, Time.time + 0.1f);
            };
            g.Apps.CanLaunch = CanLaunch;
            g.Apps.Launched += (appId, a) =>
            {
                if (appId == AppIds.Camera && a != null && a.IsPlayer && g.Flags.Has(Flags.CameraUnlocked)) _cameraReopens++;
            };
            g.Windows.Restored += (w, a) =>
            {
                // Restoring a minimized feed counts as looking again.
                if (w.AppId == AppIds.Camera && a != null && a.IsPlayer && g.Flags.Has(Flags.CameraUnlocked)) _cameraReopens++;
            };
            // employee_017.dat cannot be shredded outside the conflict (it is "in use by another user").
            g.Shred.IsInUse = id => id == ContentIds.File017;
            // Phase I: once the bin has said so, the order in the queue says it too, instead of still asking for the impossible.
            g.Shred.RefusedInUse += OnShredRefusedInUse;
        }

        /// <summary>
        /// The Disposal bin refused employee_017.dat ("File In Use ... session 017"): the priority order becomes
        /// "blocked: held by session 017", with a hint that nobody can shred it while it is held (the blind tester left
        /// Night 1 believing they had failed a task that cannot be done).
        /// </summary>
        void OnShredRefusedInUse(string fileId)
        {
            if (fileId == ContentIds.File017) BlockTask017();
        }

        void BlockTask017()
        {
            if (!_g.Tasks.IsActive(ContentIds.TaskShred017)) return;
            var c = _g.Content;
            _g.Tasks.Rewrite(ContentIds.TaskShred017, c.Text("task.blocked.017.title"), c.Text("task.blocked.017.description"), c.Text("task.blocked.017.hint"));
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

        protected override void CleanUpForJump()
        {
            base.CleanUpForJump();
            RemoveConflictHint();
        }

        protected override IEnumerator RunBeat(string beat)
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
                    StopSideRoutines();
                    CompleteNight(ContentIds.EndingN1Blackout);
                    // Phase H: the card says how the night ended (WS-04 went dark; the file came back or never went).
                    var spec = EndingSpec.Night1();
                    spec.Outcome = _g.Content.Format(_g.Flags.Has(Flags.File017ShreddedOnce) ? "end.n1.outcome.shredded" : "end.n1.outcome.kept", _g.Clock.Format12());
                    _ending = new EndingSequence(_g, spec);
                    return _ending.Run();
            }
        }

        protected override void RecordNightMemory()
        {
            var f = _g.Flags;
            void Remember(bool condition, string memory)
            {
                if (condition) f.Set(memory);
            }
            Remember(f.Has(Flags.File017ShreddedOnce), MemoryFlags.N1Shredded017);
            Remember(f.Has(Flags.PlayerAgreed), MemoryFlags.N1Agreed);
            Remember(f.Has(Flags.PlayerRefused), MemoryFlags.N1Refused);
            Remember(f.Has(Flags.PlayerSwore), MemoryFlags.N1Swore);
            Remember(f.Has(Flags.PlayerAskedWho), MemoryFlags.N1AskedWho);
            Remember(_g.Memory.Count(MemoryKind.OpenedFile, ContentIds.File017) > 0, MemoryFlags.N1Read017);
            Remember(_g.Memory.Count(MemoryKind.OpenedFile, ContentIds.FilePrevNotes) > 0, MemoryFlags.N1ReadNotes);
            Remember(_cameraReopens > 0, MemoryFlags.N1ReopenedCamera);
            f.SetCounter(MemoryFlags.N1TugWins, f.Get(Flags.CounterPlayerWins));
            f.SetCounter(MemoryFlags.N1TugLosses, f.Get(Flags.CounterTugLosses));
        }

        /// <summary>Put the world in the state a beat expects when jumping straight to it (debug or Continue).</summary>
        protected override void Prepare(int beatIndex)
        {
            var g = _g;
            ShowDesktop();
            // Complete earlier tasks and deliver their mail.
            if (beatIndex > 1)
            {
                foreach (var t in new[] { ContentIds.TaskReadBriefing, ContentIds.TaskArchiveLedger, ContentIds.TaskVerify3317, ContentIds.TaskVerify3318, ContentIds.TaskShredCache })
                    g.Tasks.ForceComplete(t);
                g.Flags.Set(Flags.TutorialDone);
                // Leave the world the way a player who did the tutorial would have.
                g.Mail.MarkRead(ContentIds.MailWelcome, null);
                MoveIfIn(ContentIds.FileLedger, ContentIds.FolderIntake, ContentIds.FolderArchive);
                if (g.Files.Exists(ContentIds.FileCache) && g.Files.Shred(ContentIds.FileCache, Actor.System)) g.Shred.MarkShredded();
                foreach (var id in new[] { ContentIds.Order3317, ContentIds.Order3318 })
                {
                    var order = g.Content.Order(id);
                    if (order != null && g.Orders.DecisionFor(id) == null) g.Orders.Decide(id, order.correct, null);
                }
                if (g.Apps.FindById(AppIds.WorkQueue) == null) g.Apps.Launch(AppIds.WorkQueue, null);
            }
            if (beatIndex > 2)
            {
                g.Mail.Deliver(ContentIds.MailIt, false);
                foreach (var f in new[] { ContentIds.FileBatchA, ContentIds.FileBatchB, ContentIds.FileBatchC })
                    MoveIfIn(f, ContentIds.FolderIntake, ContentIds.FolderArchive);
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
                // Review J3: Continue shows the order the way the fight left it: done (the file came back), or blocked.
                if (g.Flags.Has(Flags.File017ShreddedOnce)) g.Tasks.ForceComplete(ContentIds.TaskShred017);
                else BlockTask017();
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

        void MoveIfIn(string fileId, string fromFolder, string toFolder)
        {
            if (_g.Files.Exists(fileId) && _g.Files.FolderOf(fileId) == fromFolder) _g.Files.Move(fileId, toFolder, Actor.System);
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

        EntityController E => _g.Entity;

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
            yield return Wait(1.2f);
            // How to play, in the OS's own words: the first shift starts with a Quick Start the player dismisses.
            var quick = Dialogs.Message(_g, _g.Content.Text("quickstart.title"), _g.Content.Text("quickstart.body"), "icon_info", new[] { "Begin" }, null);
            yield return WaitUntil(() => !quick.IsOpen, 120f);
            if (quick.IsOpen) quick.Window.Close(null);
            yield return Wait(0.6f);
            GiveTask(ContentIds.TaskReadBriefing);
            yield return Wait(1.5f);
            if (_g.Mail.UnreadCount > 0)
                _g.Notifications.Show(_g.Content.Text("app.mail"), _g.Content.Format("notify.newmail", _g.Mail.UnreadCount), "icon_mail_unread",
                    a => _g.Apps.Launch(AppIds.Mail, a));
            yield return WaitTask(ContentIds.TaskReadBriefing, _g.Difficulty.BriefingHintFirst);

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
            // Let the twitch sink in before the next oddity (they must never land together).
            yield return Wait(UnityEngine.Random.Range(7f, 10f));

            // Anomaly A: employee_017.dat becomes selected by itself (with a click you didn't make).
            yield return WaitUntil(() =>
            {
                var files = _g.Apps.Find<FilesApp>();
                if (files == null || files.Window.IsMinimized) return false;
                var row = files.RowFor(ContentIds.File017);
                return row != null && Vector2.Distance(_g.Player.Position, row.Hit.Center) > 140f && _g.Player.Payload == null;
            }, 45f);
            var fm = _g.Apps.Find<FilesApp>();
            var r = fm != null && !fm.Window.IsMinimized ? fm.RowFor(ContentIds.File017) : null;
            if (r == null && _g.Player.Payload == null && _g.Files.Exists(ContentIds.File017))
            {
                // Nobody is looking at it, so the file manager opens by itself where the file lives.
                fm = _g.Apps.OpenFolder(_g.Files.FolderOf(ContentIds.File017), null);
                GameLog.Info(LogChannel.Story, "Anomaly: File Manager opened itself");
                yield return Wait(0.9f);
                r = fm != null && fm.IsOpen ? fm.RowFor(ContentIds.File017) : null;
            }
            if (r != null)
            {
                fm.SelectFile(ContentIds.File017, E.Agent);
                PhantomClick(r.Hit.Center);
                _g.Flags.Set(Flags.FirstAnomaly);
                GameLog.Info(LogChannel.Story, "Anomaly: employee_017 selected itself");
                RunSide(Note(2, 1.4f), "note-selection");
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

            yield return WaitTask(ContentIds.TaskArchiveBatch, 40f);
        }

        // ------------------------------------------------------------------ PHASE 2: presence

        IEnumerator Presence()
        {
            E.Phase = EntityPhase.Presence;
            E.State = EntityState.Curious;
            // Phase I: every task is ticked and nothing new is in the queue yet: say so, instead of leaving a silent gap
            // in which a file appears by itself.
            _g.Notifications.Show(_g.Content.Text("app.workqueue"), _g.Content.Text("queue.clear.toast"), "icon_task_done",
                a => _g.Apps.Launch(AppIds.WorkQueue, a), "ui_select");
            yield return Wait(3f);

            // The second cursor enters from the right edge, carrying employee_017.dat out of nowhere.
            var start = new Vector2(ScreenRig.Width + 6f, 330f);
            yield return FindDropSpot(new Vector2(600f, 300f));
            Vector2 spot = _dropSpot;
            E.Teleport(start);
            _g.Audio.SetAmbience(false, 0.15f);
            yield return E.Appear(start, 0.1f, true);
            _g.Taskbar.PointingDevices = 2;
            _g.Taskbar.FlashDevices();
            _g.Notifications.Show(_g.Content.Text("os.name"), "New pointing device detected.", "icon_info", null, "ui_select");
            _g.Flags.Set(Flags.EntitySeen);

            yield return CarryFileIn(E, ContentIds.File017, spot, MovementProfiles.Hesitant);
            // It lingers over the file, as if checking on it... then leaves.
            var icon = _g.Desktop.IconForFile(ContentIds.File017);
            if (icon != null) yield return E.Loiter(icon.Hit.Center, 14f, 2.2f, MovementProfiles.Hesitant);
            yield return E.MoveTo(new Vector2(-10f, 380f), MovementProfiles.HumanLike, 40f);
            yield return E.Vanish(0.3f);
            _g.Audio.SetAmbience(true, 2.5f);
            E.State = EntityState.Observing;

            yield return Wait(4f);
            _g.Mail.Deliver(ContentIds.MailUrgent);
            _g.Flags.Set(Flags.UrgentOrderReceived);
            yield return Wait(1f);
            GiveTask(ContentIds.TaskShred017);
        }

        // ------------------------------------------------------------------ PHASE 3: interference (key fun test)

        /// <summary>The conflict ends after this many defenses (Phase F: 6, so a struggling player reaches assist +2).</summary>
        const int ConflictDefenseCap = 6;
        /// <summary>Task hint toasts while the player has not tried yet (the supervisor's mail comes at 45 s).</summary>
        static readonly float[] ConflictHintTimes = { 48f, 90f };
        /// <summary>A player who never tries moves on after this long instead of 150 s.</summary>
        const float ConflictUntriedEnd = 100f;

        Action<DragPayload, TugOutcome> _conflictHint;

        Action<DragPayload> _conflictNote;

        void RemoveConflictHint()
        {
            if (_conflictHint != null) _g.Conflict.TugEnded -= _conflictHint;
            _conflictHint = null;
            if (_conflictNote != null) _g.Conflict.TugStarted -= _conflictNote;
            _conflictNote = null;
        }

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

            // Phase J: after the first lost tug the order admits it may not work, so a player who keeps losing can leave it
            // (every tug's result is its own notice, from the tug panel).
            RemoveConflictHint();
            _conflictHint = (p, outcome) =>
            {
                if (outcome != TugOutcome.EntityWins || CurrentBeat != "conflict" || !_g.Tasks.IsActive(ContentIds.TaskShred017)) return;
                _g.Tasks.Rewrite(ContentIds.TaskShred017, null, null, _g.Content.Text("task.017.lost.hint"));
            };
            _g.Conflict.TugEnded += _conflictHint;
            bool sessionNoted = false;
            _conflictNote = p =>
            {
                if (sessionNoted || CurrentBeat != "conflict") return;
                sessionNoted = true;
                ShowNote(8);
            };
            _g.Conflict.TugStarted += _conflictNote;

            int hintsShown = 0;
            while (true)
            {
                // After the first defence it stays on screen, watching.
                if (brain.Defenses >= 1) brain.AllowIdleLurk = true;
                bool shredded = _g.Files.GetFile(ContentIds.File017)?.Shredded ?? false;
                if (shredded) break;
                // Phase K: a file the player won is theirs until they let go, so the fight never ends (and blocks the order) under it.
                bool carrying = _g.Player.Payload != null && _g.Player.Payload.FileId == ContentIds.File017;
                if (brain.Defenses >= ConflictDefenseCap && !_g.Conflict.IsFighting && !_g.Shred.Busy && !carrying) break;
                float elapsed = Time.time - start;
                int attempts = _g.Memory.Count(MemoryKind.ShredAttempt, ContentIds.File017) - attemptsAtStart;
                // A player who has not tried yet (no shred request, no tug, no defense) is nudged, then let go.
                bool untried = attempts == 0 && !sessionNoted && brain.Defenses == 0 && !_g.Conflict.IsFighting && _g.Player.Payload == null;
                if (!followedUp && elapsed > 45f && attempts == 0)
                {
                    followedUp = true;
                    _g.Mail.Deliver(ContentIds.MailSupervisorCheck);
                }
                if (untried && hintsShown < ConflictHintTimes.Length && elapsed > ConflictHintTimes[hintsShown])
                {
                    hintsShown++;
                    ShowTaskHint(ContentIds.TaskShred017);
                }
                if (untried && elapsed > ConflictUntriedEnd)
                {
                    GameLog.Info(LogChannel.Story, "Conflict: no attempt, moving on");
                    break;
                }
                if (elapsed > 150f && !_g.Conflict.IsFighting && !_g.Shred.Busy && !carrying) break;
                if (elapsed > 175f)
                {
                    // Hard cap: never let an abandoned dialog stall the shift. A file still held here is taken back, and it says so.
                    _g.Shred.Abort();
                    if (_g.Conflict.IsFighting) _g.Conflict.Interrupt();
                    var held = _g.Player.Payload;
                    if (held != null && held.FileId == ContentIds.File017)
                    {
                        _g.DragDrop.Cancel(held);
                        _g.Conflict.Hud.ShowMessage(_g.Content.Text("tug.grabbed.back"), _g.Player.Position);
                    }
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
                _g.Audio.SetAmbience(false, 0.1f);
                // M14: 2.4 s of dead air with every sound ducked, the Disposal bin rattles, then the file is back.
                Audio.AudioManager.Duck(true);
                yield return Wait(ShredDeadAir - BinRattleSeconds);
                Audio.AudioManager.Duck(false);
                yield return RattleDisposal(BinRattleSeconds);
                _g.Fx.Glitch(0.35f, 1f);
                _g.Audio.Play("glitch_burst", 0.8f);
                // Re-arm "in use" first: the returned file must not be shreddable during the pause below.
                _g.Shred.IsInUse = id => id == ContentIds.File017;
                _g.Files.Restore(ContentIds.File017, ContentIds.FolderDesktop, Actor.Entity);
                _g.Desktop.SetFilePosition(ContentIds.File017, new Vector2(400f, 180f));
                _g.Audio.Play("low_thump", 0.8f);
                _g.Desktop.Attention(ContentIds.File017);
                _g.Audio.SetAmbience(true, 3f);
                _g.Flags.Set(Flags.File017Returned);
                yield return Wait(0.6f);
                _g.Notifications.Show(_g.Content.Text("app.disposal"), _g.Content.Format("error.inuse.body", "employee_017.dat"), "icon_error", null, "sys_error");
                // A beat of stillness: let the player notice the file is back before it speaks.
                yield return Wait(3f);
            }
            else if (!_g.Flags.Has(Flags.File017Returned))
            {
                _g.Mail.Deliver(ContentIds.MailSupervisorCheck);
                // The fight is over and the file is still held: the order says so (nobody can shred it while it is).
                BlockTask017();
            }
            _g.Shred.IsInUse = id => id == ContentIds.File017;
            RemoveConflictHint();
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
            yield return OpenNotepadAs(_ellen);
            _g.Flags.Set(Flags.EntitySpoke);
            RunSide(Note(5, 2.5f), "note-logged-on");
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeStop, OnEllenReply, turnHintKey: "notify.jotter.reply");
        }

        /// <summary>What the player's reply tells the story (flags) and the entity (memory).</summary>
        void OnEllenReply(DialogueReply r, string said)
        {
            switch (r.Category)
            {
                case "swear": _g.Flags.Set(Flags.PlayerSwore); break;
                case "who": _g.Flags.Set(Flags.PlayerAskedWho); break;
                case "refuse": _g.Flags.Set(Flags.PlayerRefused); _g.Memory.Record(MemoryKind.ResistedEntity, "notepad", Time.time); break;
                case "agree": _g.Flags.Set(Flags.PlayerAgreed); _g.Memory.Record(MemoryKind.ObeyedEntity, "notepad", Time.time); break;
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
                yield return E.Replay(rec, false, true);
                _g.Flags.Set(Flags.MimicShown);
                GameLog.Info(LogChannel.Entity, "Replayed the player's recorded movement (" + rec.Duration.ToString("0.0") + "s)");
                ShowNote(9);
                yield return Wait(1f);
            }
            yield return TypeLines(_ellen, _g.Content.Dialogue.recordLines, 4f);
            yield return Wait(1.5f);
            // A message from your own account... dated eleven years ago.
            _g.Mail.Deliver(ContentIds.MailNoSender);
            yield return Wait(3f);
            yield return TypeLines(_ellen, _g.Content.Dialogue.cameraLines, 3.5f);

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
            // It is always here for this (a debug jump straight to the reveal skips its arrival).
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.5f), 0.4f, false);
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
            _g.Audio.PlayLoop("drone_tension", DroneVolume, 6f);
            _g.Audio.Play("door_distant", 0.5f, 1f, -0.3f);
            yield return WaitWatching(4f, 12f);

            // 3) The entity panics and fights to shut the feed. Each time you reopen it, it is closer.
            E.State = EntityState.Panicked;
            var panic = _g.Content.Dialogue.panicLines;
            int panicLine = 0;
            float lastOpen = Time.time;
            int seenReopens = _cameraReopens;
            bool ducked = false;
            while (Time.time - revealStart < 150f)
            {
                // M6: while the feed is shut (its timestamp frozen) the drone sinks 6 dB and a semitone; it snaps back on.
                var shown = _g.Apps.Find<CameraApp>();
                bool feedShut = shown == null || !shown.IsOpen || shown.Window.IsMinimized;
                if (feedShut != ducked)
                {
                    ducked = feedShut;
                    _g.Audio.SetLoopVolume("drone_tension", ducked ? DroneVolume * 0.5f : DroneVolume, ducked ? 0.25f : 0f);
                    _g.Audio.SetLoopPitch("drone_tension", ducked ? OneSemitoneDown : 1f);
                }
                // Reopened (or restored) since we last looked - even mid-typing: it advances.
                if (_cameraReopens > seenReopens)
                {
                    seenReopens = _cameraReopens;
                    var reopened = _g.Apps.Find<CameraApp>();
                    if (reopened != null) reopened.Select(ContentIds.Cam03, null);
                    // M6: the new position resolves out of half a second of static instead of a hard cut.
                    var next = rig.Figure == FigureStage.Doorway ? FigureStage.Middle : FigureStage.BehindChair;
                    yield return StaticResolve(() => rig.Figure = next, 0.5f);
                    _g.Audio.Play("footstep_distant", 0.4f, 0.9f, 0.2f);
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
                    var closed = new bool[1];
                    if (close != null) yield return E.ClickElement(close.Hit, MovementProfiles.Panicked, closed, 4f);
                    // You blocking the close box is a fight it keeps losing (it tries again next time round);
                    // anything else in the way (another window) it simply pushes past.
                    if (!closed[0] && cam.IsOpen && !cam.Window.IsMinimized && close != null && !E.IsBlockedByPlayer(close.Hit))
                    {
                        cam.Window.Focus(E.Agent);
                        yield return Wait(0.2f);
                        if (close != null && cam.IsOpen) yield return E.ClickElement(close.Hit, MovementProfiles.Panicked, closed, 2f);
                        if (!closed[0] && cam.IsOpen)
                        {
                            cam.Window.Close(E.Agent);
                            _g.Fx.Glitch(0.15f, 0.7f);
                            GameLog.Info(LogChannel.Entity, "Entity forced the camera feed shut");
                        }
                    }
                    if (panicLine < panic.Length) yield return TypeLines(_ellen, new[] { panic[panicLine++] }, 6f);
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
                        if (panicLine < panic.Length) yield return TypeLines(_ellen, new[] { panic[panicLine++] }, 7f);
                        yield return WaitWatching(4f, 8f);
                        break;
                    }
                }
                yield return null;
            }

            if (ducked)
            {
                _g.Audio.SetLoopVolume("drone_tension", DroneVolume, 0f);
                _g.Audio.SetLoopPitch("drone_tension", 1f);
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

        const float DroneVolume = 0.25f;
        const float ShredDeadAir = 2.4f, BinRattleSeconds = 0.4f;

        /// <summary>The Disposal icon shakes on the spot (the shredded file is on its way back).</summary>
        IEnumerator RattleDisposal(float seconds)
        {
            var icon = _g.Desktop.DisposalIcon;
            var rt = icon != null ? (RectTransform)icon.transform : null;
            if (rt == null) yield break;
            Vector2 home = rt.anchoredPosition;
            float t = 0f, nextTick = 0f;
            int n = 0;
            while (t < seconds && rt != null)
            {
                if (t >= nextTick)
                {
                    nextTick = t + 0.12f;
                    _g.Audio.Play("mouse_release", 0.35f, 0.6f, Audio.AudioManager.PanFor(rt.position.x));
                }
                rt.anchoredPosition = home + new Vector2((n++ % 2 == 0) ? 2f : -2f, 0f);
                t += Time.deltaTime;
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = home;
        }
        /// <summary>2^(-1/12).</summary>
        const float OneSemitoneDown = 0.9439f;

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
