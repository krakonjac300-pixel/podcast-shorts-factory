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
    /// </summary>
    public sealed class EntityBrain
    {
        sealed class Behaviour
        {
            public string Name;
            public Func<float> Score;
            public Func<IEnumerator> Run;
            public float Cooldown;
            public float ReadyAt;
        }

        readonly GameServices _g;
        readonly EntityController _c;
        readonly List<Behaviour> _behaviours = new List<Behaviour>();
        float _thinkTimer;

        public bool Enabled;
        /// <summary>The file it will not let the player destroy.</summary>
        public string ProtectedFileId = ContentIds.File017;
        /// <summary>How many times it has stopped the player (drives escalation).</summary>
        public int Defenses { get; private set; }
        /// <summary>Allow lurking near the player's cursor when nothing else to do.</summary>
        public bool AllowIdleLurk = true;
        /// <summary>Allow snatching the desktop icon away when the player reaches for it.</summary>
        public bool AllowKeepAway = true;

        public event Action<string> Defended;

        public EntityBrain(GameServices g, EntityController c)
        {
            _g = g;
            _c = c;
            Add("InterceptDrag", ScoreIntercept, RunIntercept, 1.0f);
            Add("CancelShred", ScoreCancelShred, RunCancelShred, 0.5f);
            Add("DragDialogAway", ScoreDragDialog, RunDragDialog, 2.5f);
            Add("RaceToNo", ScoreRaceToNo, RunRaceToNo, 0.5f);
            Add("KeepAway", ScoreKeepAway, RunKeepAway, 5f);
            Add("CloseFilesWindow", ScoreCloseFiles, RunCloseFiles, 9f);
            Add("Lurk", ScoreLurk, RunLurk, 0.5f);
        }

        void Add(string name, Func<float> score, Func<IEnumerator> run, float cooldown)
        {
            _behaviours.Add(new Behaviour { Name = name, Score = score, Run = run, Cooldown = cooldown });
        }

        public void RegisterDefense(string how)
        {
            Defenses++;
            _c.Urgency = Mathf.Min(2.2f, 1f + Defenses * 0.15f);
            GameLog.Info(LogChannel.Entity, "Defended " + ProtectedFileId + " via " + how + " (#" + Defenses + ")");
            _g.Flags.Increment(Core.Story.Flags.CounterEntityWins);
            Defended?.Invoke(how);
        }

        /// <summary>Tug-of-war grip strength, rising as the player keeps trying.</summary>
        public float Grip => Mathf.Min(1.35f, _c.Personality.grip * (1f + Defenses * 0.12f));

        public void Tick(float dt)
        {
            _thinkTimer -= dt;
            if (_thinkTimer > 0f) return;
            _thinkTimer = 0.05f;

            Behaviour best = null;
            float bestScore = 0.01f;
            foreach (var b in _behaviours)
            {
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

        /// <summary>A spot far from the player and from the Disposal bin to hide the file.</summary>
        Vector2 SafeSpot()
        {
            var candidates = new[]
            {
                new Vector2(560f, 470f), new Vector2(420f, 480f), new Vector2(700f, 420f), new Vector2(300f, 440f),
                new Vector2(620f, 300f), new Vector2(200f, 470f), new Vector2(480f, 380f),
            };
            Vector2 disposal = _g.Desktop.DisposalIcon.Hit.Center;
            Vector2 best = candidates[0];
            float bestScore = float.MinValue;
            foreach (var c in candidates)
            {
                float s = Vector2.Distance(c, Player.Position) + Vector2.Distance(c, disposal) * 0.7f + UnityEngine.Random.Range(0f, 40f);
                // Avoid dropping onto windows (the file must land on the desktop).
                if (_g.Windows.TopmostAt(c) != null) s -= 400f;
                if (s > bestScore) { bestScore = s; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------------ InterceptDrag

        float ScoreIntercept()
        {
            var p = Player.Payload;
            if (p == null || !IsProtected(p.FileId) || p.Contested || p.Holder != Player) return 0f;
            return 100f;
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
            yield return EnsurePresent(EntryPointNear(g0.Value));
            _c.State = Core.Entity.EntityState.Aggressive;
            yield return _c.MoveToDynamic(ghost, MovementProfiles.Aggressive, 30f);
            if (payload.Holder != Player || payload.Dropped || payload.Contested) yield break;
            // Seize it: pressing on the ghost starts the contest (ConflictSystem takes over movement).
            _c.Agent.SetButton(true);
            yield return null;
            yield return null;
            if (payload.Contender != _c.Agent)
            {
                _c.Agent.SetButton(false);
                yield break;
            }
            while (_g.Conflict.IsFighting) yield return null;

            if (payload.Holder == _c.Agent && !payload.Dropped)
            {
                // Won: run off with it and leave it somewhere safe on the desktop.
                RegisterDefense("tug");
                Vector2 spot = SafeSpot();
                yield return _c.MoveTo(spot, MovementProfiles.Aggressive, 40f);
                yield return new WaitForSeconds(0.1f);
                _c.Agent.SetButton(false);
                yield return null;
                yield return new WaitForSeconds(0.4f);
                _c.State = Core.Entity.EntityState.Defensive;
                yield return _c.Loiter(spot, 50f, 1.2f, MovementProfiles.Hesitant);
            }
            else
            {
                _c.Agent.SetButton(false);
                yield return _c.Recoil(Player.Position);
                _c.State = Core.Entity.EntityState.Defensive;
            }
        }

        // ------------------------------------------------------------------ RaceToNo

        float ScoreRaceToNo()
        {
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
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.15f, 0.35f) * _c.Personality.reactionScale);
            var result = new bool[1];
            yield return _c.ClickElement(no.Hit, MovementProfiles.Aggressive, result, 1.5f);
            if (result[0] && box.Result == "No") RegisterDefense("no");
        }

        // ------------------------------------------------------------------ DragDialogAway

        float ScoreDragDialog()
        {
            var box = _g.Shred.Confirm;
            if (box == null || !box.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            var yes = box.Button("Yes");
            if (yes == null) return 0f;
            float d = Vector2.Distance(Player.Position, yes.Hit.Center);
            // The closer the player gets to "Yes", the more it wants to pull the dialog away.
            return d < 70f && Defenses >= 1 ? 95f : 0f;
        }

        IEnumerator RunDragDialog()
        {
            var box = _g.Shred.Confirm;
            if (box == null || !box.IsOpen) yield break;
            yield return EnsurePresent(EntryPointNear(box.Window.CaptionCenter));
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

        // ------------------------------------------------------------------ CancelShred

        float ScoreCancelShred()
        {
            var p = _g.Shred.Progress;
            if (p == null || !p.IsOpen || !IsProtected(_g.Shred.PendingFileId)) return 0f;
            return 100f;
        }

        IEnumerator RunCancelShred()
        {
            var p = _g.Shred.Progress;
            if (p == null) yield break;
            yield return EnsurePresent(EntryPointNear(p.CancelButton.Hit.Center));
            _c.State = Core.Entity.EntityState.Panicked;
            var result = new bool[1];
            // While fighting over Cancel the operation crawls - it is holding the process back.
            _g.Shred.SpeedMultiplier = 0.35f;
            yield return _c.ClickElement(p.CancelButton.Hit, MovementProfiles.Panicked, result, 6f);
            _g.Shred.SpeedMultiplier = 1f;
            if (result[0]) RegisterDefense("cancel");
            _c.State = Core.Entity.EntityState.Defensive;
        }

        // ------------------------------------------------------------------ KeepAway

        float ScoreKeepAway()
        {
            if (!AllowKeepAway || Defenses < 2 || Player.Payload != null || Player.Held) return 0f;
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
            yield return _c.DragTo(icon.Hit, () => spot, MovementProfiles.Aggressive, 0.02f);
            RegisterDefense("keepaway");
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

        // ------------------------------------------------------------------ Lurk

        float ScoreLurk()
        {
            if (!AllowIdleLurk || !_c.IsVisible) return 0f;
            return 1f;
        }

        IEnumerator RunLurk()
        {
            // Hover at a respectful distance from the player's cursor, watching.
            Vector2 offset = UnityEngine.Random.insideUnitCircle.normalized * UnityEngine.Random.Range(120f, 190f);
            Vector2 target = ScreenRig.ClampToScreen(Player.Position + offset);
            yield return _c.MoveTo(target, MovementProfiles.Lurking, 40f);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.4f, 1.4f));
        }
    }
}
