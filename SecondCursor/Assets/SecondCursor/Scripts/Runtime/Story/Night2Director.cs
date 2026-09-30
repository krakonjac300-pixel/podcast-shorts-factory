// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.FileSystem;
using SecondCursor.Core.Story;
using SecondCursor.Core.Tasks;
using SecondCursor.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Night 2, HELD (Thu 11/19/98, expansion spec section 4): boot -> ordinary work (Batch 45, orders 3319 and
    /// 3321) -> Ellen helps with Batch 46 and talks -> she writes her own tasks into the Work Queue -> Gary,
    /// the third pointer, surfaces and talks -> the order to shred employee_209.dat (finish or keep him) ->
    /// the first Custodial round on camera -> blackout. This file holds the flow up to Ellen's tasks;
    /// Night2Director.Gary.cs holds Gary, the finish, the round and the ending.
    /// </summary>
    public sealed partial class Night2Director : NightDirector
    {
        static readonly string[] BeatList = { "boot", "work", "help", "asks", "third", "finish", "rounds", "ending" };
        static readonly string[] Checkpoints = { "work", "asks", "finish", "rounds" };
        /// <summary>The earliest clock each beat shows after a jump (only ever raised).</summary>
        static readonly int[] BeatClock = { 112, 112, 128, 138, 152, 169, 180, 185 };
        /// <summary>Phase L: 0.054 (was 0.06), so the two new work orders (about 100 s) do not bring 2:49 and the rest of the night's landmarks forward.</summary>
        const float Rate = 0.054f;
        /// <summary>3:00 AM: the deadline of the order to shred employee_209.dat.</summary>
        const int LateShredMinute = 180;
        /// <summary>The clock minute employee_209.dat was shredded after its deadline (0 = it was not).</summary>
        const string LateShredFlag = "n2.late_shred_minute";
        const int FreezeMinute = 2 * 60 + 49;
        /// <summary>Phase L: trust lost by approving the removal of Pointing Device 2 (a refusal costs 0.15).</summary>
        const float TriedToUnplugCost = 0.10f;
        /// <summary>Seconds into the work beat before the second cursor looks in from the edge (Phase F).</summary>
        const float GlimpseAfter = 90f;

        static readonly string[] WorkTasks =
        {
            ContentIds.TaskN2Briefing, ContentIds.TaskN2Batch45, ContentIds.TaskN2Verify3319, ContentIds.TaskN2Verify3321, ContentIds.TaskN2Cache,
        };
        static readonly string[] Batch45 = { ContentIds.Batch45A, ContentIds.Batch45B, ContentIds.Batch45C };
        static readonly string[] Batch46 = { ContentIds.Batch46A, ContentIds.Batch46B, ContentIds.Batch46C, ContentIds.Batch46D };

        Speaker _ellen, _gary;
        bool _freezeClock = true;
        bool _stopHelping;
        float _firstEntityTaskAt = -1f;
        /// <summary>Phase M: a request ignored this long is whispered about (once a night).</summary>
        const float WhisperAfter = 40f;
        bool _whisperAsked;
        float _lastCloseIt = -100f;
        /// <summary>Lines Ellen types as soon as she is free (a mail read mid-task, for example).</summary>
        readonly List<string> _ellenQueue = new List<string>();

        public override string[] Beats => BeatList;
        public override int Night => 2;
        protected override string[] CheckpointBeats => Checkpoints;
        protected override float ClockRate => Rate;

        EntityController E => _g.Entity;
        EntityController Gary => _g.Gary;
        string[] Lines(string id) => _g.Content.Lines(id);
        bool Done(string taskId) => _g.Tasks.IsCompleted(taskId);
        /// <summary>Phase I: the note on a remote request that ran out or whose file is gone; it stays in the queue, struck through.</summary>
        string Expired => _g.Content.Text("workqueue.withdrawn.expired", "expired");

        protected override void Init()
        {
            base.Init();
            var g = _g;
            _ellen = AddSpeaker(g.Entity);
            _gary = AddSpeaker(g.Gary);
            _gary.MoveProfile = MovementProfiles.TiredName;
            g.Apps.CanLaunch = CanLaunch;
            g.Shred.IsInUse = id => id == ContentIds.File017;
            // Phase I: after the 3:00 deadline a late shred says what it does now (the card's "He is still held" is its result).
            g.Shred.ConfirmNote = id => id == ContentIds.File209 && g.Clock.TotalMinutes >= LateShredMinute ? g.Content.Text("shred.confirm.late", "") : null;
            // Phase K (finding 10): a shred after the deadline is remembered with its time, so the card can say it happened.
            g.Shred.Completed += (id, by) =>
            {
                if (id == ContentIds.File209 && _finishOver && g.Clock.TotalMinutes >= LateShredMinute) g.Flags.SetCounter(LateShredFlag, g.Clock.TotalMinutes);
            };
            g.Orders.Decided += (id, decision, by) =>
            {
                // Approving the wipe of Gary's drive (wrong: he is on leave) is remembered.
                if (id == ContentIds.Order3321 && decision == "approve") g.Flags.Set(MemoryFlags.N2WipedGary);
                // Phase L: approving IT's driver removal tries to unplug her hand. It fails, and she notices (a small trust cost).
                if (id == ContentIds.Order3322 && decision == "approve" && by != null && by.IsPlayer && !IsPreparing)
                {
                    g.Memory.Record(MemoryKind.ResistedEntity, id, Time.time, TriedToUnplugCost);
                    RunSide(DeviceFlicker(), "device-flicker");
                }
            };
            g.Entity.Brain.CloseCameraBlocked = OnCloseCameraBlocked;
        }

        bool CanLaunch(string appId, CursorAgent by)
        {
            if (appId != AppIds.Camera || _g.Flags.Has(Flags.CameraUnlocked)) return true;
            if (by == null || by.IsEntity) return true;
            _g.Flags.Set(Flags.CameraDeniedSeen);
            var c = _g.Content;
            Dialogs.Message(_g, c.Text("camera.denied.title"), c.Text("camera.denied.body"), "icon_lock", new[] { "OK" }, null);
            return false;
        }

        protected override void Update()
        {
            base.Update();
            // The clock waits at 2:49 until the order for 209 arrives (then 3:00 lands on its deadline).
            if (_freezeClock && !_g.Clock.Frozen && _g.Clock.TotalMinutes >= FreezeMinute)
            {
                _g.Clock.Frozen = true;
                GameLog.Info(LogChannel.Story, "Clock held at 2:49");
            }
        }

        protected override void CleanUpForJump()
        {
            base.CleanUpForJump();
            _stopHelping = false;
            _ellenQueue.Clear();
            UnhookFinish();
            UnhookRounds();
            _g.Clock.Frozen = false;
            if (_g.CameraRig != null) _g.CameraRig.SignalLost = false;
        }

        protected override IEnumerator RunBeat(string beat)
        {
            switch (beat)
            {
                case "boot": return Boot();
                case "work": return Work();
                case "help": return Help();
                case "asks": return Asks();
                case "third": return Third();
                case "finish": return Finish();
                case "rounds": return RoundsBeat();
                default: return EndingBeat();
            }
        }

        // ------------------------------------------------------------------ jumps and checkpoints

        protected override void Prepare(int beatIndex)
        {
            var g = _g;
            g.Player.Enabled = true;
            g.Fx.SetBlack(false);
            if (!g.Flags.Has(Flags.LoggedIn))
            {
                g.Flags.Set(Flags.LoggedIn);
                g.Audio.SetAmbience(true, 2f);
            }
            g.Taskbar.PointingDevices = 2;
            int minutes = BeatClock[Mathf.Clamp(beatIndex, 0, BeatClock.Length - 1)];
            if (g.Clock.TotalMinutes < minutes) g.Clock.Set(minutes / 60, minutes % 60);
            _freezeClock = beatIndex < 5;
            if (!_freezeClock) g.Clock.Frozen = false;

            if (beatIndex > 1)
            {
                foreach (var t in WorkTasks) g.Tasks.ForceComplete(t);
                g.Flags.Set(Flags.TutorialDone);
                g.Mail.MarkRead(ContentIds.MailN2Briefing, null);
                foreach (var f in Batch45) MoveIfIn(f, ContentIds.FolderIntake, ContentIds.FolderArchive);
                if (g.Files.Exists(ContentIds.FileCacheN2) && g.Files.Shred(ContentIds.FileCacheN2, Actor.System)) g.Shred.MarkShredded();
                foreach (var id in new[] { ContentIds.Order3319, ContentIds.Order3321 })
                {
                    var order = g.Content.Order(id);
                    if (order != null && g.Orders.DecisionFor(id) == null) g.Orders.Decide(id, order.correct, null);
                }
                RestoreChoice(ContentIds.TaskN2Verify3320, ContentIds.Order3320, ContentIds.MailN2CastellJoan);
                RestoreChoice(ContentIds.TaskN2Verify3322, ContentIds.Order3322, ContentIds.MailN2PellPatch);
                if (g.Apps.FindById(AppIds.WorkQueue) == null) g.Apps.Launch(AppIds.WorkQueue, null);
            }
            if (beatIndex > 2)
            {
                foreach (var f in Batch46) MoveIfIn(f, ContentIds.FolderIntake, ContentIds.FolderArchive);
                g.Tasks.ForceComplete(ContentIds.TaskN2Batch46);
                g.Flags.Set(Flags.N2EllenHelped);
                g.Flags.Set(Flags.EntitySeen);
                g.Flags.Set(Flags.EntitySpoke);
            }
            if (beatIndex > 3) PrepareAsksDone();
            if (beatIndex > 4) PrepareGaryArrived();
            if (beatIndex > 5) PrepareFinishResolved();
            if (beatIndex > 6) g.Flags.Set(Flags.N2RoundsDone);
        }

        void PrepareAsksDone()
        {
            var g = _g;
            g.Files.SetHidden(ContentIds.File214, false);
            if (g.Flags.Has(MemoryFlags.N2Hid214)) MoveIfIn(ContentIds.File214, ContentIds.FolderIntake, ContentIds.FolderArchive);
            foreach (var (task, flag) in EntityAsks)
            {
                if (g.Flags.Has(flag)) g.Tasks.ForceComplete(task);
                else g.Tasks.Withdraw(task);
            }
            g.Mail.Deliver(ContentIds.MailN2RuthWarning, false);
        }

        void MoveIfIn(string fileId, string fromFolder, string toFolder)
        {
            if (_g.Files.Exists(fileId) && _g.Files.FolderOf(fileId) == fromFolder) _g.Files.Move(fileId, toFolder, Actor.System);
        }

        // ------------------------------------------------------------------ BOOT

        IEnumerator Boot()
        {
            _g.Player.Enabled = true;
            _boot = new BootSequence(_g);
            yield return _boot.Run(false, Night);
            _boot = null;
            _g.Flags.Set(Flags.LoggedIn);
            _g.Audio.Play("sys_startup");
            _g.Audio.SetAmbience(true, 3f);
            _g.Taskbar.PointingDevices = 2;
            RunSide(Pointer3Lost(), "pointer3-lost");
            yield return Wait(1.2f);
        }

        IEnumerator Pointer3Lost()
        {
            yield return Wait(4f);
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Text("notify.pointer3.lost"), "icon_info", null, "sys_warning");
        }

        /// <summary>Approved: Pointing Device 2 drops out of the tray for a moment and comes back (the change failed).</summary>
        IEnumerator DeviceFlicker()
        {
            var g = _g;
            int devices = g.Taskbar.PointingDevices;
            yield return Wait(0.6f);
            g.Taskbar.PointingDevices = 1;
            yield return Wait(1.2f);
            g.Taskbar.PointingDevices = devices;
            g.Taskbar.BlinkDevice(2);
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("notify.pointer2.back"), "icon_info", null, "sys_warning");
        }

        // ------------------------------------------------------------------ WORK

        IEnumerator Work()
        {
            E.Phase = EntityPhase.Ambiguous;
            E.State = EntityState.Observing;
            _g.Apps.Launch(AppIds.WorkQueue, null);
            yield return Wait(1.2f);
            yield return WelcomeBack();
            GiveTask(ContentIds.TaskN2Briefing);
            yield return Wait(1.5f);
            if (_g.Mail.UnreadCount > 0)
                _g.Notifications.Show(_g.Content.Text("app.mail"), _g.Content.Format("notify.newmail", _g.Mail.UnreadCount), "icon_mail_unread",
                    a => _g.Apps.Launch(AppIds.Mail, a));
            RunSide(RenameAnomaly(), "rename");
            RunSide(EllenGlimpse(), "ellen-glimpse");
            yield return WaitTask(ContentIds.TaskN2Briefing, _g.Difficulty.BriefingHintFirst);
            GiveTask(ContentIds.TaskN2Batch45);
            yield return WaitTask(ContentIds.TaskN2Batch45);
            GiveTask(ContentIds.TaskN2Verify3319);
            yield return WaitTask(ContentIds.TaskN2Verify3319);
            // Phase L: two orders the player may decide either way (a friend's plea; IT's offer to remove the second pointer).
            RevealOrder(ContentIds.TaskN2Verify3320, ContentIds.Order3320, ContentIds.MailN2CastellJoan);
            yield return WaitOrder(ContentIds.TaskN2Verify3320, ContentIds.Order3320);
            GiveTask(ContentIds.TaskN2Verify3321);
            yield return WaitTask(ContentIds.TaskN2Verify3321);
            RevealOrder(ContentIds.TaskN2Verify3322, ContentIds.Order3322, ContentIds.MailN2PellPatch);
            yield return WaitOrder(ContentIds.TaskN2Verify3322, ContentIds.Order3322);
            GiveTask(ContentIds.TaskN2Cache);
            RunSide(ReadItFirst(), "read-it-first");
            yield return WaitTask(ContentIds.TaskN2Cache);
            _g.Flags.Set(Flags.TutorialDone);
        }

        /// <summary>Seconds the cursor rests on ~nxs0149.tmp before she asks you to read it (M3).</summary>
        const float ReadItFirstHover = 2f;

        /// <summary>
        /// M3: the player's cursor rests on ~nxs0149.tmp (the file with their own Night 1 words in it) for 2 s: she types
        /// READ IT FIRST, once, so nobody shreds it unread.
        /// </summary>
        IEnumerator ReadItFirst()
        {
            string id = "file:" + ContentIds.FileCacheN2;
            float since = -1f;
            while (CurrentBeat == "work" && _g.Files.Exists(ContentIds.FileCacheN2) && !_g.Tasks.IsCompleted(ContentIds.TaskN2Cache))
            {
                var hovered = _g.Player.Hovered;
                bool on = hovered != null && hovered.elementId == id && !_g.Player.Held;
                if (!on) since = -1f;
                else if (since < 0f) since = Time.time;
                else if (Time.time - since >= ReadItFirstHover)
                {
                    GameLog.Info(LogChannel.Story, "READ IT FIRST (cursor rested on ~nxs0149.tmp)");
                    yield return SayDirect(_ellen, Lines("n2_read_it_first"), 4.5f);
                    yield break;
                }
                yield return null;
            }
        }

        /// <summary>
        /// Phase F: about 90 s into the shift the second cursor looks in from the right edge for a second and leaves,
        /// with its tray mouse blinking. The only sign of her before the help beat, so returning players are not
        /// left with Night 1's chores alone for six minutes.
        /// </summary>
        IEnumerator EllenGlimpse()
        {
            yield return Wait(GlimpseAfter);
            if (CurrentBeat != "work" || E.IsVisible || E.Busy) yield break;
            E.Run(Glimpse(), "glimpse");
        }

        IEnumerator Glimpse()
        {
            float y = Mathf.Clamp(_g.Player.Position.y + UnityEngine.Random.Range(-70f, 70f), 120f, ScreenRig.Height - 60f);
            var edge = new Vector2(ScreenRig.Width + 6f, y);
            E.Teleport(edge);
            yield return E.Appear(edge, 0.35f, false);
            _g.Taskbar.BlinkDevice(2);
            yield return E.MoveTo(edge + new Vector2(-38f, UnityEngine.Random.Range(-6f, 6f)), MovementProfiles.Hesitant, 12f);
            GameLog.Info(LogChannel.Story, "Anomaly: the second cursor looked in");
            // Phase M (N2-1): 20 s later your own click is answered once from the room (M1's echo has left the camera).
            Scare("click_wrong", 1f, 0f, 20f, 300f);
            yield return Wait(0.9f);
            yield return E.MoveTo(edge + new Vector2(8f, 0f), MovementProfiles.HumanLike, 20f);
            yield return E.Vanish(0.25f);
        }

        /// <summary>Once: 1.5 s after the player first drags a Batch 45 file, batch45_c becomes b7_seat.dat for 6 s.</summary>
        IEnumerator RenameAnomaly()
        {
            bool Dragging45() => _g.Player.Payload != null && _g.Player.Payload.FileId != null && _g.Player.Payload.FileId.StartsWith("batch45_", StringComparison.Ordinal);
            yield return WaitUntil(() => Dragging45() || Done(ContentIds.TaskN2Batch45), 900f);
            if (!Dragging45()) yield break;
            yield return Wait(1.5f);
            if (!_g.Files.Exists(ContentIds.Batch45C)) yield break;
            string was = _g.Files.GetFile(ContentIds.Batch45C)?.Name ?? "batch45_c.dat";
            _g.Files.Rename(ContentIds.Batch45C, "b7_seat.dat");
            // Phase I: it names the file, what it is called now, and that it still counts.
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Format("notify.renamed", was, "b7_seat.dat"), "icon_info", null, "ui_select");
            var fm = _g.Apps.Find<FilesApp>();
            var row = fm != null && !fm.Window.IsMinimized ? fm.RowFor(ContentIds.Batch45C) : null;
            PhantomClick(row != null ? row.Hit.Center : new Vector2(ScreenRig.Width * 0.5f, ScreenRig.Height * 0.5f));
            GameLog.Info(LogChannel.Story, "Anomaly: batch45_c renamed b7_seat.dat");
            yield return Wait(6f);
            if (_g.Files.GetFile(ContentIds.Batch45C) != null) _g.Files.Rename(ContentIds.Batch45C, "batch45_c.dat");
        }

        // ------------------------------------------------------------------ HELP

        IEnumerator Help()
        {
            E.Phase = EntityPhase.Presence;
            E.Brain.Enabled = false;
            yield return Wait(3f);
            float start = Time.time;
            GiveTask(ContentIds.TaskN2Batch46);
            RunSide(TaskHints(ContentIds.TaskN2Batch46), "hints-46");
            _stopHelping = false;
            var helping = RunSide(EllenHelps(), "ellen-help");
            yield return WaitUntil(() => Done(ContentIds.TaskN2Batch46), 150f);

            // She finishes what her hand is doing, then talks.
            _stopHelping = true;
            yield return WaitUntil(() => helping.Done, 10f);
            if (!helping.Done)
            {
                helping.Stop();
                E.Interrupt();
            }
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width + 6f, 300f), 0.4f, true);
            E.State = EntityState.Communicating;
            yield return OpenNotepadAs(_ellen);
            _g.Flags.Set(Flags.EntitySpoke);
            yield return TypeLines(_ellen, Lines("n2_help"), 3f);
            yield return TypeLines(_ellen, Lines("n2_help_more"), 3.5f);
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeN2Back, OnEllenReply, null, 2.6f, 4f, 25f, "DONT",
                ex => ex.id == ContentIds.ExchangeN2Back ? MemoryLine() : null);
            // Phase L: she answers the pointer patch, now that her first words are said.
            var hand = _g.Flags.Has(MemoryFlags.N2TookHand) ? Lines("n2_hand_took") : _g.Flags.Has(MemoryFlags.N2LeftHand) ? Lines("n2_hand_left") : null;
            if (hand != null) yield return TypeLines(_ellen, hand, 3.5f);

            // The batch is finished by the end of the beat, one way or another (cap 240 s).
            yield return WaitUntil(() => Done(ContentIds.TaskN2Batch46), Mathf.Max(0f, 240f - (Time.time - start)));
            if (!Done(ContentIds.TaskN2Batch46))
            {
                GameLog.Warn(LogChannel.Story, "Batch 46 archived by the safety net");
                FileTheRest(ContentIds.TaskN2Batch46);
            }
            yield return Wait(1f);
        }

        /// <summary>One memory of Night 1, typed before the player's first turn (none if nothing stood out).</summary>
        string[] MemoryLine()
        {
            var f = _g.Flags;
            if (f.Has(MemoryFlags.N1Shredded017)) return Lines("n2_back_mem_shred");
            if (f.Has(MemoryFlags.N1Agreed)) return Lines("n2_back_mem_agree");
            if (f.Has(MemoryFlags.N1Refused)) return Lines("n2_back_mem_refuse");
            return null;
        }

        void OnEllenReply(DialogueReply r, string said)
        {
            if (r.Tag == "name") _g.Flags.Set(MemoryFlags.N2SaidName);
        }

        /// <summary>The company hint toasts for a task, without forcing it (the beat decides what happens next).</summary>
        IEnumerator TaskHints(string taskId)
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

        /// <summary>
        /// Ellen helps with Batch 46 through the real UI: after the player's first move (or 30 s) she comes in
        /// from the right, drags one file onto Archive, hangs around, and 20 s later drags another if any remain.
        /// </summary>
        IEnumerator EllenHelps()
        {
            E.State = EntityState.Helpful;
            int before = Archived46();
            yield return WaitUntil(() => Archived46() > before || _stopHelping, 30f);
            if (_stopHelping || Next46() == null) yield break;
            var start = new Vector2(ScreenRig.Width + 6f, 300f);
            E.Teleport(start);
            yield return E.Appear(start, 0.4f, true);
            _g.Flags.Set(Flags.EntitySeen);
            yield return EllenArchiveOne();
            yield return E.Loiter(ScreenRig.ClampToScreen(_g.Player.Position + new Vector2(90f, 50f)), 30f, 2f, MovementProfiles.Hesitant);
            _g.Flags.Set(Flags.N2EllenHelped);
            float wait = Time.time + 20f;
            while (Time.time < wait && !_stopHelping) yield return null;
            if (!_stopHelping && Next46() != null) yield return EllenArchiveOne();
        }

        int Archived46()
        {
            int n = 0;
            foreach (var f in Batch46) if (_g.Files.FolderOf(f) == ContentIds.FolderArchive) n++;
            return n;
        }

        string Next46()
        {
            foreach (var f in Batch46)
                if (_g.Files.Exists(f) && _g.Files.FolderOf(f) != ContentIds.FolderArchive) return f;
            return null;
        }

        /// <summary>
        /// Ellen drags the next Batch 46 file onto the Archive folder in File Manager, opening it on Intake
        /// herself if needed. Close File Manager under her and the file drops where she was: she tries again.
        /// </summary>
        IEnumerator EllenArchiveOne()
        {
            for (int attempt = 0; attempt < 3 && !_stopHelping; attempt++)
            {
                string next = Next46();
                if (next == null) yield break;
                bool onDesktop = _g.Files.FolderOf(next) == ContentIds.FolderDesktop;
                var fm = _g.Apps.Find<FilesApp>();
                if (fm == null || fm.Window.IsMinimized || (!onDesktop && fm.FolderId != _g.Files.FolderOf(next)))
                {
                    fm = _g.Apps.OpenFolder(onDesktop ? ContentIds.FolderIntake : _g.Files.FolderOf(next), E.Agent);
                    GameLog.Info(LogChannel.Entity, "Entity opened File Manager");
                    yield return Wait(0.8f);
                }
                if (fm == null || !fm.IsOpen) continue;
                fm.Window.Focus(E.Agent);
                yield return Wait(0.25f);
                Interactable source = onDesktop ? _g.Desktop.IconForFile(next)?.Hit : fm.RowFor(next)?.Hit;
                var target = fm.FolderRowFor(ContentIds.FolderArchive);
                if (source == null || target == null) { yield return Wait(0.5f); continue; }
                yield return E.DragTo(source, () => target != null && target.isActiveAndEnabled ? target.Center : (Vector2?)null, MovementProfiles.Hesitant);
                yield return Wait(0.3f);
                if (_g.Files.FolderOf(next) == ContentIds.FolderArchive)
                {
                    GameLog.Info(LogChannel.Entity, "Entity archived " + next);
                    yield break;
                }
            }
        }

        // ------------------------------------------------------------------ ASKS

        static readonly (string task, string flag)[] EntityAsks =
        {
            (ContentIds.TaskE2DoorLog, MemoryFlags.N2DoorLog),
            (ContentIds.TaskE2Lookup163, MemoryFlags.N2Lookup163),
            (ContentIds.TaskE2Hide214, MemoryFlags.N2Hid214),
        };

        IEnumerator Asks()
        {
            E.Phase = EntityPhase.Communication;
            E.Brain.Enabled = false;
            float start = Time.time;
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.55f), 0.5f, true);
            yield return TypeLines(_ellen, Lines("n2_ask_intro"), 3.5f);
            RunSide(RuthWarning(), "ruth-warning");
            string[] reactions = { "n2_door", "n2_163", "n2_214_done" };
            for (int i = 0; i < EntityAsks.Length; i++)
            {
                var (task, flag) = EntityAsks[i];
                // Cap 300 s: a request that no longer fits is never written.
                if (Time.time - start > 300f - 40f)
                {
                    _g.Tasks.Withdraw(task);
                    continue;
                }
                yield return EntityTask(task, flag, reactions[i]);
            }
            yield return FlushEllenQueue();
        }

        /// <summary>Twenty seconds after her first request, Ruth warns you; when you read it, Ellen answers.</summary>
        IEnumerator RuthWarning()
        {
            yield return WaitUntil(() => _firstEntityTaskAt > 0f, 400f);
            if (_firstEntityTaskAt <= 0f) yield break;
            yield return WaitUntil(() => Time.time - _firstEntityTaskAt >= 20f, 30f);
            _g.Mail.Deliver(ContentIds.MailN2RuthWarning);
            yield return WaitUntil(() => _g.Mail.IsRead(ContentIds.MailN2RuthWarning), 900f);
            if (!_g.Mail.IsRead(ContentIds.MailN2RuthWarning) || CurrentBeat == "ending") yield break;
            // During her asks the answer waits its turn; read later, she answers straight away.
            if (CurrentBeat == "asks") _ellenQueue.AddRange(Lines("n2_ruth"));
            else RunSide(SayDirect(_ellen, Lines("n2_ruth"), 4.5f), "ruth-reply");
        }

        IEnumerator FlushEllenQueue()
        {
            if (_ellenQueue.Count == 0) yield break;
            var lines = _ellenQueue.ToArray();
            _ellenQueue.Clear();
            yield return TypeLines(_ellen, lines, 4f);
        }

        /// <summary>
        /// One of Ellen's requests: she writes it into the Work Queue (keys you are not pressing), then waits.
        /// Only what the player does counts. A nudge at 35 s; withdrawn at 75 s if nothing happened.
        /// </summary>
        IEnumerator EntityTask(string taskId, string doneFlag, string reaction)
        {
            var task = _g.Tasks.Get(taskId);
            if (task == null) yield break;
            if (taskId == ContentIds.TaskE2Hide214)
            {
                _g.Files.SetHidden(ContentIds.File214, false);
                _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Text("notify.intake.remote"), "icon_info",
                    a => _g.Apps.OpenFolder(ContentIds.FolderIntake, a), "ui_select");
                yield return Wait(1.5f);
            }
            yield return WriteIntoQueue(task);
            // Only what the player does after she asks counts (a record looked up for a work order earlier does not).
            foreach (var target in task.Data.targets)
            {
                _g.Flags.SetCounter(Flags.OpenedByPlayerPrefix + target, 0);
                _g.Flags.SetCounter(Flags.ViewedByPlayerPrefix + target, 0);
            }
            GiveEntityTask(taskId);
            float t0 = Time.time;
            if (_firstEntityTaskAt < 0f) _firstEntityTaskAt = t0;
            if (taskId == ContentIds.TaskE2Hide214) yield return TypeLines(_ellen, Lines("n2_214"), 4f);

            var d = _g.Difficulty;
            float withdrawAt = task.Data.timeout > 0f ? task.Data.timeout : d.EntityTaskWithdraw;
            _g.RemoteTaskLife[taskId] = new Vector2(t0, withdrawAt);
            bool nudged = false;
            while (!Done(taskId))
            {
                float elapsed = Time.time - t0;
                if (elapsed >= withdrawAt) break;
                if (!_whisperAsked && elapsed >= WhisperAfter)
                {
                    // Phase M (N2-2, M9): ignoring her request is heard, once, from where the queue is.
                    _whisperAsked = true;
                    var wq = _g.Apps.FindById(AppIds.WorkQueue);
                    float pan = wq != null && wq.IsOpen ? Audio.AudioManager.PanFor(wq.Window.WorldRect.center.x) : 0f;
                    Scare("whisper_burst", 0.5f, pan, 0f, withdrawAt - elapsed);
                }
                if (!nudged && elapsed >= d.EntityTaskNudge)
                {
                    nudged = true;
                    yield return Nudge(taskId);
                    continue;
                }
                if (_ellenQueue.Count > 0 && !_ellen.Typing)
                {
                    yield return FlushEllenQueue();
                    continue;
                }
                yield return null;
            }
            if (Done(taskId))
            {
                _g.Memory.Record(MemoryKind.ObeyedEntity, taskId, Time.time);
                _g.Flags.Increment(MemoryFlags.N2Obeyed);
                _g.Flags.Set(doneFlag);
                yield return TypeLines(_ellen, Lines(reaction), 4f);
            }
            else
            {
                _g.Tasks.Withdraw(taskId, Expired);
                _g.Notifications.Show(_g.Content.Text("app.workqueue"), _g.Content.Text("notify.queue.withdrawn"), "icon_task_pending", null, "sys_warning");
                _g.Memory.Record(MemoryKind.ResistedEntity, taskId, Time.time);
                yield return TypeLines(_ellen, Lines("n2_withdrawn"), 4f);
            }
            yield return Wait(2.5f);
        }

        /// <summary>She opens the Work Queue if it is closed, hovers over the list and "types" the title in.</summary>
        IEnumerator WriteIntoQueue(WorkTask task)
        {
            var wq = _g.Apps.FindById(AppIds.WorkQueue);
            if (wq == null || wq.Window.IsMinimized)
            {
                yield return E.OpenApp(AppIds.WorkQueue, MovementProfiles.HumanLike);
                yield return Wait(0.5f);
                wq = _g.Apps.FindById(AppIds.WorkQueue);
            }
            if (wq == null || !wq.IsOpen) yield break;
            wq.Window.Restore(E.Agent);
            var r = wq.Window.Client.WorldRect();
            yield return E.MoveTo(new Vector2(r.center.x + UnityEngine.Random.Range(-40f, 40f), r.yMax - 70f), MovementProfiles.HumanLike, 40f);
            foreach (char c in task.Title)
            {
                Sfx.Play(c == ' ' ? "key_space" : "key_tap", E.Agent);
                yield return Wait(1f / 8f);
            }
        }

        /// <summary>At 35 s she shows you where: the door log in Documents, record 163, 214 and the Archive folder.</summary>
        IEnumerator Nudge(string taskId)
        {
            GameLog.Info(LogChannel.Story, "Nudge: " + taskId);
            if (taskId == ContentIds.TaskE2Lookup163)
            {
                yield return E.OpenApp(AppIds.Staff, MovementProfiles.HumanLike);
                yield return Wait(0.6f);
                var staff = _g.Apps.Find<StaffApp>();
                var row = staff != null ? staff.Window.Element("employee:" + ContentIds.Employee163) : null;
                if (row == null) yield break;
                // She points at it; she never clicks it for you.
                yield return E.MoveToElement(row, MovementProfiles.Hesitant);
                yield return E.Loiter(row.Center, 6f, 2.5f, MovementProfiles.Hesitant);
                yield break;
            }
            bool door = taskId == ContentIds.TaskE2DoorLog;
            string file = door ? ContentIds.FileDoorLog : ContentIds.File214;
            if (!_g.Files.Exists(file)) yield break;
            var fm = _g.Apps.OpenFolder(_g.Files.FolderOf(file), E.Agent);
            yield return Wait(0.8f);
            if (fm == null || !fm.IsOpen) yield break;
            fm.Window.Focus(E.Agent);
            var fileRow = fm.RowFor(file);
            if (fileRow != null) yield return E.ClickElement(fileRow.Hit, MovementProfiles.HumanLike, null, 2f);
            if (fm.IsOpen && fm.SelectedFileId != file) fm.SelectFile(file, E.Agent);
            if (door) yield break;
            var archive = fm.IsOpen ? fm.FolderRowFor(ContentIds.FolderArchive) : null;
            if (archive != null) yield return E.Loiter(archive.Center, 8f, 3f, MovementProfiles.Hesitant);
        }

        /// <summary>A lost close-the-viewer fight: CLOSE IT, at most every 20 s.</summary>
        void OnCloseCameraBlocked()
        {
            if (Time.time - _lastCloseIt < 20f) return;
            _lastCloseIt = Time.time;
            RunSide(SayDirect(_ellen, new[] { "CLOSE IT" }, 5f), "close-it");
        }

        /// <summary>
        /// Lines typed straight into a speaker's Notepad while its cursor is busy elsewhere (the brain is
        /// fighting): the pad is opened by that cursor's session if it was closed, the cursor does not move.
        /// </summary>
        IEnumerator SayDirect(Speaker s, string[] lines, float cps)
        {
            if (lines == null || lines.Length == 0) yield break;
            float wait = Time.time + 20f;
            while (s.Typing && Time.time < wait) yield return null;
            s.Typing = true;
            var pad = EnsurePad(s);
            if (pad == null)
            {
                pad = _g.Apps.Launch(AppIds.Notepad, s.Cursor.Agent) as NotepadApp;
                if (pad != null)
                {
                    pad.ConversationMode = true;
                    pad.PlayerCanType = false;
                    s.Pad = pad;
                    ApplyIdentity(pad, s.Cursor);
                }
            }
            foreach (var line in lines)
            {
                if (pad == null || !pad.IsOpen) break;
                if (pad.Text.Length > 0 && !pad.Text.EndsWith("\n")) pad.Append("\n");
                yield return pad.TypeAsEntity(line, cps, s.Cursor.Agent, s.Cursor.TypoRate);
                yield return Wait(0.4f);
            }
            if (pad != null && pad.IsOpen && !pad.Text.EndsWith("\n")) pad.Append("\n");
            s.Typing = false;
        }
    }
}
#endif
