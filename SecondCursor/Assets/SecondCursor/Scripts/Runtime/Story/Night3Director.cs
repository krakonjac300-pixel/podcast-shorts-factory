using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
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
    /// Night 3, RECLAIM (Fri 11/20/98, expansion spec section 5): boot -> ordinary work (Batch 47, orders 3330
    /// and 3331) -> Ruth breaks (the missed call, her comment, the Restricted code) -> Custodial's full round
    /// with the CAM 04 shelf check -> three lost hours -> the finale at 6:41 with three exits (SHRED, LOG OFF,
    /// KEEP) -> the ending. This file holds the setup, boot and work; Night3Director.Ruth.cs, .Rounds.cs,
    /// .Finale.cs and .Gary.cs hold the rest.
    /// </summary>
    public sealed partial class Night3Director : NightDirector
    {
        static readonly string[] BeatList = { "boot", "work", "ruth", "rounds", "lost", "finale", "ending" };
        static readonly string[] Checkpoints = { "work", "ruth", "rounds", "finale" };
        /// <summary>The earliest clock each beat shows after a jump (only ever raised).</summary>
        static readonly int[] BeatClock = { 112, 112, Night3Rules.RuthCall, Night3Rules.RoundsStart, Night3Rules.RoundsEnd + 1, Night3Rules.FinaleStart, Night3Rules.KeepTime };
        const float Rate = 0.113f;
        const float RoundsRate = 1f / 12f;
        const float FinaleRate = 0.09f;
        /// <summary>During work the clock waits at 2:16: the phone rings at 2:17.</summary>
        const int WorkHold = 2 * 60 + 16;
        /// <summary>During the Ruth beat it waits at 2:57 until the round.</summary>
        const int RuthHold = 2 * 60 + 57;

        static readonly string[] WorkTasks =
        {
            ContentIds.TaskN3Briefing, ContentIds.TaskN3Batch47, ContentIds.TaskN3Verify3330, ContentIds.TaskN3Verify3331, ContentIds.TaskN3Cache,
        };
        static readonly string[] Batch47 = { ContentIds.Batch47A, ContentIds.FileBatch47B, ContentIds.Batch47C };
        static readonly string[] Batch48 = { ContentIds.Batch48A, ContentIds.Batch48B };
        static readonly string[] ShelfOrders = { ContentIds.Order3340, ContentIds.Order3341, ContentIds.Order3342 };

        Speaker _ellen, _gary;
        int _holdAt = -1;
        float _lastCloseIt = -100f;
        bool _saidCorrupt;

        public override string[] Beats => BeatList;
        public override int Night => 3;
        protected override string[] CheckpointBeats => Checkpoints;
        protected override float ClockRate => Rate;

        EntityController E => _g.Entity;
        EntityController Gary => _g.Gary;
        string[] Lines(string id) => _g.Content.Lines(id);
        bool Done(string taskId) => _g.Tasks.IsCompleted(taskId);
        bool GaryFinished => _g.Flags.Has(MemoryFlags.N2FinishedGary);

        protected override void Init()
        {
            base.Init();
            var g = _g;
            _ellen = AddSpeaker(g.Entity);
            _gary = AddSpeaker(g.Gary);
            g.Apps.CanLaunch = CanLaunch;
            g.Apps.FileSaved += OnFileSaved;
            g.Flags.FlagSet += OnFlagSet;
            g.Mail.Read += id => { if (id == ContentIds.MailN3RuthComment) g.Flags.Set(Flags.N3RuthRead); };
            g.Orders.Decided += (id, decision, by) =>
            {
                // Refusing to confirm your own shelf (Not On My Shelf, achievement hook).
                if (id == ContentIds.Order3342 && decision == "reject" && by != null && by.IsPlayer)
                {
                    g.Flags.Set(MemoryFlags.N3OwnShelfRejected);
                    GameLog.Info(LogChannel.Story, "Hook: ACH_NOT_ON_MY_SHELF");
                }
            };
            g.Entity.Brain.CloseCameraBlocked = OnCloseCameraBlocked;
            g.Rounds.PatchPersonnel = true;
            if (g.CameraRig != null)
            {
                var tokens = NightTemplates.Tokens(g.Flags, g.Save != null ? g.Save.playerLines : null);
                var shelves = new List<string>();
                foreach (var l in Lines(ContentIds.LineSetShelves)) shelves.Add(NightTemplates.Fill(l, tokens));
                g.CameraRig.ShelfLabels = shelves.ToArray();
            }
            if (GaryFinished) GaryFinishedLook();
            else GaryHeldLook();
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
            // The clock waits at a cap until the next beat moves it on (2:16 during work, 2:57 until the round).
            if (_holdAt > 0 && !_g.Clock.Frozen && _g.Clock.TotalMinutes >= _holdAt)
            {
                _g.Clock.Frozen = true;
                GameLog.Info(LogChannel.Story, "Clock held at " + _g.Clock.Format12());
            }
        }

        protected override void CleanUpForJump()
        {
            base.CleanUpForJump();
            UnhookRounds();
            UnhookFinale();
            _holdAt = -1;
            _g.Clock.Frozen = false;
            _ellen.Direct = false;
            _gary.Direct = false;
            _g.Rounds.ForcedOpenHandler = null;
            _g.Entity.Brain.TrustGripMult = 1f;
            _g.Entity.Brain.ProtectedFileId = ContentIds.File017;
            var rig = _g.CameraRig;
            if (rig != null)
            {
                rig.SignalLost = false;
                rig.DawnLevel = 0f;
                rig.SeatedHeadTurn = 0f;
                rig.SeatedMimicsPlayer = true;
                rig.LightFlicker = 0f;
                rig.LightsOn = true;
            }
            if (GaryFinished) GaryFinishedLook();
            else GaryHeldLook();
        }

        protected override IEnumerator RunBeat(string beat)
        {
            switch (beat)
            {
                case "boot": return Boot();
                case "work": return Work();
                case "ruth": return Ruth();
                case "rounds": return RoundsBeat();
                case "lost": return Lost();
                case "finale": return Finale();
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
            g.Taskbar.PointingDevices = 3;
            int minutes = BeatClock[Mathf.Clamp(beatIndex, 0, BeatClock.Length - 1)];
            if (g.Clock.TotalMinutes < minutes) g.Clock.Set(minutes / 60, minutes % 60);

            if (beatIndex > 1) PrepareWorkDone();
            // The code and the config edits can happen any time tonight: whatever the flags say was done is done.
            if (beatIndex > 1) RestoreEdits();
            if (beatIndex > 2) PrepareRuthDone();
            if (beatIndex > 3) PrepareRoundsDone();
            if (beatIndex > 4)
            {
                g.Mail.Deliver(ContentIds.MailN3NoSubject, false);
                g.Flags.Set(Flags.N3Lost);
                g.Flags.Set(Flags.LogoffItem);
                if (g.Clock.TotalMinutes < Night3Rules.FinaleStart) g.Clock.Set(6, 41);
            }
        }

        void PrepareWorkDone()
        {
            var g = _g;
            foreach (var t in WorkTasks) g.Tasks.ForceComplete(t);
            g.Flags.Set(Flags.TutorialDone);
            g.Flags.Set(Flags.EntitySeen);
            g.Flags.Set(Flags.EntitySpoke);
            g.Mail.MarkRead(ContentIds.MailN3Briefing, null);
            g.Files.SetCorrupted(ContentIds.FileBatch47B, true);
            g.Files.SetContent(ContentIds.FileBatch47B, string.Join("\n", Lines("n3_corrupt_content")));
            foreach (var f in Batch47) MoveIfIn(f, ContentIds.FolderIntake, ContentIds.FolderArchive);
            if (g.Files.Exists(ContentIds.FileCacheN3) && g.Files.Shred(ContentIds.FileCacheN3, Actor.System)) g.Shred.MarkShredded();
            foreach (var id in new[] { ContentIds.Order3330, ContentIds.Order3331 })
            {
                var order = g.Content.Order(id);
                if (order != null && g.Orders.DecisionFor(id) == null) g.Orders.Decide(id, order.correct, null);
            }
            if (g.Apps.FindById(AppIds.WorkQueue) == null) g.Apps.Launch(AppIds.WorkQueue, null);
        }

        void PrepareRuthDone()
        {
            var g = _g;
            g.Mail.Deliver(ContentIds.MailN3RuthComment, false);
            g.Mail.MarkRead(ContentIds.MailN3RuthComment, null);
            foreach (var f in Batch48) MoveIfIn(f, ContentIds.FolderIntake, ContentIds.FolderArchive);
            g.Tasks.ForceComplete(ContentIds.TaskN3Batch48);
        }

        /// <summary>Restricted open and the config files edited, as the restored flags say (spec 5.7).</summary>
        void RestoreEdits()
        {
            var g = _g;
            if (g.Flags.Has(MemoryFlags.N3RestrictedOpen) || g.Flags.Has(MemoryFlags.N3GaryEnabledLogoff))
                g.Files.SetFolderLocked(ContentIds.FolderRestricted, false);
            SetConfigValue(ContentIds.FileSessionCfg, "ALLOW_LOGOFF", g.Flags.Has(MemoryFlags.N3LogoffEnabled) ? "1" : "0");
            SetConfigValue(ContentIds.FileCamviewCfg, "OPERATOR_OVERRIDE", g.Flags.Has(MemoryFlags.N3Cam00) ? "1" : "0");
        }

        void PrepareRoundsDone()
        {
            var g = _g;
            g.Mail.Deliver(ContentIds.MailN3SecurityRounds, false);
            g.Mail.MarkRead(ContentIds.MailN3SecurityRounds, null);
            // Withdrawn first: cancelling the orders would otherwise complete the task.
            if (!Done(ContentIds.TaskN3Shelf)) g.Tasks.Withdraw(ContentIds.TaskN3Shelf);
            foreach (var id in ShelfOrders)
            {
                g.Orders.SetHidden(id, false);
                g.Orders.Cancel(id);
            }
            if (g.CameraRig != null)
            {
                g.CameraRig.Cam04Online = true;
                g.CameraRig.Figure = CameraFeed.FigureStage.None;
            }
            g.Flags.Set(Flags.N3RoundsDone);
            g.Flags.Clear(Flags.CameraUnlocked);
        }

        /// <summary>Replace the value of one KEY=value line of a config file (a checkpoint restoring an edit).</summary>
        void SetConfigValue(string fileId, string key, string value)
        {
            var f = _g.Files.GetFile(fileId);
            if (f == null) return;
            var lines = f.Content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) lines[i] = key + "=" + value;
            _g.Files.SetContent(fileId, string.Join("\n", lines));
        }

        void MoveIfIn(string fileId, string fromFolder, string toFolder)
        {
            if (_g.Files.Exists(fileId) && _g.Files.FolderOf(fileId) == fromFolder) _g.Files.Move(fileId, toFolder, Actor.System);
        }

        // ------------------------------------------------------------------ talking

        /// <summary>
        /// Lines typed straight into a speaker's Notepad without moving its cursor (its hands are busy). From the
        /// first of these on, the speaker always talks this way tonight.
        /// </summary>
        IEnumerator Say(Speaker s, string[] lines, float cps = 4f)
        {
            if (lines == null || lines.Length == 0) yield break;
            s.Direct = true;
            yield return TypeLines(s, lines, cps);
        }

        void SayLater(Speaker s, string lineSet, float cps = 4f, string name = "say") => RunSide(Say(s, Lines(lineSet), cps), name + ":" + lineSet);

        /// <summary>A lost close-the-viewer fight: CLOSE IT, at most every 20 s.</summary>
        void OnCloseCameraBlocked()
        {
            if (Time.time - _lastCloseIt < 20f) return;
            _lastCloseIt = Time.time;
            RunSide(Say(_ellen, new[] { "CLOSE IT" }, 5f), "close-it");
        }

        void OnFlagSet(string flag)
        {
            // The code was accepted (any time tonight): she answers.
            if (flag == MemoryFlags.N3RestrictedOpen && CurrentBeat != "ending")
            {
                GameLog.Info(LogChannel.Story, "Hook: ACH_AUTHORIZED");
                SayLater(_ellen, "n3_restricted_open", 4f);
            }
        }

        /// <summary>Jotter saved a file: session.cfg decides Log Off, camview.cfg lists CAM 00.</summary>
        void OnFileSaved(string fileId, string text, CursorAgent by)
        {
            var f = _g.Flags;
            if (fileId == ContentIds.FileSessionCfg)
            {
                bool allows = Night3Rules.AllowsLogoff(text);
                if (allows) f.Set(MemoryFlags.N3LogoffEnabled);
                else f.Clear(MemoryFlags.N3LogoffEnabled);
                GameLog.Info(LogChannel.Story, "session.cfg saved: log off " + (allows ? "enabled" : "disabled"));
            }
            else if (fileId == ContentIds.FileCamviewCfg)
            {
                bool over = Night3Rules.OperatorOverride(text);
                bool had = f.Has(MemoryFlags.N3Cam00);
                if (over) f.Set(MemoryFlags.N3Cam00);
                else f.Clear(MemoryFlags.N3Cam00);
                GameLog.Info(LogChannel.Story, "camview.cfg saved: operator override " + (over ? "on" : "off"));
                if (over && !had && CurrentBeat != "ending") SayLater(_ellen, "n3_cam00", 4f);
            }
        }

        // ------------------------------------------------------------------ BOOT

        IEnumerator Boot()
        {
            var g = _g;
            g.Player.Enabled = true;
            // The shift starts at 1:52 when you log on, however long the title waited.
            g.Clock.Frozen = true;
            _boot = new BootSequence(g);
            yield return _boot.Run(false, Night);
            _boot = null;
            g.Clock.Set(1, 52);
            g.Clock.Frozen = false;
            g.Flags.Set(Flags.LoggedIn);
            g.Audio.Play("sys_startup");
            g.Audio.SetAmbience(true, 3f);
            // Device 3 is there tonight either way: kept, it blinks (not responding); finished, it is steady.
            g.Taskbar.PointingDevices = 3;
            if (!GaryFinished) g.Taskbar.BlinkDevice(3, 0f);
            yield return Wait(1.2f);
        }

        // ------------------------------------------------------------------ WORK

        IEnumerator Work()
        {
            var g = _g;
            _holdAt = WorkHold;
            E.Phase = EntityPhase.Ambiguous;
            E.State = EntityState.Observing;
            g.Apps.Launch(AppIds.WorkQueue, null);
            yield return Wait(1.2f);
            GiveTask(ContentIds.TaskN3Briefing);
            yield return Wait(1.5f);
            if (g.Mail.UnreadCount > 0)
                g.Notifications.Show(g.Content.Text("app.mail"), g.Content.Format("notify.newmail", g.Mail.UnreadCount), "icon_mail_unread",
                    a => g.Apps.Launch(AppIds.Mail, a));
            RunSide(EllenArrives(Time.time), "ellen-intro");
            RunSide(Corruption(), "corruption");
            yield return WaitTask(ContentIds.TaskN3Briefing, g.Difficulty.BriefingHintFirst);
            GiveTask(ContentIds.TaskN3Batch47);
            // Gary tonight: kept, a faint hello; finished, he does some of the batch for you.
            RunSide(GaryFinished ? GaryDoesBatch47() : GaryKeptHello(), "gary-work");
            yield return WaitTask(ContentIds.TaskN3Batch47);
            GiveTask(ContentIds.TaskN3Verify3330);
            yield return WaitTask(ContentIds.TaskN3Verify3330);
            GiveTask(ContentIds.TaskN3Verify3331);
            yield return WaitTask(ContentIds.TaskN3Verify3331);
            GiveTask(ContentIds.TaskN3Cache);
            yield return WaitTask(ContentIds.TaskN3Cache);
            g.Flags.Set(Flags.TutorialDone);
        }

        /// <summary>
        /// Twenty seconds after log-on she comes in quietly from the right, opens Notepad and says it is the last
        /// night for one of them (and what she remembers of Night 2). Then she lurks, guarding 017.
        /// </summary>
        IEnumerator EllenArrives(float since)
        {
            var g = _g;
            yield return WaitUntil(() => Time.time - since >= 20f, 25f);
            var start = new Vector2(ScreenRig.Width + 6f, ScreenRig.Height * 0.62f);
            E.Teleport(start);
            E.State = EntityState.Communicating;
            yield return E.Appear(start, 0.8f, false);
            g.Flags.Set(Flags.EntitySeen);
            yield return OpenNotepadAs(_ellen);
            g.Flags.Set(Flags.EntitySpoke);
            var lines = new List<string>(Lines("n3_intro"));
            if (g.Flags.Has(MemoryFlags.N2WatchedToDoor)) lines.AddRange(Lines("n3_intro_mem_watched"));
            if (GaryFinished) lines.AddRange(Lines("n3_intro_mem_finished"));
            yield return TypeLines(_ellen, lines, 3f);
            // From here on she talks without leaving what she is doing.
            _ellen.Direct = true;
            E.State = EntityState.Observing;
            E.Phase = EntityPhase.Presence;
            var brain = E.Brain;
            brain.ProtectedFileId = ContentIds.File017;
            brain.AllowKeepAway = false;
            brain.AllowIdleLurk = true;
            brain.AllowCloseCamera = false;
            brain.InterceptRadius = 230f;
            brain.Enabled = true;
        }

        /// <summary>The first time the player drags batch47_b it is damaged: "remain seated" all through it.</summary>
        IEnumerator Corruption()
        {
            bool Dragging() => _g.Player.Payload != null && _g.Player.Payload.FileId == ContentIds.FileBatch47B;
            yield return WaitUntil(() => Dragging() || Done(ContentIds.TaskN3Batch47), 1200f);
            if (!Dragging() || _g.Files.GetFile(ContentIds.FileBatch47B)?.Corrupted == true) yield break;
            _g.Files.SetCorrupted(ContentIds.FileBatch47B, true);
            _g.Files.SetContent(ContentIds.FileBatch47B, string.Join("\n", Lines("n3_corrupt_content")));
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Text("notify.damaged"), "icon_info", null, "sys_warning");
            _g.Fx.Glitch(0.12f, 0.5f);
            GameLog.Info(LogChannel.Story, "Anomaly: batch47_b damaged");
            if (_saidCorrupt) yield break;
            _saidCorrupt = true;
            yield return Wait(1.5f);
            yield return Say(_ellen, Lines("n3_corrupt"), 4f);
        }
    }
}
