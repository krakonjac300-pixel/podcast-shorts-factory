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
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Shared story flow for every night: a list of beats run in order, jumps to any beat (with
    /// <see cref="Prepare"/> setting the world up as if the earlier beats had been played), background side
    /// routines, checkpoints, and the helpers beats are written with (waiting on tasks with hints, typing
    /// into Notepad as a cursor, exchanges, carrying files, watching the camera feed). Beats wait on what
    /// the PLAYER does, with timeouts so nothing can soft-lock. One subclass per night holds its beats.
    /// </summary>
    public abstract partial class NightDirector : MonoBehaviour
    {
        protected GameServices _g;
        Routine _flow;
        readonly List<Routine> _side = new List<Routine>();
        protected BootSequence _boot;
        protected EndingSequence _ending;
        readonly List<Speaker> _speakers = new List<Speaker>();
        /// <summary>Remote sessions whose first talk this night already came with the "type a reply" notice.</summary>
        readonly HashSet<Speaker> _hintedSpeakers = new HashSet<Speaker>();

        /// <summary>Every line the player sent in an exchange this night, in order (saved at the end of Night 1).</summary>
        protected readonly List<string> PlayerLines = new List<string>();
        /// <summary>The game clock (minutes) when each of <see cref="PlayerLines"/> was typed.</summary>
        protected readonly List<int> PlayerLineMinutes = new List<int>();
        /// <summary>When this night's director was built (play time per night).</summary>
        protected float NightStartedAt { get; private set; }

        /// <summary>This night's beats, in order. The last one is the ending.</summary>
        public abstract string[] Beats { get; }
        /// <summary>The night these beats belong to.</summary>
        public abstract int Night { get; }
        /// <summary>Beats whose start saves a checkpoint (Continue resumes there).</summary>
        protected virtual string[] CheckpointBeats => Array.Empty<string>();
        /// <summary>The night's default clock speed (game minutes per real second).</summary>
        protected virtual float ClockRate => 1f / 12f;

        public string CurrentBeat { get; private set; } = "";
        public float BeatStartedAt { get; private set; }

        /// <summary>
        /// True when this director stands in for a night whose own director does not exist yet (the debug
        /// panel can start Night 2 or 3 early to try their difficulty): nothing is saved.
        /// </summary>
        protected bool IsStandIn => _g.Night != Night;

        public static NightDirector Create(GameServices g, Transform parent, int night)
        {
            var go = new GameObject("Night Director");
            go.transform.SetParent(parent, false);
            NightDirector d;
            switch (night)
            {
                case 1:
                    d = go.AddComponent<Night1Director>();
                    break;
#if !SC_DEMO
                case 2:
                    d = go.AddComponent<Night2Director>();
                    break;
                case 3:
                    d = go.AddComponent<Night3Director>();
                    break;
#endif
                default:
                    // No such night: run Night 1's beats with that night's difficulty (nothing is saved).
                    GameLog.Warn(LogChannel.Story, "Night " + night + " has no director: running Night 1's beats (nothing is saved)");
                    d = go.AddComponent<Night1Director>();
                    break;
            }
            d._g = g;
            d.NightStartedAt = Time.time;
            d.Init();
            return d;
        }

        /// <summary>Hook up to the game's systems (called once, right after creation).</summary>
        protected virtual void Init()
        {
            var g = _g;
            g.Tasks.TaskCompleted += t => Sfx.Play("ui_select");
            g.Clock.Rate = ClockRate;
            g.DragDrop.PayloadFinished += OnDropMissed;
            g.Orders.Decided += OnChoiceDecided;
            _intro = new IntroTips(g, () => IsPreparing);
        }

        IntroTips _intro;

        /// <summary>
        /// Phase J (third blind playtest): a file the player let go of somewhere that did not take it flies back; say where it
        /// is, and bring a desktop icon that flew back under a window out into view (the tester thought the file was gone).
        /// </summary>
        void OnDropMissed(DragPayload p, bool accepted, CursorAgent by)
        {
            if (accepted || by == null || !by.IsPlayer || p.Kind != PayloadKind.File || (p.GhostPosition - p.Origin).sqrMagnitude < 40f * 40f) return;
            var g = _g;
            string folder = g.Files.FolderOf(p.FileId);
            if (folder == null) return;
            bool desktop = folder == ContentIds.FolderDesktop;
            string text = desktop ? g.Content.Format("drop.missed.desktop", p.Label) : g.Content.Format("drop.missed", p.Label, g.Files.GetFolder(folder)?.Name ?? folder);
            g.Notifications.Show(g.Content.Text("os.name"), text, "icon_info", null, "ui_select");
            GameLog.Info(LogChannel.OS, "Drop missed: " + p.Label + " back in " + folder);
            if (desktop) RunSide(BringIntoView(p.FileId, p.GhostPosition + new Vector2(16f, -16f)), "drop-missed");
        }

        /// <summary>
        /// A desktop file that something covers is moved out onto the nearest bare desktop from <paramref name="preferred"/> and
        /// blinks. Nothing moves while the player carries something, a shred runs or a tug is on.
        /// </summary>
        protected IEnumerator BringIntoView(string fileId, Vector2 preferred)
        {
            var g = _g;
            bool Busy() => g.Player.Payload != null || g.Shred.Busy || g.Conflict.IsFighting;
            var icon = g.Desktop.IconForFile(fileId);
            if (icon == null || Busy() || g.Router.HitTest(icon.Hit.Center) == icon.Hit) yield break;
            yield return FindDropSpot(preferred);
            if (g.Desktop.IconForFile(fileId) == null || Busy()) yield break;
            g.Desktop.SetFilePosition(fileId, OSLayers.WorldToDesktop(_dropSpot) - new Vector2(37f, 16f));
            g.Desktop.Attention(fileId, 1.6f);
            g.Audio.Play("mouse_release", 0.4f, 0.8f, Audio.AudioManager.PanFor(_dropSpot.x));
            GameLog.Info(LogChannel.Story, fileId + " moved into view");
        }

        /// <summary>Log Off from the NEXUS menu. By default the workstation refuses, like Shut Down.</summary>
        public virtual void RequestLogOff(CursorAgent a)
        {
            var c = _g.Content;
            Dialogs.Message(_g, c.Text("shutdown.denied.title"), c.Text("shutdown.denied.body"), "icon_warning", new[] { "OK" }, null);
        }

        // ------------------------------------------------------------------ flow control

        /// <summary>Run a background story routine alongside the current beat (stopped on jumps).</summary>
        protected Routine RunSide(IEnumerator routine, string name)
        {
            var r = new Routine(routine, name);
            r.Tick(Time.time);
            if (!r.Done) _side.Add(r);
            return r;
        }

        /// <summary>
        /// Start the night from its first beat. The start itself is recorded by the boot beat
        /// (<see cref="MarkNightStarted"/>), after the title menu, so launching the game never changes the save.
        /// </summary>
        public void Begin()
        {
            JumpTo(Beats[0]);
        }

        public bool HasBeat(string beat) => beat != null && Array.IndexOf(Beats, beat) >= 0;

        public void JumpTo(string beat)
        {
            int index = Array.IndexOf(Beats, beat);
            if (index < 0) return;
            if (_flow != null) _flow.Stop(); // stops the whole chain, including nested beat coroutines
            foreach (var r in _side) r.Stop();
            _side.Clear();
            CleanUpForJump();
            _flow = new Routine(Flow(index), "story");
            _flow.Tick(Time.time);
        }

        protected virtual void Update()
        {
            _intro.Tick();
            for (int i = _side.Count - 1; i >= 0; i--)
            {
                _side[i].Tick(Time.time);
                if (_side[i].Done) _side.RemoveAt(i);
            }
            if (_flow == null) return;
            _flow.Tick(Time.time);
            if (_flow.Done) _flow = null;
        }

        /// <summary>Undo whatever a half-finished beat left on screen before starting another one.</summary>
        protected virtual void CleanUpForJump()
        {
            var g = _g;
            g.Entity.Interrupt();
            g.Entity.Brain.Enabled = false;
            g.Entity.Brain.AllowCloseCamera = false;
            g.Entity.Brain.InterceptRadius = 0f;
            g.Entity.Brain.InterceptRadiusHeading = 0f;
            g.Entity.Urgency = 1f;
            g.Gary?.Interrupt();
            g.Rounds?.Stop();
            _boot?.Clear();
            _boot = null;
            _ending?.Clear();
            _ending = null;
            g.Player.ShapeOverride = null;
            g.Player.Enabled = true;
            g.Player.Visible = true;
            g.Audio.StopLoop("drone_tension", 0.3f);
            // Phase M: nothing a half-finished scare or climax left behind (an in-place jump out of a dip used to leave the room silent).
            g.Scares.CancelAll();
            EndClimax();
            if (g.Flags.Has(Flags.LoggedIn)) g.Audio.SetAmbience(true, 0.5f);
            g.Shred.Abort();
            g.Shred.SpeedMultiplier = 1f;
            if (g.Fx.IsPoweredOff) g.Fx.PowerOn();
            g.Fx.SetBlack(false);
            g.Clock.Rate = ClockRate;
            foreach (var s in _speakers)
            {
                s.Typing = false;
                if (s.Pad == null || !s.Pad.IsOpen) continue;
                s.Pad.ConversationMode = false;
                s.Pad.PlayerCanType = true;
            }
        }

        /// <summary>
        /// Stops every side routine and frees every speaker (an ending must not be typed over by a routine of the
        /// beat before it, which would reopen a Notepad in the dark).
        /// </summary>
        protected void StopSideRoutines()
        {
            foreach (var r in _side) r.Stop();
            _side.Clear();
            foreach (var s in _speakers) s.Typing = false;
        }

        public void SkipBeat()
        {
            int index = Array.IndexOf(Beats, CurrentBeat);
            if (index >= 0 && index < Beats.Length - 1) JumpTo(Beats[index + 1]);
        }

        IEnumerator Flow(int start)
        {
            var beats = Beats;
            if (start > 0)
            {
                // Nothing Prepare sets up (flags, decisions, files) may unlock an achievement.
                IsPreparing = true;
                try { Prepare(start); }
                finally { IsPreparing = false; }
            }
            for (int i = start; i < beats.Length; i++)
            {
                CurrentBeat = beats[i];
                BeatStartedAt = Time.time;
                GameLog.Info(LogChannel.Story, "Beat: " + CurrentBeat);
                if (Array.IndexOf(CheckpointBeats, CurrentBeat) >= 0)
                {
                    ApplyPendingDifficulty();
                    SaveCheckpoint(CurrentBeat);
                }
                yield return RunBeat(beats[i]);
            }
        }

        protected abstract IEnumerator RunBeat(string beat);

        /// <summary>Put the world in the state a beat expects when jumping straight to it (debug jump or Continue).</summary>
        protected abstract void Prepare(int beatIndex);

        // ------------------------------------------------------------------ saving

        void SaveCheckpoint(string beat)
        {
            if (!IsStandIn) SaveSystem.SaveCheckpoint(_g, beat);
        }

        /// <summary>The night is over: record its memory (<see cref="RecordNightMemory"/>) and progress.</summary>
        protected void CompleteNight(string endingId)
        {
            if (IsStandIn)
            {
                GameLog.Info(LogChannel.System, "Night " + _g.Night + " stand-in finished: not saved");
                return;
            }
            RecordNightMemory();
            SaveSystem.RecordNightComplete(_g, endingId, PlayerLines, NightElapsed, PlayerLineMinutes);
            _g.AchievementWatch?.OnNightComplete(Night, endingId);
        }

        /// <summary>Write this night's cross-night memory ("m." flags and counters) before it is saved.</summary>
        protected virtual void RecordNightMemory() { }

        // ------------------------------------------------------------------ helpers

        protected static IEnumerator Wait(float seconds)
        {
            yield return Waits.Seconds(seconds);
        }

        protected static IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float end = Time.time + timeout;
            while (!condition() && Time.time < end) yield return null;
        }

        /// <summary>
        /// Waits for a task. After <paramref name="hintAfter"/> seconds (default: the difficulty's first hint)
        /// its hint pops up as a toast, and again at the difficulty's repeat interval while the player is
        /// still stuck. Story difficulty caps every first hint at its own (short) delay. It gives up after the hint and the
        /// difficulty's patience, and never before <paramref name="minPatience"/> seconds.
        /// </summary>
        protected IEnumerator WaitTask(string taskId, float hintAfter = -1f, Action onTimeout = null, float minPatience = 0f)
        {
            var d = _g.Difficulty;
            if (hintAfter < 0f) hintAfter = d.TaskHintFirst;
            else if (d.Mode == DifficultyMode.Story) hintAfter = Mathf.Min(hintAfter, d.TaskHintFirst);
            float start = Time.time;
            float nextHint = start + hintAfter;
            while (!_g.Tasks.IsCompleted(taskId) && !_g.Tasks.IsWithdrawn(taskId))
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
                if (Time.time - start > Mathf.Max(hintAfter + d.TaskForceAfterHint, minPatience))
                {
                    // Phase L: an order the player may decide either way lapses instead of being completed for them.
                    if (onTimeout != null) onTimeout();
                    else
                    {
                        GameLog.Warn(LogChannel.Story, "Task " + taskId + " force-completed after timeout");
                        FileTheRest(taskId);
                    }
                    break;
                }
                if (Time.time > nextHint)
                {
                    nextHint = Time.time + d.TaskHintRepeat;
                    ShowTaskHint(taskId);
                }
                yield return null;
            }
            yield return Wait(0.8f);
        }

        /// <summary>
        /// Phase K: a task the story finishes itself (a safety net) moves its remaining files where they belong and says so, so
        /// the Work Queue never shows a finished batch while one of its files is still in Intake (an "archive mismatch").
        /// </summary>
        protected void FileTheRest(string taskId)
        {
            var t = _g.Tasks.Get(taskId);
            if (t != null && t.Type == TaskType.MoveFile && t.State == Core.Tasks.TaskState.Active)
            {
                var moved = new List<string>();
                foreach (var id in t.Data.targets)
                    if (_g.Files.Exists(id) && _g.Files.FolderOf(id) != t.Data.param && _g.Files.Move(id, t.Data.param, Actor.System)) moved.Add(_g.Files.GetFile(id).Name);
                if (moved.Count > 0)
                    _g.Notifications.Show(_g.Content.Text("app.workqueue"), _g.Content.Format("task.filed.rest", t.Title, string.Join(", ", moved),
                        _g.Files.GetFolder(t.Data.param)?.Name ?? t.Data.param), "icon_task_done", a => _g.Apps.Launch(AppIds.WorkQueue, a), "ui_select");
            }
            _g.Tasks.ForceComplete(taskId);
        }

        /// <summary>
        /// The Work Queue's hint toast for a task (clicking it opens the Work Queue). It goes away by itself once the task
        /// is done or withdrawn, so a hint never outlives its task (Phase H). Phase I: never while a shred dialog is open
        /// ("Drag it onto the bin" over "Shredding... Do not interrupt" read as if the shred had failed): it waits for the
        /// dialog to go, and a hint that is showing when the bar starts goes away.
        /// </summary>
        protected internal void ShowTaskHint(string taskId)
        {
            var t = _g.Tasks.Get(taskId);
            if (t == null || string.IsNullOrEmpty(t.Hint) || t.State != Core.Tasks.TaskState.Active) return;
            if (_g.Shred.Busy)
            {
                RunSide(ShowTaskHintWhenIdle(taskId), "hint-later:" + taskId);
                return;
            }
            var tasks = _g.Tasks;
            bool shred = t.Type == TaskType.DeleteFile;
            _g.Notifications.Show(_g.Content.Text("app.workqueue"), t.Hint, "icon_info", a => _g.Apps.Launch(AppIds.WorkQueue, a), "ui_select",
                false, () => tasks.IsActive(taskId) && !(shred && _g.Shred.Progress != null && _g.Shred.Progress.IsOpen));
        }

        IEnumerator ShowTaskHintWhenIdle(string taskId)
        {
            float end = Time.time + 20f;
            while (_g.Shred.Busy && Time.time < end) yield return null;
            if (!_g.Shred.Busy) ShowTaskHint(taskId);
        }

        /// <summary>
        /// The second cursor writes a task into the Work Queue (entity-authored): it appears in her colours
        /// and the OS says a remote session changed the queue.
        /// </summary>
        protected void GiveEntityTask(string taskId)
        {
            var t = _g.Tasks.Get(taskId);
            if (t == null || t.State != Core.Tasks.TaskState.Hidden) return;
            _g.Tasks.Activate(taskId);
            if (!_g.Tasks.IsCompleted(taskId))
                _g.Notifications.Show(_g.Content.Text("app.workqueue"), _g.Content.Text("notify.queue.remote") + "\n" + t.Title, "icon_task_active",
                    a => _g.Apps.Launch(AppIds.WorkQueue, a), "sys_warning");
        }

        protected void GiveTask(string taskId)
        {
            _g.Tasks.Activate(taskId);
            if (!_g.Tasks.IsCompleted(taskId))
                _g.Notifications.Show(_g.Content.Text("app.workqueue"), _g.Tasks.Get(taskId)?.Title ?? "", "icon_task_active",
                    a => _g.Apps.Launch(AppIds.WorkQueue, a), "notify_task");
        }

        protected void PhantomClick(Vector2 at)
        {
            _g.Audio.Play("mouse_click", 0.85f, UnityEngine.Random.Range(0.96f, 1.04f), Audio.AudioManager.PanFor(at.x));
        }

        /// <summary>One of story.json's dry system notes as an OS toast ("Session 017 is still open.").</summary>
        protected void ShowNote(int index)
        {
            var notes = _g.Content.Story.anomalyNotes;
            if (notes != null && index >= 0 && index < notes.Length)
                _g.Notifications.Show(_g.Content.Text("os.name"), notes[index], "icon_info", null, "ui_select");
        }

        protected IEnumerator Note(int index, float delay)
        {
            yield return Wait(delay);
            ShowNote(index);
        }

        /// <summary>
        /// Raise the clock's speed until it shows at least h:mm, then restore it (a night's time jumps stay
        /// visible instead of snapping).
        /// </summary>
        protected IEnumerator EnsureClockAtLeast(int hour24, int minute, float overSeconds)
        {
            var clock = _g.Clock;
            double target = hour24 * 60 + minute;
            double missing = target - clock.ExactMinutes;
            if (missing <= 0) yield break;
            float rate = clock.Rate;
            bool frozen = clock.Frozen;
            clock.Frozen = false;
            clock.Rate = (float)(missing / Mathf.Max(0.1f, overSeconds));
            float end = Time.time + overSeconds + 2f;
            while (clock.ExactMinutes < target && Time.time < end) yield return null;
            clock.Set(hour24, minute);
            clock.Rate = rate;
            clock.Frozen = frozen;
        }

        // ------------------------------------------------------------------ cursors doing things

        /// <summary>
        /// A cursor carries a file (from wherever it is, out of nowhere) to <paramref name="spot"/> and lets go.
        /// Whatever was under the cursor, the file ends up visibly on the desktop where it was dropped.
        /// </summary>
        protected IEnumerator CarryFileIn(EntityController c, string fileId, Vector2 spot, MovementProfileData profile, float targetSize = 40f)
        {
            bool carried = false;
            if (_g.Files.Exists(fileId) && _g.Files.FolderOf(fileId) != ContentIds.FolderDesktop)
            {
                var file = _g.Files.GetFile(fileId);
                c.Agent.SetButton(true);
                yield return null;
                _g.DragDrop.BeginFileDrag(c.Agent, file.Id, file.Name, FileIcons.SpriteFor(file), null, c.Agent.Position + new Vector2(-16f, 16f));
                carried = true;
            }
            // A grab on the way is a tug (the conflict moves the cursor); she carries on only if she kept the file.
            yield return carried ? c.CarryTo(spot, profile, targetSize) : c.MoveTo(spot, profile, targetSize);
            if (!carried) yield break;
            yield return Wait(0.3f);
            // Letting go during a tug would hand the file over: wait the fight out first.
            while (_g.Conflict.IsFighting) yield return null;
            bool hers = c.Agent.Held && c.Agent.Payload != null && c.Agent.Payload.FileId == fileId;
            c.Agent.SetButton(false);
            yield return null;
            yield return null;
            bool playerHasIt = _g.Player.Payload != null && _g.Player.Payload.FileId == fileId;
            if (_g.Files.Exists(fileId) && !playerHasIt && !_g.Shred.Busy)
            {
                bool moved = _g.Files.FolderOf(fileId) != ContentIds.FolderDesktop;
                if (moved) _g.Files.Move(fileId, ContentIds.FolderDesktop, Actor.Entity, SystemNotices.SessionOf(_g, c.Agent));
                if (hers || moved) _g.Desktop.SetFilePosition(fileId, OSLayers.WorldToDesktop(spot) - new Vector2(37f, 16f));
            }
        }

        /// <summary>
        /// A cursor parks on an element so the player's presses bounce off it, following it if its window is
        /// dragged, for up to <paramref name="seconds"/> (or until <paramref name="until"/> is true).
        /// </summary>
        protected IEnumerator GuardElement(EntityController c, Interactable element, float seconds, Func<bool> until = null)
        {
            if (element == null) yield break;
            yield return c.MoveToElement(element, MovementProfiles.Aggressive);
            c.Guarding = element;
            float end = Time.time + seconds;
            while (element != null && element.isActiveAndEnabled && Time.time < end && (until == null || !until()))
            {
                Vector2 target = element.Center;
                if (Vector2.Distance(c.Agent.Position, target) > 12f)
                {
                    c.Guarding = null;
                    yield return c.MoveToDynamic(() => element != null && element.isActiveAndEnabled ? element.Center : (Vector2?)null, MovementProfiles.Panicked, 20f);
                    c.Guarding = element;
                }
                else
                {
                    c.Agent.Position = Vector2.Lerp(c.Agent.Position, target, 0.2f);
                }
                yield return null;
            }
            c.Guarding = null;
        }

        /// <summary>After a reaction delay a cursor goes for an element and clicks it, unless something covers it.</summary>
        protected IEnumerator RaceTo(EntityController c, Interactable element, float reactionDelay, bool[] result = null, float patience = 1.5f)
        {
            if (result != null && result.Length > 0) result[0] = false;
            if (element == null) yield break;
            yield return Wait(reactionDelay);
            yield return c.ClickElement(element, MovementProfiles.Aggressive, result, patience);
        }

        /// <summary>
        /// Where a cursor lets go of a file so the player sees the icon arrive: the preferred spot if its whole
        /// icon cell is visible desktop, else the most visible cell on screen (ties go to the spot nearest the
        /// preferred one). Toasts count as visible (they are gone in seconds), except in the notices' column. Result in
        /// <see cref="_dropSpot"/>, set only when the search ends (two searches can run at once: each caller reads its own).
        /// </summary>
        protected Vector2 _dropSpot;
        readonly List<Interactable> _probeHits = new List<Interactable>();

        protected IEnumerator FindDropSpot(Vector2 preferred)
        {
            Vector2 best = preferred;
            if (VisibleProbes(preferred) == DropCellProbes.Length)
            {
                _dropSpot = best;
                yield break;
            }
            // Nearest candidates first; the first fully visible cell wins. Spread over frames so the
            // search never hitches (each probe hit-tests every interactable).
            var candidates = new List<Vector2>();
            for (float y = 440f; y > 120f; y -= 24f)
                for (float x = 840f; x > 100f; x -= 24f)
                    candidates.Add(new Vector2(x, y));
            candidates.Sort((a, b) => (a - preferred).sqrMagnitude.CompareTo((b - preferred).sqrMagnitude));
            int bestScore = -1;
            float frameStart = Time.realtimeSinceStartup;
            for (int i = 0; i < candidates.Count; i++)
            {
                int score = VisibleProbes(candidates[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidates[i];
                    if (score == DropCellProbes.Length) break;
                }
                // About 2 ms of hit-testing per frame, however crowded the desktop is.
                if (Time.realtimeSinceStartup - frameStart > 0.002f)
                {
                    yield return null;
                    frameStart = Time.realtimeSinceStartup;
                }
            }
            _dropSpot = best;
        }

        /// <summary>
        /// How many points of the dropped icon's cell (74 px wide, 16 px above the tip down to its label) are
        /// bare desktop. The notices' column counts as covered (Phase J: a notice sat on the file a new task pointed at);
        /// a toast anywhere else is ignored: it is gone in seconds.
        /// </summary>
        int VisibleProbes(Vector2 tip)
        {
            int n = 0;
            foreach (var o in DropCellProbes)
            {
                Vector2 p = tip + o;
                if (p.x < 8f || p.x > ScreenRig.Width - 60f || p.y < WindowManager.TaskbarHeight + 8f || p.y > ScreenRig.Height - 8f) continue;
                if (WindowManager.InToastColumn(p)) continue;
                Interactable hit = null;
                foreach (var h in _g.Router.HitTestAll(p, _probeHits))
                    if (!h.elementId.StartsWith("toast:", StringComparison.Ordinal)) { hit = h; break; }
                if (hit == _g.Desktop.Background) n++;
            }
            return n;
        }

        static readonly Vector2[] DropCellProbes =
        {
            new Vector2(0f, -10f), new Vector2(-35f, 14f), new Vector2(35f, 14f), new Vector2(-35f, -34f), new Vector2(35f, -34f), new Vector2(0f, -34f),
        };

        // ------------------------------------------------------------------ the camera feed

        /// <summary>Waits until the player has watched a camera (default CAM 03) for <paramref name="watchSeconds"/> (or timeout).</summary>
        protected IEnumerator WaitWatching(float watchSeconds, float timeout, string cameraId = ContentIds.Cam03)
        {
            float watched = 0f, end = Time.time + timeout;
            while (watched < watchSeconds && Time.time < end)
            {
                var cam = _g.Apps.Find<CameraApp>();
                if (cam != null && !cam.Window.IsMinimized && cam.CurrentCamera == cameraId) watched += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>The change happens at once under heavy static, which then clears over <paramref name="seconds"/>.</summary>
        protected IEnumerator StaticResolve(Action change, float seconds)
        {
            var rig = _g.CameraRig;
            _g.Audio.Play("static_burst", 0.9f);
            rig.ExtraNoise = 0.95f;
            change();
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                rig.ExtraNoise = Mathf.Lerp(0.95f, 0f, t / seconds);
                yield return null;
            }
            rig.ExtraNoise = 0f;
        }

        /// <summary>A burst of feed static that hides a change in the scene (things only move when you blink).</summary>
        protected IEnumerator StaticCut(Action change)
        {
            var rig = _g.CameraRig;
            _g.Audio.Play("static_burst", 0.9f);
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
    }
}
