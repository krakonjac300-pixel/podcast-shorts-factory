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
        /// <summary>
        /// Phase P: this cursor's press grabs its contested file again (the tug's re-grip window): it is swallowed, so no button or icon
        /// under the pointer receives it. Menus still close, as for any press.
        /// </summary>
        public Func<CursorAgent, bool> ContestRegrip;

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
                try { ProcessAgent(a, _state[a], time); }
                catch (Exception e)
                {
                    // A handler that throws must not fire again every frame (the press edge is consumed below) or starve the other cursors.
                    Core.FaultLog.Report("pointer " + a.Name, e);
                    ReleaseAfterFault(a);
                }
                finally
                {
                    a.ConsumeEdges();
                }
            }
        }

        /// <summary>
        /// The cursor lets go of what it was pressing, dragging or carrying, as a disabled cursor does (a button must not stay drawn as pressed,
        /// a window drag must end, a file must not stay glued to the pointer), whatever the handler did.
        /// </summary>
        void ReleaseAfterFault(CursorAgent a)
        {
            var pressed = a.Pressed;
            bool dragging = a.IsDragging;
            a.Pressed = null;
            a.IsDragging = false;
            try
            {
                if (a.ReleasedThisFrame && a.Payload != null)
                {
                    var p = a.Payload;
                    PayloadReleased?.Invoke(a, p, false);
                    if (a.Payload == p) a.Payload = null;
                }
                if (pressed == null) return;
                if (dragging) pressed.RaiseDragEnd(a);
                pressed.RaisePointerUp(a);
            }
            catch (Exception e) { Core.FaultLog.Report("pointer " + a.Name + " release", e); }
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
            if (a.PressedThisFrame && ContestRegrip != null && ContestRegrip(a))
            {
                a.Pressed = null;
                a.IsDragging = false;
                st.PressPosition = a.Position;
                AnyPointerDown?.Invoke(a, null);
            }
            else if (a.PressedThisFrame && hit != null && PressBlocked != null && PressBlocked(a, hit))
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
                    p.RefusedBy = !accepted && hit != null && hit.AcceptsDrop != null ? hit : null;
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
                var scroll = hit.GetComponentInParent<UI.ScrollArea>() ?? ScrollAreaAt(hit, a.Position);
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

        /// <summary>
        /// Phase K (finding 18): the wheel over something that sits on top of a scroll area without belonging to it (Mail's "More
        /// below" button) scrolls the area under it, in the same window.
        /// </summary>
        static UI.ScrollArea ScrollAreaAt(Interactable hit, Vector2 point)
        {
            var window = hit.GetComponentInParent<OS.OSWindow>();
            if (window == null) return null;
            foreach (var area in window.GetComponentsInChildren<UI.ScrollArea>())
                if (area.Viewport != null && area.Viewport.WorldRect().Contains(point)) return area;
            return null;
        }

        // ------------------------------------------------------------------ hit testing

        // Phase Q4 (CH6): while a probe batch is open, every element's world rect is computed once (a hundred probes in one frame would
        // otherwise compute them a hundred times); nothing moves during a batch, so the answers are the same as without it.
        Rect[] _rects = new Rect[256];
        bool _batch;

        /// <summary>Opens a probe batch: from now until <see cref="EndBatch"/> hit tests read each element's rect from a cache made now. Call only around code that does not move anything.</summary>
        public void BeginBatch()
        {
            var items = Interactable.Items;
            if (_rects.Length < items.Count) _rects = new Rect[Mathf.NextPowerOfTwo(items.Count)];
            for (int i = 0; i < items.Count; i++) _rects[i] = items[i] != null ? items[i].WorldRect : default;
            _batch = true;
        }

        public void EndBatch() => _batch = false;

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
            var items = Interactable.Items;
            bool cached = _batch && items.Count <= _rects.Length;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || !it.interactable) continue;
                if (forAgent != null && it.Tag is DragPayload dp && (dp.Holder == forAgent || dp.Contender == forAgent)) continue;
                // The icon a file is being dragged FROM is transparent to its carrier (so short moves can land on the desktop).
                if (forAgent != null && forAgent.Payload != null && (forAgent.Payload.Source == it || it.passThroughWhileCarrying)) continue;
                if (!(cached ? _rects[i] : it.WorldRect).Contains(point)) continue;
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
            var items = Interactable.Items;
            bool cached = _batch && items.Count <= _rects.Length;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || !it.interactable) continue;
                if (!(cached ? _rects[i] : it.WorldRect).Contains(point) || IsClipped(it.transform, point)) continue;
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
