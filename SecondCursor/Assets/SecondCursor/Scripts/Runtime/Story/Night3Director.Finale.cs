// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
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
    /// Night 3's finale (spec 5.3 N3.6) and ending (N3.7, section 6): from 6:41 Ellen offers the choice. Shred
    /// employee_017.dat (SHRED), log off at 7:00 if session.cfg allows it (LOG OFF), or stay: say so, wait for
    /// 7:05, or watch the feed until the seat is cleared (KEEP). The first exit wins.
    /// </summary>
    public sealed partial class Night3Director
    {
        /// <summary>Real seconds after which the finale ends in KEEP whatever the clock says (a stalled clock never soft-locks).</summary>
        const float FinaleSafetyCap = 480f;
        const float LogOffSeconds = 6f;
        /// <summary>Spec 13.5: seconds without input before the finale's clock fast-forwards to 7:00 (Phase F).</summary>
        const float IdleFastForwardAfter = 60f;
        const float IdleFastForwardSeconds = 15f;
        /// <summary>6:58, the finale's one event between the 6:55 feed and 7:00.</summary>
        const int FeedFlickerTime = 6 * 60 + 58;
        /// <summary>M8: game minutes before 7:00 during which the tray clock is amber.</summary>
        const int AmberMinutes = 5;
        /// <summary>
        /// Phase K: real seconds after 7:00 before Gary's line and before Security opens the feed. The fourth blind tester got three
        /// notices, two Jotters and the viewer in the same second, read for 55 s, and the night ended unseen.
        /// </summary>
        const float GaryAfter700 = 4f, FeedAfter700 = 8f;
        /// <summary>Squared virtual pixels the cursor must travel from its last anchor to count as activity (4 px).</summary>
        const float IdleMoveSqr = 16f;
        float _idleSince;
        Vector2 _idleLastPos;
        static readonly Vector2 File017Spot = new Vector2(600f, 300f);

        Night3Exit _exit;
        string _keepCause = "time";
        bool _confirmed, _fastForward, _tugLineSaid, _garyGuardSaid, _logOffCut;
        int _garyLogOffTries;
        MessageBox _logOffConfirm;
        ProgressDialog _logOffProgress;
        Routine _lastWords;
        Action<string, CursorAgent> _onFinaleRequested;
        Action<string, MessageBox> _onFinaleConfirm;
        Action<string, ProgressDialog> _onFinaleProgress;
        Action<string, CursorAgent> _onFinaleShredded;
        Action<string, CursorAgent> _onFinaleCancelled;
        Action<DragPayload> _onFinaleTug;
        Action _onFinaleSeat;

        IEnumerator Finale()
        {
            var g = _g;
            _holdAt = -1;
            _exit = Night3Exit.None;
            _keepCause = "time";
            _confirmed = _fastForward = _tugLineSaid = _garyGuardSaid = _logOffCut = _logOffAsked = false;
            _garyLogOffTries = 0;
            if (g.Clock.TotalMinutes < Night3Rules.FinaleStart) g.Clock.Set(6, 41);
            // Phase Q3 (D4): the offer breathes: at 6:41 the clock waits, nothing is announced and the room is quieter until she has
            // typed it (OfferBreath gives the clock, the rule and the request back).
            g.Clock.Frozen = true;
            g.Notifications.HeldByStory = true;
            g.Audio.SetAmbienceLevel(OfferAmbience, 1f);
            _offerTyped = _offerReleased = false;
            g.Clock.Rate = FinaleRate;
            // 017 stays "in use" (every shred refused) until the finale's shred hooks are attached below.
            g.Flags.Set(Flags.CameraUnlocked);
            g.Flags.Set(Flags.LogoffItem);
            E.Phase = EntityPhase.Interference;
            float start = Time.time;
            _lastTry = null;

            // employee_017.dat comes to the desktop, the way it did on the first night.
            yield return EnsureEllenPresentStill();
            if (g.Files.Exists(ContentIds.File017) && g.Files.FolderOf(ContentIds.File017) != ContentIds.FolderDesktop)
            {
                yield return FindDropSpot(File017Spot);
                var spot = _dropSpot;
                var edge = new Vector2(ScreenRig.Width + 6f, spot.y + 30f);
                E.Teleport(edge);
                yield return CarryFileIn(E, ContentIds.File017, spot, MovementProfiles.Hesitant);
                g.Desktop.Attention(ContentIds.File017);
                var icon = g.Desktop.IconForFile(ContentIds.File017);
                if (icon != null) yield return E.Loiter(icon.Hit.Center, 14f, 1.5f, MovementProfiles.Hesitant);
            }

            // Her brain guards 017 with everything Night 3 allows; trust softens or hardens her grip.
            var brain = E.Brain;
            brain.ProtectedFileId = ContentIds.File017;
            brain.AllowKeepAway = true;
            brain.AllowIdleLurk = false;
            brain.AllowCloseCamera = true;
            brain.InterceptRadius = 0f;
            brain.TrustGripMult = Night3Rules.GripMultForTrust(g.Memory.Trust);
            brain.Enabled = true;
            E.State = EntityState.Defensive;
            _ellen.Direct = true;
            _gary.Direct = true;
            HookFinale();
            g.Shred.IsInUse = null;
            // A shred that slipped through before the hooks existed still counts.
            if (g.Files.GetFile(ContentIds.File017)?.Shredded == true) _exit = Night3Exit.Shred;
            RunSide(FinalExchange(), "final-exchange");
            RunSide(OfferBreath(), "offer-breath");
            RunSide(LetGoRequest(), "letgo-request");
            RunSide(KeepFile017InView(), "keep-017-in-view");

            // The feed: the corridor to the seat. Security (or finished Gary) opens it by the clock.
            g.Rounds.ForcedBy = null;
            g.Rounds.ForcedOpenHandler = GaryFinished ? GaryForcedOpen : (Action<string>)null;
            // Phase K: every forced open of the finale says the feed is the danger now (close it or look elsewhere).
            g.Rounds.OnItNoticeKey = "finale.onit";
            g.Rounds.NotOnItNoticeKey = "finale.notonit";
            g.Rounds.ClosedNoticeKey = "camera.closed.finale";
            g.Rounds.Begin(RoundsConfig.Night3Finale(g.Difficulty.Mode, g.Memory.Trust));
            bool open650 = false, open655 = false, open700 = false, open702 = false, said652 = false, said700 = false, flicker658 = false, gary700 = false;
            // Phase M (N3-6, N3-7, N3-bed2): a whisper at 6:45 and your chair at 6:52 (a reply being waited for does not hold them:
            // the finale listens for "stay" throughout), and a drone that tightens toward seven.
            g.Scares.Finale = true;
            bool whisper645 = false, creak652 = false, drone650 = false, drone655 = false, drone700 = false;
            float keepSince = -1f, at700 = -1f;
            _idleSince = Time.time;
            _idleLastPos = g.Player.Position;

            while (_exit == Night3Exit.None)
            {
                int clock = g.Clock.TotalMinutes;
                if (brain.Defenses >= 1) brain.AllowIdleLurk = true;
                if (!open650 && clock >= 6 * 60 + 50) { open650 = true; g.Rounds.OpenViewer(null); }
                if (!whisper645 && clock >= 6 * 60 + 45 && !_logOffAsked)
                {
                    whisper645 = true;
                    g.Scares.Slot("whisper_burst", 0.55f, () => Audio.AudioManager.PanFor(E.Agent.Position.x), 0f, 20f, ScareGate.Reply);
                }
                if (!creak652 && clock >= 6 * 60 + 52 && !_logOffAsked)
                {
                    creak652 = true;
                    Scare("chair_creak", 0.7f, 0f, 0f, 20f, ScareGate.Reply);
                }
                if (!_logOffAsked) FinaleDrone(clock, ref drone650, ref drone655, ref drone700);
                if (!open655 && clock >= 6 * 60 + 55)
                {
                    open655 = true;
                    g.Rounds.OpenViewer(ContentIds.Cam03);
                    SayLater(_ellen, "n3_finale_feed", 4.5f);
                    // Phase Q3 (V4): Security's feed fills the desktop for her line, if nothing the player needs would be covered.
                    RunSide(FullSizeFeedWhenAllowed(Lines("n3_finale_feed")[0], FinaleFullSeconds, 8f), "full-view-655");
                }
                if (!flicker658 && clock >= FeedFlickerTime)
                {
                    // Phase F: the 6:55 to 7:00 stretch gets one event (footsteps and a flicker on the feed).
                    flicker658 = true;
                    RunSide(FeedFlicker(), "feed-flicker");
                }
                // Phase K: at 7:00 the notice and the countdown come first; Gary 4 s later; Security opens the feed 8 s later.
                if (!open700 && at700 > 0f && Time.time - at700 >= FeedAfter700 && !LogOffRunning) { open700 = true; g.Rounds.OpenViewer(null); }
                // Phase J: Security does not open the feed on a log off that is under way (it cut the tester's log off short).
                if (!open702 && clock >= Night3Rules.LogOffTime + 2 && !LogOffRunning) { open702 = true; g.Rounds.OpenViewer(null); }

                // Gary by the clock: kept unlocks the log off at 6:48; finished says the one thing in capitals at 6:52.
                if (!GaryFinished && clock >= Night3Rules.GaryLogOff && !g.Flags.Has(MemoryFlags.N3LogoffEnabled) && !Gary.Busy
                    && _garyLogOffTries < 3 && !g.Shred.Busy && !g.Conflict.IsFighting)
                {
                    _garyLogOffTries++;
                    Gary.Run(GaryEnablesLogOff(), "gary-logoff");
                }
                if (GaryFinished && !said652 && clock >= Night3Rules.GarySeated)
                {
                    said652 = true;
                    RunSide(GarySays("g3c_seated"), "gary-seated");
                }
                if (!said700 && clock >= Night3Rules.LogOffTime)
                {
                    said700 = true;
                    at700 = Time.time;
                    // Where to log off: a click on the notice opens the Nexus menu. Phase K: the Work Queue and the taskbar count down to
                    // 7:05 and name what doing nothing means and the ways out (suggestion 5, finding 1).
                    g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("logoff.available"), "icon_info", a => g.Taskbar.StartMenu.OpenFromElsewhere(a), "ui_select", true, () => _exit == Night3Exit.None, Core.Game.NoticeKind.Deadline);
                    // Phase N: the task given at 6:41 now says what to do.
                    g.Tasks.Rewrite(ContentIds.TaskN3LogOffBy, g.Content.Text("task.logoff.now.title"), null, g.Content.Text("task.logoff.now.hint"));
                    GameLog.Info(LogChannel.Story, "7:00: log off available");
                }
                if (!gary700 && at700 > 0f && Time.time - at700 >= GaryAfter700)
                {
                    gary700 = true;
                    if (!GaryFinished) RunSide(GarySays(g.Flags.Has(MemoryFlags.N3Box209Kept) ? "g3_finale_box" : "g3_finale"), "gary-finale");
                }
                if (clock >= Night3Rules.LogOffTime && !_fastForward) g.Clock.Rate = PostSevenRate;
                // M8: the last five minutes before seven read amber on the tray clock.
                g.Taskbar.ClockAmber = clock >= Night3Rules.LogOffTime - AmberMinutes && clock < Night3Rules.LogOffTime;

                // KEEP by confirmation lands at 7:00 (unless a shred is already running). Phase P (T1): a LetGo hold under way is an exit too.
                bool running = g.Shred.Busy || LogOffRunning || LetGoHoldRunning;
                // Spec 13.5: a player who does nothing for a minute does not wait the clock out at its own pace.
                if (PlayerActive()) _idleSince = Time.time;
                else if (!_fastForward && !running && !g.Conflict.IsFighting && clock < Night3Rules.LogOffTime
                         && Time.time - _idleSince >= IdleFastForwardAfter)
                    RunSide(IdleFastForward(), "idle-fast-forward");
                if (_confirmed && clock >= Night3Rules.LogOffTime && !g.Shred.Busy) { _exit = Night3Exit.Keep; _keepCause = "confirm"; break; }
                if (clock >= Night3Rules.KeepTime && keepSince < 0f) keepSince = Time.time;
                // Phase N (finding 3): time's KEEP names what the player was trying to do when it came.
                // Phase Q4 (A4): the grace after a log off or shred has started is as long again when relaxed (the time since is divided by the scale).
                if (Night3Rules.KeepByTime(clock, running, keepSince < 0f ? 0f : (Time.time - keepSince) / Mathf.Max(1f, g.TimeScale))) { _exit = Night3Exit.Keep; _keepCause = _lastTry ?? "time"; break; }
                if (Time.time - start > FinaleSafetyCap * Mathf.Max(1f, g.TimeScale))
                {
                    GameLog.Warn(LogChannel.Story, "Finale hit its safety cap");
                    _exit = Night3Exit.Keep;
                    break;
                }
                yield return null;
            }
            g.Taskbar.ClockAmber = false;
            // Phase M: the KEEP climax starts here: nothing ambient any more, and the cause notice shows without its chime.
            if (_exit == Night3Exit.Keep) BeginClimax();
            // Phase J: a log off that the seat or the clock cut short says so at once (and the card names it).
            _logOffCut = _exit == Night3Exit.Keep && LogOffRunning && _keepCause != "confirm";
            if (_logOffCut)
                g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text(_keepCause == "seat" ? "logoff.cancelled.seat" : "logoff.cancelled.time"),
                    "icon_shutdown", null, "sys_warning");
            GameLog.Info(LogChannel.Story, "Finale exit: " + _exit + (_exit == Night3Exit.Keep ? " (" + _keepCause + ")" : "") + (_logOffCut ? ", log off cut short" : ""));
        }

        bool LogOffRunning => (_logOffConfirm != null && _logOffConfirm.IsOpen) || (_logOffProgress != null && _logOffProgress.IsOpen);
        /// <summary>Phase M: a Log Off confirm has opened tonight: from then on nothing scary plays (the escape must feel clean).</summary>
        bool _logOffAsked;

        /// <summary>Phase M: the finale's drone, 0.1 from 6:50, 0.18 at 6:55 (the feed opens on CAM 03), 0.25 at 7:00.</summary>
        void FinaleDrone(int clock, ref bool at650, ref bool at655, ref bool at700)
        {
            float level = 0f;
            if (!at650 && clock >= 6 * 60 + 50) { at650 = true; level = 0.1f; }
            if (!at655 && clock >= 6 * 60 + 55) { at655 = true; level = 0.18f; }
            if (!at700 && clock >= Night3Rules.LogOffTime) { at700 = true; level = 0.25f; }
            if (level <= 0f) return;
            _g.Audio.PlayLoop("drone_tension", level, 4f);
            _g.Audio.SetLoopPitch("drone_tension", 1f);
        }

        /// <summary>The player moved the cursor, holds the button or typed something this frame.</summary>
        bool PlayerActive()
        {
            // Measured from where the cursor last really moved (not from last frame), so slow motion counts at any
            // frame rate; the wheel, the right button and typing count too (reading and scrolling is not idle).
            var p = _g.Player.Position;
            bool moved = (p - _idleLastPos).sqrMagnitude > IdleMoveSqr;
            if (moved) _idleLastPos = p;
            var input = _g.Input;
            bool other = input != null && (input.RightDown || input.RightUp || Mathf.Abs(input.Scroll) > 0.001f || !string.IsNullOrEmpty(input.TypedText));
            return moved || _g.Player.Held || other;
        }

        /// <summary>
        /// After a minute without input the clock runs to 7:00 over 15 s (the 6:50 and 6:55 feed opens still happen on
        /// the way). Any input hands the clock back to its own pace.
        /// </summary>
        IEnumerator IdleFastForward()
        {
            var clock = _g.Clock;
            double target = Night3Rules.LogOffTime;
            double missing = target - clock.ExactMinutes;
            if (missing <= 0) yield break;
            _fastForward = true;
            GameLog.Info(LogChannel.Story, "Finale: idle fast-forward to 7:00");
            clock.Rate = (float)(missing / IdleFastForwardSeconds);
            float end = Time.time + IdleFastForwardSeconds + 2f;
            float since = _idleSince;
            while (clock.ExactMinutes < target && Time.time < end && _exit == Night3Exit.None && _idleSince <= since) yield return null;
            if (clock.ExactMinutes < target) clock.Rate = FinaleRate;
            else
            {
                clock.Rate = PostSevenRate;
                GameLog.Info(LogChannel.Story, "Finale: idle fast-forward reached 7:00");
            }
            _fastForward = false;
            _idleSince = Time.time;
        }

        /// <summary>6:58: a knock on the office door (Phase M, N3-8: it is at your door two minutes before seven), a glitch, and static over the feed if it is showing.</summary>
        IEnumerator FeedFlicker()
        {
            GameLog.Info(LogChannel.Story, "Finale: feed flicker");
            if (!_logOffAsked) Scare("knock_door", 0.9f, -0.1f, 0f, ScareRules.StoryEventWindow, ScareRules.IgnoreAllButStory);
            _g.Fx.Glitch(0.18f, 0.55f);
            var cam = _g.Apps.Find<CameraApp>();
            if (cam != null && cam.IsOpen && !cam.Window.IsMinimized && _g.CameraRig != null) yield return StaticCut(() => { });
        }

        /// <summary>Debug (F1 panel): end the night now with this exit's ending.</summary>
        public void ForceExit(Night3Exit exit)
        {
            if (CurrentBeat == "ending" || exit == Night3Exit.None) return;
            GameLog.Info(LogChannel.Debug, "Forced Night 3 exit: " + exit);
            _keepCause = "time";
            _exit = exit;
            // The finale's loop picks it up; any other beat goes straight to the ending.
            if (CurrentBeat != "finale") JumpTo("ending");
        }

        IEnumerator GarySays(string lineSet)
        {
            yield return GaryPresentFor(5f);
            yield return Say(_gary, Lines(lineSet), GaryCps);
        }

        /// <summary>Phase Q3 (D4): the room's level while she offers, how long after her last line the clock moves, and the longest the offer is held.</summary>
        const float OfferAmbience = 0.6f, OfferPause = 1.5f, OfferMaxSeconds = 40f;
        /// <summary>
        /// How fast the five offer lines are typed (it was 2.4: about 35 s, which the clock used to run through; with the clock held that is too
        /// long to wait). 4.2 characters a second is about 17 s, each line still readable as it lands.
        /// </summary>
        const float OfferCps = 4.2f;
        bool _offerTyped, _offerReleased;

        /// <summary>
        /// Phase Q3 (D4, fifth blind playtest): at 6:41 the offer used to compete with the end-of-shift notice, a task and the Log Off toast.
        /// The clock stays at 6:41 and every notice waits until her five lines are typed (and 1.5 s after); then the clock runs, the room
        /// comes back, and the end-of-shift rule is given. Every signpost is still at the same game minute.
        /// </summary>
        IEnumerator OfferBreath()
        {
            yield return WaitUntil(() => _offerTyped || _exit != Night3Exit.None || CurrentBeat != "finale", OfferMaxSeconds);
            yield return Wait(OfferPause);
            ReleaseOffer();
            if (_exit != Night3Exit.None || CurrentBeat != "finale") yield break;
            EndOfShiftRule();
        }

        /// <summary>The offer's hold ends (also when the night leaves the finale): the clock, the notices and the room come back.</summary>
        void ReleaseOffer()
        {
            var g = _g;
            if (_offerReleased) return;
            _offerReleased = true;
            g.Clock.Frozen = false;
            g.Notifications.HeldByStory = false;
            g.Audio.SetAmbienceLevel(1f, 2f);
            GameLog.Info(LogChannel.Story, "Finale: the offer is made, the clock runs");
        }

        /// <summary>Seconds after the offer is released before her request to be put in the bin appears in the queue.</summary>
        const float LetGoTaskDelay = 3f;

        /// <summary>
        /// Phase I: "or let me go" is something you do, so it is written into the Work Queue in her colours, like her
        /// asks on Night 2: PUT ME IN THE BIN, with the file it means. (The blind tester guessed the file and lost three
        /// fights while the Jotter said nothing about it.)
        /// </summary>
        IEnumerator LetGoRequest()
        {
            yield return WaitUntil(() => _offerReleased || _exit != Night3Exit.None, OfferMaxSeconds + OfferPause + 5f);
            yield return Wait(LetGoTaskDelay);
            if (_exit != Night3Exit.None || CurrentBeat != "finale") yield break;
            if (!_g.Files.Exists(ContentIds.File017) || (_g.Files.GetFile(ContentIds.File017)?.Shredded ?? false)) yield break;
            GiveEntityTask(ContentIds.TaskE3LetGo);
        }

        /// <summary>
        /// Phase I: "put me in the bin" is about a file you must be able to see. Her Jotter (and any window of yours) can open
        /// over employee_017.dat, so for the first 30 s of the finale it is moved out onto bare desktop and blinks whenever
        /// something covers it. Nothing is moved while you carry it or a shred is going on.
        /// </summary>
        IEnumerator KeepFile017InView()
        {
            for (int i = 0; i < 15 && _exit == Night3Exit.None; i++)
            {
                yield return Wait(2f);
                yield return BringIntoView(ContentIds.File017, File017Spot);
            }
        }

        /// <summary>
        /// "At seven they finish you. Stay with me (say stay), or let me go (put me in the bin)." Staying asks to be confirmed.
        /// Phase I: she keeps reading until an exit is chosen, whatever else is typed first: a question no longer leaves you
        /// talking to nobody, and "stay" typed later still counts.
        /// </summary>
        IEnumerator FinalExchange()
        {
            yield return Wait(1.5f);
            var last = new DialogueReply[1];
            // M8: a miss gets another turn; Phase J: from the second miss on she names the two ways out plainly.
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeN3Final, OnFinalReply, last, OfferCps, 4f, 25f, "DONT",
                fallbackRetries: 1, lastFallbackSet: "n3_final_third", keepListening: r => r.Tag != "stay" && _exit == Night3Exit.None,
                onLinesTyped: ex => _offerTyped = true);
            if (last[0] == null || last[0].Tag != "stay" || _exit != Night3Exit.None) yield break;
            var confirm = new DialogueReply[1];
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeN3Confirm, OnFinalReply, confirm, 3f, 4f, 25f, "DONT",
                keepListening: r => r.Tag != "confirm" && _exit == Night3Exit.None);
            if (confirm[0] == null || confirm[0].Tag != "confirm" || _exit != Night3Exit.None) yield break;
            // She keeps the time: seven comes quickly, and her request to be put in the bin is over (Review J7).
            _confirmed = true;
            if (_g.Tasks.IsActive(ContentIds.TaskE3LetGo)) _g.Tasks.Withdraw(ContentIds.TaskE3LetGo, _g.Content.Text("workqueue.withdrawn.expired", "expired"));
            GameLog.Info(LogChannel.Story, "KEEP confirmed: the clock runs to 7:00");
            _fastForward = true;
            yield return EnsureClockAtLeast(7, 0, 25f);
            _fastForward = false;
        }

        void OnFinalReply(DialogueReply r, string said)
        {
            OnEllenReply(r, said);
            if (r.Tag == "stay") _g.Flags.Set(MemoryFlags.N3SaidStay);
            NoteFinalReplyForLetGo(r);
            if (!string.IsNullOrEmpty(r.Tag)) GameLog.Info(LogChannel.Story, "Finale reply tag: " + r.Tag);
        }

        // ------------------------------------------------------------------ hooks during the finale

        void HookFinale()
        {
            UnhookFinale();
            var g = _g;
            _onFinaleConfirm = (id, box) =>
            {
                if (id != ContentIds.File017 || _exit != Night3Exit.None) return;
                bool first = !_garyGuardSaid;
                _garyGuardSaid = true;
                if (GaryFinished)
                {
                    // Finished Gary helps the shred along: he sits on No so she cannot answer it.
                    Gary.Run(GaryGuard(() => box.IsOpen ? box.Button("No")?.Hit : null, 3f), "gary-guard-no");
                    if (first) RunSide(Say(_gary, Lines("g3c_shred"), GaryCps), "gary-shred-line");
                }
                else if (_binMode == FinaleBinMode.LetGo)
                {
                    // Phase P (T1): she is letting it go; kept Gary does not guard, he says it is all right.
                    if (first) RunSide(GarySays("g3_letgo_go"), "gary-letgo-line");
                }
                else
                {
                    Gary.Run(GaryGuardsShred(box, first), "gary-guard-shred");
                }
            };
            _onFinaleProgress = (id, progress) =>
            {
                if (id != ContentIds.File017 || _exit != Night3Exit.None) return;
                StopLastWords();
                _lastWords = RunSide(LastWords(progress), "last-words");
            };
            _onFinaleCancelled = (id, by) =>
            {
                // 017 survived: no last words (and a line cut half way must not keep her pad busy).
                if (id == ContentIds.File017) StopLastWords();
            };
            _onFinaleShredded = (id, by) =>
            {
                if (id != ContentIds.File017 || _exit != Night3Exit.None) return;
                _exit = Night3Exit.Shred;
                // The shred completes while her last words are still being typed: they stop mid-word.
                _lastWords?.Stop();
                _lastWords = null;
            };
            _onFinaleRequested = (id, by) =>
            {
                if (id == ContentIds.File017 && by != null && by.IsPlayer && _exit == Night3Exit.None) _lastTry = "letgo";
            };
            _onFinaleTug = p =>
            {
                if (p.FileId == ContentIds.File017 && _exit == Night3Exit.None) _lastTry = "letgo";
                if (p.FileId != ContentIds.File017 || _tugLineSaid || _exit != Night3Exit.None) return;
                _tugLineSaid = true;
                // Phase P (T1): the line set follows the bin mode; LetGo's lines are paced to the hold (Night3Director.LetGo.cs).
                string set = Night3Rules.TugLineSet(_binMode);
                GameLog.Info(LogChannel.Story, "Finale tug: " + set + " (" + _binMode + ", grip x" + E.Brain.TrustGripMult.ToString("0.0") + ")");
                if (_binMode == FinaleBinMode.Fight) RunSide(Say(_ellen, Lines(set), 5f), "tug-line");
            };
            _onFinaleSeat = () =>
            {
                if (_exit != Night3Exit.None) return;
                _exit = Night3Exit.Keep;
                _keepCause = "seat";
                g.Flags.Set(MemoryFlags.N3SeatCleared);
            };
            g.Shred.Requested += _onFinaleRequested;
            g.Shred.ConfirmShown += _onFinaleConfirm;
            g.Shred.ProgressStarted += _onFinaleProgress;
            g.Shred.Completed += _onFinaleShredded;
            g.Shred.Cancelled += _onFinaleCancelled;
            g.Conflict.TugStarted += _onFinaleTug;
            g.Rounds.SeatCleared += _onFinaleSeat;
            HookLetGo();
        }

        void UnhookFinale()
        {
            var g = _g;
            if (_onFinaleRequested != null) g.Shred.Requested -= _onFinaleRequested;
            if (_onFinaleConfirm != null) g.Shred.ConfirmShown -= _onFinaleConfirm;
            if (_onFinaleProgress != null) g.Shred.ProgressStarted -= _onFinaleProgress;
            if (_onFinaleShredded != null) g.Shred.Completed -= _onFinaleShredded;
            if (_onFinaleCancelled != null) g.Shred.Cancelled -= _onFinaleCancelled;
            if (_onFinaleTug != null) g.Conflict.TugStarted -= _onFinaleTug;
            if (_onFinaleSeat != null && g.Rounds != null) g.Rounds.SeatCleared -= _onFinaleSeat;
            UnhookLetGo();
            if (g.Rounds != null) g.Rounds.OnItNoticeKey = g.Rounds.NotOnItNoticeKey = g.Rounds.ClosedNoticeKey = null;
            _onFinaleRequested = null;
            _onFinaleConfirm = null;
            _onFinaleProgress = null;
            _onFinaleShredded = null;
            _onFinaleCancelled = null;
            _onFinaleTug = null;
            _onFinaleSeat = null;
            if (_logOffConfirm != null && _logOffConfirm.IsOpen) _logOffConfirm.Window.Close(null);
            if (_logOffProgress != null && _logOffProgress.IsOpen) _logOffProgress.Close(null);
            _logOffConfirm = null;
            _logOffProgress = null;
        }

        /// <summary>
        /// Past 85% of the shred she starts her last words (and the last stretch of the bar slows, so a few
        /// letters get out). Completion cuts the line.
        /// </summary>
        IEnumerator LastWords(ProgressDialog progress)
        {
            while (progress != null && progress.IsOpen && progress.Progress < 0.85f) yield return null;
            if (progress == null || !progress.IsOpen) yield break;
            string set = Night3Rules.ShredLastWordsSet(_g.Memory.Trust);
            RunSide(SlowLastStretch(progress), "last-stretch");
            _lastWordsSaying = true;
            yield return Say(_ellen, Lines(set), 5f);
            _lastWordsSaying = false;
        }

        bool _lastWordsSaying;

        void StopLastWords()
        {
            // Phase N: only last words that were being typed free her pad (a shred cancelled at the confirm used to unlock it under the
            // exchange she was typing, and her tug line typed into the middle of it).
            _lastWords?.Stop();
            _lastWords = null;
            if (_lastWordsSaying) _ellen.Typing = false;
            _lastWordsSaying = false;
        }

        IEnumerator SlowLastStretch(ProgressDialog progress)
        {
            while (progress != null && progress.IsOpen)
            {
                _g.Shred.SpeedMultiplier = Mathf.Min(_g.Shred.SpeedMultiplier, 0.35f);
                yield return null;
            }
        }

        // ------------------------------------------------------------------ ENDING

        IEnumerator EndingBeat()
        {
            var g = _g;
            // A jump straight to the ending (debug) ends the way an idle night does.
            if (_exit == Night3Exit.None)
            {
                _exit = Night3Exit.Keep;
                _keepCause = "time";
            }
            var exit = _exit;
            // Nothing from the finale (her exchange, tug or log-off lines, Gary) may type into the dark ending, and no scare that is still
            // waiting may land in SHRED's silence (Phase M review).
            StopSideRoutines();
            ReleaseOffer();
            g.Scares.CancelAll();
            g.Scares.ClimaxRunning = true;
            // A stopped fast-forward never restores the clock itself.
            if (_fastForward)
            {
                _fastForward = false;
                g.Clock.Rate = PostSevenRate;
            }
            UnhookFinale();
            g.Tasks.Withdraw(ContentIds.TaskN3LogOffBy);
            g.Rounds.Stop();
            g.Rounds.ForcedOpenHandler = null;
            E.Brain.Enabled = false;
            E.Brain.AllowCloseCamera = false;
            E.Interrupt();
            Gary.Interrupt();
            if (g.Conflict.IsFighting) g.Conflict.Interrupt();
            if (exit != Night3Exit.Shred) g.Shred.Abort();
            string id = exit == Night3Exit.Shred ? ContentIds.EndingN3Shred : exit == Night3Exit.LogOff ? ContentIds.EndingN3LogOff : ContentIds.EndingN3Keep;
            CompleteNight(id);
            // Phase Q2: tokens from the save the night just wrote, with tonight's last input.
            RefreshTokens();

            EndingSpec spec;
            if (exit == Night3Exit.Shred)
            {
                yield return ShredAftermath();
                var set = g.Content.LineSet("n3_end_shred");
                // Phase Q2 (V1, V3): the copy types your first words and the first letter of the name you gave.
                var lines = NightTemplates.FillLines(set?.lines, Tokens, set?.speakers, out var speakers);
                spec = FinalSpec(id, EndingKind.Shred, "end.shred.title", "end.shred.subtitle", lines, speakers);
            }
            else if (exit == Night3Exit.LogOff)
            {
                var set = g.Content.LineSet(g.Flags.Has(MemoryFlags.N3SeatCleared) ? "n3_end_logoff_cleared" : "n3_end_logoff");
                spec = FinalSpec(id, EndingKind.LogOff, "end.logoff.title", "end.logoff.subtitle", set?.lines, set?.speakers);
                // Phase L: Gary's box, kept for him, leaves with you.
                // Phase Q2 (V1): the log's last line is the last thing you typed tonight.
                var log = new System.Collections.Generic.List<string>(Lines("n3_end_logoff_sys"));
                if (g.Flags.Has(MemoryFlags.N3Box209Kept)) log.Insert(2, Lines("n3_end_logoff_box")[0]);
                // Phase Q3 (D6): Ruth's drive order reaches the log: out of the lobby at 7:02 if you wiped it, her seat still taken if you did not.
                string ruth = WorkOrderRules.Remembered(g.Flags, 3, ContentIds.Order3333);
                if (ruth != null) log.Insert(log.FindIndex(l => l.StartsWith("LOBBY EXIT", StringComparison.Ordinal)), Lines(ruth == "approve" ? "n3_end_logoff_ruth_out" : "n3_end_logoff_ruth_kept")[0]);
                spec.SystemLines = log.ToArray();
            }
            else
            {
                yield return KeepFinalImage(_keepCause != "confirm");
                var set = g.Content.LineSet(Night3Rules.KeepLineSet(g.Flags.Has(MemoryFlags.N3SaidStay)));
                var name = g.Content.LineSet("n3_end_keep_name");
                Night3Rules.KeepLines(set?.lines, set?.speakers, name?.lines, name?.speakers, MemoryFlags.SaidNameAny(g.Flags), out var keepLines, out var keepSpeakers);
                // Phase Q2 (V1, V3): YOU SAID (your Night 1 words) and GOODNIGHT (your name), each only when there is one.
                var lines = NightTemplates.FillLines(keepLines, Tokens, keepSpeakers, out var speakers);
                spec = FinalSpec(id, EndingKind.Keep, "end.keep.title", Night3Rules.KeepSubtitleKey(_keepCause), lines, speakers);
            }
            // Phase J: the card says what caused this ending (the tester logged off and read "You stayed").
            spec.Outcome = g.Content.Text(Night3Rules.EndingCauseKey(exit, _keepCause, _logOffCut));
            spec.AfterHit = exit == Night3Exit.Keep;
            // Phase Q2 (T2): the run's Retention Record, before the card (a debug or stand-in ending never replaces the kept one).
            if (!IsStandIn && g.RecordsArmed) spec.RecordPage = FullRecord(id);
            _ending = new EndingSequence(g, spec);
            yield return _ending.Run();
        }

        /// <summary>Phase Q2 (T2): the whole Retention Record of this run, from the save the night just wrote.</summary>
        System.Collections.Generic.List<RecordRow> FullRecord(string endingId)
        {
            var g = _g;
            var correct = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var o in g.Content.WorkOrders.orders)
                if (o != null && WorkOrderRules.IsChoice(o)) correct[o.id] = o.correct;
            var c = g.Content;
            return RetentionRecord.Full(g.Save ?? new SaveData(), endingId, correct, (k, a) => a == null ? c.Text(k) : c.Format(k, a));
        }

        EndingSpec FinalSpec(string id, EndingKind kind, string title, string subtitle, string[] lines, string[] speakers) => new EndingSpec
        {
            Id = id,
            Kind = kind,
            Lines = lines ?? Array.Empty<string>(),
            Speakers = speakers,
            TitleKey = title,
            SubtitleKey = subtitle,
            ThanksKey = "end.card.thanks",
            DemoCard = false,
            FinalCard = true,
            Stinger = _g.Flags.Has(MemoryFlags.N3Cam00),
        };

        /// <summary>SHRED: session 017 closes, one pointer fewer, silence, and the office on CAM 03 turns to look.</summary>
        IEnumerator ShredAftermath()
        {
            var g = _g;
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("shred.closed017"), "icon_info", null, "sys_warning", false, null, Core.Game.NoticeKind.Entity);
            g.Taskbar.PointingDevices = Mathf.Max(1, g.Taskbar.PointingDevices - 1);
            E.SetPresent(false, 0.2f);
            g.Audio.StopAllLoops(0.3f);
            g.Audio.SetAmbience(false, 0.3f);
            // Phase M (5.6): no hit on SHRED; the head turns in total silence (its horror is your own cursor typing in the dark).
            if (g.CameraRig != null) g.CameraRig.Quiet = true;
            yield return Wait(3f);
            yield return FinalImage();
        }

        /// <summary>KEEP: by time or by the seat, she says it over the feed; confirmed, the feed simply opens at seven.</summary>
        IEnumerator KeepFinalImage(bool withLine)
        {
            var g = _g;
            if (g.Clock.TotalMinutes < Night3Rules.LogOffTime) g.Clock.Set(7, 0);
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.5f), 0.4f, false);
            yield return KeepClimax(withLine);
        }

        /// <summary>The viewer shows CAM 03 (opened or restored) with the figure behind the chair and the door wide; null rig = no feed.</summary>
        SecurityCameraRig ShowFinalFeed()
        {
            var g = _g;
            var cam = g.Apps.Find<CameraApp>();
            if (cam == null) cam = g.Apps.Launch(AppIds.Camera, null) as CameraApp;
            else cam.Window.Restore(null);
            if (cam != null) g.Windows.Front(cam.Window);
            cam?.Select(ContentIds.Cam03, null);
            var rig = g.CameraRig;
            if (rig == null) return null;
            rig.DoorOpen = 1f;
            rig.SeatedMimicsPlayer = false;
            rig.LightFlicker = 1f;
            return rig;
        }

        /// <summary>
        /// KEEP, the losing path, as the game's big climax (Phase M, 5.2): it resolves out of static right behind you, the room drops out
        /// while she types DONT TURN AROUND, pressure and the monitor's whine build while "you" turn to the camera, a breath at the
        /// microphone, then true silence on a frozen frame, the hit, NO SIGNAL, the tube dies, and your ears ring in the dark.
        /// </summary>
        IEnumerator KeepClimax(bool feedLine)
        {
            var g = _g;
            var rig = ShowFinalFeed();
            if (rig == null) yield break;
            BeginClimax();
            // Review M5: the feed stays up through the build (the player's pointer is still until the card).
            g.Player.Enabled = false;
            // Phase Q3 (V4): the game's big climax fills the desktop, DONT TURN AROUND over the feed when she says it.
            yield return FullSizeFeed(feedLine ? Lines("n3_finale_feed")[0] : null, null, KeepFullSeconds);
            if (rig.Figure == FigureStage.BehindChair) g.Audio.Play("step_near", 1f);
            RunSide(StaticResolve(() => rig.Figure = FigureStage.BehindChair, 0.5f), "keep-resolve");
            yield return Wait(0.3f);
            DropRoom(1.5f);
            yield return Wait(0.1f);
            if (feedLine) yield return Say(_ellen, Lines("n3_finale_feed"), 4f);
            else yield return Wait(0.2f);
            GameLog.Info(LogChannel.Story, "Climax: build (keep)");
            g.Audio.Play("sub_swell", 1f);
            float t = 0f, noise = g.Fx.ReduceFlashing ? 0.2f : 0.35f;
            bool whine = false, breath = false;
            while (t < KeepBuildToCut)
            {
                t += Time.deltaTime;
                if (!whine && t >= KeepWhineAt) { whine = true; g.Audio.Play("crt_whine_rise", 1f); }
                if (!breath && t >= KeepTurnAt - 0.2f) { breath = true; g.Audio.Play("breath_near", 0.9f); }
                float turn = t - KeepTurnAt, toCut = KeepBuildToCut - t;
                if (turn > 0f) rig.SeatedHeadTurn = Mathf.SmoothStep(0f, 1f, turn / 3.2f);
                // The last 0.35 s the feed's noise rises instead of glitching, then the cut leaves a clean frame.
                if (toCut < 0.35f) rig.ExtraNoise = (1f - toCut / 0.35f) * noise;
                else if (turn > 0f) GlitchNowAndThen(1.2f);
                yield return null;
            }
            CutToSilence(KeepSilence);
            yield return Wait(KeepSilence - StingerPreRoll);
            // A lighter flash than Night 1's: the dark shape at the lens must still read through it.
            yield return Hit(1f, 1f, 6f, 0.3f, "crt_off", "ear_ring");
            // Full effects: for the hit's first frames its head is right at the lens.
            if (!g.Fx.ReduceFlashing) rig.Figure = FigureStage.AtLens;
            yield return TubeDies(true, 0.6f, 0.9f, 0.8f);
        }

        /// <summary>Phase Q3 (V4): how long the 6:55 feed and the KEEP climax's feed stay full size at most.</summary>
        const float FinaleFullSeconds = 5f, KeepFullSeconds = 12f;

        /// <summary>KEEP's build from sub_swell: the whine joins, the head starts to turn, the cut (both build clips end there), the silence.</summary>
        const float KeepWhineAt = 0.5f, KeepTurnAt = 1.05f, KeepBuildToCut = 4f, KeepSilence = 0.45f;

        /// <summary>SHRED's CAM 03 image: the figure behind the chair, and "you" turn to the camera, in silence.</summary>
        IEnumerator FinalImage()
        {
            var g = _g;
            var rig = ShowFinalFeed();
            if (rig == null) yield break;
            rig.Figure = FigureStage.BehindChair;
            float t = 0f;
            while (t < 3.2f)
            {
                t += Time.deltaTime;
                rig.SeatedHeadTurn = Mathf.SmoothStep(0f, 1f, t / 3.2f);
                rig.ExtraNoise = t / 3.2f * 0.5f;
                GlitchNowAndThen(3f);
                yield return null;
            }
            yield return Wait(1.2f);
            rig.ExtraNoise = 0f;
        }
    }
}
#endif
