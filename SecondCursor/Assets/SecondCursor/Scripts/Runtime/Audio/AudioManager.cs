using System;
using System.Collections;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SecondCursor.Audio
{
    /// <summary>
    /// Plays the procedurally generated sound bank. One-shots come from a small source pool; loops
    /// (ambience, tension, strain) have their own sources with fades. Sounds caused by the second cursor
    /// are panned to where it is on screen and paired with a physical mouse click - you hear a mouse you
    /// are not clicking. Phase M: a free source is taken first and long or climactic sounds are never cut, the room can
    /// drop out without stopping, a stinger clears the mix around itself, and every request is recorded for the test bridge.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        const int PoolSize = 16;
        /// <summary>Clips generated from several seeds (a keystroke or a mouse button never repeats one sample).</summary>
        const int VariantCount = 4;
        static readonly string[] VariedIds = { "key_tap", "mouse_click" };
        /// <summary>One-shots a newer sound may never cut when every source is busy.</summary>
        static readonly HashSet<string> Protected = new HashSet<string>
            { "scare_hit", "scare_hit_soft", "sub_swell", "crt_whine_rise", "breath_near", "end_tone", "sys_startup", "power_down" };
        /// <summary>Story sounds an ambient scare must not crowd (<see cref="ScareRules.EventNearSeconds"/>).</summary>
        static readonly HashSet<string> EventIds = new HashSet<string>
            { "glitch_burst", "entity_appear", "low_thump", "grab_snap", "phone_ring", "door_distant", "footstep_distant", "step_near", "static_burst" };

        sealed class Loop
        {
            public AudioSource Source;
            /// <summary>Call volume x default; ambience is scaled by the room level on top.</summary>
            public float Base, Target;
            public float FadeSpeed;
            public bool StopWhenSilent;
        }

        // Synthesized once per session and shared: a restarted shift reuses the clips instead of leaking a new set.
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, AudioClip[]> Variants = new Dictionary<string, AudioClip[]>();
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly string[] _voiceIds = new string[PoolSize];
        readonly float[] _voiceStarted = new float[PoolSize];
        readonly Dictionary<string, Loop> _loops = new Dictionary<string, Loop>();
        AudioSource _pauseVoice;
        int _next;
        float _ambienceLevel = 1f, _exclusiveUntil = -1f;
        string[] _exclusiveAllowed = Array.Empty<string>();

        public float MasterVolume = 0.9f;
        public bool Ready { get; private set; }
        /// <summary>Phase M: sounds with no cursor behind them (toasts, message boxes) are dropped, e.g. through a climax.</summary>
        public bool UiMuted;
        /// <summary>
        /// Phase M (click_wrong): until <see cref="ArmClickAnswer"/>'s time runs out, the player's next click is answered by a second one out
        /// in the room, if <see cref="ClickAnswerAllowed"/> says so at that moment (never over a pause, a dialog or a tug).
        /// </summary>
        public Func<bool> ClickAnswerAllowed;
        public event Action ClickAnswered;
        float _answerUntil = -1f;

        public void ArmClickAnswer(float seconds) => _answerUntil = seconds > 0f ? Time.time + seconds : -1f;

        public bool ClickAnswerArmed => Time.time < _answerUntil;
        /// <summary>Game time of the last keystroke sound, the last story event sound and the last stinger (the scare gate reads them).</summary>
        public float LastKeyAt { get; private set; } = -100f;
        public float LastEventAt { get; private set; } = -100f;
        public float LastStingerAt { get; private set; } = -100f;

        public static AudioManager Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var am = go.AddComponent<AudioManager>();
            for (int i = 0; i < PoolSize; i++) am._pool.Add(am.NewSource());
            am._limiterSeen = MasterLimiter.EngagedSamples;   // the counters are static: only what happens from here is news
            Sfx.Handler = am.PlayFor;
            return am;
        }

        AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        /// <summary>Synthesizes every clip, a few per frame so boot never hitches.</summary>
        public IEnumerator GenerateAll()
        {
            float budget = 0.012f;
            float start = Time.realtimeSinceStartup;
            foreach (var id in ProceduralSoundBank.Ids)
            {
                if (Clips.TryGetValue(id, out var existing) && existing != null) continue;
                int seeds = Array.IndexOf(VariedIds, id) >= 0 ? VariantCount : 1;
                var set = new AudioClip[seeds];
                for (int s = 0; s < seeds; s++)
                {
                    set[s] = Synthesize(id, s + 1);
                    if (Time.realtimeSinceStartup - start > budget)
                    {
                        yield return null;
                        start = Time.realtimeSinceStartup;
                    }
                }
                if (set[0] == null) continue;
                Clips[id] = set[0];
                if (seeds > 1) Variants[id] = Array.FindAll(set, c => c != null);
            }
            Ready = true;
            GameLog.Info(LogChannel.Audio, "Sound bank ready (" + Clips.Count + " sounds)");
        }

        static AudioClip Synthesize(string id, int seed)
        {
            float[] data;
            try
            {
                data = ProceduralSoundBank.Generate(id, seed);
            }
            catch (Exception e)
            {
                GameLog.Error(LogChannel.Audio, "Could not generate '" + id + "': " + e.Message);
                return null;
            }
            if (data == null || data.Length == 0) return null;
            var clip = AudioClip.Create(seed == 1 ? id : id + "#" + seed, data.Length, 1, ProceduralSoundBank.SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public bool Has(string id) => Clips.TryGetValue(id, out var clip) && clip != null;

        /// <summary>A clip's length in seconds (0 when it does not exist).</summary>
        public float Length(string id) => Clips.TryGetValue(id, out var clip) && clip != null ? clip.length : 0f;

        static float DefaultVolume(string id) => ProceduralSoundBank.Has(id) ? ProceduralSoundBank.DefaultVolume(id) : 0.6f;

        /// <summary>
        /// A one-shot. <paramref name="startOffset"/> skips into the clip (a sound whose peak must land on a moment).
        /// While the listener is paused (the pause menu) it plays on a source that ignores the pause.
        /// </summary>
        public void Play(string id, float volume = 1f, float pitch = 1f, float pan = 0f, float startOffset = 0f)
        {
            float v = Mathf.Clamp01(volume * DefaultVolume(id) * MasterVolume);
            if (!Clips.TryGetValue(id, out var clip) || clip == null)
            {
                Record('X', id, v, pitch, pan);
                return;
            }
            if (Time.time < _exclusiveUntil && Array.IndexOf(_exclusiveAllowed, id) < 0)
            {
                Record('M', id, v, pitch, pan);
                return;
            }
            if (Variants.TryGetValue(id, out var set)) clip = set[Random.Range(0, set.Length)];
            var src = AudioListener.pause ? PauseVoice() : Voice(id);
            src.pitch = pitch;
            src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            src.volume = v;
            src.clip = clip;
            src.time = Mathf.Clamp(startOffset, 0f, Mathf.Max(0f, clip.length - 0.05f));
            src.Play();
            Record('P', id, v, pitch, pan);
            if (id.StartsWith("key_", StringComparison.Ordinal)) LastKeyAt = Time.time;
            if (volume >= ScareRules.EventMinVolume && EventIds.Contains(id)) LastEventAt = Time.time;
        }

        /// <summary>A free source; when all are busy the oldest one that is not playing a protected sound.</summary>
        AudioSource Voice(string id)
        {
            int pick = -1;
            for (int k = 0; k < _pool.Count && pick < 0; k++)
            {
                int i = (_next + k) % _pool.Count;
                if (!_pool[i].isPlaying) pick = i;
            }
            if (pick < 0)
                for (int i = 0; i < _pool.Count; i++)
                    if (!Protected.Contains(_voiceIds[i]) && (pick < 0 || _voiceStarted[i] < _voiceStarted[pick])) pick = i;
            if (pick < 0) pick = _next;
            _next = (pick + 1) % _pool.Count;
            _voiceIds[pick] = id;
            _voiceStarted[pick] = Time.unscaledTime;
            return _pool[pick];
        }

        AudioSource PauseVoice()
        {
            if (_pauseVoice == null)
            {
                _pauseVoice = NewSource();
                _pauseVoice.ignoreListenerPause = true;
            }
            return _pauseVoice;
        }

        /// <summary>
        /// The hit (scare_hit, or scare_hit_soft with Reduce flashing), 0.10 s before the visual cut. For 1.3 s nothing else plays
        /// but <paramref name="allowedAfter"/> (the climax's own aftermath).
        /// </summary>
        public void PlayStinger(float volume, bool reduced, params string[] allowedAfter)
        {
            string id = reduced ? "scare_hit_soft" : "scare_hit";
            _exclusiveUntil = -1f;
            Play(id, volume);
            Exclusive(1.3f, allowedAfter);
            LastStingerAt = Time.time;
            GameLog.Info(LogChannel.Audio, "Stinger " + id + " v=" + (volume * DefaultVolume(id) * MasterVolume).ToString("0.00"));
        }

        /// <summary>For <paramref name="seconds"/>, drop every one-shot and loop start except <paramref name="allowed"/> (recorded as muted).</summary>
        public void Exclusive(float seconds, params string[] allowed)
        {
            _exclusiveUntil = Time.time + seconds;
            _exclusiveAllowed = allowed ?? Array.Empty<string>();
        }

        /// <summary>Stereo pan for a virtual-screen x position.</summary>
        public static float PanFor(float x) => Mathf.Clamp((x / ScreenRig.Width * 2f - 1f) * 0.75f, -0.75f, 0.75f);

        /// <summary>Everything at once (M14's dead air): a global dip through the listener, undone by <c>Duck(false)</c> and every rebuild.</summary>
        public static void Duck(bool on) => AudioListener.volume = on ? DuckVolume : 1f;

        const float DuckVolume = 0.12f;

        /// <summary>The third pointer's agent name (Gary, Night 2 on).</summary>
        public const string ThirdPointerName = "Gary";

        /// <summary>Sfx hook: UI sounds attributed to a cursor.</summary>
        void PlayFor(string id, CursorAgent agent)
        {
            if (agent == null && UiMuted)
            {
                Record('M', id, 0f, 1f, 0f);
                return;
            }
            if (agent != null && agent.IsEntity)
            {
                float pan = PanFor(agent.Position.x);
                bool gary = agent.Name == ThirdPointerName;
                if (id == "ui_click" || id == "ui_select")
                {
                    // The entity's clicks are heard as a real mouse button. M11: each pointer has its own timbre: yours
                    // is the crisp UI click, the second cursor's is hollow (lower), the third's soft and 15% quieter.
                    if (gary) Play("mouse_click", 0.9f * 0.85f, Random.Range(1.12f, 1.2f), pan);
                    else Play("mouse_click", 0.9f, Random.Range(0.8f, 0.86f), pan);
                    return;
                }
                // Phase M: Gary types faintly (the spec's held, tired man), Ellen at full strength.
                Play(id, gary && id.StartsWith("key_", StringComparison.Ordinal) ? 0.7f : 1f, Random.Range(0.92f, 1.0f), pan);
                return;
            }
            if (id == "ui_click" && agent != null && agent.IsPlayer && ClickAnswerArmed && (ClickAnswerAllowed == null || ClickAnswerAllowed()))
            {
                _answerUntil = -1f;
                Play("click_wrong");
                ClickAnswered?.Invoke();
                return;
            }
            float jitter = id.StartsWith("key_", StringComparison.Ordinal) ? Random.Range(0.94f, 1.06f) : 1f;
            Play(id, 1f, jitter, 0f);
        }

        static bool IsAmbience(string id) => id.StartsWith("amb_", StringComparison.Ordinal);

        public void PlayLoop(string id, float volume, float fadeTime = 0.4f)
        {
            if (!Clips.TryGetValue(id, out var clip) || clip == null)
            {
                Record('X', id, volume, 1f, 0f);
                return;
            }
            if (Time.time < _exclusiveUntil && Array.IndexOf(_exclusiveAllowed, id) < 0)
            {
                Record('M', id, volume, 1f, 0f);
                return;
            }
            bool created = !_loops.TryGetValue(id, out var loop);
            if (created)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.spatialBlend = 0f;
                s.clip = clip;
                s.volume = 0f;
                loop = new Loop { Source = s };
                _loops[id] = loop;
            }
            bool starting = !loop.Source.isPlaying || loop.StopWhenSilent;
            loop.Base = Mathf.Clamp01(volume * DefaultVolume(id));
            float target = loop.Base * (IsAmbience(id) ? _ambienceLevel : 1f);
            if (starting || Mathf.Abs(target - loop.Target) > 0.05f) Record('L', id, target * MasterVolume, loop.Source.pitch, loop.Source.panStereo);
            SetTarget(loop, target, fadeTime);
            loop.StopWhenSilent = false;
            if (!loop.Source.isPlaying)
            {
                // Start loops at a random point so layered ambience never phases identically.
                loop.Source.timeSamples = Random.Range(0, Mathf.Max(1, clip.samples - 1));
                loop.Source.Play();
            }
        }

        static void SetTarget(Loop loop, float target, float fadeTime)
        {
            loop.Target = target;
            loop.FadeSpeed = fadeTime <= 0f ? 1000f : 1f / fadeTime;
        }

        public void StopLoop(string id, float fadeTime = 0.3f)
        {
            if (!_loops.TryGetValue(id, out var loop)) return;
            if (loop.Source.isPlaying && !loop.StopWhenSilent) Record('S', id, 0f, loop.Source.pitch, loop.Source.panStereo);
            loop.Target = 0f;
            loop.FadeSpeed = fadeTime <= 0f ? 1000f : 1f / fadeTime;
            loop.StopWhenSilent = true;
        }

        public bool IsLoopPlaying(string id) => _loops.TryGetValue(id, out var l) && l.Source.isPlaying && !l.StopWhenSilent;

        public void SetLoopVolume(string id, float volume, float fadeTime = 0.1f)
        {
            if (!_loops.TryGetValue(id, out var loop)) return;
            // Not recorded: the entity's static and the tug's strain move their volume every frame.
            loop.Base = Mathf.Clamp01(volume * DefaultVolume(id));
            SetTarget(loop, loop.Base * (IsAmbience(id) ? _ambienceLevel : 1f), fadeTime);
        }

        public void SetLoopPitch(string id, float pitch)
        {
            if (_loops.TryGetValue(id, out var loop)) loop.Source.pitch = pitch;
        }

        /// <summary>The loop's current pitch (1 when it is not playing).</summary>
        public float LoopPitch(string id) => _loops.TryGetValue(id, out var loop) ? loop.Source.pitch : 1f;

        public void SetLoopPan(string id, float pan)
        {
            if (_loops.TryGetValue(id, out var loop)) loop.Source.panStereo = Mathf.Clamp(pan, -1f, 1f);
        }

        /// <summary>The office bed: room tone, fluorescent buzz, CRT hum. Turning it on also brings the room back to full level.</summary>
        public void SetAmbience(bool on, float fadeTime = 1.5f)
        {
            if (on)
            {
                _ambienceLevel = 1f;
                PlayLoop("amb_room", 1f, fadeTime);
                PlayLoop("amb_fluorescent", 1f, fadeTime);
                PlayLoop("amb_crt_hum", 1f, fadeTime);
            }
            else
            {
                StopLoop("amb_room", fadeTime);
                StopLoop("amb_fluorescent", fadeTime);
                StopLoop("amb_crt_hum", fadeTime);
            }
        }

        /// <summary>
        /// Phase M: scales the office bed without stopping it (0 = the room drops out, the scariest sound in a quiet office;
        /// 0.5 = a scare's -6 dB duck; 1 = back). Only a bed that is playing is changed.
        /// </summary>
        public void SetAmbienceLevel(float level, float fadeTime)
        {
            _ambienceLevel = Mathf.Clamp01(level);
            Record('A', "ambience", _ambienceLevel, 1f, 0f);
            foreach (var kv in _loops)
                if (IsAmbience(kv.Key) && !kv.Value.StopWhenSilent) SetTarget(kv.Value, kv.Value.Base * _ambienceLevel, fadeTime);
        }

        public void StopAllLoops(float fadeTime = 0.5f)
        {
            foreach (var id in new List<string>(_loops.Keys)) StopLoop(id, fadeTime);
        }

        int _limiterSeen;

        void Update()
        {
            // The limiter should never work in normal play; when it does, the log names what had just played.
            int engaged = MasterLimiter.EngagedSamples;
            if (engaged < _limiterSeen) _limiterSeen = engaged;
            if (engaged > _limiterSeen)
            {
                _limiterSeen = engaged;
                GameLog.Info(LogChannel.Audio, "Limiter engaged (max input " + MasterLimiter.MaxInput.ToString("0.00") + "), just played: " + RecentOneShots(0.3f));
            }
            float dt = Time.unscaledDeltaTime;
            foreach (var kv in _loops)
            {
                var l = kv.Value;
                float v = Mathf.MoveTowards(l.Source.volume / Mathf.Max(0.0001f, MasterVolume), l.Target, l.FadeSpeed * dt);
                l.Source.volume = v * MasterVolume;
                if (l.StopWhenSilent && v <= 0.0001f && l.Source.isPlaying) l.Source.Stop();
            }
        }

        // ------------------------------------------------------------------ test record (Phase M)

        /// <summary>
        /// One audio request, kept for the test bridge. Kind: P one-shot, L loop start or target change, S loop stop, A room level,
        /// X dropped (no such clip), M muted (UI muted or a stinger's exclusive window). Volume is the final source volume.
        /// </summary>
        internal readonly struct SfxRecord
        {
            public readonly float Time;
            public readonly int Frame;
            public readonly char Kind;
            public readonly string Id;
            public readonly float Volume, Pitch, Pan;

            public SfxRecord(char kind, string id, float volume, float pitch, float pan)
            {
                Time = UnityEngine.Time.time;
                Frame = UnityEngine.Time.frameCount;
                Kind = kind;
                Id = id;
                Volume = volume;
                Pitch = pitch;
                Pan = pan;
            }
        }

        /// <summary>The last 2048 requests (static: they survive the root rebuilds of jumps and the title); index = count % 2048.</summary>
        internal static readonly SfxRecord[] History = new SfxRecord[2048];
        internal static int HistoryCount;
        /// <summary>One-shots and loop starts per id since the last clear.</summary>
        internal static readonly Dictionary<string, int> PlayCounts = new Dictionary<string, int>();

        static void Record(char kind, string id, float volume, float pitch, float pan)
        {
            History[HistoryCount++ % History.Length] = new SfxRecord(kind, id, volume, pitch, pan);
            if (kind == 'P' || kind == 'L') PlayCounts[id] = PlayCounts.TryGetValue(id, out var n) ? n + 1 : 1;
        }

        /// <summary>The one-shots of the last <paramref name="seconds"/> (id, final volume and pan).</summary>
        static string RecentOneShots(float seconds)
        {
            var parts = new List<string>();
            for (int i = HistoryCount - 1; i >= Math.Max(0, HistoryCount - History.Length); i--)
            {
                var r = History[i % History.Length];
                if (r.Time < Time.time - seconds) break;
                if (r.Kind == 'P') parts.Add(r.Id + " " + r.Volume.ToString("0.00") + (r.Pan != 0f ? " pan " + r.Pan.ToString("0.00") : ""));
            }
            return parts.Count > 0 ? string.Join(", ", parts) : "nothing new";
        }

        internal static void ClearHistory()
        {
            HistoryCount = 0;
            PlayCounts.Clear();
        }

        /// <summary>Every loop: id, playing, target, current volume, pitch, pan (the bridge's sfxloops).</summary>
        internal IEnumerable<string> DescribeLoops()
        {
            foreach (var kv in _loops)
            {
                var s = kv.Value.Source;
                yield return kv.Key + " playing=" + (s.isPlaying && !kv.Value.StopWhenSilent) + " target=" + (kv.Value.Target * MasterVolume).ToString("0.000")
                    + " volume=" + s.volume.ToString("0.000") + " pitch=" + s.pitch.ToString("0.00") + " pan=" + s.panStereo.ToString("0.00");
            }
        }
    }
}
