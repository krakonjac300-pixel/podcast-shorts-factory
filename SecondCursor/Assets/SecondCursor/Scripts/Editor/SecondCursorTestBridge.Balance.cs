using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using SecondCursor.Core;
using SecondCursor.Game;
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
            "         waitaction NAME [TIMEOUT] (the second cursor's current behaviour, e.g. Lurk) | tugs (tug totals this shift)\n";

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
                case "waitaction":
                {
                    string name = a.Length > 1 ? a[1] : "Lurk";
                    return WaitFor(() => G != null && G.Entity != null && G.Entity.CurrentAction == name, F(a, 2, 30f), "entity action " + name);
                }
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

        /// <summary>The direction closest to <paramref name="dir"/> that still has at least 40 px of screen ahead.</summary>
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
