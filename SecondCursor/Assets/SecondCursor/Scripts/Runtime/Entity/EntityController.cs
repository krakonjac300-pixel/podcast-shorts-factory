using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Apps;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Drives the second cursor. It has no special powers over the UI: it moves a CursorAgent along
    /// human-like (or deliberately inhuman) trajectories and presses its button, and the normal pointer
    /// system does the rest. Exposes awaitable primitives (MoveTo, Click, DragTo, Type, Replay...) used by
    /// both scripted story beats and the systemic <see cref="EntityBrain"/>.
    /// </summary>
    public sealed class EntityController : MonoBehaviour
    {
        GameServices _g;
        CursorAgent _agent;
        CursorView _view;
        readonly Rng _rng = new Rng(20171);
        Coroutine _current;
        string _currentName;
        float _targetAlpha;
        float _alphaSpeed = 2f;
        EntityState _state = EntityState.Dormant;

        public EntityPhase Phase = EntityPhase.Invisible;
        public EntityPersonality Personality = new EntityPersonality();
        public EntityBrain Brain { get; private set; }

        /// <summary>Radius within which the player's cursor physically blocks the entity from clicking.</summary>
        public float BlockRadius = 11f;
        /// <summary>Speed multiplier applied on top of movement profiles (escalation).</summary>
        public float Urgency = 1f;

        /// <summary>An element the entity is physically covering; the player cannot press it while the entity sits on it.</summary>
        public Interactable Guarding { get; set; }

        public CursorAgent Agent => _agent;
        public CursorView View => _view;
        public bool IsVisible => _view != null && _view.Alpha > 0.05f && _agent.Visible;
        public bool Busy => _current != null;
        public string CurrentAction => _currentName;

        public event Action<EntityState, EntityState> StateChanged;

        public EntityState State
        {
            get => _state;
            set
            {
                if (_state == value) return;
                var old = _state;
                _state = value;
                GameLog.Info(LogChannel.Entity, "State changed " + old + " -> " + value);
                StateChanged?.Invoke(old, value);
                ApplyStateLook();
            }
        }

        public static EntityController Create(GameServices g, Transform parent)
        {
            var go = new GameObject("Entity");
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<EntityController>();
            c._g = g;
            c._agent = g.EntityAgent;
            c._view = g.EntityView;
            c._agent.Enabled = false;
            c._agent.Visible = false;
            c._agent.Position = new Vector2(ScreenRig.Width + 20, ScreenRig.Height * 0.5f);
            c._view.Alpha = 0f;
            var tuning = EntityTuningAsset.LoadOptional();
            if (tuning != null)
            {
                c.Personality = tuning.personality.Clone();
                c.BlockRadius = tuning.blockRadius;
                MovementProfiles.SetOverrides(tuning.movementOverrides);
                GameLog.Info(LogChannel.Entity, "Loaded designer tuning asset '" + tuning.name + "'");
            }
            else
            {
                MovementProfiles.SetOverrides(null);
            }
            c.Brain = new EntityBrain(g, c);
            g.Router.PressBlocked = c.BlocksPress;
            g.Router.PressRefused += (a, hit) =>
            {
                // The player's click bounces off the second cursor.
                g.Audio?.Play("mouse_click", 0.6f, 0.8f, Audio.AudioManager.PanFor(c._agent.Position.x));
                c._view.Flinch(new Vector2(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(-3f, 3f)), 0.25f);
                g.PlayerView?.Flinch((a.Position - c._agent.Position).normalized * 4f, 0.3f);
            };
            return c;
        }

        // ------------------------------------------------------------------ lifecycle

        void Update()
        {
            float dt = Time.deltaTime;
            if (_view != null)
            {
                _view.Alpha = Mathf.MoveTowards(_view.Alpha, _targetAlpha, dt * _alphaSpeed);
                _agent.Visible = _view.Alpha > 0.01f;
            }
            _agent.UpdateVelocity(dt);
            UpdateStaticSound();
            if (_current == null && Brain.Enabled) Brain.Tick(dt);
        }

        void UpdateStaticSound()
        {
            if (_g.Audio == null) return;
            bool moving = IsVisible && _agent.Velocity.sqrMagnitude > 400f;
            if (moving)
            {
                if (!_g.Audio.IsLoopPlaying("entity_static")) _g.Audio.PlayLoop("entity_static", 0.05f, 0.05f);
                float v = Mathf.Clamp01(_agent.Velocity.magnitude / 1800f);
                _g.Audio.SetLoopVolume("entity_static", 0.15f + v * 0.85f, 0.08f);
                _g.Audio.SetLoopPan("entity_static", Audio.AudioManager.PanFor(_agent.Position.x));
            }
            else if (_g.Audio.IsLoopPlaying("entity_static"))
            {
                _g.Audio.StopLoop("entity_static", 0.25f);
            }
        }

        void ApplyStateLook()
        {
            if (_view == null) return;
            switch (_state)
            {
                case EntityState.Panicked: _view.Jitter = 1.2f; _view.Flicker = 0.04f; break;
                case EntityState.Aggressive: _view.Jitter = 0.4f; _view.Flicker = 0.01f; break;
                case EntityState.Defensive: _view.Jitter = 0.25f; _view.Flicker = 0f; break;
                default: _view.Jitter = 0f; _view.Flicker = 0f; break;
            }
        }

        // ------------------------------------------------------------------ action runner

        /// <summary>Run a behaviour, interrupting whatever the entity was doing.</summary>
        public Coroutine Run(IEnumerator routine, string name)
        {
            Interrupt();
            _currentName = name;
            _current = StartCoroutine(Wrap(routine));
            return _current;
        }

        IEnumerator Wrap(IEnumerator routine)
        {
            yield return routine;
            _current = null;
            _currentName = null;
        }

        /// <summary>Stop the current behaviour and let go of anything held.</summary>
        public void Interrupt()
        {
            if (_current != null) StopCoroutine(_current);
            _current = null;
            _currentName = null;
            ReleaseEverything();
        }

        void ReleaseEverything()
        {
            if (_agent.Held) _agent.SetButton(false);
            Guarding = null;
        }

        /// <summary>Wait for the current behaviour (use from story coroutines: yield return entity.WaitIdle()).</summary>
        public IEnumerator WaitIdle()
        {
            while (_current != null) yield return null;
        }

        // ------------------------------------------------------------------ presence

        public void SetPresent(bool present, float fadeSeconds = 0.6f)
        {
            _targetAlpha = present ? 1f : 0f;
            _alphaSpeed = fadeSeconds <= 0f ? 1000f : 1f / fadeSeconds;
            _agent.Enabled = present;
            if (!present) ReleaseEverything();
        }

        /// <summary>Fade in at a position (default: just off the right edge) - the cursor "arrives".</summary>
        public IEnumerator Appear(Vector2? at = null, float fadeSeconds = 0.6f, bool sound = true)
        {
            if (at.HasValue) _agent.Position = at.Value;
            SetPresent(true, fadeSeconds);
            if (sound) _g.Audio?.Play("entity_appear", 0.8f, 1f, Audio.AudioManager.PanFor(_agent.Position.x));
            if (_g.Taskbar != null) _g.Taskbar.PointingDevices = 2;
            yield return new WaitForSeconds(fadeSeconds);
        }

        public IEnumerator Vanish(float fadeSeconds = 0.5f)
        {
            SetPresent(false, fadeSeconds);
            yield return new WaitForSeconds(fadeSeconds);
        }

        /// <summary>Instantly place the cursor (e.g. off-screen before entering).</summary>
        public void Teleport(Vector2 world) => _agent.Position = world;

        // ------------------------------------------------------------------ movement

        MovementProfileData Scaled(MovementProfileData p)
        {
            var q = (p ?? MovementProfiles.HumanLike).Clone();
            q.speed *= Mathf.Max(0.1f, Urgency);
            q.reactionDelay *= Personality.reactionScale / Mathf.Max(0.5f, Urgency);
            return q;
        }

        /// <summary>Move to a fixed point.</summary>
        public IEnumerator MoveTo(Vector2 target, MovementProfileData profile, float targetSize = 12f)
        {
            yield return MoveToDynamic(() => target, profile, targetSize);
        }

        /// <summary>
        /// Move toward a target that may move while travelling (homes in). Aborts early if the target
        /// function returns null (target vanished).
        /// </summary>
        public IEnumerator MoveToDynamic(Func<Vector2?> target, MovementProfileData profile, float targetSize = 12f)
        {
            var first = target();
            if (!first.HasValue) yield break;
            var plan = MovementPlanner.Plan(_agent.Position.ToCore(), first.Value.ToCore(), Scaled(profile), _rng, targetSize);
            float t = 0f;
            while (t < plan.Duration)
            {
                var now = target();
                if (!now.HasValue) yield break;
                t += Time.deltaTime;
                _agent.Position = ScreenRig.ClampToScreen(plan.EvaluateHoming(t, now.Value.ToCore()).ToUnity());
                yield return null;
            }
            var end = target();
            if (end.HasValue) _agent.Position = end.Value;
        }

        public IEnumerator MoveToElement(Interactable element, MovementProfileData profile)
        {
            if (element == null) yield break;
            Vector2 local = RandomPointIn(element);
            yield return MoveToDynamic(() => element != null && element.isActiveAndEnabled ? element.WorldRect.min + local : (Vector2?)null,
                profile, Mathf.Min(element.WorldRect.width, element.WorldRect.height));
        }

        /// <summary>A comfortable point inside an element (not the exact centre every time).</summary>
        Vector2 RandomPointIn(Interactable element)
        {
            var r = element.WorldRect;
            float fx = Mathf.Clamp01(0.5f + _rng.Range(-0.18f, 0.18f));
            float fy = Mathf.Clamp01(0.5f + _rng.Range(-0.18f, 0.18f));
            return new Vector2(r.width * fx, r.height * fy);
        }

        /// <summary>Drift aimlessly near a point for a while (observing).</summary>
        public IEnumerator Loiter(Vector2 around, float radius, float seconds, MovementProfileData profile = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                var p = around + UnityEngine.Random.insideUnitCircle * radius;
                yield return MoveTo(ScreenRig.ClampToScreen(p), profile ?? MovementProfiles.Lurking, 30f);
                yield return new WaitForSeconds(_rng.Range(0.2f, 0.9f));
            }
        }

        // ------------------------------------------------------------------ buttons

        public IEnumerator Press()
        {
            _agent.SetButton(true);
            yield return null;
        }

        public IEnumerator Release()
        {
            _agent.SetButton(false);
            yield return null;
        }

        public IEnumerator Click()
        {
            _agent.SetButton(true);
            yield return null;
            yield return new WaitForSeconds(_rng.Range(0.05f, 0.1f));
            _agent.SetButton(false);
            yield return null;
        }

        public IEnumerator DoubleClick()
        {
            yield return Click();
            yield return new WaitForSeconds(0.08f);
            yield return Click();
        }

        /// <summary>Router hook: the entity's cursor sitting on a guarded element blocks the player's press on it.</summary>
        bool BlocksPress(CursorAgent a, Interactable hit)
        {
            if (!a.IsPlayer || Guarding == null || hit != Guarding || !IsVisible) return false;
            return Vector2.Distance(a.Position, _agent.Position) < 16f || Guarding.IsHoveredBy(_agent);
        }

        /// <summary>True while the player's own cursor sits on the element near where the entity would click.</summary>
        public bool IsBlockedByPlayer(Interactable element)
        {
            var p = _g.Player;
            if (element == null || !p.Enabled) return false;
            if (!element.WorldRect.Contains(p.Position)) return false;
            return Vector2.Distance(p.Position, _agent.Position) < BlockRadius || element.IsHoveredBy(p);
        }

        /// <summary>
        /// Move to an element and click it. If the player's cursor is covering it, the entity jostles
        /// around the player's cursor trying to get a clean click until <paramref name="patience"/> runs out.
        /// Result is written to <paramref name="result"/>[0] (true = clicked).
        /// </summary>
        public IEnumerator ClickElement(Interactable element, MovementProfileData profile, bool[] result = null, float patience = 3f, bool doubleClick = false)
        {
            if (result != null && result.Length > 0) result[0] = false;
            if (element == null) yield break;
            yield return MoveToElement(element, profile);
            float giveUp = Time.time + patience;
            while (element != null && element.isActiveAndEnabled && IsBlockedByPlayer(element))
            {
                if (Time.time > giveUp) yield break;
                // Nudge against the player's cursor, looking for an uncovered spot.
                var r = element.WorldRect;
                Vector2 away = (_agent.Position - _g.Player.Position);
                if (away.sqrMagnitude < 1f) away = UnityEngine.Random.insideUnitCircle;
                Vector2 probe = _g.Player.Position + away.normalized * (BlockRadius + 3f) + UnityEngine.Random.insideUnitCircle * 4f;
                probe = new Vector2(Mathf.Clamp(probe.x, r.xMin + 2, r.xMax - 2), Mathf.Clamp(probe.y, r.yMin + 2, r.yMax - 2));
                yield return MoveTo(probe, MovementProfiles.Panicked, 6f);
                yield return new WaitForSeconds(0.05f);
            }
            if (element == null || !element.isActiveAndEnabled) yield break;
            if (!element.WorldRect.Contains(_agent.Position)) yield return MoveToElement(element, MovementProfiles.Aggressive);
            if (doubleClick) yield return DoubleClick();
            else yield return Click();
            if (result != null && result.Length > 0) result[0] = true;
        }

        /// <summary>Press on a source element, drag to a target point, release there.</summary>
        public IEnumerator DragTo(Interactable source, Func<Vector2?> destination, MovementProfileData profile, float holdBefore = 0.12f)
        {
            if (source == null) yield break;
            yield return MoveToElement(source, profile);
            if (source == null || !source.isActiveAndEnabled) yield break;
            _agent.SetButton(true);
            yield return null;
            yield return new WaitForSeconds(holdBefore);
            // Tear it loose (crosses the drag threshold).
            _agent.Position += new Vector2(6f, -4f);
            yield return null;
            yield return MoveToDynamic(destination, profile, 24f);
            yield return new WaitForSeconds(0.08f);
            _agent.SetButton(false);
            yield return null;
        }

        /// <summary>Grab the window by its caption and haul it somewhere.</summary>
        public IEnumerator DragWindow(OSWindow window, Vector2 captionDestination, MovementProfileData profile)
        {
            if (window == null || window.IsClosed) yield break;
            yield return MoveToDynamic(() => window != null && !window.IsClosed ? window.CaptionCenter + new Vector2(-30f, 0f) : (Vector2?)null, profile, 20f);
            if (window == null || window.IsClosed) yield break;
            _agent.SetButton(true);
            yield return null;
            yield return new WaitForSeconds(0.06f);
            yield return MoveTo(captionDestination, profile, 30f);
            _agent.SetButton(false);
            yield return null;
        }

        // ------------------------------------------------------------------ higher-level abilities

        /// <summary>Open an app the way a person would: double-click its desktop icon.</summary>
        public IEnumerator OpenApp(string appId, MovementProfileData profile)
        {
            var icon = _g.Desktop.IconForApp(appId);
            if (icon == null)
            {
                _g.Apps.Launch(appId, _agent);
                yield break;
            }
            yield return ClickElement(icon.Hit, profile, null, 2f, true);
            yield return new WaitForSeconds(0.25f);
            if (_g.Apps.FindById(appId) == null) _g.Apps.Launch(appId, _agent, icon.GlyphWorldRect);
        }

        /// <summary>Type into a Notepad (keyboard sounds you are not making).</summary>
        public IEnumerator Type(NotepadApp notepad, string text, float charsPerSecond = 7f)
        {
            if (notepad == null || !notepad.IsOpen) yield break;
            // Hover over the text area like someone about to type.
            var area = notepad.Window.Client;
            var target = area.WorldCenter() + new Vector2(_rng.Range(-60f, 60f), _rng.Range(-40f, 20f));
            yield return MoveTo(target, MovementProfiles.HumanLike, 40f);
            yield return notepad.TypeAsEntity(text, charsPerSecond, _agent);
        }

        /// <summary>
        /// Replay the player's own recorded cursor movement (the "it's copying me" moment). The recording
        /// is offset so it starts where the entity is, optionally mirrored/stretched by the caller.
        /// </summary>
        public IEnumerator Replay(CursorRecording rec, bool pressButtons = false)
        {
            if (rec == null || rec.IsEmpty) yield break;
            var start = rec.Sample(0f);
            Vector2 offset = Vector2.zero;
            // Travel to the recording's first point first.
            yield return MoveTo(ScreenRig.ClampToScreen(new Vector2(start.x, start.y) + offset), MovementProfiles.ImitatingPlayer, 30f);
            float t = 0f;
            bool down = false;
            while (t <= rec.Duration)
            {
                var s = rec.Sample(t);
                _agent.Position = ScreenRig.ClampToScreen(new Vector2(s.x, s.y) + offset);
                if (pressButtons && s.down != down)
                {
                    down = s.down;
                    _agent.SetButton(down);
                }
                t += Time.deltaTime;
                yield return null;
            }
            if (down) _agent.SetButton(false);
        }

        // ------------------------------------------------------------------ recoil (lost a fight)

        public IEnumerator Recoil(Vector2 awayFrom)
        {
            Vector2 dir = (_agent.Position - awayFrom);
            if (dir.sqrMagnitude < 1f) dir = Vector2.right;
            Vector2 target = ScreenRig.ClampToScreen(_agent.Position + dir.normalized * _rng.Range(70f, 120f));
            var prevJitter = _view.Jitter;
            _view.Jitter = 2.5f;
            yield return MoveTo(target, MovementProfiles.Aggressive, 40f);
            yield return new WaitForSeconds(0.25f);
            _view.Jitter = prevJitter;
        }
    }
}
