using UnityEngine;

namespace SecondCursor.Input
{
    public enum AgentKind { Player, Entity }

    public enum CursorShape { Arrow, Hand, IBeam, Move, Busy, No, Drag, Grab }

    /// <summary>
    /// A pointer on the fake screen. The player's cursor and the second cursor are both CursorAgents and
    /// go through exactly the same interaction code, so the entity genuinely uses the computer (and a
    /// second human could drive the entity agent later). Positions are virtual pixels, origin bottom-left.
    /// </summary>
    public sealed class CursorAgent
    {
        public readonly AgentKind Kind;
        public readonly string Name;

        public Vector2 Position;
        public Vector2 Velocity { get; private set; }
        public bool Visible = true;
        /// <summary>Disabled agents neither hover nor click (e.g. entity not yet present, player during cutscene).</summary>
        public bool Enabled = true;

        // Button state. Drivers set these; the PointerRouter consumes the edges once per frame.
        public bool Held { get; private set; }
        public bool PressedThisFrame { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public bool RightPressedThisFrame { get; private set; }
        public bool RightReleasedThisFrame { get; private set; }

        /// <summary>Mouse-wheel notches this frame (positive = away from the user / scroll up).</summary>
        public float Scroll;

        /// <summary>Current cursor sprite (set by the router from what is under it; can be overridden).</summary>
        public CursorShape Shape = CursorShape.Arrow;
        public CursorShape? ShapeOverride;

        /// <summary>What this agent is dragging (a file etc.), managed by DragDropSystem.</summary>
        public DragPayload Payload;

        public Interactable Hovered { get; internal set; }
        public Interactable Pressed { get; internal set; }
        public bool IsDragging { get; internal set; }

        Vector2 _lastPosition;
        bool _hasLast;

        public CursorAgent(AgentKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        public bool IsPlayer => Kind == AgentKind.Player;
        public bool IsEntity => Kind == AgentKind.Entity;

        /// <summary>The third pointer's name (Gary, session 209).</summary>
        const string ThirdPointer = "Gary";

        /// <summary>Phase Q4 (R2): whose colours this pointer wears in notices and on windows it closes: 017, 209 or nobody (the player).</summary>
        public Core.Game.NoticeKind Actor => Core.Game.ActorStyle.Of(IsEntity, Name == ThirdPointer);

        public void SetButton(bool down)
        {
            if (down == Held) return;
            Held = down;
            if (down) PressedThisFrame = true;
            else ReleasedThisFrame = true;
        }

        /// <summary>Player driver: exact edges from the input backend (so a press+release inside one frame is not lost).</summary>
        public void SetButtonEdges(bool held, bool downEdge, bool upEdge)
        {
            // A release that happened while the game lost focus (Alt-Tab) still counts as a release.
            if (Held && !held) upEdge = true;
            if (!Held && held) downEdge = true;
            Held = held;
            if (downEdge) PressedThisFrame = true;
            if (upEdge) ReleasedThisFrame = true;
        }

        public void RightClickEdge(bool down, bool up)
        {
            if (down) RightPressedThisFrame = true;
            if (up) RightReleasedThisFrame = true;
        }

        /// <summary>Called by the router after it has processed this frame.</summary>
        internal void ConsumeEdges()
        {
            PressedThisFrame = ReleasedThisFrame = false;
            RightPressedThisFrame = RightReleasedThisFrame = false;
            Scroll = 0f;
        }

        internal void UpdateVelocity(float dt)
        {
            if (_hasLast && dt > 0f) Velocity = Vector2.Lerp(Velocity, (Position - _lastPosition) / dt, Core.MathUtil.LerpAt60(0.5f, dt));
            _lastPosition = Position;
            _hasLast = true;
        }

        /// <summary>Forget any button state (used when an agent is teleported, disabled or loses a fight).</summary>
        public void ResetButtons()
        {
            if (Held) ReleasedThisFrame = true;
            Held = false;
        }

        public override string ToString() => Name;
    }
}
