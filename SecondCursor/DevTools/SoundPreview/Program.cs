// SoundPreview: renders every ProceduralSoundBank id to 16-bit WAV, times generation and prints
// per-sound diagnostics (duration, peak, RMS, DC offset, loop-seam continuity) plus sanity checks.
//
//   dotnet run -c Release --project DevTools/SoundPreview [outDir]

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SecondCursor.Core.Audio;

internal static class Program
{
    private const int Sr = ProceduralSoundBank.SampleRate;

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string outDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(FindProjectDir(), "out");
        Directory.CreateDirectory(outDir);
        var failures = new List<string>();
        IReadOnlyList<string> ids = ProceduralSoundBank.Ids;

        // ---- cold pass: first call of every generator (includes JIT) - what the game pays at start-up
        var buffers = new Dictionary<string, float[]>();
        var coldMs = new Dictionary<string, double>();
        var total = Stopwatch.StartNew();
        foreach (string id in ids)
        {
            var sw = Stopwatch.StartNew();
            buffers[id] = ProceduralSoundBank.Generate(id);
            coldMs[id] = sw.Elapsed.TotalMilliseconds;
        }
        double coldTotal = total.Elapsed.TotalMilliseconds;

        // ---- warm passes: steady-state cost
        var warmMs = ids.ToDictionary(id => id, id => double.MaxValue);
        double warmTotal = double.MaxValue;
        for (int rep = 0; rep < 5; rep++)
        {
            total.Restart();
            foreach (string id in ids)
            {
                var sw = Stopwatch.StartNew();
                ProceduralSoundBank.Generate(id);
                warmMs[id] = Math.Min(warmMs[id], sw.Elapsed.TotalMilliseconds);
            }
            warmTotal = Math.Min(warmTotal, total.Elapsed.TotalMilliseconds);
        }

        // ---- API checks
        foreach (string id in ids)
        {
            if (!ProceduralSoundBank.Has(id)) failures.Add($"Has({id}) is false");
            float[] again = ProceduralSoundBank.Generate(id);
            if (!again.SequenceEqual(buffers[id])) failures.Add($"{id}: not deterministic");
        }
        if (ProceduralSoundBank.Has("nope") || ProceduralSoundBank.Has(null)) failures.Add("Has() accepts unknown ids");
        try { ProceduralSoundBank.Generate("nope"); failures.Add("Generate(unknown) did not throw"); }
        catch (ArgumentException) { }
        // thread safety: generating everything concurrently must give the same buffers
        var parallel = new System.Collections.Concurrent.ConcurrentDictionary<string, float[]>();
        System.Threading.Tasks.Parallel.ForEach(ids, id => parallel[id] = ProceduralSoundBank.Generate(id));
        foreach (string id in ids)
            if (!parallel[id].SequenceEqual(buffers[id])) failures.Add($"{id}: differs when generated concurrently");
        var keyVariants = Enumerable.Range(0, 6).Select(s => ProceduralSoundBank.Generate("key_tap", s)).ToList();
        for (int a = 0; a < keyVariants.Count; a++)
            for (int b = a + 1; b < keyVariants.Count; b++)
                if (keyVariants[a].SequenceEqual(keyVariants[b])) failures.Add($"key_tap seeds {a} and {b} are identical");

        // ---- per-sound report
        var sb = new StringBuilder();
        sb.AppendLine($"{"id",-17} {"loop",4} {"dur s",6} {"peak",6} {"peakdB",7} {"rmsdB",7} {"stRms",7} {"mixdB",7} {"dc",9} " +
                      $"{"edge/seam",10} {"stepRatio",9} {"seamRmsdB",9} {"seam pct d1/d2/rms",19} {"cold ms",8} {"warm ms",8}");
        double totalSeconds = 0;
        foreach (string id in ids)
        {
            float[] x = buffers[id];
            bool loop = ProceduralSoundBank.IsLoop(id);
            float vol = ProceduralSoundBank.DefaultVolume(id);
            int n = x.Length;
            double dur = (double)n / Sr;
            totalSeconds += dur;
            double peak = 0, sum = 0, sumSq = 0;
            bool finite = true;
            foreach (float v in x)
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) finite = false;
                peak = Math.Max(peak, Math.Abs(v));
                sum += v;
                sumSq += (double)v * v;
            }
            double rms = Math.Sqrt(sumSq / n);
            double rmsDb = 20 * Math.Log10(Math.Max(rms, 1e-12));
            double dc = sum / n;
            double stRmsDb = 20 * Math.Log10(Math.Max(MaxShortTermRms(x, Ms(50)), 1e-12));
            double mixDb = rmsDb + 20 * Math.Log10(vol);

            string edge, stepRatio = "", seamRms = "", seamPct = "";
            if (loop)
            {
                // Sample level: the seam's first and second differences, ranked among every boundary of the
                // (circular) loop. A seamless loop's seam is an ordinary boundary, not an outlier.
                double jump = Math.Abs(x[0] - x[n - 1]);
                double curv = Math.Abs(x[1] - 2 * x[0] + x[n - 1]);
                var d1 = new double[n];
                var d2 = new double[n];
                double meanStep = 0;
                for (int i = 0; i < n; i++)
                {
                    float prev = x[(i + n - 1) % n], next = x[(i + 1) % n];
                    d1[i] = Math.Abs(x[i] - prev);
                    d2[i] = Math.Abs(next - 2 * x[i] + prev);
                    meanStep += d1[i];
                }
                meanStep /= n;
                double p1 = Percentile(d1, jump), p2 = Percentile(d2, curv);
                double ratio = jump / Math.Max(meanStep, 1e-12);
                // Envelope level: 100 ms RMS either side of the seam vs. every adjacent 100 ms window pair
                // (long enough that 60 Hz ripple or a rhythmic loop starting on a stroke is not a "level jump").
                int w = Ms(100);
                double seamDb = Db(Rms(x, 0, w)) - Db(Rms(x, n - w, w));
                var pairs = new List<double>();
                for (int s = w; s + w <= n; s += w) pairs.Add(Math.Abs(Db(Rms(x, s, w)) - Db(Rms(x, s - w, w))));
                double pRms = Percentile(pairs.ToArray(), Math.Abs(seamDb));
                double maxOther = pairs.Count > 0 ? pairs.Max() : 0;
                edge = jump.ToString("0.00000");
                stepRatio = ratio.ToString("0.00");
                seamRms = seamDb.ToString("+0.00;-0.00");
                seamPct = $"{p1,5:0.0}/{p2,5:0.0}/{pRms,5:0.0}";
                if (p2 >= 99.9 && curv > 3 * Median(d2)) failures.Add($"{id}: seam curvature is an outlier ({p2:0.00} pct)");
                if (Math.Abs(seamDb) > 3 && Math.Abs(seamDb) > maxOther + 1)
                    failures.Add($"{id}: seam level jump {seamDb:0.0} dB exceeds every other window step ({maxOther:0.0} dB)");
                if (dur < 0.99 || dur > 8.01) failures.Add($"{id}: loop length {dur:0.00}s outside 1-8 s");
            }
            else
            {
                double e = Math.Max(Math.Abs(x[0]), Math.Abs(x[n - 1]));
                edge = e.ToString("0.00000");
                if (e > 1e-3) failures.Add($"{id}: one-shot does not start/end at zero ({e:0.0000})");
            }
            if (!finite) failures.Add($"{id}: NaN/Inf");
            if (peak > 0.9) failures.Add($"{id}: peak {peak:0.000} > 0.9");
            if (Math.Abs(dc) > 1e-3) failures.Add($"{id}: DC offset {dc:0.00000}");

            sb.AppendLine($"{id,-17} {(loop ? "yes" : ""),4} {dur,6:0.000} {peak,6:0.000} {20 * Math.Log10(peak),7:0.0} {rmsDb,7:0.0} {stRmsDb,7:0.0} " +
                          $"{mixDb,7:0.0} {dc,9:0.0e0} {edge,10} {stepRatio,9} {seamRms,9} {seamPct,19} {coldMs[id],8:0.0} {warmMs[id],8:0.00}");

            WriteWav(Path.Combine(outDir, id + ".wav"), x);
        }
        for (int s = 1; s < 4; s++) WriteWav(Path.Combine(outDir, $"key_tap_seed{s}.wav"), keyVariants[s]);

        sb.AppendLine();
        sb.AppendLine($"sounds: {ids.Count}   total audio: {totalSeconds:0.0} s ({totalSeconds * Sr / 1e6:0.00} M samples)");
        sb.AppendLine($"generation, cold (first call incl. JIT): {coldTotal:0.0} ms");
        sb.AppendLine($"generation, warm (best of 5):             {warmTotal:0.0} ms");
        if (coldTotal > 1000) failures.Add($"cold generation {coldTotal:0} ms > 1000 ms");

        Console.Write(sb.ToString());
        File.WriteAllText(Path.Combine(outDir, "report.txt"), sb.ToString());
        Console.WriteLine();
        if (failures.Count == 0)
        {
            Console.WriteLine("ALL CHECKS PASSED");
            return 0;
        }
        Console.WriteLine($"{failures.Count} CHECK(S) FAILED:");
        foreach (string f in failures) Console.WriteLine("  - " + f);
        return 1;
    }

    private static int Ms(double ms) => (int)(ms * 0.001 * Sr);

    private static double Rms(float[] x, int start, int len)
    {
        double s = 0;
        for (int i = start; i < start + len; i++) s += (double)x[i] * x[i];
        return Math.Sqrt(s / len);
    }

    private static double Db(double v) => 20 * Math.Log10(Math.Max(v, 1e-9));

    /// <summary>Percentage of values strictly below v.</summary>
    private static double Percentile(double[] values, double v) => 100.0 * values.Count(a => a < v) / values.Length;

    private static double Median(double[] values)
    {
        var s = (double[])values.Clone();
        Array.Sort(s);
        return s[s.Length / 2];
    }

    private static double MaxShortTermRms(float[] x, int window)
    {
        if (x.Length <= window) return Rms(x, 0, x.Length);
        double s = 0, best = 0;
        for (int i = 0; i < x.Length; i++)
        {
            s += (double)x[i] * x[i];
            if (i >= window) s -= (double)x[i - window] * x[i - window];
            if (i >= window - 1) best = Math.Max(best, s);
        }
        return Math.Sqrt(Math.Max(best, 0) / window);
    }

    private static void WriteWav(string path, float[] x)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var w = new BinaryWriter(fs);
        int dataBytes = x.Length * 2;
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt "));
        w.Write(16);
        w.Write((short)1);      // PCM
        w.Write((short)1);      // mono
        w.Write(Sr);
        w.Write(Sr * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (float v in x)
            w.Write((short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f));
    }

    private static string FindProjectDir()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "SoundPreview.csproj")))
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return dir ?? Directory.GetCurrentDirectory();
    }
}
