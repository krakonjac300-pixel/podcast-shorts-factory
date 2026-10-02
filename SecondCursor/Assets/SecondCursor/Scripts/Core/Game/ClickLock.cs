namespace SecondCursor.Core.Game
{
    /// <summary>
    /// Phase P (A2), "Click lock": for a player who cannot keep a button held. Once a press has turned into a drag and the button has been
    /// down <see cref="ArmAfter"/>, letting go keeps the button logically held; the next press is the release (the drop), and its own
    /// raw release is swallowed. Short clicks pass through unchanged. Engine-free; the runtime filters the player's raw button through it.
    /// </summary>
    public sealed class ClickLock
    {
        public const float ArmAfter = 0.4f;

        public bool Enabled;
        /// <summary>The raw button is up but the drag goes on.</summary>
        public bool Locked { get; private set; }

        float _heldFor;
        bool _swallow, _releaseNext;

        /// <param name="dragging">The player's pointer is dragging or carrying something (the router's state before this frame).</param>
        /// <returns>The logical button: held, pressed this frame, released this frame.</returns>
        public (bool held, bool down, bool up) Filter(bool rawHeld, bool rawDown, bool rawUp, bool dragging, float dt)
        {
            if (_releaseNext)
            {
                // Cleared while locked: the drag ends here (a press still down stays swallowed until it comes up).
                _releaseNext = false;
                _swallow = rawHeld;
                return (false, false, true);
            }
            if (_swallow)
            {
                // The press that dropped a locked drag: neither it nor its release is another click.
                if (!rawHeld) _swallow = false;
                return (false, false, false);
            }
            if (Locked)
            {
                // The drag ended without a press (a tug lost, a file hauled into the bin): the lock lets go of nothing more.
                if (!dragging)
                {
                    Locked = false;
                    _swallow = rawHeld;
                    return (false, false, true);
                }
                if (!rawDown) return (true, false, false);
                Locked = false;
                _swallow = true;
                return (false, false, true);
            }
            bool armed = _heldFor >= ArmAfter;
            _heldFor = rawHeld ? _heldFor + dt : 0f;
            if (Enabled && rawUp && dragging && armed)
            {
                Locked = true;
                _heldFor = 0f;
                return (true, false, false);
            }
            return (rawHeld, rawDown, rawUp);
        }

        /// <summary>Pause, a scene change or Esc: a locked drag is released on the next frame.</summary>
        public void Clear()
        {
            if (Locked) _releaseNext = true;
            Locked = false;
            _heldFor = 0f;
        }
    }
}
