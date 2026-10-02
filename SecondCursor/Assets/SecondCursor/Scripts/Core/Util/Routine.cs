using System;
using System.Collections;
using System.Collections.Generic;

namespace SecondCursor.Core
{
    /// <summary>
    /// Wait for game-time seconds inside a <see cref="Routine"/> (the Routine runner cannot see inside
    /// UnityEngine.WaitForSeconds, so story/entity code yields this instead).
    /// </summary>
    public sealed class WaitSeconds
    {
        public readonly float Seconds;
        public WaitSeconds(float seconds) { Seconds = seconds; }
    }

    public static class Waits
    {
        public static WaitSeconds Seconds(float seconds) => new WaitSeconds(seconds);
    }

    /// <summary>
    /// A coroutine runner that flattens nested IEnumerators into one stack, so stopping it stops EVERYTHING
    /// it started (Unity's StopCoroutine leaves nested "yield return enumerator" children running, which
    /// would let an interrupted entity keep moving and clicking). Ticked explicitly by its owner's Update
    /// with the current game time. Engine-free, so it is unit-tested outside Unity.
    /// Supports: null (next frame), WaitSeconds, nested IEnumerator, and Routine (wait for it).
    /// </summary>
    public sealed class Routine
    {
        readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
        readonly string _name;
        float _resumeAt = float.NegativeInfinity;
        Routine _waitingFor;

        public bool Done { get; private set; }
        public string Name => _name;
        /// <summary>The exception that ended this routine (null when it finished, or was stopped, without one).</summary>
        public Exception Fault { get; private set; }

        public Routine(IEnumerator root, string name = null)
        {
            _name = name ?? "routine";
            if (root != null) _stack.Push(root);
            else Done = true;
        }

        public void Stop()
        {
            Done = true;
            _stack.Clear();
            _waitingFor = null;
        }

        /// <summary>Advance until the next yield that needs time to pass. Call once per frame with game time.</summary>
        public void Tick(float now)
        {
            if (Done) return;
            if (now < _resumeAt) return;
            if (_waitingFor != null)
            {
                if (!_waitingFor.Done) return;
                _waitingFor = null;
            }

            int guard = 0;
            while (_stack.Count > 0)
            {
                if (++guard > 10000)
                {
                    GameLog.Error(LogChannel.System, "Routine '" + _name + "' did not yield (infinite loop?) - stopped");
                    Fault = new InvalidOperationException("routine did not yield");
                    Stop();
                    return;
                }
                var top = _stack.Peek();
                bool moved;
                try
                {
                    moved = top.MoveNext();
                }
                catch (Exception e)
                {
                    FaultLog.Report("routine '" + _name + "'", e);
                    Fault = e;
                    Stop();
                    return;
                }
                if (!moved)
                {
                    _stack.Pop();
                    continue;
                }
                object y = top.Current;
                switch (y)
                {
                    case null:
                        return;
                    case IEnumerator child:
                        _stack.Push(child);
                        continue;
                    case WaitSeconds w:
                        _resumeAt = now + Math.Max(0f, w.Seconds);
                        return;
                    case Routine other:
                        if (!other.Done) { _waitingFor = other; return; }
                        continue;
                    default:
                        // Unsupported yield instruction (e.g. WaitForSeconds): treat as one frame.
                        return;
                }
            }
            Done = true;
        }
    }
}
