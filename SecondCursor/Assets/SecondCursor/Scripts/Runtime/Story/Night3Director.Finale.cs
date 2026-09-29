using System;
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
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
        static readonly Vector2 File017Spot = new Vector2(600f, 300f);

        Night3Exit _exit;
        string _keepCause = "time";
        bool _confirmed, _fastForward, _tugLineSaid, _garyGuardSaid;
        int _garyLogOffTries;
        MessageBox _logOffConfirm;
        ProgressDialog _logOffProgress;
        Routine _lastWords;
        Action<string, MessageBox> _onFinaleConfirm;
        Action<string, ProgressDialog> _onFinaleProgress;
        Action<string, CursorAgent> _onFinaleShredded;
        Action<DragPayload> _onFinaleTug;
        Action _onFinaleSeat;

        IEnumerator Finale()
        {
            var g = _g;
            _holdAt = -1;
            _exit = Night3Exit.None;
            _keepCause = "time";
            _confirmed = _fastForward = _tugLineSaid = _garyGuardSaid = false;
            _garyLogOffTries = 0;
            if (g.Clock.TotalMinutes < Night3Rules.FinaleStart) g.Clock.Set(6, 41);
            g.Clock.Frozen = false;
            g.Clock.Rate = FinaleRate;
            g.Shred.IsInUse = null;
            g.Flags.Set(Flags.CameraUnlocked);
            g.Flags.Set(Flags.LogoffItem);
            E.Phase = EntityPhase.Interference;
            float start = Time.time;

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
            RunSide(FinalExchange(), "final-exchange");

            // The feed: the corridor to the seat. Security (or finished Gary) opens it by the clock.
            g.Rounds.ForcedBy = null;
            g.Rounds.ForcedOpenHandler = GaryFinished ? GaryForcedOpen : (Action<string>)null;
            g.Rounds.Begin(RoundsConfig.Night3Finale(g.Difficulty.Mode, g.Memory.Trust));
            bool open650 = false, open655 = false, open700 = false, open702 = false, said652 = false, said700 = false;
            float keepSince = -1f;

            while (_exit == Night3Exit.None)
            {
                int clock = g.Clock.TotalMinutes;
                if (brain.Defenses >= 1) brain.AllowIdleLurk = true;
                if (!open650 && clock >= 6 * 60 + 50) { open650 = true; g.Rounds.OpenViewer(null); }
                if (!open655 && clock >= 6 * 60 + 55)
                {
                    open655 = true;
                    g.Rounds.OpenViewer(ContentIds.Cam03);
                    SayLater(_ellen, "n3_finale_feed", 4.5f);
                }
                if (!open700 && clock >= Night3Rules.LogOffTime) { open700 = true; g.Rounds.OpenViewer(null); }
                if (!open702 && clock >= Night3Rules.LogOffTime + 2) { open702 = true; g.Rounds.OpenViewer(null); }

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
                    g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("logoff.available"), "icon_info", null, "ui_select");
                    GameLog.Info(LogChannel.Story, "7:00: log off available");
                    if (!GaryFinished) RunSide(GarySays("g3_finale"), "gary-finale");
                }
                if (clock >= Night3Rules.LogOffTime && !_fastForward) g.Clock.Rate = RoundsRate;

                // KEEP by confirmation lands at 7:00 (unless a shred is already running).
                bool running = g.Shred.Busy || LogOffRunning;
                if (_confirmed && clock >= Night3Rules.LogOffTime && !g.Shred.Busy) { _exit = Night3Exit.Keep; _keepCause = "confirm"; break; }
                if (clock >= Night3Rules.KeepTime && keepSince < 0f) keepSince = Time.time;
                if (Night3Rules.KeepByTime(clock, running, keepSince < 0f ? 0f : Time.time - keepSince)) { _exit = Night3Exit.Keep; _keepCause = "time"; break; }
                if (Time.time - start > FinaleSafetyCap)
                {
                    GameLog.Warn(LogChannel.Story, "Finale hit its safety cap");
                    _exit = Night3Exit.Keep;
                    break;
                }
                yield return null;
            }
            GameLog.Info(LogChannel.Story, "Finale exit: " + _exit + (_exit == Night3Exit.Keep ? " (" + _keepCause + ")" : ""));
        }

        bool LogOffRunning => (_logOffConfirm != null && _logOffConfirm.IsOpen) || (_logOffProgress != null && _logOffProgress.IsOpen);

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

        /// <summary>"At seven they finish you. Stay with me, or let me go." One exchange; staying asks to be confirmed.</summary>
        IEnumerator FinalExchange()
        {
            yield return Wait(1.5f);
            var last = new DialogueReply[1];
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeN3Final, OnFinalReply, last, 2.4f, 4f, 25f, "DONT");
            if (last[0] == null || last[0].Tag != "stay" || _exit != Night3Exit.None) yield break;
            var confirm = new DialogueReply[1];
            yield return RunExchangeChain(_ellen, ContentIds.ExchangeN3Confirm, OnFinalReply, confirm, 3f, 4f, 25f, "DONT");
            if (confirm[0] == null || confirm[0].Tag != "confirm" || _exit != Night3Exit.None) yield break;
            // She keeps the time: seven comes quickly.
            _confirmed = true;
            GameLog.Info(LogChannel.Story, "KEEP confirmed: the clock runs to 7:00");
            _fastForward = true;
            yield return EnsureClockAtLeast(7, 0, 25f);
            _fastForward = false;
        }

        void OnFinalReply(DialogueReply r, string said)
        {
            OnEllenReply(r, said);
            if (r.Tag == "stay") _g.Flags.Set(MemoryFlags.N3SaidStay);
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
                else
                {
                    Gary.Run(GaryGuardsShred(box, first), "gary-guard-shred");
                }
            };
            _onFinaleProgress = (id, progress) =>
            {
                if (id != ContentIds.File017 || _exit != Night3Exit.None) return;
                _lastWords?.Stop();
                _lastWords = RunSide(LastWords(progress), "last-words");
            };
            _onFinaleShredded = (id, by) =>
            {
                if (id != ContentIds.File017 || _exit != Night3Exit.None) return;
                _exit = Night3Exit.Shred;
                // The shred completes while her last words are still being typed: they stop mid-word.
                _lastWords?.Stop();
                _lastWords = null;
            };
            _onFinaleTug = p =>
            {
                if (p.FileId != ContentIds.File017 || _tugLineSaid || _exit != Night3Exit.None) return;
                _tugLineSaid = true;
                string set = Night3Rules.TugLineSet(g.Memory.Trust);
                GameLog.Info(LogChannel.Story, "Finale tug: " + set + " (grip x" + E.Brain.TrustGripMult.ToString("0.0") + ")");
                RunSide(Say(_ellen, Lines(set), 5f), "tug-line");
            };
            _onFinaleSeat = () =>
            {
                if (_exit != Night3Exit.None) return;
                _exit = Night3Exit.Keep;
                _keepCause = "seat";
                g.Flags.Set(MemoryFlags.N3SeatCleared);
            };
            g.Shred.ConfirmShown += _onFinaleConfirm;
            g.Shred.ProgressStarted += _onFinaleProgress;
            g.Shred.Completed += _onFinaleShredded;
            g.Conflict.TugStarted += _onFinaleTug;
            g.Rounds.SeatCleared += _onFinaleSeat;
        }

        void UnhookFinale()
        {
            var g = _g;
            if (_onFinaleConfirm != null) g.Shred.ConfirmShown -= _onFinaleConfirm;
            if (_onFinaleProgress != null) g.Shred.ProgressStarted -= _onFinaleProgress;
            if (_onFinaleShredded != null) g.Shred.Completed -= _onFinaleShredded;
            if (_onFinaleTug != null) g.Conflict.TugStarted -= _onFinaleTug;
            if (_onFinaleSeat != null && g.Rounds != null) g.Rounds.SeatCleared -= _onFinaleSeat;
            _onFinaleConfirm = null;
            _onFinaleProgress = null;
            _onFinaleShredded = null;
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
            yield return Say(_ellen, Lines(set), 5f);
        }

        IEnumerator SlowLastStretch(ProgressDialog progress)
        {
            while (progress != null && progress.IsOpen)
            {
                _g.Shred.SpeedMultiplier = Mathf.Min(_g.Shred.SpeedMultiplier, 0.35f);
                yield return null;
            }
        }

        // ------------------------------------------------------------------ log off

        /// <summary>
        /// Start, Log Off CROURKE...: too early before 7:00, refused while session.cfg says ALLOW_LOGOFF=0,
        /// otherwise the confirm (Yes, No), then 6 s of "Logging off" with Cancel.
        /// </summary>
        public override void RequestLogOff(CursorAgent a)
        {
            var g = _g;
            var c = g.Content;
            if (CurrentBeat == "ending" || _exit != Night3Exit.None) return;
            if (LogOffRunning)
            {
                var w = _logOffConfirm != null && _logOffConfirm.IsOpen ? _logOffConfirm.Window : _logOffProgress?.Window;
                if (w != null) { w.Focus(a); w.Shake(0.2f, 2f); }
                return;
            }
            string title = c.Text("logoff.item").TrimEnd('.');
            var check = Night3Rules.CheckLogOff(g.Clock.TotalMinutes, g.Flags.Has(MemoryFlags.N3LogoffEnabled));
            GameLog.Info(LogChannel.Player, "Log off requested: " + check);
            if (check == LogOffCheck.Early)
            {
                Dialogs.Message(g, title, c.Text("logoff.early"), "icon_info", new[] { "OK" }, null);
                return;
            }
            if (check == LogOffCheck.Disabled)
            {
                Dialogs.Message(g, title, c.Text("logoff.disabled"), "icon_lock", new[] { "OK" }, null);
                return;
            }
            _logOffConfirm = Dialogs.Message(g, title, c.Text("logoff.confirm"), "icon_question", new[] { "Yes", "No" }, OnLogOffAnswer, 1);
            var box = _logOffConfirm;
            RunSide(Say(_ellen, Lines(Night3Rules.LogOffLineSet(g.Memory.Trust)), 4.5f), "logoff-line");
            if (GaryFinished) Gary.Run(GaryRacesToNo(box), "gary-race-no");
            else Gary.Run(GaryGuard(() => box.IsOpen ? box.Button("No")?.Hit : null, 3f), "gary-guard-logoff");
        }

        void OnLogOffAnswer(string result, CursorAgent by)
        {
            GameLog.Info(LogChannel.Story, "Log off confirm: " + result + " by " + (by?.Name ?? "System"));
            if (result != "Yes" || _exit != Night3Exit.None) return;
            var c = _g.Content;
            _logOffProgress = Dialogs.Progress(_g, c.Text("logoff.item").TrimEnd('.'), c.Text("logoff.progress"), "icon_shutdown");
            var progress = _logOffProgress;
            bool cancelled = false;
            progress.Cancelled += a =>
            {
                cancelled = true;
                GameLog.Info(LogChannel.Story, "Log off cancelled by " + (a?.Name ?? "System"));
                progress.Close(a);
            };
            RunSide(LogOffProgress(progress, () => cancelled), "logoff-progress");
            if (GaryFinished) Gary.Run(GaryCancelsLogOff(progress), "gary-cancel-logoff");
        }

        IEnumerator LogOffProgress(ProgressDialog progress, Func<bool> cancelled)
        {
            float t = 0f;
            _g.Audio.Play("hdd_seek", 0.7f);
            while (progress.IsOpen && !cancelled() && t < LogOffSeconds)
            {
                t += Time.deltaTime;
                progress.Progress = t / LogOffSeconds;
                yield return null;
            }
            if (cancelled() || t < LogOffSeconds || _exit != Night3Exit.None) yield break;
            progress.Close(null);
            _exit = Night3Exit.LogOff;
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
            UnhookFinale();
            g.Rounds.Stop();
            g.Rounds.ForcedOpenHandler = null;
            E.Brain.Enabled = false;
            E.Brain.AllowCloseCamera = false;
            E.Interrupt();
            Gary.Interrupt();
            if (g.Conflict.IsFighting) g.Conflict.Interrupt();
            if (exit != Night3Exit.Shred) g.Shred.Abort();
            string id = exit == Night3Exit.Shred ? ContentIds.EndingN3Shred : exit == Night3Exit.LogOff ? ContentIds.EndingN3LogOff : ContentIds.EndingN3Keep;
            GameLog.Info(LogChannel.Story, "Hook: ACH_NIGHT_3, ending " + id);
            CompleteNight(id);

            EndingSpec spec;
            if (exit == Night3Exit.Shred)
            {
                yield return ShredAftermath();
                var set = g.Content.LineSet("n3_end_shred");
                spec = FinalSpec(id, EndingKind.Shred, "end.shred.title", "end.shred.subtitle", set?.lines, set?.speakers);
            }
            else if (exit == Night3Exit.LogOff)
            {
                var set = g.Content.LineSet(g.Flags.Has(MemoryFlags.N3SeatCleared) ? "n3_end_logoff_cleared" : "n3_end_logoff");
                spec = FinalSpec(id, EndingKind.LogOff, "end.logoff.title", "end.logoff.subtitle", set?.lines, set?.speakers);
                spec.SystemLines = Lines("n3_end_logoff_sys");
            }
            else
            {
                yield return KeepFinalImage(_keepCause != "confirm");
                var set = g.Content.LineSet(Night3Rules.KeepLineSet(g.Flags.Has(MemoryFlags.N3SaidStay)));
                var name = g.Content.LineSet("n3_end_keep_name");
                Night3Rules.KeepLines(set?.lines, set?.speakers, name?.lines, name?.speakers, g.Flags.Has(MemoryFlags.SaidName), out var lines, out var speakers);
                spec = FinalSpec(id, EndingKind.Keep, "end.keep.title", "end.keep.subtitle", lines, speakers);
            }
            _ending = new EndingSequence(g, spec);
            yield return _ending.Run();
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
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("shred.closed017"), "icon_info", null, "sys_warning");
            g.Taskbar.PointingDevices = Mathf.Max(1, g.Taskbar.PointingDevices - 1);
            E.SetPresent(false, 0.2f);
            g.Audio.StopAllLoops(0.3f);
            g.Audio.SetAmbience(false, 0.3f);
            yield return Wait(3f);
            yield return FinalImage(false);
        }

        /// <summary>KEEP: by time or by the seat, she says it over the feed; confirmed, the feed simply opens at seven.</summary>
        IEnumerator KeepFinalImage(bool withLine)
        {
            var g = _g;
            if (g.Clock.TotalMinutes < Night3Rules.LogOffTime) g.Clock.Set(7, 0);
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.5f), 0.4f, false);
            yield return FinalImage(withLine);
        }

        /// <summary>The CAM 03 image of Night 1's ending: the figure behind the chair, and "you" turn to the camera.</summary>
        IEnumerator FinalImage(bool feedLine)
        {
            var g = _g;
            var rig = g.CameraRig;
            var cam = g.Apps.Find<CameraApp>();
            if (cam == null) cam = g.Apps.Launch(AppIds.Camera, null) as CameraApp;
            else cam.Window.Restore(null);
            cam?.Select(ContentIds.Cam03, null);
            if (rig == null) yield break;
            rig.Figure = FigureStage.BehindChair;
            rig.DoorOpen = 1f;
            rig.SeatedMimicsPlayer = false;
            rig.LightFlicker = 1f;
            if (feedLine) yield return Say(_ellen, Lines("n3_finale_feed"), 4f);
            float t = 0f;
            while (t < 3.2f)
            {
                t += Time.deltaTime;
                rig.SeatedHeadTurn = Mathf.SmoothStep(0f, 1f, t / 3.2f);
                rig.ExtraNoise = t / 3.2f * 0.5f;
                if (UnityEngine.Random.value < 0.05f) g.Fx.Glitch(0.05f, 0.6f);
                yield return null;
            }
            yield return Wait(1.2f);
            rig.ExtraNoise = 0f;
        }
    }
}
