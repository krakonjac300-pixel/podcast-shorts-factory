// ============================================================================
//  SECOND CURSOR - ProceduralSoundBank
//  Every sound in the game is synthesised here, from code, at start-up.
//  Pure C# (no UnityEngine), deterministic (own PCG32 PRNG), mono 44.1 kHz.
//
//  Layout (a partial class, one file per family since Phase Q4):
//    ProceduralSoundBank.cs                 public API and the registry
//    ProceduralSoundBank.Dsp.*.cs           the DSP toolkit (PRNG, oscillators, filters, envelopes, buffers, finishing)
//    ProceduralSoundBank.Ui / Hardware / Ambience / Entity / Building .cs   the sound designs, one method per id
//    ProceduralSoundBank.Scares.cs          the Phase M scares
//
//  Design rule: the player's own actions sound clean, dry and digital (ui_*);
//  everything the entity does is physical, in the room, or electrically "wrong".
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace SecondCursor.Core.Audio
{
    /// <summary>
    /// Procedural sound bank for SECOND CURSOR. Every effect and ambience is synthesised on demand
    /// (there are no audio assets). Output is mono float PCM at <see cref="SampleRate"/>,
    /// peak &lt;= 0.9, DC-free; one-shots are de-clicked and loops are seamless.
    /// Generation is deterministic for a given (id, seed) pair and thread-safe.
    /// </summary>
    public static partial class ProceduralSoundBank
    {
        /// <summary>Sample rate of every generated buffer, in Hz.</summary>
        public const int SampleRate = 44100;

        /// <summary>All sound ids in a stable order.</summary>
        public static IReadOnlyList<string> Ids => IdList;

        /// <summary>True if <paramref name="id"/> names a sound in the bank.</summary>
        public static bool Has(string id) => id != null && Index.ContainsKey(id);

        /// <summary>True if the sound is built to loop seamlessly.</summary>
        /// <exception cref="ArgumentException">Unknown id.</exception>
        public static bool IsLoop(string id) => Find(id).Loop;

        /// <summary>Suggested AudioSource volume (0..1). Relative loudness is already baked into the PCM.</summary>
        /// <exception cref="ArgumentException">Unknown id.</exception>
        public static float DefaultVolume(string id) => Find(id).Volume;

        /// <summary>
        /// Synthesises a sound as mono PCM in [-1, 1] at <see cref="SampleRate"/>.
        /// Different seeds give natural variations (e.g. every keystroke differs slightly).
        /// </summary>
        /// <exception cref="ArgumentException">Unknown id.</exception>
        public static float[] Generate(string id, int seed = 0)
        {
            Entry entry = Find(id);
            var rng = new Rng(Mix64(HashId(id) ^ Mix64((ulong)(uint)seed + 0x632BE59BD9B4E019UL)));
            return entry.Build(rng);
        }

        // ====================================================================
        //  1. Registry
        // ====================================================================

        private delegate float[] Builder(Rng rng);

        private sealed class Entry
        {
            public readonly string Id;
            public readonly bool Loop;
            public readonly float Volume;
            public readonly Builder Build;

            public Entry(string id, bool loop, float volume, Builder build)
            {
                Id = id; Loop = loop; Volume = volume; Build = build;
            }
        }

        private static readonly Entry[] Entries =
        {
            // --- OS / UI (the player's own world: clean, dry, digital)
            new Entry("ui_click",         false, 0.70f, UiClick),
            new Entry("ui_select",        false, 0.60f, UiSelect),
            new Entry("ui_window",        false, 0.60f, UiWindow),
            new Entry("notify_mail",      false, 0.70f, NotifyMail),
            new Entry("notify_task",      false, 0.70f, NotifyTask),
            new Entry("sys_error",        false, 0.70f, SysError),
            new Entry("sys_warning",      false, 0.70f, SysWarning),
            new Entry("sys_startup",      false, 0.80f, SysStartup),
            // --- physical input devices (the second cursor is heard through these: in the room)
            new Entry("mouse_click",      false, 0.80f, MouseClick),
            new Entry("mouse_release",    false, 0.80f, MouseRelease),
            new Entry("key_tap",          false, 0.70f, KeyTap),
            new Entry("key_space",        false, 0.70f, KeySpace),
            new Entry("key_enter",        false, 0.70f, KeyEnter),
            // --- hardware
            new Entry("bios_beep",        false, 0.55f, BiosBeep),
            new Entry("hdd_seek",         false, 0.65f, HddSeek),
            new Entry("hdd_spinup",       false, 0.65f, HddSpinup),
            new Entry("crt_on",           false, 0.75f, CrtOn),
            new Entry("crt_off",          false, 0.75f, CrtOff),
            new Entry("shred_loop",       true,  0.60f, ShredLoop),
            // --- ambience
            new Entry("amb_room",         true,  0.80f, AmbRoom),
            new Entry("amb_fluorescent",  true,  0.60f, AmbFluorescent),
            new Entry("amb_crt_hum",      true,  0.60f, AmbCrtHum),
            new Entry("drone_tension",    true,  0.70f, DroneTension),
            // --- the entity
            new Entry("entity_static",    true,  0.80f, EntityStatic),
            new Entry("entity_appear",    false, 0.80f, EntityAppear),
            new Entry("glitch_burst",     false, 0.70f, GlitchBurst),
            new Entry("tug_strain",       true,  0.70f, TugStrain),
            new Entry("grab_snap",        false, 0.80f, GrabSnap),
            // --- CCTV
            new Entry("camera_switch",    false, 0.70f, CameraSwitch),
            new Entry("camera_static",    true,  0.60f, CameraStatic),
            // --- the building
            new Entry("low_thump",        false, 0.90f, LowThump),
            new Entry("door_distant",     false, 0.90f, DoorDistant),
            new Entry("footstep_distant", false, 0.90f, FootstepDistant),
            new Entry("power_down",       false, 0.90f, PowerDown),
            new Entry("end_tone",         false, 0.80f, EndTone),
            // --- the desk phone (Night 3: Ruth calls)
            new Entry("phone_ring",       false, 0.70f, PhoneRing),
            // --- Phase M: scares (ProceduralSoundBank.Scares.cs), rare and gated; only the hits may reach volume 1.0
            new Entry("knock_door",       false, 0.80f, KnockDoor),
            new Entry("chair_creak",      false, 0.80f, ChairCreak),
            new Entry("breath_near",      false, 0.80f, BreathNear),
            new Entry("whisper_burst",    false, 0.75f, WhisperBurst),
            new Entry("key_tap_rev",      false, 0.70f, KeyTapRev),
            new Entry("click_wrong",      false, 0.70f, ClickWrong),
            new Entry("metal_scrape",     false, 0.80f, MetalScrape),
            new Entry("step_near",        false, 0.85f, StepNear),
            new Entry("sub_swell",        false, 0.90f, SubSwell),
            new Entry("crt_whine_rise",   false, 0.60f, CrtWhineRise),
            new Entry("scare_hit",        false, 1.00f, ScareHit),
            new Entry("scare_hit_soft",   false, 1.00f, ScareHitSoft),
            new Entry("ear_ring",         false, 0.60f, EarRing),
            new Entry("static_burst",     false, 0.60f, StaticBurst),
        };

        private static readonly Dictionary<string, Entry> Index = BuildIndex();
        private static readonly ReadOnlyCollection<string> IdList = BuildIdList();

        private static Dictionary<string, Entry> BuildIndex()
        {
            var d = new Dictionary<string, Entry>(Entries.Length, StringComparer.Ordinal);
            foreach (Entry e in Entries) d.Add(e.Id, e);
            return d;
        }

        private static ReadOnlyCollection<string> BuildIdList()
        {
            var ids = new string[Entries.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = Entries[i].Id;
            return Array.AsReadOnly(ids);
        }

        private static Entry Find(string id)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (!Index.TryGetValue(id, out Entry e))
                throw new ArgumentException("Unknown sound id '" + id + "'.", nameof(id));
            return e;
        }

        private static ulong HashId(string id)
        {
            ulong h = 14695981039346656037UL; // FNV-1a (string.GetHashCode is randomised per process)
            foreach (char c in id) { h ^= c; h = unchecked(h * 1099511628211UL); }
            return h;
        }

        private static ulong Mix64(ulong z)
        {
            z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
            z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
            return z ^ (z >> 31);
        }
    }
}
