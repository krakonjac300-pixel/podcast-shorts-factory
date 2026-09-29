using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondCursor.Input
{
    /// <summary>
    /// Anything on the fake screen a cursor can hover, click, drag or drop onto. Events carry the
    /// CursorAgent that caused them, so every system knows whether the player or the entity did it.
    /// Hit-testing is done by <see cref="PointerRouter"/> (no Unity EventSystem: that only supports one
    /// hardware pointer, and we need two independent cursors).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Interactable : MonoBehaviour
    {
        static readonly HashSet<Interactable> Registry = new HashSet<Interactable>();
        public static IReadOnlyCollection<Interactable> All => Registry;

        [Tooltip("When false the element is invisible to cursors (clicks go to whatever is underneath).")]
        public bool interactable = true;
        [Tooltip("Cursor shown while hovering.")]
        public CursorShape cursor = CursorShape.Arrow;
        [Tooltip("Drag events fire only when true.")]
        public bool draggable;
        [Tooltip("Logical id the entity can target, e.g. 'window.close' or 'file:employee_017'.")]
        public string elementId = "";
        [Tooltip("Pixels the pointer must move before a press becomes a drag.")]
        public float dragThreshold = 3f;
        [Tooltip("Ignored by a cursor that is carrying something (e.g. toasts never block a drop).")]
        public bool passThroughWhileCarrying;

        /// <summary>Optional owner used for focus-on-press and entity queries.</summary>
        public OS.OSWindow Window { get; set; }

        /// <summary>Arbitrary payload for game code (e.g. the VFile an icon represents).</summary>
        [System.NonSerialized] public object Tag;

        public event Action<CursorAgent> HoverEnter;
        public event Action<CursorAgent> HoverExit;
        public event Action<CursorAgent> PointerDown;
        public event Action<CursorAgent> PointerUp;
        /// <summary>clickCount is 1 for a single click, 2 for a double click.</summary>
        public event Action<CursorAgent, int> Click;
        public event Action<CursorAgent> RightClick;
        public event Action<CursorAgent> DragBegin;
        public event Action<CursorAgent, Vector2> Drag;
        public event Action<CursorAgent> DragEnd;
        public event Action<CursorAgent, DragPayload> Drop;
        /// <summary>Return true to accept a payload dropped here (also used for drop highlighting).</summary>
        public Func<CursorAgent, DragPayload, bool> AcceptsDrop;
        public event Action<CursorAgent, DragPayload, bool> DropHover; // agent, payload, entering

        readonly List<CursorAgent> _hoveredBy = new List<CursorAgent>(2);
        readonly List<CursorAgent> _pressedBy = new List<CursorAgent>(2);

        RectTransform _rect;
        /// <summary>Lazy: Awake may not have run yet when created under an inactive parent.</summary>
        public RectTransform Rect => _rect != null ? _rect : (_rect = (RectTransform)transform);
        public bool IsHovered => _hoveredBy.Count > 0;
        public bool IsHoveredBy(CursorAgent a) => _hoveredBy.Contains(a);
        public bool IsPressedBy(CursorAgent a) => _pressedBy.Contains(a);
        public bool IsPressed => _pressedBy.Count > 0;
        public IReadOnlyList<CursorAgent> HoveredBy => _hoveredBy;

        /// <summary>Monotonic registration order: used as a tiebreak for draw order.</summary>
        internal int Serial;
        static int _nextSerial;

        void OnEnable()
        {
            Serial = ++_nextSerial;
            Registry.Add(this);
        }

        void OnDisable()
        {
            Registry.Remove(this);
            // Release anyone still hovering/pressing so no agent keeps a dangling reference.
            for (int i = _hoveredBy.Count - 1; i >= 0; i--) RaiseHoverExit(_hoveredBy[i]);
            _pressedBy.Clear();
        }

        public Vector2 Center => Rect.WorldCenter();
        public Rect WorldRect => Rect.WorldRect();

        internal void RaiseHoverEnter(CursorAgent a)
        {
            if (_hoveredBy.Contains(a)) return;
            _hoveredBy.Add(a);
            HoverEnter?.Invoke(a);
        }

        internal void RaiseHoverExit(CursorAgent a)
        {
            if (!_hoveredBy.Remove(a)) return;
            if (a.Hovered == this) a.Hovered = null;
            HoverExit?.Invoke(a);
        }

        internal void RaisePointerDown(CursorAgent a)
        {
            if (!_pressedBy.Contains(a)) _pressedBy.Add(a);
            PointerDown?.Invoke(a);
        }

        internal void RaisePointerUp(CursorAgent a)
        {
            _pressedBy.Remove(a);
            PointerUp?.Invoke(a);
        }

        internal void RaiseClick(CursorAgent a, int count) => Click?.Invoke(a, count);
        internal void RaiseRightClick(CursorAgent a) => RightClick?.Invoke(a);
        internal void RaiseDragBegin(CursorAgent a) => DragBegin?.Invoke(a);
        internal void RaiseDrag(CursorAgent a, Vector2 delta) => Drag?.Invoke(a, delta);
        internal void RaiseDragEnd(CursorAgent a) => DragEnd?.Invoke(a);
        internal void RaiseDrop(CursorAgent a, DragPayload p) => Drop?.Invoke(a, p);
        internal void RaiseDropHover(CursorAgent a, DragPayload p, bool entering) => DropHover?.Invoke(a, p, entering);

        public bool Accepts(CursorAgent a, DragPayload p) => AcceptsDrop != null && p != null && AcceptsDrop(a, p);

        /// <summary>Lets game code (e.g. the story) synthesise a click without moving a cursor. Prefer real cursor clicks.</summary>
        public void SimulateClick(CursorAgent a, int count = 1) => Click?.Invoke(a, count);
    }
}
