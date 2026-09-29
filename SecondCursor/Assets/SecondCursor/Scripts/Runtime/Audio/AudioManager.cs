using System.Collections;
using System.Collections.Generic;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using SecondCursor.Input;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.Audio
{
    /// <summary>
    /// Plays the procedurally generated sound bank. One-shots come from a small source pool; loops
    /// (ambience, tension, strain) have their own sources with fades. Sounds caused by the second cursor
    /// are panned to where it is on screen and paired with a physical mouse click - you hear a mouse you
    /// are not clicking.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        const int PoolSize = 12;

        sealed class Loop
        {
            public AudioSource Source;
            public float Target;
            public float FadeSpeed;
            public bool StopWhenSilent;
        }

        // Synthesized once per session and shared: a restarted shift reuses the clips instead of leaking a new set.
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> _pool = new List<AudioSource>();
        readonly Dictionary<string, Loop> _loops = new Dictionary<string, Loop>();
        int _next;

        public float MasterVolume = 0.9f;
        public bool Ready { get; private set; }

        public static AudioManager Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var am = go.AddComponent<AudioManager>();
            for (int i = 0; i < PoolSize; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                am._pool.Add(s);
            }
            Sfx.Handler = am.PlayFor;
            return am;
        }

        /// <summary>Synthesizes every clip, a few per frame so boot never hitches.</summary>
        public IEnumerator GenerateAll()
        {
            float budget = 0.012f;
            float start = Time.realtimeSinceStartup;
            foreach (var id in ProceduralSoundBank.Ids)
            {
                if (Clips.TryGetValue(id, out var existing) && existing != null) continue;
                float[] data;
                try
                {
                    data = ProceduralSoundBank.Generate(id, 1);
                }
                catch (System.Exception e)
                {
                    GameLog.Error(LogChannel.Audio, "Could not generate '" + id + "': " + e.Message);
                    continue;
                }
                if (data == null || data.Length == 0) continue;
                var clip = AudioClip.Create(id, data.Length, 1, ProceduralSoundBank.SampleRate, false);
                clip.SetData(data, 0);
                Clips[id] = clip;
                if (Time.realtimeSinceStartup - start > budget)
                {
                    yield return null;
                    start = Time.realtimeSinceStartup;
                }
            }
            Ready = true;
            GameLog.Info(LogChannel.Audio, "Sound bank ready (" + Clips.Count + " sounds)");
        }

        public bool Has(string id) => Clips.TryGetValue(id, out var clip) && clip != null;

        static float DefaultVolume(string id) => ProceduralSoundBank.Has(id) ? ProceduralSoundBank.DefaultVolume(id) : 0.6f;

        public void Play(string id, float volume = 1f, float pitch = 1f, float pan = 0f)
        {
            if (!Clips.TryGetValue(id, out var clip) || clip == null) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            src.pitch = pitch;
            src.panStereo = Mathf.Clamp(pan, -1f, 1f);
            src.volume = Mathf.Clamp01(volume * DefaultVolume(id) * MasterVolume);
            src.clip = clip;
            src.Play();
        }

        /// <summary>Stereo pan for a virtual-screen x position.</summary>
        public static float PanFor(float x) => Mathf.Clamp((x / ScreenRig.Width * 2f - 1f) * 0.75f, -0.75f, 0.75f);

        /// <summary>Sfx hook: UI sounds attributed to a cursor.</summary>
        void PlayFor(string id, CursorAgent agent)
        {
            if (agent != null && agent.IsEntity)
            {
                float pan = PanFor(agent.Position.x);
                if (id == "ui_click" || id == "ui_select")
                {
                    // The entity's clicks are heard as a real mouse button.
                    Play("mouse_click", 0.9f, Random.Range(0.95f, 1.05f), pan);
                    return;
                }
                Play(id, 1f, Random.Range(0.92f, 1.0f), pan);
                return;
            }
            float jitter = id.StartsWith("key_") ? Random.Range(0.94f, 1.06f) : 1f;
            Play(id, 1f, jitter, 0f);
        }

        public void PlayLoop(string id, float volume, float fadeTime = 0.4f)
        {
            if (!Clips.TryGetValue(id, out var clip) || clip == null) return;
            if (!_loops.TryGetValue(id, out var loop))
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
            loop.Target = Mathf.Clamp01(volume * DefaultVolume(id));
            loop.FadeSpeed = fadeTime <= 0f ? 1000f : 1f / fadeTime;
            loop.StopWhenSilent = false;
            if (!loop.Source.isPlaying)
            {
                // Start loops at a random point so layered ambience never phases identically.
                loop.Source.timeSamples = Random.Range(0, Mathf.Max(1, clip.samples - 1));
                loop.Source.Play();
            }
        }

        public void StopLoop(string id, float fadeTime = 0.3f)
        {
            if (!_loops.TryGetValue(id, out var loop)) return;
            loop.Target = 0f;
            loop.FadeSpeed = fadeTime <= 0f ? 1000f : 1f / fadeTime;
            loop.StopWhenSilent = true;
        }

        public bool IsLoopPlaying(string id) => _loops.TryGetValue(id, out var l) && l.Source.isPlaying && !l.StopWhenSilent;

        public void SetLoopVolume(string id, float volume, float fadeTime = 0.1f)
        {
            if (!_loops.TryGetValue(id, out var loop)) return;
            loop.Target = Mathf.Clamp01(volume * DefaultVolume(id));
            loop.FadeSpeed = fadeTime <= 0f ? 1000f : 1f / fadeTime;
        }

        public void SetLoopPitch(string id, float pitch)
        {
            if (_loops.TryGetValue(id, out var loop)) loop.Source.pitch = pitch;
        }

        public void SetLoopPan(string id, float pan)
        {
            if (_loops.TryGetValue(id, out var loop)) loop.Source.panStereo = Mathf.Clamp(pan, -1f, 1f);
        }

        /// <summary>The office bed: room tone, fluorescent buzz, CRT hum.</summary>
        public void SetAmbience(bool on, float fadeTime = 1.5f)
        {
            if (on)
            {
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

        public void StopAllLoops(float fadeTime = 0.5f)
        {
            foreach (var id in new List<string>(_loops.Keys)) StopLoop(id, fadeTime);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var kv in _loops)
            {
                var l = kv.Value;
                float v = Mathf.MoveTowards(l.Source.volume / Mathf.Max(0.0001f, MasterVolume), l.Target, l.FadeSpeed * dt);
                l.Source.volume = v * MasterVolume;
                if (l.StopWhenSilent && v <= 0.0001f && l.Source.isPlaying) l.Source.Stop();
            }
        }
    }
}
