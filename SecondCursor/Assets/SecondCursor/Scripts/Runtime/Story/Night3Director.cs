// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
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
        /// <summary>Phase Q1 (T8): the shelf check is the order on your own shelf (18); WO-3340 and WO-3341 stay in the data, never shown.</summary>
        static readonly string[] ShelfOrders = { ContentIds.Order3342 };

        Speaker _ellen, _gary;
        /// <summary>Phase P (E9): MINE before the finale only (in the finale her own tug lines take that role).</summary>
        protected override Speaker MineSpeaker => CurrentBeat == "finale" || CurrentBeat == "ending" ? null : _ellen;
        int _holdAt = -1;
        float _lastCloseIt = -100f;
        bool _saidCorrupt;

        public override string[] Beats => BeatList;
        public override int Night => 3;
        protected override string[] CheckpointBeats => Checkpoints;
        protected override float ClockRate => Rate;

        EntityController E => _g.Entity;
        EntityController Gary => _g.Gary;
        string[] Lines(string id) => Fill(_g.Content.Lines(id));
        protected override string BriefingMailId => ContentIds.MailN3Briefing;

        /// <summary>Phase Q2 (V1): LOG OFF's log ends with the last thing typed tonight, quoted (filtered), or NONE.</summary>
        protected override void AddNightTokens(System.Collections.Generic.Dictionary<string, string> tokens)
        {
            string last = PlayerLines.Count > 0 ? EchoFilter.ForFile(PlayerLines[PlayerLines.Count - 1]) : "";
            tokens["lastn3"] = last.Length > 0 ? "\"" + last + "\"" : NightTemplates.NoLastInput;
        }
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
            g.Mail.Read += id =>
            {
                if (id != ContentIds.MailN3RuthComment) return;
                g.Flags.Set(Flags.N3RuthRead);
                _ruthReadAt = Time.time;
            };
            g.Orders.Decided += (id, decision, by) =>
            {
                // Refusing to confirm your own shelf (Not On My Shelf, achievement hook).
                if (id == ContentIds.Order3342 && decision == "reject" && by != null && by.IsPlayer)
                {
                    g.Flags.Set(MemoryFlags.N3OwnShelfRejected);
                }
                if (Array.IndexOf(ShelfOrders, id) >= 0) ShelfResultLine(id, decision);
                // Phase L: Gary answers what happens to his box; Ruth answers what happens to her drive.
                if (by == null || !by.IsPlayer || IsPreparing) return;
                if (id == ContentIds.Order3332) RunSide(GarySays(GaryBoxSet(decision == "approve" ? "gone" : "kept")), "gary-box-reply");
                else if (id == ContentIds.Order3333) SayLater(_ellen, decision == "approve" ? "n3_ruth_wiped" : "n3_ruth_kept", afterTurn: true);
            };
            g.Entity.Brain.CloseCameraBlocked = OnCloseCameraBlocked;
            // Phase Q1 (T8): after her second close in a round she stops, and says so.
            g.Rounds.EntityGaveUp += () => RunSide(Say(_ellen, new[] { "YOU KNOW NOW", "YOUR CHOICE" }, 5f), "close-giveup");
            g.Rounds.PatchPersonnel = true;
            // Phase H: a saved config file says what it now decides; the shelf check says what it filed; your own shelf
            // order gets her line the first time you open it during the round.
            g.Apps.SavedNote = PolicyNote;
            g.Tasks.TaskCompleted += t => { if (t.Id == ContentIds.TaskN3Shelf) FileShelfResult(); };
            g.Orders.Viewed += (id, by) =>
            {
                if (by == null || !by.IsPlayer || g.Orders.DecisionFor(id) != null) return;
                if (id == ContentIds.Order3332 && !_saidBox)
                {
                    _saidBox = true;
                    RunSide(GarySays(GaryBoxSet("view")), "gary-box");
                }
                if (id != ContentIds.Order3342 || _saidShelfYou || CurrentBeat != "rounds") return;
                _saidShelfYou = true;
                SayLater(_ellen, "n3_shelf_you", 4f);
            };
            if (g.CameraRig != null)
            {
                var tokens = NightTemplates.ForSave(g.Flags, g.Save);
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
            // Phase Q3 (D4): a jump out of the finale's offer gives the notices and the room back.
            _g.Notifications.HeldByStory = false;
            _g.Audio.SetAmbienceLevel(1f, 0.5f);
            _offerReleased = true;
            _saidShelfYou = false;
            _saidBox = false;
            _ruthReadAt = -1f;
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
                g.Files.SetHidden(ContentIds.FileRecovered, false);
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
            RestoreChoice(ContentIds.TaskN3Verify3332, ContentIds.Order3332, null);
            if (g.Apps.FindById(AppIds.WorkQueue) == null) g.Apps.Launch(AppIds.WorkQueue, null);
        }

        void PrepareRuthDone()
        {
            var g = _g;
            RestoreChoice(ContentIds.TaskN3Verify3333, ContentIds.Order3333, ContentIds.MailN3RuthDrive);
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
            // Live Personnel is patched in memory: rebuild what the round left (118 on leave once Custodial reached
            // the B-Level hall; Custodial back in Sublevel C after a safe round, at WS-04 after the seat was cleared).
            if (g.Flags.Get(MemoryFlags.N3MaxStage) >= 2) g.Rounds.PatchPersonnelFor("HallFar");
            g.Rounds.PatchPersonnelFor(g.Flags.Has(MemoryFlags.N3SeatCleared) ? "BehindChair" : "SublevelC");
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

        void SayLater(Speaker s, string lineSet, float cps = 4f, string name = "say", bool afterTurn = false) =>
            RunSide(SayWhenFree(s, Lines(lineSet), cps, afterTurn), name + ":" + lineSet);

        /// <summary>
        /// Phase L: a reaction waits for what the speaker is typing (Ruth's exchange can go on for a minute; TypeLines gives up after 20 s),
        /// so two sets never interleave. <paramref name="afterTurn"/> (Phase L review, D5's answer): it also waits out the player's turn in
        /// that Jotter, so it never lands between Ruth's question and the reply.
        /// </summary>
        IEnumerator SayWhenFree(Speaker s, string[] lines, float cps, bool afterTurn)
        {
            yield return WaitUntil(() => !s.Typing && !(afterTurn && s.Pad != null && s.Pad.IsOpen && s.Pad.ConversationMode && s.Pad.PlayerCanType), 90f);
            yield return Say(s, lines, cps);
        }

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
                SayLater(_ellen, "n3_restricted_open", 4f);
            }
        }

        bool _saidShelfYou, _saidBox;
        /// <summary>The moment Ruth's mail was read (Phase L: her order for her own drive comes a while after).</summary>
        float _ruthReadAt = -1f;

        /// <summary>Gary's lines about his box for what just happened: view, kept or gone (the finished Gary speaks flatly).</summary>
        string GaryBoxSet(string what) => (GaryFinished ? "g3c_box_" : "g3_box_") + what;

        /// <summary>The line the "saved" notice adds for a config file: what it decides now (the last KEY= line counts).</summary>
        string PolicyNote(string fileId, string text)
        {
            var c = _g.Content;
            if (fileId == ContentIds.FileSessionCfg) return c.Text(Night3Rules.AllowsLogoff(text) ? "policy.logoff.on" : "policy.logoff.off");
            if (fileId == ContentIds.FileCamviewCfg) return c.Text(Night3Rules.OperatorOverride(text) ? "policy.cam00.on" : "policy.cam00.off");
            return null;
        }

        /// <summary>
        /// Phase K (finding 9): each shelf decision gets its result line, in a notice and under the order in the Work Queue: what CAM 04
        /// shows on the listed shelf and whether the decision follows the rule ("WO-3342 rejected: shelf 18 reads 214 ROURKE C.
        /// (RESERVED). The rule says approve."). The tester rejected 3342 as a guess and never learned what the rule made of it.
        /// </summary>
        void ShelfResultLine(string orderId, string decision)
        {
            var c = _g.Content;
            var order = c.Order(orderId);
            if (order == null || (decision != "approve" && decision != "reject")) return;
            string listed = "", owner = "";
            foreach (var f in order.fields)
            {
                if (f.label == "Listed Location") listed = f.value;
                else if (f.label == "Owner Emp. No.") owner = f.value;
            }
            var labels = _g.CameraRig != null ? _g.CameraRig.ShelfLabels : null;
            string caption = Night3Rules.ShelfCaptionFor(listed, labels) ?? "";
            int colon = caption.IndexOf(':');
            string reads = colon >= 0 ? caption.Substring(colon + 1).Trim() : c.Text("shelf.result.nolabel");
            string rule = Night3Rules.ShelfDecision(listed, owner, labels);
            string line = c.Format(decision == rule ? "shelf.result.match" : "shelf.result.against", orderId.Replace("wo_", "WO-"),
                c.Text(decision == "approve" ? "workqueue.check.approved" : "workqueue.check.rejected"), Night3Rules.ShelfNumber(listed), reads,
                c.Text(rule == "approve" ? "shelf.rule.approve" : "shelf.rule.reject"));
            _g.Tasks.SetTargetNote(ContentIds.TaskN3Shelf, orderId, line);
            _g.Notifications.Show(c.Text("app.workorders"), line, "icon_info", null, "ui_select");
            GameLog.Info(LogChannel.Story, "Shelf result: " + line);
        }

        /// <summary>The shelf check is done: the queue line and a notice say what was filed.</summary>
        void FileShelfResult()
        {
            int approved = 0, rejected = 0;
            foreach (var id in ShelfOrders)
            {
                string d = _g.Orders.DecisionFor(id);
                if (d == "approve") approved++;
                else if (d == "reject") rejected++;
            }
            // Phase Q1 (T8): the check is one order (your own shelf): its line says what was filed, and its result notice already said it.
            if (ShelfOrders.Length == 1)
            {
                string d = _g.Orders.DecisionFor(ShelfOrders[0]);
                if (d == "approve" || d == "reject")
                    _g.Tasks.SetResult(ContentIds.TaskN3Shelf, _g.Content.Format(d == "approve" ? "workqueue.order.approved" : "workqueue.order.rejected", ShelfOrders[0].Replace("wo_", "WO-")));
                GameLog.Info(LogChannel.Story, "Shelf check filed: " + d);
                return;
            }
            string note = _g.Content.Format("workorders.shelf.filed", approved, rejected);
            _g.Tasks.SetResult(ContentIds.TaskN3Shelf, note);
            _g.Notifications.Show(_g.Content.Text("app.workorders"), note + ".", "icon_info", a => _g.Apps.Launch(AppIds.WorkQueue, a), "ui_select");
            GameLog.Info(LogChannel.Story, "Shelf check filed: " + approved + " approved, " + rejected + " rejected");
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
            // Review J5: a fresh shift's clock (Set refuses to go back if it ticked past 1:53 during a cold start).
            g.Clock.Reset(1, 52);
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
            yield return WelcomeBack();
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
            // Phase L: Gary's box (the rule and the owner disagree).
            RevealOrder(ContentIds.TaskN3Verify3332, ContentIds.Order3332, null);
            yield return WaitOrder(ContentIds.TaskN3Verify3332, ContentIds.Order3332);
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
            if (MemoryFlags.NamedChatAny(g.Flags)) lines.AddRange(Lines("n3_intro_mem_chat"));
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
            // Phase N (finding 15): it names the file and what it means for the task.
            _g.Notifications.Show(_g.Content.Text("os.name"), _g.Content.Format("notify.damaged", _g.Files.GetFile(ContentIds.FileBatch47B)?.Name ?? "batch47_b.dat"),
                "icon_info", null, "sys_warning");
            _g.Fx.Glitch(0.12f, 0.5f);
            // Phase M (N3-1): the file was typed over by someone, backwards (heard where you hold it).
            Scare("key_tap_rev", 0.6f, Audio.AudioManager.PanFor(_g.Player.Position.x), 0.4f, ScareRules.StoryEventWindow, ScareRules.IgnoreAllButStory);
            GameLog.Info(LogChannel.Story, "Anomaly: batch47_b damaged");
            if (_saidCorrupt) yield break;
            _saidCorrupt = true;
            yield return Wait(1.5f);
            yield return Say(_ellen, Lines("n3_corrupt"), 4f);
        }
    }
}
#endif
