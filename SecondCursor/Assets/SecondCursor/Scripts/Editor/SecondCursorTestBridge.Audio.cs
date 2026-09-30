using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SecondCursor.Audio;
using SecondCursor.Core.Audio;
using SecondCursor.Game;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Phase M bridge commands: prove each sound played (the AudioManager's request record), at what level, and never where it
    /// must not; the scare scheduler's state; a sweep of the whole bank through the real listener; and a render of the output.
    /// </summary>
    public static partial class SecondCursorTestBridge
    {
        const string AudioHelp =
            "Phase M: sfx [N] (last N audio requests) | sfxclear | sfxwait ID [TIMEOUT] | sfxexpect ID MIN MAX (latest volume) | sfxnone ID SECONDS\n" +
            "         sfxorder ID1 ID2 ... (latest records in this order, with gaps) | sfxplay ID [VOL] [PITCH] [PAN] | sfxloops | sfxsweep | sfxreport\n" +
            "         scares (budget, cooldown, skips) | scareforce ID | sfxrecord start | sfxrecord stop PATH (WAV of the limited output)\n" +
            "         shotsafter PREFIX T1 T2 ... (game view shots T seconds from now, while the next commands run)\n";

        static IEnumerator TryGameAudioCommand(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "sfx":
                {
                    var all = Records(0);
                    foreach (var r in all.Skip(Math.Max(0, all.Count - (int)F(a, 1, 20f)))) Say(Line(r));
                    return Done();
                }
                case "sfxclear":
                    AudioManager.ClearHistory();
                    MasterLimiter.ResetCounters();
                    Say("audio record cleared");
                    return Done();
                case "sfxwait": return SfxWait(a[1], F(a, 2, 30f));
                case "sfxexpect":
                {
                    var r = Records(0).LastOrDefault(x => x.Id == a[1] && (x.Kind == 'P' || x.Kind == 'L'));
                    if (r.Id == null) Say("FAIL " + a[1] + ": never played");
                    else Say((r.Volume >= F(a, 2, 0f) - 0.0005f && r.Volume <= F(a, 3, 1f) + 0.0005f ? "PASS " : "FAIL ") + Line(r));
                    return Done();
                }
                case "sfxnone": return SfxNone(a[1], F(a, 2, 5f));
                case "sfxorder": SfxOrder(a.Skip(1).ToArray()); return Done();
                case "sfxplay": g.Audio.Play(a[1], F(a, 2, 1f), F(a, 3, 1f), F(a, 4, 0f)); return Done();
                case "sfxloops":
                    foreach (var l in g.Audio.DescribeLoops()) Say(l);
                    return Done();
                case "sfxsweep": return SfxSweep(g);
                case "sfxreport": SfxReport(); return Done();
                case "shotsafter": ShotsAfter(a[1], a.Skip(2).Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray()); return Done();
                case "scares": Say(g.Scares.Describe()); return Done();
                case "scareforce": g.Scares.Force(a[1]); Say("forced " + a[1]); return Done();
                case "sfxrecord":
                    if (a.Length > 1 && a[1] == "start")
                    {
                        MasterLimiter.StartCapture();
                        Say("recording the output");
                    }
                    else SaveCapture(a.Length > 2 ? rest.Substring(rest.IndexOf(' ') + 1) : "capture.wav");
                    return Done();
                default: return null;
            }
        }

        /// <summary>Game view captures (CRT effects included) at these seconds from now, taken while the next commands run: a climax's frames.</summary>
        static void ShotsAfter(string prefix, float[] times)
        {
            Directory.CreateDirectory(ShotDir);
            double start = EditorApplication.timeSinceStartup;
            int next = 0;
            EditorApplication.CallbackFunction cb = null;
            cb = () =>
            {
                if (next >= times.Length || !EditorApplication.isPlaying) { EditorApplication.update -= cb; return; }
                if (EditorApplication.timeSinceStartup - start < times[next]) return;
                ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, prefix + "_" + times[next].ToString("0.00", CultureInfo.InvariantCulture) + ".png"));
                next++;
            };
            EditorApplication.update += cb;
            Say(times.Length + " game view shots scheduled (" + prefix + "_SECONDS.png)");
        }

        /// <summary>The records made since game time <paramref name="since"/>, oldest first.</summary>
        static List<AudioManager.SfxRecord> Records(float since)
        {
            var list = new List<AudioManager.SfxRecord>();
            int n = AudioManager.HistoryCount, size = AudioManager.History.Length;
            for (int i = Math.Max(0, n - size); i < n; i++)
            {
                var r = AudioManager.History[i % size];
                if (r.Time >= since) list.Add(r);
            }
            return list;
        }

        static string Line(AudioManager.SfxRecord r) => string.Format(CultureInfo.InvariantCulture, "t={0:0.00} f={1} {2} {3} v={4:0.000} p={5:0.00} pan={6:0.00}",
            r.Time, r.Frame, r.Kind, r.Id, r.Volume, r.Pitch, r.Pan);

        static IEnumerator SfxWait(string id, float timeout)
        {
            float since = Time.time;
            AudioManager.SfxRecord hit = default;
            var wait = WaitFor(() => (hit = Records(since).LastOrDefault(r => r.Id == id && (r.Kind == 'P' || r.Kind == 'L'))).Id != null, timeout, "sound " + id);
            while (wait.MoveNext()) yield return null;
            if (hit.Id != null) Say(Line(hit));
        }

        static IEnumerator SfxNone(string id, float seconds)
        {
            float since = Time.time;
            double end = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < end && G != null) yield return null;
            var played = Records(since).Where(r => r.Id == id && (r.Kind == 'P' || r.Kind == 'L')).ToList();
            Say(played.Count == 0 ? "PASS " + id + " not played in " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + " s" : "FAIL " + Line(played[0]));
        }

        static void SfxOrder(string[] ids)
        {
            var all = Records(0);
            float prev = float.MinValue;
            bool ok = true;
            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                var r = all.LastOrDefault(x => x.Id == id && (x.Kind == 'P' || x.Kind == 'L'));
                if (r.Id == null) { sb.Append(id).Append(" MISSING; "); ok = false; continue; }
                if (r.Time < prev) ok = false;
                sb.Append(id).Append(prev > float.MinValue ? " +" + (r.Time - prev).ToString("0.00", CultureInfo.InvariantCulture) : " t=" + r.Time.ToString("0.00", CultureInfo.InvariantCulture)).Append("; ");
                prev = r.Time;
            }
            Say((ok ? "PASS " : "FAIL ") + sb);
        }

        /// <summary>Every id in the bank, one after another through the real listener: peak and RMS of what came out.</summary>
        static IEnumerator SfxSweep(GameServices g)
        {
            g.Audio.StopAllLoops(0f);
            yield return WaitSeconds(0.4f);
            int ok = 0, total = 0;
            var buf = new float[1024];
            foreach (var id in ProceduralSoundBank.Ids)
            {
                total++;
                bool loop = ProceduralSoundBank.IsLoop(id);
                if (loop) g.Audio.PlayLoop(id, 1f, 0f);
                else g.Audio.Play(id);
                float peak = 0f;
                double sum = 0;
                int count = 0;
                // Long enough to reach each one-shot's loudest part (entity_appear peaks 0.78 s in), at most 1 s.
                float window = loop ? 0.3f : Mathf.Clamp(g.Audio.Length(id), 0.3f, 1f);
                double end = EditorApplication.timeSinceStartup + window;
                while (EditorApplication.timeSinceStartup < end)
                {
                    for (int ch = 0; ch < 2; ch++)
                    {
                        AudioListener.GetOutputData(buf, ch);
                        foreach (float v in buf) { peak = Mathf.Max(peak, Mathf.Abs(v)); sum += v * v; count++; }
                    }
                    yield return null;
                }
                float rms = (float)Math.Sqrt(sum / Math.Max(1, count));
                bool pass = rms > 0.0005f && peak < 0.95f;
                if (pass) ok++;
                Say((pass ? "PASS " : "FAIL ") + id + " peak=" + peak.ToString("0.000") + " rms=" + rms.ToString("0.0000"));
                if (loop)
                {
                    yield return WaitSeconds(0.3f);
                    g.Audio.StopLoop(id, 0f);
                }
                yield return WaitSeconds(loop ? 0.2f : Mathf.Min(1.2f, g.Audio.Length(id) - window) + 0.15f);
            }
            Say(ok + "/" + total + " audible, limiter engaged " + MasterLimiter.EngagedSamples + " frames, max input " + MasterLimiter.MaxInput.ToString("0.000"));
        }

        static void SfxReport()
        {
            var counts = AudioManager.PlayCounts;
            Say("played: " + string.Join(", ", counts.OrderBy(kv => kv.Key).Select(kv => kv.Key + " x" + kv.Value)));
            Say("never played: " + string.Join(", ", ProceduralSoundBank.Ids.Where(id => !counts.ContainsKey(id))));
            var all = Records(0);
            var quiet = all.Where(r => r.Kind == 'P' && r.Volume < 0.02f).Select(Line).ToList();
            Say("one-shots under 0.02: " + (quiet.Count == 0 ? "none" : string.Join(" | ", quiet.Take(8))));
            var floods = all.Where(r => r.Kind == 'P' && !r.Id.StartsWith("key_", StringComparison.Ordinal))
                .GroupBy(r => r.Id + "@" + Mathf.FloorToInt(r.Time)).Where(grp => grp.Count() > 15).Select(grp => grp.Key + " x" + grp.Count()).ToList();
            Say("floods (> 15 a second, typing aside): " + (floods.Count == 0 ? "none" : string.Join(", ", floods)));
            var muted = all.Where(r => r.Kind == 'M' || r.Kind == 'X').Select(Line).ToList();
            Say("muted or missing: " + (muted.Count == 0 ? "none" : string.Join(" | ", muted.Take(10))));
            Say("limiter engaged " + MasterLimiter.EngagedSamples + " frames, max input " + MasterLimiter.MaxInput.ToString("0.000"));
        }

        /// <summary>Writes what the listener put out since <c>sfxrecord start</c> as a 16-bit WAV (relative paths go to the bridge folder).</summary>
        static void SaveCapture(string path)
        {
            var data = MasterLimiter.StopCapture();
            if (data == null || data.Length == 0) { Say("ERROR: nothing recorded (sfxrecord start first)"); return; }
            if (!Path.IsPathRooted(path)) path = Path.Combine(Dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            int channels = MasterLimiter.CaptureChannels, rate = AudioSettings.outputSampleRate;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + data.Length * 2);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16);
                w.Write((short)1);
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);
                w.Write((short)(channels * 2));
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(data.Length * 2);
                float peak = 0f;
                foreach (float v in data)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                    w.Write((short)Mathf.Round(Mathf.Clamp(v, -1f, 1f) * 32767f));
                }
                Say("wrote " + path + " (" + (data.Length / (float)channels / rate).ToString("0.0") + " s, " + channels + " ch, " + rate + " Hz, peak "
                    + (20f * Mathf.Log10(Mathf.Max(peak, 1e-9f))).ToString("0.0") + " dBFS)");
            }
        }
    }
}
