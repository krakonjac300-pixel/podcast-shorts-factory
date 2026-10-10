using System.Collections.Generic;
using SecondCursor.Core.Audio;
using UnityEngine;

namespace SecondCursor.Audio
{
    /// <summary>
    /// Phase M: the safety limiter on the listener (<see cref="PeakLimiter"/>): the whole mix never leaves the device above
    /// -1 dBFS. In the Editor it also records the limited output on request, for the test bridge's listening renders.
    /// </summary>
    [RequireComponent(typeof(AudioListener))]
    public sealed class MasterLimiter : MonoBehaviour
    {
        static PeakLimiter _limiter;

        /// <summary>The limiter's counters (the audio thread writes them): frames limited and the loudest input since the last reset.</summary>
        internal static int EngagedSamples => _limiter?.EngagedSamples ?? 0;
        internal static float MaxInput => _limiter?.MaxInput ?? 0f;

        internal static void ResetCounters() => _limiter?.ResetCounters();

#if UNITY_EDITOR
        static readonly object CaptureLock = new object();
        static List<float> _capture;
        internal static int CaptureChannels { get; private set; } = 2;

        /// <summary>Starts recording the output (interleaved, <see cref="CaptureChannels"/> channels).</summary>
        internal static void StartCapture()
        {
            lock (CaptureLock) _capture = new List<float>(AudioSettings.outputSampleRate * 2 * 30);
        }

        /// <summary>Stops recording and hands back what was captured (null if nothing was).</summary>
        internal static float[] StopCapture()
        {
            lock (CaptureLock)
            {
                var data = _capture?.ToArray();
                _capture = null;
                return data;
            }
        }
#endif

        void Awake()
        {
            if (_limiter == null) _limiter = new PeakLimiter(AudioSettings.outputSampleRate);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            _limiter?.Process(data, channels);
#if UNITY_EDITOR
            lock (CaptureLock)
            {
                if (_capture == null) return;
                CaptureChannels = channels;
                _capture.AddRange(data);
            }
#endif
        }
    }
}
