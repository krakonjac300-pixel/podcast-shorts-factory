using System;
using System.Collections.Generic;
using System.Threading;
using SecondCursor.Core;
using SecondCursor.Core.Audio;

namespace SecondCursor.Audio
{
    /// <summary>
    /// Synthesizes the sound bank's samples on worker threads. The generator (<see cref="ProceduralSoundBank"/>) is engine-free,
    /// deterministic per (id, seed) and thread-safe, so a few threads share the list, the longest sounds first; the AudioClips are created
    /// afterwards on the main thread (Unity objects belong to it). Workers never touch Unity or <see cref="GameLog"/>.
    /// </summary>
    internal sealed class SoundBankBuilder
    {
        internal sealed class Job
        {
            public string Id;
            public int Seed;
            public float[] Data;
            public string Error;
            /// <summary>Set by a worker once <see cref="Data"/> (or <see cref="Error"/>) is written.</summary>
            public volatile bool Done;
            /// <summary>The main thread has made this job's clip.</summary>
            public bool Taken;
        }

        /// <summary>The sounds that take longest to synthesize, in order (measured): started first, so the last worker is not left with one of them.</summary>
        static readonly string[] Longest =
            { "drone_tension", "sys_startup", "end_tone", "scare_hit", "power_down", "scare_hit_soft", "metal_scrape", "hdd_spinup" };

        public readonly Job[] Jobs;
        public int Workers { get; private set; }
        int _next = -1;

        public SoundBankBuilder(List<KeyValuePair<string, int>> wanted)
        {
            var jobs = new List<Job>(wanted.Count);
            foreach (var w in wanted) jobs.Add(new Job { Id = w.Key, Seed = w.Value });
            // Stable: the longest sounds first (in the order above), the rest in registry order.
            jobs.Sort((a, b) =>
            {
                int ra = Array.IndexOf(Longest, a.Id), rb = Array.IndexOf(Longest, b.Id);
                if (ra < 0) ra = int.MaxValue;
                if (rb < 0) rb = int.MaxValue;
                return ra != rb ? ra.CompareTo(rb) : a.Seed.CompareTo(b.Seed);
            });
            Jobs = jobs.ToArray();
        }

        /// <summary>Starts the workers (two cores stay free for the main and render threads). False when no thread could be started.</summary>
        public bool Start()
        {
            int wanted = Math.Max(1, Math.Min(Jobs.Length, Environment.ProcessorCount - 2));
            try
            {
                for (; Workers < wanted; Workers++)
                    new Thread(Work) { IsBackground = true, Name = "SC sound bank " + Workers }.Start();
            }
            catch (Exception e)
            {
                GameLog.Warn(LogChannel.Audio, "Sound bank: could not start worker thread " + (Workers + 1) + " (" + e.Message + ")");
            }
            return Workers > 0;
        }

        void Work()
        {
            while (RunNext()) { }
        }

        /// <summary>Takes the next job nobody has taken and makes its samples on the calling thread (a worker, or the main thread when no worker started). False when none is left.</summary>
        public bool RunNext()
        {
            int i = Interlocked.Increment(ref _next);
            if (i >= Jobs.Length) return false;
            var job = Jobs[i];
            try { job.Data = ProceduralSoundBank.Generate(job.Id, job.Seed); }
            catch (Exception e) { job.Error = e.Message; }
            job.Done = true;
            return true;
        }
    }
}
