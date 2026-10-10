using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Phase F bridge commands for the balance checks: real (not forced) tugs played by the scripted cursor, the
    /// time from a drag to her grab, and waiting for what the second cursor is doing.
    /// </summary>
    public static partial class SecondCursorTestBridge
    {
        const string BalanceHelp =
            "Phase F: dragtug X Y DUR [HOLD] (drag toward X Y, wait there up to HOLD s, stop when a tug starts; prints the grab time) | tugplay SPEED [TIMEOUT] (yank away from her at SPEED px/s until the tug ends; 0 = hold still)\n" +
            "         waitaction NAME [TIMEOUT] (the second cursor's current behaviour, e.g. Lurk) | tugs (tug totals this shift)\n" +
            "Phase I: clockmon start|report|stop (samples the taskbar clock every editor frame and counts steps back) | clockcheck (the clock's own counters)\n" +
            "Phase J: tugsteps STEP INTERVAL COUNT HOLD (button held: COUNT jumps of STEP px away from her (the reel: toward the bin) every INTERVAL s, hold HOLD s, let go; prints the result)\n" +
            "         tughuman SPEED [TIMEOUT] [SHOT] (in a tug: pull along the arrow at SPEED px/s until the bar is yours, then let go; prints the result)\n" +
            "Phase K: tugangle DEGREES SPEED [PULLSECONDS] (in a tug: pull at DEGREES from the arrow, 0 = along it, for PULLSECONDS, then hold still until it ends; prints the loss reason)\n";

        static IEnumerator TryGameBalanceCommand(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "dragtug":
                    _scripted = true;
                    AttachInput();
                    return DragTug(V(a, 1), F(a, 3, 1.5f), F(a, 4, 0f));
                case "tugplay":
                    _scripted = true;
                    AttachInput();
                    return TugPlay(F(a, 1, 450f), F(a, 2, 8f));
                case "tugsteps":
                    _scripted = true;
                    AttachInput();
                    return TugSteps(F(a, 1, 30f), F(a, 2, 0.1f), (int)F(a, 3, 8f), F(a, 4, 0.3f));
                case "tughuman":
                    _scripted = true;
                    AttachInput();
                    return TugHuman(F(a, 1, 400f), F(a, 2, 8f), a.Length > 3 ? a[3] : null);
                case "tugangle":
                    _scripted = true;
                    AttachInput();
                    return TugAngle(F(a, 1, 0f), F(a, 2, 400f), F(a, 3, 99f));
                case "waitaction":
                {
                    string name = a.Length > 1 ? a[1] : "Lurk";
                    return WaitFor(() => G != null && G.Entity != null && G.Entity.CurrentAction == name, F(a, 2, 30f), "entity action " + name);
                }
                case "hint":
                    // Shows a task's hint toast now (Phase I: it waits while a shred dialog is open).
                    g.Director.ShowTaskHint(a.Length > 1 ? a[1] : "");
                    return Done();
                case "clockmon":
                    ClockMonitorCommand(a.Length > 1 ? a[1] : "report");
                    return Done();
                case "clockcheck":
                    Say("clock " + g.Clock.Format12() + " (" + g.Clock.ExactMinutes.ToString("0.00", CultureInfo.InvariantCulture) + " min), high water "
                        + g.Clock.HighWater.ToString("0.00", CultureInfo.InvariantCulture) + ", rate " + g.Clock.Rate.ToString("0.000", CultureInfo.InvariantCulture)
                        + (g.Clock.Frozen ? " (held)" : "") + ", refused back-sets " + g.Clock.RefusedBackSets);
                    return Done();
                case "tugs":
                    Say("tug wins=" + g.Flags.Get(Core.Story.Flags.CounterPlayerWins) + " losses=" + g.Flags.Get(Core.Story.Flags.CounterTugLosses)
                        + " defenses=" + g.Entity.Brain.Defenses + " tugLosses=" + g.Entity.Brain.TugLosses + " " + AssistLine(g));
                    return Done();
            }
            return TryGamePhaseGCommand(g, cmd, a, rest);
        }

        /// <summary>
        /// Moves the (already pressed) cursor toward <paramref name="target"/> over <paramref name="duration"/> and
        /// stops the moment a tug-of-war starts. Prints when the drag began, when she grabbed, and what she was doing
        /// when the drag began.
        /// </summary>
        static IEnumerator DragTug(Vector2 target, float duration, float hold)
        {
            var g = G;
            float t = 0f, dragAt = -1f, grabAt = -1f;
            string actionAtDrag = null;
            Vector2 from = Vector2.zero;
            bool started = false, fighting = false;
            _input.Steer((pos, dt) =>
            {
                if (!started) { started = true; from = pos; }
                t += dt;
                var gg = G;
                if (gg == null) return null;
                if (dragAt < 0f && gg.Player.Payload != null)
                {
                    dragAt = t;
                    actionAtDrag = gg.Entity.CurrentAction ?? "-";
                }
                if (gg.Conflict.IsFighting)
                {
                    grabAt = t;
                    fighting = true;
                    return null;
                }
                float k = duration <= 0f ? 1f : Mathf.Clamp01(t / duration);
                if (k >= 1f && t > duration + hold) return null;
                return Vector2.Lerp(from, target, Mathf.SmoothStep(0f, 1f, k));
            });
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            if (!fighting)
            {
                Say("dragtug: no tug (drag began " + (dragAt < 0f ? "never" : dragAt.ToString("0.00", CultureInfo.InvariantCulture) + "s") + ", entity " + (g.Entity.CurrentAction ?? "-") + ")");
                yield break;
            }
            Say("dragtug: tug started " + grabAt.ToString("0.00", CultureInfo.InvariantCulture) + "s into the move; grab "
                + (dragAt < 0f ? "?" : (grabAt - dragAt).ToString("0.00", CultureInfo.InvariantCulture) + "s after the drag began")
                + " (entity was " + (actionAtDrag ?? "-") + ")");
        }

        /// <summary>
        /// Plays a tug for real: each frame the cursor moves straight away from her at <paramref name="speed"/> px/s
        /// (0 = holds still), turning toward open space near a screen edge, until the contest ends. The model decides.
        /// </summary>
        static IEnumerator TugPlay(float speed, float timeout)
        {
            var wait = WaitFor(() => G != null && G.Conflict.IsFighting, timeout, "a tug");
            while (wait.MoveNext()) yield return wait.Current;
            var g = G;
            if (g == null || !g.Conflict.IsFighting) yield break;
            double start = EditorApplication.timeSinceStartup;
            _input.Steer((pos, dt) =>
            {
                var gg = G;
                if (gg == null || !gg.Conflict.IsFighting || EditorApplication.timeSinceStartup - start > timeout) return null;
                if (speed <= 0f || dt <= 0f) return pos;
                Vector2 away = pos - gg.EntityAgent.Position;
                if (away.sqrMagnitude < 1f) away = Vector2.left;
                return pos + RoomyDirection(pos, away.normalized) * speed * dt;
            });
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            string end = GameLog.Recent(60).Select(e => e.ToString()).LastOrDefault(e => e.Contains("Tug-of-war ended"));
            Say("tugplay " + speed.ToString("0", CultureInfo.InvariantCulture) + " px/s: " + (end ?? "no result")
                + (g.Conflict.LastOutcomeForced ? " (FORCED)" : " (real)") + "; " + AssistLine(g));
        }

        // ------------------------------------------------------------------ Phase J tug patterns

        /// <summary>
        /// The blind testers' input (their bridge moves are jumps): with the button held, <paramref name="count"/> jumps of
        /// <paramref name="step"/> px straight away from her pointer every <paramref name="interval"/> s, then <paramref name="hold"/> s
        /// still, then the button goes up. Prints whether a tug happened, the meter when the button went up and the result.
        /// </summary>
        static IEnumerator TugSteps(float step, float interval, int count, float hold)
        {
            float t = 0f, meter = -1f;
            int done = 0;
            bool tug = false;
            _input.Steer((pos, dt) =>
            {
                var gg = G;
                if (gg == null) return null;
                t += dt;
                tug |= gg.Conflict.IsFighting;
                if (done < count && t >= done * interval)
                {
                    done++;
                    // Phase P: in the reel the jumps go toward the bin (the way that fight is won); in the speed model, away from her.
                    Vector2 away = gg.Conflict.IsFighting && gg.Conflict.IsReel ? gg.Conflict.PullDirection : pos - gg.EntityAgent.Position;
                    return pos + RoomyDirection(pos, away.sqrMagnitude < 1f ? Vector2.left : away.normalized) * step;
                }
                return t < count * interval + hold ? pos : (Vector2?)null;
            });
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            var g = G;
            if (g == null) yield break;
            if (g.Conflict.IsFighting) meter = g.Conflict.PlayerLead;
            var release = TugRelease(g, tug, meter, "tugsteps " + step.ToString("0", CultureInfo.InvariantCulture) + " px x" + count);
            while (release.MoveNext()) yield return release.Current;
        }

        /// <summary>
        /// A player who read the label: pulls along the arrow at <paramref name="speed"/> px/s until the bar is past its line (or the
        /// fight ends), then lets go (<paramref name="shot"/>: a screenshot of the bar past its line first). Prints the meter at the
        /// release and the result.
        /// </summary>
        static IEnumerator TugHuman(float speed, float timeout, string shot)
        {
            var wait = WaitFor(() => G != null && G.Conflict.IsFighting, timeout, "a tug");
            while (wait.MoveNext()) yield return wait.Current;
            var g = G;
            if (g == null || !g.Conflict.IsFighting) yield break;
            double start = EditorApplication.timeSinceStartup;
            _input.Steer((pos, dt) =>
            {
                var gg = G;
                if (gg == null || !gg.Conflict.IsFighting || gg.Conflict.PlayerKeepsOnRelease || EditorApplication.timeSinceStartup - start > timeout) return null;
                return pos + RoomyDirection(pos, gg.Conflict.PullDirection.normalized) * speed * dt;
            });
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            float meter = g.Conflict.IsFighting ? g.Conflict.PlayerLead : -1f;
            if (shot != null && g.Conflict.IsFighting) SaveScreen(g, shot);
            var release = TugRelease(g, true, meter, "tughuman " + speed.ToString("0", CultureInfo.InvariantCulture) + " px/s, "
                + (EditorApplication.timeSinceStartup - start).ToString("0.00", CultureInfo.InvariantCulture) + " s");
            while (release.MoveNext()) yield return release.Current;
        }

        /// <summary>
        /// Phase K: a pull at <paramref name="degrees"/> from the arrow (0 = along it, 90 = across, 180 = against it) at
        /// <paramref name="speed"/> px/s for <paramref name="pullSeconds"/>, then the pointer holds still with the button down until the
        /// fight ends. Prints the result and the loss reason the tug panel shows.
        /// </summary>
        static IEnumerator TugAngle(float degrees, float speed, float pullSeconds)
        {
            var wait = WaitFor(() => G != null && G.Conflict.IsFighting, 8f, "a tug");
            while (wait.MoveNext()) yield return wait.Current;
            var g = G;
            if (g == null || !g.Conflict.IsFighting) yield break;
            float t = 0f;
            // Phase P-b: a sideways pull (90 degrees) goes to the side with more room, so the screen's edge does not stop it at once.
            if (Mathf.Abs(Mathf.Abs(degrees) - 90f) < 0.5f)
            {
                Vector2 way = g.Conflict.PullDirection.normalized, here = g.Player.Position;
                Vector2 a = Quaternion.Euler(0f, 0f, degrees) * way, b = Quaternion.Euler(0f, 0f, -degrees) * way;
                if (Room(here, b) > Room(here, a)) degrees = -degrees;
            }
            // Phase P-b: the result comes from this fight's own end (the log's last lines can belong to an earlier fight).
            TugOutcome result = TugOutcome.None;
            Action<DragPayload, TugOutcome> onEnd = (p, o) => result = o;
            g.Conflict.TugEnded += onEnd;
            _input.Steer((pos, dt) =>
            {
                var gg = G;
                if (gg == null || !gg.Conflict.IsFighting || t > 12f) return null;
                t += dt;
                if (t > pullSeconds || speed <= 0f) return pos;
                Vector2 dir = Quaternion.Euler(0f, 0f, degrees) * gg.Conflict.PullDirection.normalized;
                Vector2 next = pos + dir * speed * dt;
                // Phase P: at the screen's edge the pull stops (sliding along the edge would turn a sideways pull toward the bin).
                return next == ScreenRig.ClampToScreen(next) ? next : pos;
            });
            try
            {
                var drain = Drain();
                while (drain.MoveNext()) yield return drain.Current;
            }
            finally
            {
                g.Conflict.TugEnded -= onEnd;
            }
            var c = g.Conflict;
            string why = result == TugOutcome.EntityWins ? GameLog.Recent(20).Select(e => e.ToString()).LastOrDefault(e => e.Contains("Tug lost:")) : null;
            Say("tugangle " + degrees.ToString("0", CultureInfo.InvariantCulture) + " deg " + speed.ToString("0", CultureInfo.InvariantCulture) + " px/s: "
                + "[ENTITY] Tug-of-war ended: " + result + " at " + (c.Reel != null ? c.Reel.Elapsed : t).ToString("0.00", CultureInfo.InvariantCulture) + " s"
                + (why != null ? "; " + why : ""));
        }

        /// <summary>Lets go of the button, waits for the fight to be decided and prints how it ended.</summary>
        static IEnumerator TugRelease(GameServices g, bool tug, float meter, string what)
        {
            _input.Release();
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            float end = Time.realtimeSinceStartup + 1.5f;
            while (g.Conflict.IsFighting && Time.realtimeSinceStartup < end) yield return null;
            string result = GameLog.Recent(40).Select(e => e.ToString()).LastOrDefault(e => e.Contains("Tug-of-war ended"));
            bool ahead = GameLog.Recent(40).Any(e => e.ToString().Contains("let go ahead"));
            Say(what + ": " + (!tug ? "no tug" : (result ?? "no result")) + (meter >= 0f ? "; meter at release " + meter.ToString("0.00", CultureInfo.InvariantCulture) : "; fight already over")
                + (ahead ? " (kept by letting go ahead)" : ""));
        }

        // ------------------------------------------------------------------ clock monitor (Phase I)

        static bool _monActive;
        static object _monClock;
        static double _monLast, _monFirst, _monMax, _monWorst;
        static int _monBack, _monSamples;

        /// <summary>Called every editor update: samples the game clock (independent of the clock's own bookkeeping).</summary>
        static void ClockMonitorTick()
        {
            if (!_monActive) return;
            var g = G;
            if (g == null || g.Clock == null) return;
            double now = g.Clock.ExactMinutes;
            if (!ReferenceEquals(_monClock, g.Clock))
            {
                // A new shift (a new clock object) starts from its own beginning.
                _monClock = g.Clock;
                _monLast = now;
                if (_monSamples == 0) _monFirst = now;
            }
            if (now + 1e-6 < _monLast)
            {
                _monBack++;
                _monWorst = Math.Max(_monWorst, _monLast - now);
            }
            _monLast = now;
            _monMax = Math.Max(_monMax, now);
            _monSamples++;
        }

        static void ClockMonitorCommand(string what)
        {
            switch (what)
            {
                case "start":
                    _monActive = true;
                    _monClock = null;
                    _monBack = 0;
                    _monSamples = 0;
                    _monWorst = 0;
                    _monMax = 0;
                    Say("clockmon started");
                    break;
                case "stop":
                    _monActive = false;
                    goto default;
                default:
                    Say("clockmon " + (_monActive ? "running" : "stopped") + ": " + _monSamples + " samples, first " + _monFirst.ToString("0.00", CultureInfo.InvariantCulture)
                        + " last " + _monLast.ToString("0.00", CultureInfo.InvariantCulture) + " highest " + _monMax.ToString("0.00", CultureInfo.InvariantCulture)
                        + " min, steps back " + _monBack + " (worst " + _monWorst.ToString("0.000", CultureInfo.InvariantCulture) + " min)");
                    break;
            }
        }

        /// <summary>The direction closest to <paramref name="dir"/> that still has at least 40 px of screen ahead.</summary>
        /// <summary>How far the pointer can go from <paramref name="pos"/> along <paramref name="dir"/> before the screen's edge (px).</summary>
        static float Room(Vector2 pos, Vector2 dir)
        {
            float rx = dir.x > 1e-4f ? (ScreenRig.Width - 1f - pos.x) / dir.x : dir.x < -1e-4f ? pos.x / -dir.x : float.MaxValue;
            float ry = dir.y > 1e-4f ? (ScreenRig.Height - 1f - pos.y) / dir.y : dir.y < -1e-4f ? (pos.y - WindowManager.TaskbarHeight) / -dir.y : float.MaxValue;
            return Mathf.Min(rx, ry);
        }

        static Vector2 RoomyDirection(Vector2 pos, Vector2 dir)
        {
            foreach (float deg in new[] { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f, 125f, -125f })
            {
                Vector2 d = Quaternion.Euler(0f, 0f, deg) * dir;
                Vector2 ahead = pos + d * 40f;
                if (ahead.x > 4f && ahead.x < ScreenRig.Width - 4f && ahead.y > WindowManager.TaskbarHeight + 4f && ahead.y < ScreenRig.Height - 4f) return d;
            }
            return dir;
        }
    }
}
