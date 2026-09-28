using UnityEngine;

namespace SecondCursor.Input
{
    public enum PayloadKind { File, Other }

    /// <summary>
    /// Something being carried by a cursor (a file dragged from a folder or the desktop). The ghost image
    /// follows the holder. When the second cursor grabs a ghost the player is carrying (or vice versa)
    /// the payload becomes contested and the tug-of-war starts.
    /// </summary>
    public sealed class DragPayload
    {
        public PayloadKind Kind;
        public string FileId;
        public string Label;
        public string IconSprite;
        /// <summary>Where the drag started (for the fly-back animation when a drop is refused).</summary>
        public Interactable Source;
        public Vector2 Origin;
        /// <summary>Offset of the grab point from the ghost's top-left corner.</summary>
        public Vector2 GrabOffset;

        public CursorAgent Holder;
        /// <summary>Second agent also gripping the payload (tug-of-war), else null.</summary>
        public CursorAgent Contender;
        public bool Contested => Contender != null;

        /// <summary>Where the ghost currently is (top-left of the icon, virtual px). Driven by DragDropSystem.</summary>
        public Vector2 GhostPosition;

        /// <summary>Set when a drop target accepted the payload.</summary>
        public bool Dropped;

        /// <summary>The ghost's own interactable (lets the other cursor grab it).</summary>
        public Interactable Ghost;

        public override string ToString() => Kind + ":" + (FileId ?? Label);
    }
}
