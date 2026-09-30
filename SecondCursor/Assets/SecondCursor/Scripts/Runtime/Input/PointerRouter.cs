using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Input
{
    /// <summary>
    /// The cursor interaction system (brief: CursorInteractionSystem). Every frame, for every CursorAgent
    /// (player and entity alike): hit-tests the topmost Interactable under it, and dispatches hover,
    /// press, click / double-click, drag and drop events. There is deliberately no special path for the
    /// entity: whatever it does, it does by moving a pointer and pressing a button.
    /// </summary>
    public sealed class PointerRouter
    {
        sealed class AgentState
        {
            public Vector2 PressPosition;
            public Vector2 LastPosition;
            public Interactable LastClickTarget;
            public float LastClickTime = -10f;
            public Vector2 LastClickPosition;
            public Interactable DropHover;
        }

        readonly List<CursorAgent> _agents = new List<CursorAgent>();
        readonly Dictionary<CursorAgent, AgentState> _state = new Dictionary<CursorAgent, AgentState>();

        public float DoubleClickTime = 0.45f;
        public float DoubleClickDistance = 5f;

        /// <summary>Any button press anywhere (menus close, windows focus). Target may be null.</summary>
        public event Action<CursorAgent, Interactable> AnyPointerDown;
        /// <summary>A carried payload was released. bool = accepted by a drop target.</summary>
        public event Action<CursorAgent, DragPayload, bool> PayloadReleased;

        /// <summary>
        /// Return true to refuse a press (another cursor is physically covering the target). The press
        /// still counts as "something happened" (menus close) but the target receives nothing.
        /// </summary>
        public Func<CursorAgent, Interactable, bool> PressBlocked;
        /// <summary>A press was refused by <see cref="PressBlocked"/>.</summary>
        public event Action<CursorAgent, Interactable> PressRefused;

        /// <summary>
        /// Phase J: a cursor lets go (called before its release is handled). A tug-of-war the player is clearly ahead in ends
        /// here in the player's favour, so the release below is an ordinary drop.
        /// </summary>
        public Action<CursorAgent> ContestRelease;
        /// <summary>Phase J: letting go of this cursor's contested payload now would keep it (so drop targets light up for it).</summary>
        public Func<CursorAgent, bool> ContestKeeps;

        public IReadOnlyList<CursorAgent> Agents => _agents;

        public void Register(CursorAgent agent)
        {
            if (_agents.Contains(agent)) return;
            _agents.Add(agent);
            _state[agent] = new AgentState();
        }

        public void Process(float time)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var a = _agents[i];
                ProcessAgent(a, _state[a], time);
                a.ConsumeEdges();
            }
        }

        void ProcessAgent(CursorAgent a, AgentState st, float time)
        {
            if (!a.Enabled)
            {
                // A disabled agent lets go of everything properly (payload, drop highlight, drag).
                if (a.Payload != null)
                {
                    var p = a.Payload;
                    PayloadReleased?.Invoke(a, p, false);
                    if (a.Payload == p) a.Payload = null;
                }
                if (st.DropHover != null)
                {
                    st.DropHover.RaiseDropHover(a, null, false);
                    st.DropHover = null;
                }
                if (a.Hovered != null) a.Hovered.RaiseHoverExit(a);
                a.Hovered = null;
                if (a.Pressed != null)
                {
                    if (a.IsDragging) a.Pressed.RaiseDragEnd(a);
                    a.Pressed.RaisePointerUp(a);
                }
                a.Pressed = null;
                a.IsDragging = false;
                return;
            }

            var hit = HitTest(a.Position, a);

            // Hover
            var hovered = a.Hovered;
            if (hovered != hit)
            {
                if (hovered != null) hovered.RaiseHoverExit(a);
                a.Hovered = hit;
                if (hit != null) hit.RaiseHoverEnter(a);
            }

            // Press
            if (a.PressedThisFrame && hit != null && PressBlocked != null && PressBlocked(a, hit))
            {
                PressRefused?.Invoke(a, hit);
                a.Pressed = null;
                a.IsDragging = false;
                st.PressPosition = a.Position;
                AnyPointerDown?.Invoke(a, null);
            }
            else if (a.PressedThisFrame)
            {
                a.Pressed = hit;
                a.IsDragging = false;
                st.PressPosition = a.Position;
                st.LastPosition = a.Position;
                AnyPointerDown?.Invoke(a, hit);
                if (hit != null) hit.RaisePointerDown(a);
            }

            // Drag
            var pressed = a.Pressed;
            if (a.Held && pressed != null && !a.IsDragging && pressed.draggable && pressed.interactable
                && (a.Position - st.PressPosition).magnitude >= pressed.dragThreshold)
            {
                a.IsDragging = true;
                st.LastPosition = st.PressPosition;
                pressed.RaiseDragBegin(a);
            }
            if (a.IsDragging && pressed != null && a.Held)
            {
                Vector2 delta = a.Position - st.LastPosition;
                if (delta.sqrMagnitude > 0f) pressed.RaiseDrag(a, delta);
            }
            st.LastPosition = a.Position;

            // Drop-target highlighting while carrying something
            if (a.Payload != null)
            {
                // A contested payload cannot be dropped anywhere, so nothing lights up under it (unless letting go would keep it).
                bool droppable = !a.Payload.Contested || (ContestKeeps != null && ContestKeeps(a));
                var target = hit != null && droppable && hit.Accepts(a, a.Payload) ? hit : null;
                if (target != st.DropHover)
                {
                    if (st.DropHover != null) st.DropHover.RaiseDropHover(a, a.Payload, false);
                    st.DropHover = target;
                    if (target != null) target.RaiseDropHover(a, a.Payload, true);
                }
            }
            else if (st.DropHover != null)
            {
                st.DropHover.RaiseDropHover(a, null, false);
                st.DropHover = null;
            }

            // Release
            if (a.ReleasedThisFrame)
            {
                var before = a.Payload;
                ContestRelease?.Invoke(a);
                // A player who was the contender carries the file only now: look again as its carrier (ghost and toasts let through).
                if (before == null && a.Payload != null) hit = HitTest(a.Position, a);
                if (a.Payload != null)
                {
                    var p = a.Payload;
                    // Letting go during a tug-of-war is letting go, not a drop: the contest decides (Phase F), unless the
                    // player was clearly ahead (Phase J: then the contest has just ended and this is an ordinary drop).
                    bool accepted = hit != null && !p.Contested && hit.Accepts(a, p);
                    if (st.DropHover != null)
                    {
                        st.DropHover.RaiseDropHover(a, p, false);
                        st.DropHover = null;
                    }
                    if (accepted)
                    {
                        p.Dropped = true;
                        hit.RaiseDrop(a, p);
                    }
                    PayloadReleased?.Invoke(a, p, accepted);
                }

                pressed = a.Pressed;
                if (a.IsDragging)
                {
                    if (pressed != null) pressed.RaiseDragEnd(a);
                }
                else if (pressed != null && hit == pressed && pressed.interactable)
                {
                    int count = 1;
                    if (st.LastClickTarget == pressed && time - st.LastClickTime <= DoubleClickTime
                        && (a.Position - st.LastClickPosition).magnitude <= DoubleClickDistance)
                        count = 2;
                    st.LastClickTarget = count == 2 ? null : pressed;
                    st.LastClickTime = time;
                    st.LastClickPosition = a.Position;
                    pressed.RaiseClick(a, count);
                }
                if (pressed != null) pressed.RaisePointerUp(a);
                a.Pressed = null;
                a.IsDragging = false;
            }

            // Mouse wheel goes to the nearest scrollable container under the pointer.
            if (Mathf.Abs(a.Scroll) > 0.001f && hit != null)
            {
                var scroll = hit.GetComponentInParent<UI.ScrollArea>();
                if (scroll != null) scroll.ScrollBy(-a.Scroll * scroll.WheelStep);
            }

            // Right button: menus open on release, like the old desktops.
            if (a.RightPressedThisFrame) AnyPointerDown?.Invoke(a, hit);
            if (a.RightReleasedThisFrame && hit != null && hit.interactable) hit.RaiseRightClick(a);

            // Cursor shape
            if (a.ShapeOverride.HasValue) a.Shape = a.ShapeOverride.Value;
            else if (a.Payload != null) a.Shape = CursorShape.Drag;
            else if (a.IsDragging && a.Pressed != null) a.Shape = a.Pressed.cursor;
            else a.Shape = hit != null && hit.interactable ? hit.cursor : CursorShape.Arrow;
        }

        // ------------------------------------------------------------------ hit testing

        readonly List<Interactable> _candidates = new List<Interactable>(16);
        readonly List<int> _pathA = new List<int>(24);
        readonly List<int> _pathB = new List<int>(24);

        /// <summary>
        /// Topmost interactable containing the point (virtual px), honouring RectMask2D clipping and draw
        /// order. Drag ghosts are transparent to the agents carrying them.
        /// </summary>
        public Interactable HitTest(Vector2 point, CursorAgent forAgent = null)
        {
            _candidates.Clear();
            foreach (var it in Interactable.All)
            {
                if (it == null || !it.interactable) continue;
                if (forAgent != null && it.Tag is DragPayload dp && (dp.Holder == forAgent || dp.Contender == forAgent)) continue;
                // The icon a file is being dragged FROM is transparent to its carrier (so short moves can land on the desktop).
                if (forAgent != null && forAgent.Payload != null && (forAgent.Payload.Source == it || it.passThroughWhileCarrying)) continue;
                if (!it.WorldRect.Contains(point)) continue;
                if (IsClipped(it.transform, point)) continue;
                _candidates.Add(it);
            }
            if (_candidates.Count == 0) return null;
            var best = _candidates[0];
            for (int i = 1; i < _candidates.Count; i++)
                if (DrawnAfter(_candidates[i], best)) best = _candidates[i];
            return best;
        }

        /// <summary>All interactables under a point (topmost first). Used by the entity to reason about occlusion.</summary>
        public List<Interactable> HitTestAll(Vector2 point, List<Interactable> into)
        {
            into.Clear();
            foreach (var it in Interactable.All)
            {
                if (it == null || !it.interactable) continue;
                if (!it.WorldRect.Contains(point) || IsClipped(it.transform, point)) continue;
                into.Add(it);
            }
            into.Sort((x, y) => x == y ? 0 : (DrawnAfter(x, y) ? -1 : 1));
            return into;
        }

        static bool IsClipped(Transform t, Vector2 point)
        {
            var p = t.parent;
            while (p != null)
            {
                var mask = p.GetComponent<RectMask2D>();
                if (mask != null && mask.enabled && !((RectTransform)p).WorldRect().Contains(point)) return true;
                p = p.parent;
            }
            return false;
        }

        bool DrawnAfter(Interactable a, Interactable b)
        {
            BuildPath(a.transform, _pathA);
            BuildPath(b.transform, _pathB);
            int n = Mathf.Min(_pathA.Count, _pathB.Count);
            for (int i = 0; i < n; i++)
            {
                if (_pathA[i] != _pathB[i]) return _pathA[i] > _pathB[i];
            }
            if (_pathA.Count != _pathB.Count) return _pathA.Count > _pathB.Count; // descendants draw after parents
            return a.Serial > b.Serial;
        }

        static void BuildPath(Transform t, List<int> path)
        {
            path.Clear();
            while (t != null)
            {
                path.Add(t.GetSiblingIndex());
                t = t.parent;
            }
            path.Reverse();
        }
    }
}
