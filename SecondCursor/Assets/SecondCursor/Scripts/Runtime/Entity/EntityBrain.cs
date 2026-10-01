using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Systemic (unscripted) behaviour: utility scoring over a small set of behaviours, evaluated whenever
    /// the entity is idle. Defends a protected file wherever it is and whatever the player tries: grabs
    /// the dragged file, races you to "No", drags dialogs out from under your cursor, cancels shreds,
    /// snatches the file away when you reach for it, closes windows. Scripted beats can disable it.
    /// How hard it defends (grip, reaction times, which tricks it uses and when) comes from the night's
    /// <see cref="DifficultyProfile"/> scaled by the <see cref="AdaptiveAssist"/>, which it tells about
    /// every defense that was not a tug-of-war.
    /// </summary>
    public sealed partial class EntityBrain
    {
        sealed class Behaviour
        {
            public string Name;
            public Func<float> Score;
            public Func<IEnumerator> Run;
            public float Cooldown;
            public float ReadyAt;
        }

        /// <summary>The idle behaviour: the brain keeps thinking while it runs (EntityController ticks it).</summary>
        public const string LurkName = "Lurk";
        const string InterceptName = "InterceptDrag";
        /// <summary>After it loses a tug it does not lunge again for this long (the player gets to the bin).</summary>
        const float ReGrabCooldown = 2f;
        /// <summary>A drag counts as heading for the bin when its direction is within about 37 degrees of it.</summary>
        public const float InterceptHeadingCos = 0.8f;
        const float HeadingMinSpeed = 80f;

        readonly GameServices _g;
        readonly EntityController _c;
        readonly List<Behaviour> _behaviours = new List<Behaviour>();
        float _thinkTimer;
        /// <summary>The shred progress it fought over Cancel for a whole patience and gave up on (no retry for that shred).</summary>
        object _cancelGaveUp;
        /// <summary>
        /// Phase K: the drag the player took from her in a tug. She does not grab it again until the player lets go ("YOU KEPT THE
        /// FILE" must stay true: the blind tester saw a win taken back 0.35 s later).
        /// </summary>
        DragPayload _wonByPlayer;

        public bool Enabled;
        /// <summary>The file it will not let the player destroy.</summary>
        public string ProtectedFileId = ContentIds.File017;
        /// <summary>How many times it has stopped the player (drives escalation).</summary>
        public int Defenses { get; private set; }
        /// <summary>Tugs the player has lost to it (only these make its grip grow).</summary>
        public int TugLosses { get; private set; }
        /// <summary>Allow lurking near the player's cursor when nothing else to do.</summary>
        public bool AllowIdleLurk = true;
        /// <summary>Allow snatching the desktop icon away when the player reaches for it.</summary>
        public bool AllowKeepAway = true;
        /// <summary>
        /// Phase P (T1): the Night 3 finale's LetGo: she still grabs 017 ("she can't stop her hand") but never races, guards, drags the dialog,
        /// cancels the shred or snatches the icon; she follows the file to the bin and rests on Yes (EntityBrain.LetGo.cs).
        /// </summary>
        public bool LetsGo;
        /// <summary>Grip multiplier from trust (1 except in the Night 3 finale: 0.9 or 1.1).</summary>
        public float TrustGripMult = 1f;
        /// <summary>
        /// Only grab a dragged protected file once it is this close to the Disposal bin (0 = wherever it is
        /// dragged). Night 2: she lets you carry 209 to Archive, never to the bin.
        /// </summary>
        public float InterceptRadius;
        /// <summary>
        /// A wider radius used while the drag heads for the bin (0 = none). Night 2 (Phase F): she lunges early for a
        /// drag aimed at the bin, so the fight happens with room to yank instead of in the bin's corner.
        /// </summary>
        public float InterceptRadiusHeading;
        /// <summary>During Custodial rounds: close a Camera Viewer that shows the figure (spec 7.4).</summary>
        public bool AllowCloseCamera;
        /// <summary>Her close click was blocked by another cursor (the story types CLOSE IT, at most every 20 s).</summary>
        public Action CloseCameraBlocked;

        /// <summary>Defenses that count toward the adaptive assist (a lost tug is reported by the conflict itself; weights in AdaptiveAssist.DefenseWeight).</summary>
        static readonly HashSet<string> AssistDefenses = new HashSet<string> { "no", "dialog", "guard", "cancel", "keepaway", "close" };
        static DifficultyProfile _fallbackProfile;

        DifficultyProfile Profile => _g.Difficulty ?? (_fallbackProfile ?? (_fallbackProfile = DifficultyTable.For(1, DifficultyMode.Normal)));
        AdaptiveAssist Assist => _g.Assist;

        public event Action<string> Defended;

        public EntityBrain(GameServices g, EntityController c)
        {
            _g = g;
            _c = c;
            Add(InterceptName, ScoreIntercept, RunIntercept, 1.0f);
            Add("CancelShred", ScoreCancelShred, RunCancelShred, 0.5f);
            Add("DragDialogAway", ScoreDragDialog, RunDragDialog, 2.5f);
            Add("GuardYes", ScoreGuardYes, RunGuardYes, 1.5f);
            Add("RaceToNo", ScoreRaceToNo, RunRaceToNo, 0.5f);
            Add("KeepAway", ScoreKeepAway, RunKeepAway, 5f);
            Add("CloseFilesWindow", ScoreCloseFiles, RunCloseFiles, 9f);
            Add("CloseCamera", ScoreCloseCamera, RunCloseCamera, 0.5f);
            Add(LurkName, ScoreLurk, RunLurk, 0.5f);
            AddLetGo();
        }

        void Add(string name, Func<float> score, Func<IEnumerator> run, float cooldown)
        {
            _behaviours.Add(new Behaviour { Name = name, Score = score, Run = run, Cooldown = cooldown });
        }

        public void RegisterDefense(string how)
        {
            Defenses++;
            if (how == "tug") TugLosses++;
            _c.Urgency = Profile.Urgency(Defenses);
            GameLog.Info(LogChannel.Entity, "Defended " + ProtectedFileId + " via " + how + " (#" + Defenses + ")");
            _g.Flags.Increment(Core.Story.Flags.CounterEntityWins);
            if (AssistDefenses.Contains(how)) Assist?.ReportDefense(how);
            Defended?.Invoke(how);
        }

        /// <summary>Tug-of-war grip strength, rising with each tug the player loses (eased by the assist).</summary>
        public float Grip => Profile.Grip(TugLosses, Assist, TrustGripMult);

        public void Tick(float dt)
        {
            _thinkTimer -= dt;
            if (_thinkTimer > 0f) return;
            _thinkTimer = 0.05f;

            // While lurking it keeps watching: only something that matters more than lurking interrupts it.
            bool lurking = _c.CurrentAction == LurkName;
            Behaviour best = null;
            float bestScore = lurking ? 1f : 0.01f;
            foreach (var b in _behaviours)
            {
                if (lurking && b.Name == LurkName) continue;
                if (Time.time < b.ReadyAt) continue;
                float s = b.Score();
                if (s > bestScore)
                {
                    bestScore = s;
                    best = b;
                }
            }
            if (best == null) return;
            best.ReadyAt = Time.time + best.Cooldown;
            _c.Run(best.Run(), best.Name);
        }

        /// <summary>Scripted override: re-evaluate immediately (e.g. right after a dialog opens).</summary>
        public void Poke() => _thinkTimer = 0f;

        /// <summary>Keep a behaviour from starting again for at least <paramref name="seconds"/> from now.</summary>
        void Delay(string name, float seconds)
        {
            foreach (var b in _behaviours)
                if (b.Name == name) b.ReadyAt = Mathf.Max(b.ReadyAt, Time.time + seconds);
        }

        // ------------------------------------------------------------------ helpers

        bool IsProtected(string fileId) => fileId != null && fileId == ProtectedFileId;
        CursorAgent Player => _g.Player;

        IEnumerator EnsurePresent(Vector2 from)
        {
            if (_c.IsVisible) yield break;
            _c.Teleport(from);
            yield return _c.Appear(null, 0.15f, true);
        }

        Vector2 EntryPointNear(Vector2 target)
        {
            // Enter from the nearest screen edge, like it came from outside the screen.
            float left = target.x, right = ScreenRig.Width - target.x, bottom = target.y, top = ScreenRig.Height - target.y;
            float m = Mathf.Min(Mathf.Min(left, right), Mathf.Min(bottom, top));
            if (m == left) return new Vector2(-8f, target.y);
            if (m == right) return new Vector2(ScreenRig.Width + 8f, target.y);
            if (m == bottom) return new Vector2(target.x, WindowManager.TaskbarHeight + 2f);
            return new Vector2(target.x, ScreenRig.Height + 8f);
        }

        /// <summary>
        /// A spot on BARE desktop (a drop there always lands the file on the desktop), preferably far from the
        /// player's cursor and the Disposal bin. Falls back to the least-bad spot if the screen is covered.
        /// </summary>
        Vector2 SafeSpot()
        {
            Vector2 disposal = _g.Desktop.DisposalIcon.Hit.Center;
            Vector2 best = new Vector2(480f, 400f);
            float bestScore = float.MinValue;
            for (float x = 110f; x <= ScreenRig.Width - 90f; x += 50f)
            {
                for (float y = WindowManager.TaskbarHeight + 70f; y <= ScreenRig.Height - 40f; y += 45f)
                {
                    var c = new Vector2(x, y);
                    float s = Vector2.Distance(c, Player.Position) + Vector2.Distance(c, disposal) * 0.7f + UnityEngine.Random.Range(0f, 30f);
                    if (!_c.IsBareDesktop(c)) s -= 2000f;
                    if (s > bestScore) { bestScore = s; best = c; }
                }
            }
            return best;
        }

        /// <summary>
        /// Phase K: where a file she won is set down: bare desktop about 90 px from the grab along her pull, clear of the Disposal
        /// bin, the player's pointer and the notices, so the next try starts where the last one did. Far away only as a fallback.
        /// </summary>
        Vector2 NearSpot(Vector2 grab, Vector2 away)
        {
            Vector2 target = grab + (away.sqrMagnitude > 0.1f ? away.normalized : Vector2.up) * 90f;
            Vector2 bin = _g.Desktop.DisposalIcon.Hit.Center;
            Vector2 best = Vector2.zero;
            float bestScore = float.MaxValue;
            for (float dx = -240f; dx <= 240f; dx += 30f)
            {
                for (float dy = -180f; dy <= 180f; dy += 30f)
                {
                    var c = ScreenRig.ClampToScreen(target + new Vector2(dx, dy));
                    if (Vector2.Distance(c, bin) < 110f || Vector2.Distance(c, Player.Position) < 60f || WindowManager.InToastColumn(c) || !_c.IsBareDesktop(c)) continue;
                    float score = Vector2.Distance(c, target);
                    if (score < bestScore) { bestScore = score; best = c; }
                }
            }
            return bestScore < float.MaxValue ? best : SafeSpot();
        }

        /// <summary>Make sure the protected file ends up visible on the desktop (a refused drop would leave it elsewhere).</summary>
        void EnsureFileOnDesktop(Vector2 spot)
        {
            if (!_g.Files.Exists(ProtectedFileId)) return;
            if (_g.Files.FolderOf(ProtectedFileId) != ContentIds.FolderDesktop)
                _g.Files.Move(ProtectedFileId, ContentIds.FolderDesktop, Core.FileSystem.Actor.Entity, SystemNotices.SessionOf(_g, _c.Agent));
            var icon = _g.Desktop.IconForFile(ProtectedFileId);
            var desk = OSLayers.WorldToDesktop(spot) - new Vector2(DesktopIcon.CellW * 0.5f, 18f);
            if (icon == null || !_c.IsBareDesktop(icon.Hit.Center)) _g.Desktop.SetFilePosition(ProtectedFileId, desk);
        }

        // ------------------------------------------------------------------ InterceptDrag

        float ScoreIntercept()
        {
            var p = Player.Payload;
            if (p == null || !IsProtected(p.FileId) || p.Contested || p.Holder != Player || p == _wonByPlayer) return 0f;
            if (InterceptRadius > 0f)
            {
                Vector2 bin = _g.Desktop.DisposalIcon.Hit.Center;
                float radius = InterceptRadius;
                if (InterceptRadiusHeading > radius && HeadsFor(bin)) radius = InterceptRadiusHeading;
                if (Vector2.Distance(p.GhostPosition + new Vector2(16f, -14f), bin) > radius) return 0f;
            }
            return 100f;
        }

        /// <summary>The player's cursor is moving toward <paramref name="point"/> (within the heading cone).</summary>
        bool HeadsFor(Vector2 point)
        {
            Vector2 v = Player.Velocity;
            Vector2 to = point - Player.Position;
            if (v.sqrMagnitude < HeadingMinSpeed * HeadingMinSpeed || to.sqrMagnitude < 1f) return false;
            return Vector2.Dot(v.normalized, to.normalized) > InterceptHeadingCos;
        }

        IEnumerator RunIntercept()
        {
            var payload = Player.Payload;
            if (payload == null) yield break;
            Func<Vector2?> ghost = () =>
            {
                if (payload.Holder == null || payload.Dropped) return null;
                return payload.GhostPosition + new Vector2(16f, -14f);
            };
            var g0 = ghost();
            if (!g0.HasValue) yield break;
            float noticed = Time.time;
            yield return EnsurePresent(EntryPointNear(g0.Value));
            // A human beat before the lunge (fading in counts toward it).
            float wait = Profile.InterceptDelay - (Time.time - noticed);
            if (wait > 0f) yield return Waits.Seconds(wait);
            if (!ghost().HasValue || payload.Holder != Player || payload.Contested) yield break;
            _c.State = Core.Entity.EntityState.Aggressive;
            yield return _c.MoveToDynamic(ghost, MovementProfiles.Aggressive, 30f);
            if (payload.Holder != Player || payload.Dropped || payload.Contested) yield break;
            // Seize it: pressing on the ghost starts the contest (ConflictSystem takes over movement).
            int lossesBefore = _g.Flags.Get(Core.Story.Flags.CounterTugLosses);
            _c.Agent.SetButton(true);
            yield return null;
            yield return null;
            if (payload.Contender != _c.Agent)
            {
                _c.Agent.SetButton(false);
                yield break;
            }
            while (_g.Conflict.IsFighting) yield return null;

            if (_g.Conflict.LastOutcome == TugOutcome.Released)
            {
                // Phase P (T1): the finale's hold was let go early: nobody won, nothing is held against anyone. She hovers by the file.
                _c.Agent.SetButton(false);
                yield return HoverAfterRelease();
                yield break;
            }
            if (payload.Holder == _c.Agent && !payload.Dropped)
            {
                // Won: she sets it down on bare desktop near where she grabbed it (Phase K: it used to be thrown to the far corner)
                // and it blinks. If the player grabs it again on the way, the fight resumes (ConflictSystem moves the cursor meanwhile).
                RegisterDefense("tug");
                Vector2 grab = _g.Conflict.LastGrabPoint, away = -_g.Conflict.PullDirection;
                Vector2 spot = NearSpot(grab, away);
                int guard = 0;
                while (payload.Holder == _c.Agent && !payload.Dropped)
                {
                    // A re-grab fight runs as long as it runs; only carrying attempts count toward the guard.
                    if (_g.Conflict.IsFighting) { yield return null; continue; }
                    if (++guard > 20) break;
                    spot = NearSpot(grab, away);
                    yield return _c.MoveToDynamic(() => _g.Conflict.IsFighting || payload.Holder != _c.Agent ? (Vector2?)null : spot, MovementProfiles.Aggressive, 40f);
                    if (_g.Conflict.IsFighting || payload.Holder != _c.Agent) continue;
                    yield return Waits.Seconds(0.1f);
                    // A re-grab in that last beat is a fight, not a drop: letting go now would hand the file over.
                    if (_g.Conflict.IsFighting || payload.Holder != _c.Agent) continue;
                    _c.Agent.SetButton(false);
                    yield return null;
                    yield return null;
                    EnsureFileOnDesktop(spot);
                    _g.Desktop.Attention(ProtectedFileId, 1.6f);
                    break;
                }
                if (_c.Agent.Held) _c.Agent.SetButton(false);
                // Every tug she won on the way counts (a re-grab she fought off is a lost tug too), like KeepAway.
                int extraLosses = _g.Flags.Get(Core.Story.Flags.CounterTugLosses) - lossesBefore - 1;
                for (int i = 0; i < extraLosses; i++) RegisterDefense("tug");
                if (payload.Holder == _c.Agent || payload.Dropped)
                {
                    yield return Waits.Seconds(0.4f);
                    _c.State = Core.Entity.EntityState.Defensive;
                    yield return _c.Loiter(spot, 50f, 1.2f, MovementProfiles.Hesitant);
                }
                else
                {
                    // The player took it back on the way: same as losing the tug.
                    _wonByPlayer = Player.Payload;
                    Delay(InterceptName, ReGrabCooldown);
                    yield return _c.Recoil(Player.Position);
                }
            }
            else
            {
                // Lost the tug: not this drag again, and a moment before she can lunge at the next one.
                _wonByPlayer = Player.Payload;
                Delay(InterceptName, ReGrabCooldown);
                _c.Agent.SetButton(false);
                // Phase P (T1): letting go, she does not recoil: she walks with the file (FollowFile, EntityBrain.LetGo.cs).
                if (LetsGo) yield break;
                yield return _c.Recoil(Player.Position);
                _c.State = Core.Entity.EntityState.Defensive;
            }
        }

        // ------------------------------------------------------------------ RaceToNo

        float ScoreRaceToNo()
        {
            if (LetsGo) return 0f;
            var box = _g.Shred.Confirm;
            if (box == null || !box.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            return 85f;
        }

        IEnumerator RunRaceToNo()
        {
            var box = _g.Shred.Confirm;
            if (box == null) yield break;
            var no = box.Button("No");
            if (no == null) yield break;
            yield return EnsurePresent(EntryPointNear(no.Hit.Center));
            _c.State = Core.Entity.EntityState.Aggressive;
            // A beat of reaction time: the player gets a real chance to click Yes first.
            yield return Waits.Seconds(Profile.RaceToNoDelay(UnityEngine.Random.value, Assist));
            var result = new bool[1];
            yield return _c.ClickElement(no.Hit, MovementProfiles.Aggressive, result, 1.5f);
            if (result[0] && box.Result == "No") RegisterDefense("no");
        }

        // ------------------------------------------------------------------ DragDialogAway

        float ScoreDragDialog()
        {
            if (LetsGo) return 0f;
            var box = _g.Shred.Confirm;
            if (box == null || !box.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            var yes = box.Button("Yes");
            if (yes == null) return 0f;
            float d = Vector2.Distance(Player.Position, yes.Hit.Center);
            // The closer the player gets to "Yes", the more it wants to pull the dialog away.
            return Profile.DragsDialog(Assist) && d < Profile.DragDialogRadius && Defenses >= 1 ? 95f : 0f;
        }

        IEnumerator RunDragDialog()
        {
            var box = _g.Shred.Confirm;
            if (box == null || !box.IsOpen) yield break;
            yield return EnsurePresent(EntryPointNear(box.Window.CaptionCenter));
            if (!box.IsOpen) yield break;
            _c.State = Core.Entity.EntityState.Aggressive;
            // Haul it toward the side of the screen away from the player's cursor.
            Vector2 caption = box.Window.CaptionCenter;
            Vector2 away = caption - Player.Position;
            if (away.sqrMagnitude < 1f) away = Vector2.right;
            Vector2 dest = caption + away.normalized * 260f;
            dest = new Vector2(Mathf.Clamp(dest.x, 80f, ScreenRig.Width - 80f), Mathf.Clamp(dest.y, 120f, ScreenRig.Height - 12f));
            yield return _c.DragWindow(box.Window, dest, MovementProfiles.Aggressive);
            if (box.IsOpen)
            {
                var no = box.Button("No");
                var result = new bool[1];
                if (no != null) yield return _c.ClickElement(no.Hit, MovementProfiles.Aggressive, result, 2f);
                if (result[0] && box.Result == "No") RegisterDefense("dialog");
            }
        }

        // ------------------------------------------------------------------ GuardYes

        float ScoreGuardYes()
        {
            if (LetsGo) return 0f;
            var box = _g.Shred.Confirm;
            if (box == null || !box.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            // Alternate between guarding and racing so it stays unpredictable.
            return Profile.GuardsYes(Defenses, Assist) ? 90f : 0f;
        }

        /// <summary>Park on "Yes" so the player's clicks bounce off; follow the button if the dialog is dragged.</summary>
        IEnumerator RunGuardYes()
        {
            var box = _g.Shred.Confirm;
            var yes = box?.Button("Yes");
            if (yes == null) yield break;
            yield return EnsurePresent(EntryPointNear(yes.Hit.Center));
            _c.State = Core.Entity.EntityState.Defensive;
            yield return _c.MoveToElement(yes.Hit, MovementProfiles.Aggressive);
            _c.Guarding = yes.Hit;
            float until = Time.time + UnityEngine.Random.Range(Profile.GuardYesHoldMin, Profile.GuardYesHoldMax);
            while (box.IsOpen && Time.time < until)
            {
                // Stay glued to the button; if the dialog is dragged away it scrambles after it.
                Vector2 target = yes.Hit.Center + new Vector2(UnityEngine.Random.Range(-2f, 2f), UnityEngine.Random.Range(-1f, 1f));
                if (Vector2.Distance(_c.Agent.Position, target) > 12f)
                {
                    _c.Guarding = null;
                    yield return _c.MoveToDynamic(() => box.IsOpen ? yes.Hit.Center : (Vector2?)null, MovementProfiles.Panicked, 20f);
                    _c.Guarding = yes.Hit;
                }
                else
                {
                    _c.Agent.Position = Vector2.Lerp(_c.Agent.Position, target, MathUtil.LerpAt60(0.2f, Time.deltaTime));
                }
                yield return null;
            }
            _c.Guarding = null;
            if (!box.IsOpen) yield break;
            var no = box.Button("No");
            var result = new bool[1];
            if (no != null) yield return _c.ClickElement(no.Hit, MovementProfiles.Aggressive, result, 2f);
            if (result[0] && box.Result == "No") RegisterDefense("guard");
        }

        // ------------------------------------------------------------------ CancelShred

        float ScoreCancelShred()
        {
            if (LetsGo) return 0f;
            var p = _g.Shred.Progress;
            if (p == null || !p.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            // Held off Cancel for a whole patience: this shred is the player's (Phase F, no endless retries).
            if (ReferenceEquals(p, _cancelGaveUp)) return 0f;
            return 100f;
        }

        IEnumerator RunCancelShred()
        {
            var p = _g.Shred.Progress;
            if (p == null) yield break;
            float noticed = Time.time;
            yield return EnsurePresent(EntryPointNear(p.CancelButton.Hit.Center));
            _c.State = Core.Entity.EntityState.Panicked;
            // A beat of reaction: a player who knows the trick gets to Cancel first.
            float wait = Profile.CancelDelay(UnityEngine.Random.value) - (Time.time - noticed);
            if (wait > 0f) yield return Waits.Seconds(wait);
            if (!p.IsOpen)
            {
                // The dialog went while she reacted: no panic left over for the next behaviour.
                _c.State = Core.Entity.EntityState.Defensive;
                yield break;
            }
            var result = new bool[1];
            // While fighting over Cancel the operation crawls - it is holding the process back.
            _g.Shred.SpeedMultiplier = Profile.CancelCrawl;
            float fightStart = Time.time;
            yield return _c.ClickElement(p.CancelButton.Hit, MovementProfiles.Panicked, result, Profile.CancelPatience);
            _g.Shred.SpeedMultiplier = 1f;
            if (result[0]) RegisterDefense("cancel");
            // Only a whole patience held off counts (a click that missed for another reason retries as before).
            else if (p.IsOpen && Time.time - fightStart >= Profile.CancelPatience)
            {
                _cancelGaveUp = p;
                GameLog.Info(LogChannel.Entity, "Gave up on Cancel");
            }
            _c.State = Core.Entity.EntityState.Defensive;
        }

        // ------------------------------------------------------------------ KeepAway

        float ScoreKeepAway()
        {
            if (LetsGo || !AllowKeepAway || !Profile.KeepsAway(Defenses) || Player.Payload != null || Player.Held) return 0f;
            var icon = _g.Desktop.IconForFile(ProtectedFileId);
            if (icon == null) return 0f;
            var r = icon.GlyphWorldRect;
            float d = Vector2.Distance(Player.Position, r.center);
            float approaching = Vector2.Dot(Player.Velocity, (r.center - Player.Position).normalized);
            if (d < 90f && approaching > 250f) return 70f;
            return 0f;
        }

        IEnumerator RunKeepAway()
        {
            var icon = _g.Desktop.IconForFile(ProtectedFileId);
            if (icon == null) yield break;
            yield return EnsurePresent(EntryPointNear(icon.Hit.Center));
            _c.State = Core.Entity.EntityState.Defensive;
            Vector2 spot = SafeSpot();
            icon = _g.Desktop.IconForFile(ProtectedFileId);
            if (icon == null || Player.Payload != null) yield break;
            Vector2 before = icon.TopLeft;
            int winsBefore = _g.Flags.Get(Core.Story.Flags.CounterPlayerWins);
            int lossesBefore = _g.Flags.Get(Core.Story.Flags.CounterTugLosses);
            yield return _c.DragTo(icon.Hit, () => spot, MovementProfiles.Aggressive, 0.02f);
            yield return null;
            // The player grabbed it on the way and a tug decided it (Phase F: she holds still for the fight).
            if (_g.Flags.Get(Core.Story.Flags.CounterPlayerWins) > winsBefore)
            {
                _wonByPlayer = Player.Payload;
                Delay(InterceptName, ReGrabCooldown);
                yield break;
            }
            if (_g.Flags.Get(Core.Story.Flags.CounterTugLosses) > lossesBefore)
            {
                RegisterDefense("tug");
                yield break;
            }
            icon = _g.Desktop.IconForFile(ProtectedFileId);
            bool playerHasIt = Player.Payload != null && IsProtected(Player.Payload.FileId);
            if (icon != null && !playerHasIt && Vector2.Distance(icon.TopLeft, before) > 20f)
            {
                // Phase K: the file blinks where she left it, so the player can follow where it went.
                _g.Desktop.Attention(ProtectedFileId, 1.6f);
                RegisterDefense("keepaway");
            }
        }

        // ------------------------------------------------------------------ CloseFilesWindow

        float ScoreCloseFiles()
        {
            if (Defenses < 1) return 0f;
            var files = _g.Apps.Find<FilesApp>();
            if (files == null || !files.IsOpen || files.Window.IsMinimized) return 0f;
            var row = files.RowFor(ProtectedFileId);
            if (row == null) return 0f;
            bool selected = files.SelectedFileId == ProtectedFileId;
            bool hovered = row.Hit.IsHoveredBy(Player);
            return selected && hovered ? 55f : 0f;
        }

        IEnumerator RunCloseFiles()
        {
            var files = _g.Apps.Find<FilesApp>();
            if (files == null || files.Window.CloseButton == null) yield break;
            yield return EnsurePresent(EntryPointNear(files.Window.CloseButton.Hit.Center));
            var result = new bool[1];
            yield return _c.ClickElement(files.Window.CloseButton.Hit, MovementProfiles.Aggressive, result, 2f);
            if (result[0]) RegisterDefense("close");
        }

        // ------------------------------------------------------------------ CloseCamera (Custodial rounds)

        float ScoreCloseCamera()
        {
            if (!AllowCloseCamera || _g.Rounds == null || !_g.Rounds.IsFigureOnShownCamera || _g.Rounds.ShownCameraSpared) return 0f;
            return 80f;
        }

        /// <summary>
        /// The viewer shows Custodial: after her reaction delay she clicks its close box. Blocked, she jostles
        /// and gives up after 2 s. Not a defense: it never raises her grip.
        /// </summary>
        IEnumerator RunCloseCamera()
        {
            var cam = _g.Apps.Find<CameraApp>();
            var close = cam != null ? cam.Window.CloseButton : null;
            if (close == null) yield break;
            yield return EnsurePresent(EntryPointNear(close.Hit.Center));
            _c.State = Core.Entity.EntityState.Panicked;
            yield return Waits.Seconds(_g.Rounds.CloseReaction());
            if (!_g.Rounds.IsFigureOnShownCamera || cam == null || !cam.IsOpen)
            {
                // The viewer was closed or switched first: she calms down again.
                _c.State = Core.Entity.EntityState.Observing;
                yield break;
            }
            // Another window over the close box is simply pushed aside (the viewer comes to the front).
            if (_g.Router.HitTest(close.Hit.Center, _c.Agent) != close.Hit && !_c.IsBlockedByOthers(close.Hit)) cam.Window.Focus(_c.Agent);
            var result = new bool[1];
            yield return _c.ClickElement(close.Hit, MovementProfiles.Panicked, result, 2f);
            if (!result[0] && cam.IsOpen && !cam.Window.IsMinimized) CloseCameraBlocked?.Invoke();
            _c.State = Core.Entity.EntityState.Observing;
        }

        // ------------------------------------------------------------------ Lurk

        float ScoreLurk()
        {
            if (!AllowIdleLurk || !_c.IsVisible) return 0f;
            return 1f;
        }

        IEnumerator RunLurk()
        {
            Vector2 target;
            var guardPoint = PathToBin();
            if (guardPoint.HasValue)
            {
                // Its file can be shredded: it hovers on the way between the file and the Disposal bin, so a grab
                // happens mid-path with room to yank, not in the bin's corner (Phase F).
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(60f, 120f);
                target = guardPoint.Value + offset;
                target.y = Mathf.Max(target.y, WindowManager.TaskbarHeight + 12f);
            }
            else
            {
                // Hover at a respectful distance from the player's cursor, watching.
                Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(120f, 190f);
                target = Player.Position + offset;
            }
            yield return _c.MoveTo(ScreenRig.ClampToScreen(target), MovementProfiles.Lurking, 40f);
            yield return Waits.Seconds(UnityEngine.Random.Range(0.4f, 1.4f));
        }

        /// <summary>The midpoint between its file's desktop icon and the Disposal bin, while the file can be shredded.</summary>
        Vector2? PathToBin()
        {
            if (string.IsNullOrEmpty(ProtectedFileId)) return null;
            var shred = _g.Shred;
            if (shred.IsInUse != null && shred.IsInUse(ProtectedFileId)) return null;
            var icon = _g.Desktop.IconForFile(ProtectedFileId);
            if (icon == null) return null;
            return (icon.Hit.Center + _g.Desktop.DisposalIcon.Hit.Center) * 0.5f;
        }
    }
}
