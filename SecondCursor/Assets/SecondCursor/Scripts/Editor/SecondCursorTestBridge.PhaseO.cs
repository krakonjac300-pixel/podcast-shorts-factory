using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SecondCursor.Audio;
using SecondCursor.Core.Game;
using SecondCursor.FX;
using SecondCursor.Game;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>Phase O bridge commands: cold sound bank starts, fault injection, frame rate targets and flash event counts.</summary>
    public static partial class SecondCursorTestBridge
    {
        const string PhaseOHelp =
            "Phase O: soundcold (forget the generated sounds: the next Play generates the bank again) | fault beat|app|click [TIMES] (the next TIMES passes through that catch point throw)\n" +
            "         fps N (frame rate cap now: 0 = VSync, 30, 60, 120, 144, 240) | flashrate SECONDS [LABEL] (count the flash events and what the budget said, and write them to LABEL.txt in the bridge folder)\n" +
            "         flashwatch start [LABEL] | flashwatch stop (the same count, while the commands between run: a tug, a scene)\n";

        static IEnumerator TryEditorPhaseOCommand(string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "soundcold":
                    AudioManager.ClearCache();
                    Say("sound clips forgotten: the next start generates the bank");
                    return Done();
                default: return null;
            }
        }

        static IEnumerator TryGamePhaseOCommand(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "fault":
                {
                    string site = a.Length > 1 ? a[1] : "beat";
                    int times = (int)F(a, 2, 1f);
                    FaultInjector.Arm(site, times);
                    Say("armed: the next " + times + " pass(es) through '" + site + "' throw");
                    return Done();
                }
                case "fps":
                    DisplaySettings.ApplyFrameRate((int)F(a, 1, 60f));
                    Say("frame rate " + DisplaySettings.FrameRate + " (vSyncCount " + QualitySettings.vSyncCount + ", target " + Application.targetFrameRate + ")");
                    return Done();
                case "flashrate": return FlashRate(F(a, 1, 30f), a.Length > 2 ? a[2] : "flash");
                case "flashwatch":
                    if (a.Length > 1 && a[1] == "stop") FlashWatchStop();
                    else FlashWatchStart(a.Length > 2 ? a[2] : "flash");
                    return Done();
                default: return TryGameHaulCommand(g, cmd, a, rest);
            }
        }

        static List<(float t, string kind, FlashVerdict verdict)> _watchEvents;
        static Action<string, FlashVerdict> _watchOn;
        static string _watchLabel;
        static float _watchT0;
        static int _watchF0;

        static void FlashWatchStart(string label)
        {
            FlashWatchStop();
            var events = new List<(float t, string kind, FlashVerdict verdict)>();
            _watchEvents = events;
            _watchOn = (kind, verdict) => events.Add((Time.unscaledTime, kind, verdict));
            _watchLabel = label;
            _watchT0 = Time.unscaledTime;
            _watchF0 = Time.frameCount;
            VisualFx.FlashEvent += _watchOn;
            Say("counting flash events as '" + label + "'");
        }

        static void FlashWatchStop()
        {
            if (_watchOn == null) return;
            VisualFx.FlashEvent -= _watchOn;
            _watchOn = null;
            ReportFlashEvents(_watchEvents, _watchLabel, Time.unscaledTime - _watchT0, Time.frameCount - _watchF0, _watchT0);
        }

        /// <summary>Counts every flash event for a while: how many, what the budget said, the most in any second, and the frame rate it ran at.</summary>
        static IEnumerator FlashRate(float seconds, string label)
        {
            FlashWatchStart(label);
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end) yield return null;
            FlashWatchStop();
        }

        static void ReportFlashEvents(List<(float t, string kind, FlashVerdict verdict)> events, string label, float span, int frames, float t0)
        {
            var full = events.Where(e => e.verdict == FlashVerdict.Full).Select(e => e.t).ToList();
            var all = events.Select(e => e.t).ToList();
            Say(label + ": " + span.ToString("0.0", CultureInfo.InvariantCulture) + " s, " + frames + " frames (" + (frames / Mathf.Max(0.001f, span)).ToString("0.0", CultureInfo.InvariantCulture) + " fps), target "
                + Application.targetFrameRate + " vsync " + QualitySettings.vSyncCount);
            Say("events asked " + events.Count + " (" + (events.Count / Mathf.Max(0.001f, span)).ToString("0.00", CultureInfo.InvariantCulture) + "/s): full " + full.Count + ", softened "
                + events.Count(e => e.verdict == FlashVerdict.Soft) + ", dropped " + events.Count(e => e.verdict == FlashVerdict.Dropped)
                + " | by kind: " + string.Join(", ", events.GroupBy(e => e.kind).Select(gr => gr.Key + " " + gr.Count())));
            Say("most events asked in any 1 s: " + MaxInWindow(all, 1f) + ", most full-strength events in any 1 s: " + MaxInWindow(full, 1f) + " (budget: 3)");
            var sb = new StringBuilder();
            foreach (var e in events) sb.Append((e.t - t0).ToString("0.000", CultureInfo.InvariantCulture)).Append(' ').Append(e.kind).Append(' ').Append(e.verdict).Append('\n');
            File.WriteAllText(Path.Combine(Dir, label + ".txt"), sb.ToString());
        }

        static int MaxInWindow(List<float> times, float window)
        {
            int best = 0;
            for (int i = 0; i < times.Count; i++)
            {
                int n = 0;
                for (int j = i; j < times.Count && times[j] < times[i] + window; j++) n++;
                best = Math.Max(best, n);
            }
            return best;
        }
    }
}
