using System;
using System.Collections.Generic;

namespace SecondCursor.Core.Audio
{
    /// <summary>Which side of the screen a captioned sound came from.</summary>
    public enum CaptionSide { Centre = 0, Left = 1, Right = 2 }

    /// <summary>One caption to show: the string key (<c>caption.&lt;sound id&gt;</c>), the side, and how long it stays.</summary>
    public readonly struct CaptionCue
    {
        public readonly string SoundId, Key;
        public readonly CaptionSide Side;
        public readonly float Seconds;

        public CaptionCue(string soundId, string key, CaptionSide side, float seconds)
        {
            SoundId = soundId;
            Key = key;
            Side = side;
            Seconds = seconds;
        }
    }

    /// <summary>
    /// Phase Q4 (review board A6): which sounds are captioned and when. Only the sounds that tell the story or scare (a knock, a phone,
    /// a chair, footsteps, the hits) carry a caption; the player's own clicks and keys, the notices' chimes and every loop do not.
    /// A caption shows on the frame its sound plays (so it never names a scare early), says which side the sound came from, is not
    /// repeated for the same sound within <see cref="RepeatGap"/>, and keeps the list to <see cref="MaxVisible"/>. The wording is in
    /// strings.json (<c>caption.&lt;id&gt;</c>), so a sound with no string (a Night 3 sound in the demo) is silent in text too. Engine-free.
    /// </summary>
    public sealed class CaptionRules
    {
        /// <summary>The sounds that get a caption.</summary>
        public static readonly IReadOnlyList<string> CaptionedIds = new[]
        {
            "knock_door", "chair_creak", "breath_near", "whisper_burst", "key_tap_rev", "click_wrong", "metal_scrape", "step_near",
            "sub_swell", "crt_whine_rise", "scare_hit", "scare_hit_soft", "ear_ring", "static_burst", "entity_appear", "glitch_burst",
            "low_thump", "door_distant", "footstep_distant", "phone_ring", "grab_snap", "power_down", "end_tone", "crt_off",
        };

        static readonly HashSet<string> Captioned = new HashSet<string>(CaptionedIds);

        /// <summary>A sound quieter than this (after the volume it was asked at) is not captioned.</summary>
        public const float MinVolume = 0.08f;
        /// <summary>The same sound is captioned at most this often (a run of footsteps is one caption, not five).</summary>
        public const float RepeatGap = 1.2f;
        /// <summary>Two captions start at least this far apart.</summary>
        public const float AnyGap = 0.2f;
        /// <summary>How many captions stand on screen at once.</summary>
        public const int MaxVisible = 3;
        /// <summary>Pan beyond this (either way) reads as left or right.</summary>
        public const float SideThreshold = 0.2f;
        const float MinSeconds = 2.4f, SecondsPerChar = 0.06f, MaxSeconds = 4.5f;

        readonly Dictionary<string, float> _lastById = new Dictionary<string, float>();
        float _lastAny = float.NegativeInfinity;

        public static bool IsCaptioned(string id) => id != null && Captioned.Contains(id);

        public static string KeyFor(string id) => "caption." + id;

        /// <summary>The side a pan value belongs to.</summary>
        public static CaptionSide SideFor(float pan) => pan < -SideThreshold ? CaptionSide.Left : pan > SideThreshold ? CaptionSide.Right : CaptionSide.Centre;

        /// <summary>How long a caption of <paramref name="length"/> characters stays.</summary>
        public static float SecondsFor(int length) => Math.Min(MaxSeconds, Math.Max(MinSeconds, 1.5f + SecondsPerChar * Math.Max(0, length)));

        /// <summary>
        /// The caption for a sound played now, or null: not captioned, too quiet, repeated too soon, or too close behind another caption.
        /// <paramref name="textLength"/> is the length of the wording (it sets the time on screen).
        /// </summary>
        public CaptionCue? Select(string id, float volume, float pan, float now, int textLength)
        {
            if (!IsCaptioned(id) || volume < MinVolume) return null;
            if (_lastById.TryGetValue(id, out float at) && now - at < RepeatGap) return null;
            if (now - _lastAny < AnyGap) return null;
            _lastById[id] = now;
            _lastAny = now;
            return new CaptionCue(id, KeyFor(id), SideFor(pan), SecondsFor(textLength));
        }

        /// <summary>Forgets what was shown (a new game root).</summary>
        public void Reset()
        {
            _lastById.Clear();
            _lastAny = float.NegativeInfinity;
        }

        /// <summary>The wording with its side mark: "&lt; [x]" from the left, "[x] &gt;" from the right, "[x]" from the middle.</summary>
        public static string Format(string text, CaptionSide side)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return side == CaptionSide.Left ? "< " + text : side == CaptionSide.Right ? text + " >" : text;
        }
    }
}
