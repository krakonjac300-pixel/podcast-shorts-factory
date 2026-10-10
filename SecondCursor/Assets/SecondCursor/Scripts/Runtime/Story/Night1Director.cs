using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Game;
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
    public sealed partial class Night1Director : NightDirector
    {
        static readonly string[] BeatList =
        {
            "boot", "work", "anomaly", "presence", "conflict", "communication", "escalation", "reveal", "ending",
        };

        static readonly string[] Checkpoints = { "work", "anomaly", "conflict", "escalation", "reveal" };

        CursorRecording _ledgerClip;
        Speaker _ellen;
        int _cameraReopens;
        /// <summary>Phase M: the reveal ended on its hit (the ending skips its own power down).</summary>
        bool _afterHit;
        bool _caught;

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
                if (t.Id == ContentIds.TaskArchiveLedger)
                {
                    _ledgerClip = g.Recorder.Extract(Time.time - 6.5f, Time.time + 0.1f);
                    if (!IsPreparing) DeliverFirstShiftMail("mail_ledger_receipt");
                }
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
            // Phase Q1 (owner 4): a program icon the player moved goes back where it was, once, when they are not looking.
            g.Desktop.AppIconMovedByPlayer += OnAppIconMoved;
            // Phase Q2 (owner 3): deciding Joan Nakamura's wipe changes her record tonight, whoever decided it.
            g.Orders.Decided += OnOrderDecided;
            WatchOpeningProgress();
        }

        /// <summary>
        /// Phase Q2 (owner 3, personal work): WO-3318 is Joan Nakamura's drive (Denise's mail asks you to go easy on her). Either way it
        /// is decided, her Personnel record changes at once (her review moved up to 3:00 AM tonight), and a notice says so; Night 2
        /// finds her TERMINATED.
        /// </summary>
        void OnOrderDecided(string id, string decision, CursorAgent by)
        {
            if (IsPreparing) return;
            if (id != ContentIds.Order3317 && id != ContentIds.Order3318) return;
            _g.Flags.Set(WorkOrderRules.MemoryKey(1, id, decision));
            if (id == ContentIds.Order3318) Note163(decision, true);
            DeliverFirstShiftMail("mail_" + id + "_" + decision);
        }

        /// <summary>Joan's record after WO-3318 (a jump or Continue past the order puts the line back without a notice).</summary>
        void Note163(string decision, bool notify)
        {
            if (decision != "approve" && decision != "reject") return;
            var g = _g;
            var e = g.Content.Employee(ContentIds.Employee163);
            string note = g.Content.Text(decision == "approve" ? "n1.163.approve" : "n1.163.reject");
            if (e == null || e.notes.Contains(note)) return;
            e.notes = e.notes + " " + note;
            g.Apps.Find<StaffApp>()?.Refresh();
            if (!notify) return;
            g.Notifications.Show(g.Content.Text("app.staff"), g.Content.Format("notify.personnel.updated", ContentIds.Employee163, "J. Nakamura"), "icon_info",
                a => g.Apps.Launch(AppIds.Staff, a), "ui_select");
            GameLog.Info(LogChannel.Story, "Personnel 163 changed after WO-3318 (" + decision + ")");
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

        // Night 1: a launch with no pointer behind it is denied too (the shift's own world has no reason to open the viewer).
        bool CanLaunch(string appId, CursorAgent by)
        {
            if (appId == AppIds.Camera && _g.Flags.Has(CameraDeclined))
            {
                Dialogs.Message(_g, "Camera 03: offline", "Live video was declined for this shift. Session 017 is watching the door.",
                    "icon_camera", new[] { "OK" }, null);
                return false;
            }
            return CameraGate(appId, by, false);
        }

        protected override void CleanUpForJump()
        {
            base.CleanUpForJump();
            RemoveConflictHint();
            EndFirstViewGuard();
            // The scheduler dropped a waiting icon slot (CancelAll): a later beat may arm it again.
            _iconBackArmed = false;
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
                    if (_caught)
                    {
                        _ending = new EndingSequence(_g, EndingSpec.Capture());
                        return _ending.Run();
                    }
                    CompleteNight(ContentIds.EndingN1Blackout);
                    // Phase H: the card says how the night ended (WS-04 went dark; the file came back or never went).
                    var spec = EndingSpec.Night1();
                    spec.AfterHit = _afterHit;
                    bool shreddedOnce = _g.Flags.Has(Flags.File017ShreddedOnce);
                    spec.Outcome = _g.Content.Format(shreddedOnce ? "end.n1.outcome.shredded" : "end.n1.outcome.kept", _g.Clock.Format12());
                    if (_g.Flags.Has(CameraDeclined)) spec.Outcome = _g.Content.Text("end.n1.outcome.offline");
                    // Phase R: what it means (the order could not be done, and why), why the shift ended before 7:00, and what carries into Night 2.
                    spec.OutcomeDetail = new[]
                    {
                        _g.Content.Text(shreddedOnce ? "end.n1.detail.shredded" : "end.n1.detail.kept"),
                        _g.Content.Format("end.n1.shift", _g.Clock.Format12()),
                    };
                    spec.CarryLine = _g.Content.Text("end.n1.carry");
                    // Phase Q2: what session 017 kept (V7) and the night's Retention Record (T2), on the demo's card too.
                    spec.KeptLine = KeptLine();
                    spec.RecordRows = CardRows();
                    if (!IsStandIn) Game.DemoHandoffIO.WriteFromDemo(_g.Save, _g.Flags.Has(Flags.File017ShreddedOnce));
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

        /// <summary>"Session 017 kept a copy of: your first reply and your hand on the bin." from what really carries over ("" = nothing).</summary>
        string KeptLine()
        {
            var c = _g.Content;
            var parts = new List<string>();
            if (PlayerLines.Count > 0) parts.Add(c.Text("kept.reply"));
            if (_g.Flags.Has(Flags.File017ShreddedOnce)) parts.Add(c.Text("kept.bin"));
            if (_g.Save != null && NameCapture.IsValid(_g.Save.playerName)) parts.Add(c.Text("kept.name"));
            if (parts.Count == 0) return "";
            string joined = parts.Count == 1 ? parts[0] : string.Join(", ", parts.GetRange(0, parts.Count - 1)) + c.Text("kept.and") + parts[parts.Count - 1];
            return c.Format("end.n1.kept", joined);
        }

        /// <summary>Phase Q2 (T5): the archive drag of the ledger, resampled for the title's second pointer.</summary>
        protected override int[] GhostPath()
        {
            var clip = _ledgerClip;
            if (clip == null || clip.IsEmpty) return null;
            int rate = Core.Game.SaveData.GhostRate;
            int n = Mathf.Min(Core.Game.SaveData.GhostMaxPoints, Mathf.FloorToInt(clip.Duration * rate) + 1);
            var path = new int[n * 2];
            for (int i = 0; i < n; i++)
            {
                var sample = clip.Sample(i / (float)rate);
                path[i * 2] = Mathf.RoundToInt(sample.x);
                path[i * 2 + 1] = Mathf.RoundToInt(sample.y);
            }
            return path;
        }

        /// <summary>Put the world in the state a beat expects when jumping straight to it (debug or Continue).</summary>
        protected override void Prepare(int beatIndex)
        {
            _caught = false;
            _afterHit = false;
            var g = _g;
            ShowDesktop();
            if ((beatIndex == 1 || beatIndex == 2) && RestoreOpeningWork()) return;
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
                RestoreFirstShiftOrder(ContentIds.Order3317);
                RestoreFirstShiftOrder(ContentIds.Order3318);
                DeliverFirstShiftMail("mail_ledger_receipt", false);
                Note163(g.Orders.DecisionFor(ContentIds.Order3318), false);
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
            if (!_quickStartDismissed)
            {
                yield return QuickStartWithComfort();
                _quickStartDismissed = true;
                SaveCurrentProgress();
            }
            foreach (var id in new[] { ContentIds.TaskReadBriefing, ContentIds.TaskArchiveLedger,
                         ContentIds.TaskVerify3317, ContentIds.TaskVerify3318, ContentIds.TaskShredCache })
            {
                if (_g.Tasks.IsCompleted(id))
                {
                    if (id == ContentIds.TaskArchiveLedger) yield return LedgerAcknowledgement();
                    continue;
                }
                GiveTask(id);
                if (id == ContentIds.TaskVerify3317) RunSide(TinyNudge(), "tiny-nudge");
                yield return WaitTask(id, id == ContentIds.TaskReadBriefing ? _g.Difficulty.BriefingHintFirst : -1f);
                if (id == ContentIds.TaskArchiveLedger) yield return LedgerAcknowledgement();
            }
            _g.Flags.Set(Flags.TutorialDone);
            // An icon dragged during the tutorial counts too (the scare is armed here, not only by a later drag).
            ArmIconBack();
        }

        /// <summary>Phase 0's single ambiguous oddity: the Staff Directory window shifts a few pixels.</summary>
        IEnumerator TinyNudge()
        {
            if (_g.Flags.Has("n1.personnel_nudge")) yield break;
            yield return WaitUntil(() => _g.Apps.FindById(AppIds.Staff) != null, 90f);
            yield return Wait(UnityEngine.Random.Range(6f, 9f));
            var staff = _g.Apps.FindById(AppIds.Staff);
            if (staff == null || !staff.IsOpen || staff.Window.DraggedBy != null) yield break;
            staff.Window.MoveBy(new Vector2(6f, 2f));
            _g.Flags.Set("n1.personnel_nudge");
            SaveCurrentProgress();
            GameLog.Info(LogChannel.Story, "Anomaly: window nudged (tiny)");
        }

        // ------------------------------------------------------------------ PHASE 1: ambiguous

        IEnumerator Anomaly()
        {
            E.Phase = EntityPhase.Ambiguous;
            ArmIconBack();
            if (_g.Flags.Has(Flags.FirstAnomaly))
            {
                GiveTask(ContentIds.TaskArchiveBatch);
                yield return WaitTask(ContentIds.TaskArchiveBatch, 40f);
                yield break;
            }
            yield return Wait(4f);
            _g.Mail.Deliver(ContentIds.MailIt);
            yield return Wait(3f);
            GiveTask(ContentIds.TaskArchiveBatch);

            // Anomaly 0: your own cursor twitches a few pixels, once.
            yield return Wait(6f);
            _g.PlayerView.Flinch(new Vector2(UnityEngine.Random.Range(3f, 5f), UnityEngine.Random.Range(-4f, -2f)));
            GameLog.Info(LogChannel.Story, "Anomaly: player cursor flinched");
            // Phase M (N1-1): then your chair creaks, before the file selects itself. Nothing says anyone is there.
            Scare("chair_creak", 0.35f, 0f, UnityEngine.Random.Range(3.5f, 5f), 20f);
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
            HoldControl(true);   // Phase R: the wait line, until she has gone
            _g.Audio.SetAmbience(false, 0.15f);
            yield return E.Appear(start, 0.1f, true);
            _g.Taskbar.PointingDevices = 2;
            _g.Taskbar.FlashDevices();
            _g.Notifications.Show(_g.Content.Text("os.name"), "Pointing device 2 (session 017) is active.", "icon_info", null, "ui_select", false, null, Core.Game.NoticeKind.Entity);
            _g.Flags.Set(Flags.EntitySeen);

            yield return CarryFileIn(E, ContentIds.File017, spot, MovementProfiles.Hesitant);
            // It lingers over the file, as if checking on it... then leaves.
            var icon = _g.Desktop.IconForFile(ContentIds.File017);
            if (icon != null) yield return E.Loiter(icon.Hit.Center, 14f, 2.2f, MovementProfiles.Hesitant);
            yield return E.MoveTo(new Vector2(-10f, 380f), MovementProfiles.HumanLike, 40f);
            yield return E.Vanish(0.3f);
            HoldControl(false);
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
                // Phase S: the file is held by the story. After two lost shred races the fight is over (the card said so plainly the first time).
                if (RaceRules.Night1FightOver(_g.Shred.RacesLost) && !_g.Conflict.IsFighting && !_g.Shred.Busy && !carrying)
                {
                    GameLog.Info(LogChannel.Story, "Conflict: two shred races lost, session 017 takes the night over");
                    // Let the last card (it says to stop fighting) be read before the takeover starts.
                    yield return Wait(3f);
                    break;
                }
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
                HoldControl(true);   // Phase R: 2.4 s of dead air and the bin: the wait line says it is on purpose
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
                // Phase Q2 (V7): your hand on the bin carries into the next night.
                KeptCopy("bin");
                // A beat of stillness: let the player notice the file is back before it speaks.
                yield return Wait(3f);
                HoldControl(false);
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
            HoldControl(true);   // Phase R: she is finishing her move before she types
            yield return E.WaitIdle();
            HoldControl(false);
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
            // Phase Q2 (V9, V6): after an ending (or the demo's handoff) she remembers the last run's first words: NOT AGAIN.
            bool again = _g.Save != null && _g.Save.EchoesLastRun;
            if (again) GameLog.Info(LogChannel.Story, "Night 1 remembers the last run");
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeStop, OnEllenReply, turnHintKey: "notify.jotter.reply",
                extraLines: ex => again && ex.id == ContentIds.ExchangeStop ? _g.Content.Lines("n1_again") : null);
        }

        /// <summary>What the player's reply tells the story (flags) and the entity (memory).</summary>
        void OnEllenReply(DialogueReply r, string said)
        {
            // Phase Q2 (V7): the first reply is saved with the night (Night 2 quotes it).
            if (r.Tag != DialogueEngine.NameTag) KeptCopy("reply"); // a name reply already announced "name" (one toast, not two)
            // Phase Q3 (D2, V2): her name (the demo's Her Name beat) and a word to chat are remembered.
            if (r.Tag == "name" || DialogueEngine.MentionsHerName(said)) _g.Flags.Set(MemoryFlags.N1SaidName);
            if (r.Tag == "chat") _g.Flags.Set(MemoryFlags.N1NamedChat);
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
            HoldControl(true);   // Phase R: the replay, her lines and the camera are hers: the wait line, until the viewer is open
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.5f, ScreenRig.Height * 0.5f), 0.5f, true);
            // Phase L (finding 15): her last line tells you to watch the screen; the replay starts a second later than it did,
            // so you have looked up from the Jotter by then.
            yield return Wait(2.5f);

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
            // Phase M (N1-2): a knock on the office door, before she shows you the camera that sees it (M1, M6).
            Scare("knock_door", 0.45f, -0.1f, 2f, 15f);
            yield return Wait(3f);
            yield return TypeLines(_ellen, _g.Content.Dialogue.cameraLines, 3.5f);

            // It opens what you were not allowed to open: the Restricted folder, then the cameras.
            _g.Files.SetFolderLocked(ContentIds.FolderRestricted, false);
            _g.Notifications.Show(_g.Content.Text("os.name"), "Permissions on Restricted changed by session 017.", "icon_lock", a => _g.Apps.OpenFolder(ContentIds.FolderRestricted, a), "ui_select", false, null, Core.Game.NoticeKind.Entity);
            _g.Flags.Set(Flags.CameraUnlocked);
            _g.Notifications.Show(_g.Content.Text("app.camera"), "Clearance override accepted: session 017.", "icon_lock", null, "sys_warning", false, null, Core.Game.NoticeKind.Entity);
            if (_g.Flags.Has(Flags.PlayerRefused))
            {
                HoldControl(false);
                yield return CameraConsent();
                if (_g.Flags.Has(CameraDeclined)) yield break;
                HoldControl(true);
            }
            yield return Wait(0.8f);
            yield return E.OpenApp(AppIds.Camera, MovementProfiles.HumanLike);
            yield return Wait(0.8f);
            var cam = _g.Apps.Find<CameraApp>();
            if (cam == null) cam = (CameraApp)_g.Apps.Launch(AppIds.Camera, E.Agent);
            HoldControl(false);
            if (cam != null)
            {
                var button = cam.Window.Element("camera:" + ContentIds.Cam03);
                if (button != null) yield return E.ClickElement(button, MovementProfiles.HumanLike, null, 3f);
                if (cam.CurrentCamera != ContentIds.Cam03) cam.Select(ContentIds.Cam03, E.Agent);
                // Phase Q1 (owner 5): the first look at your own office is protected.
                RunSide(GuardFirstView(), "first-view");
            }
        }
    }
}
