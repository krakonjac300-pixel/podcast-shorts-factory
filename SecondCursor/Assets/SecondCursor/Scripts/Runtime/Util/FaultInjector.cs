using System;
using System.Diagnostics;

namespace SecondCursor
{
    /// <summary>
    /// Test bridge "fault SITE [TIMES]": the next TIMES passes through a catch point throw on purpose, to prove one failing step is logged
    /// once and the night goes on. <see cref="Check"/> exists only in the Editor (the calls are removed from every player build).
    /// </summary>
    internal static class FaultInjector
    {
        static string _site;
        static int _left;

        internal static void Arm(string site, int times)
        {
            _site = site;
            _left = times;
        }

        [Conditional("UNITY_EDITOR")]
        internal static void Check(string site)
        {
            if (_left <= 0 || _site != site) return;
            _left--;
            throw new InvalidOperationException("test fault at " + site);
        }
    }
}
