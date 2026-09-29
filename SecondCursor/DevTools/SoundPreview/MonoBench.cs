// Times ProceduralSoundBank on the Mono JIT, the runtime behind the Unity editor (see bench-mono.sh).
// Not part of SoundPreview.csproj (which compiles only Program.cs).

using System;
using System.Diagnostics;
using SecondCursor.Core.Audio;

internal static class MonoBench
{
    private static int Main()
    {
        var ids = ProceduralSoundBank.Ids;
        var sw = Stopwatch.StartNew();
        long samples = 0;
        foreach (string id in ids) samples += ProceduralSoundBank.Generate(id).Length;
        double cold = sw.Elapsed.TotalMilliseconds;

        double warm = double.MaxValue;
        for (int rep = 0; rep < 3; rep++)
        {
            sw.Reset();
            sw.Start();
            foreach (string id in ids) ProceduralSoundBank.Generate(id);
            warm = Math.Min(warm, sw.Elapsed.TotalMilliseconds);
        }

        foreach (string id in ids)
        {
            float peak = 0f;
            foreach (float v in ProceduralSoundBank.Generate(id))
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) { Console.WriteLine(id + ": NaN/Inf"); return 1; }
                peak = Math.Max(peak, Math.Abs(v));
            }
            if (peak > 0.9f) { Console.WriteLine(id + ": peak " + peak); return 1; }
        }
        Console.WriteLine("Mono: {0} sounds, {1} samples; cold (first call incl. JIT) {2:0} ms, warm {3:0} ms",
                          ids.Count, samples, cold, warm);
        return 0;
    }
}
