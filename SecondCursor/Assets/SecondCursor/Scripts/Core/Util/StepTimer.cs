namespace SecondCursor.Core
{
    /// <summary>
    /// Fires once per <c>step</c> seconds, at most once a frame. Effects tuned per frame at 60 Hz (a re-rolled glitch strip, a grain
    /// offset, a held flicker) run on a 1/60 s step, so they look the same at 30, 60, 144 or 240 frames per second: a frame at least 0.9
    /// of a step long fires (a step fired early is paid back, so the rate stays 60 a second).
    /// </summary>
    public struct StepTimer
    {
        public const float Hz60 = 1f / 60f;

        float _acc;

        public bool Tick(float dt, float step = Hz60)
        {
            _acc += dt;
            if (_acc < step * 0.9f) return false;
            _acc -= step;
            if (_acc >= step) _acc = 0f;   // never a burst of catch-up steps after a long frame
            return true;
        }

        /// <summary>The next <see cref="Tick"/> fires at once (a glitch that starts rolls its strips now, not 0.17 s later).</summary>
        public void Prime(float step = Hz60) => _acc = step;
    }
}
