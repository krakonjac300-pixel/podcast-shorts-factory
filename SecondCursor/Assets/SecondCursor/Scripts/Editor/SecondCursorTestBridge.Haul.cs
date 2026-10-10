using System;
using System.Collections;
using System.Globalization;
using SecondCursor.Core.Entity;
using SecondCursor.Entity;
using SecondCursor.Game;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEditor;
using UnityEngine;

namespace SecondCursor.EditorTools
{
    /// <summary>
    /// Phase P bridge commands for the reel ("haul it to the bin"): choose the tug model, play reel patterns (strokes toward the bin with a
    /// swing back or a lift, holding still, a slip and a re-grip) with the button held throughout, and read the fight's state.
    /// </summary>
    public static partial class SecondCursorTestBridge
    {
        const string HaulHelp =
            "Phase P: tugmode [reel|speed] (the tug model from the next contest) | tugstate (the fight now: model, s/finish, her line, pull, reel, surge, regrip, lead, strain)\n" +
            "         tugreel SPEED STROKE [swing|lift|keep] [PAUSE] [TIMEOUT] [SHOTPREFIX] (in a tug: strokes toward the bin at SPEED px/s for STROKE px, then a swing back at\n" +
            "           0.8x SPEED or a still PAUSE s, until it ends; never lets go, except keep: the swing, letting go once past half way; shots at 0.1, 0.3, 0.6, 1.0,\n" +
            "           1.5 s from the grab and at the result)\n" +
            "         tughold [SECONDS] [SHOTPREFIX] (in a tug: hold still, button down) | tugslip AFTER GAP [SPEED] [STROKE] [SHOTPREFIX] (tugreel's swing; the button goes up\n" +
            "           AFTER s after GET READY for GAP s, then down again)\n" +
            "         heldpath X1 Y1 D1 [X2 Y2 D2 ...] [shots=T1,T2,...] [release] (press, move through the points over D s each with the button held, shots\n" +
            "           T s after the press; lets go only with release)\n";

        static readonly float[] HaulShotTimes = { 0.1f, 0.3f, 0.6f, 1.0f, 1.5f };

        static IEnumerator TryGameHaulCommand(GameServices g, string cmd, string[] a, string rest)
        {
            switch (cmd)
            {
                case "tugmode":
                    if (a.Length > 1) DebugOverlay.SetTugModel(g, a[1].ToLowerInvariant() == "speed" ? TugModel.Speed : TugModel.Reel);
                    Say("tug model " + g.Difficulty.Tug.model + " (next contest)");
                    return Done();
                case "tugstate":
                    Say(TugState(g));
                    return Done();
                case "tugreel":
                    _scripted = true;
                    AttachInput();
                    return TugReelRun("tugreel", F(a, 1, 300f), F(a, 2, 120f), a.Length > 3 && a[3] == "lift", F(a, 4, 0.25f), F(a, 5, 8f), a.Length > 6 ? a[6] : null,
                        keep: a.Length > 3 && a[3] == "keep");
                case "tughold":
                    _scripted = true;
                    AttachInput();
                    return TugReelRun("tughold", 0f, 0f, true, 99f, F(a, 1, 8f), a.Length > 2 ? a[2] : null);
                case "tugslip":
                    _scripted = true;
                    AttachInput();
                    return TugReelRun("tugslip", F(a, 3, 300f), F(a, 4, 120f), false, 0f, 8f, a.Length > 5 ? a[5] : null, F(a, 1, 0.8f), F(a, 2, 0.3f));
                case "heldpath":
                    _scripted = true;
                    AttachInput();
                    return HeldPath(a);
                case "lockpick":
                    // Click lock: press on what is under the pointer, drag it 17 px over 0.5 s, let go (the lock keeps holding it).
                    _scripted = true;
                    AttachInput();
                    _input.Press();
                    _input.MoveTo(g.Player.Position + new Vector2(14f, -10f), 0.5f);
                    _input.Release();
                    return LockPick();
                default: return TryGameQ4Command(g, cmd, a, rest);
            }
        }

        static IEnumerator LockPick()
        {
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            var g = G;
            if (g == null) yield break;
            Say("lockpick: carrying " + (g.Player.Payload != null ? g.Player.Payload.FileId : "nothing") + ", held " + g.Player.Held
                + ", click lock " + (AccessSettings.Lock.Locked ? "locked" : "free"));
        }

        /// <summary>
        /// Phase P (E10): a held drag through several points with screenshots inside it (the click lock and the tug's frames in mid-drag). The
        /// button goes down where the pointer is and stays down; it comes up at the end only with "release".
        /// </summary>
        static IEnumerator HeldPath(string[] a)
        {
            var points = new System.Collections.Generic.List<Vector3>();
            var shots = new System.Collections.Generic.List<float>();
            bool release = false;
            for (int i = 1; i < a.Length; i++)
            {
                if (a[i] == "release") { release = true; continue; }
                if (a[i].StartsWith("shots=", StringComparison.Ordinal))
                {
                    foreach (var t in a[i].Substring(6).Split(','))
                        if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) shots.Add(v);
                    continue;
                }
                if (i + 2 >= a.Length) break;
                points.Add(new Vector3(F(a, i, 0f), F(a, i + 1, 0f), F(a, i + 2, 0.3f)));
                i += 2;
            }
            if (points.Count == 0) { Say("ERROR: heldpath X1 Y1 D1 [...] [shots=T1,T2] [release]"); yield break; }
            _input.Press();
            float total = 0f;
            foreach (var p in points)
            {
                _input.MoveTo(new Vector2(p.x, p.y), p.z);
                total += p.z;
            }
            if (release) _input.Release();
            float start = Time.unscaledTime;
            int next = 0;
            shots.Sort();
            while (_input.Pending > 0 || next < shots.Count)
            {
                float since = Time.unscaledTime - start;
                if (next < shots.Count && since >= shots[next])
                {
                    var g = G;
                    if (g != null) SaveScreen(g, "held_" + shots[next].ToString("0.00", CultureInfo.InvariantCulture));
                    next++;
                }
                if (since > total + 10f) break;
                yield return null;
            }
            var drain = Drain();
            while (drain.MoveNext()) yield return drain.Current;
            var gg = G;
            Say("heldpath: " + points.Count + " point(s) over " + total.ToString("0.00", CultureInfo.InvariantCulture) + " s" + (release ? ", released" : ", still held")
                + (gg != null ? "; pointer " + gg.Player.Position + ", held " + gg.Player.Held + ", carrying " + (gg.Player.Payload != null ? gg.Player.Payload.FileId : "nothing")
                    + ", click lock " + (AccessSettings.Lock.Locked ? "locked" : "free") : ""));
        }

        static string TugState(GameServices g)
        {
            var c = g.Conflict;
            if (!c.IsFighting) return "no fight (model " + g.Difficulty.Tug.model + ")";
            string common = " lead " + c.PlayerLead.ToString("0.00", CultureInfo.InvariantCulture) + " strain " + c.Strain.ToString("0.00", CultureInfo.InvariantCulture)
                            + " elapsed " + c.Elapsed.ToString("0.00", CultureInfo.InvariantCulture) + (c.InReady ? " READY" : "") + (c.IsMercyContest ? " MERCY" : "");
            if (!c.IsReel) return "speed: share " + c.EntityShare.ToString("0.00", CultureInfo.InvariantCulture) + " arrow " + c.ArrowDirection + common;
            var r = c.Reel;
            return "reel " + r.Variant + ":" + DebugOverlay.ReelLine(r) + " her line " + r.HerLine.ToString("0", CultureInfo.InvariantCulture) + " bin " + c.ArrowDirection + common;
        }

        /// <summary>
        /// Plays a reel pattern with the button held: strokes of <paramref name="stroke"/> px toward the bin at <paramref name="speed"/> px/s,
        /// each followed by a swing back at 0.8x (the button stays down: hand over hand) or, with <paramref name="lift"/>, a still
        /// <paramref name="pause"/> (a trackpad finger lifting). A stroke that would leave the screen swings back early. Speed 0 holds still.
        /// <paramref name="slipAfter"/> (0 or more): the button goes up that long after GET READY for <paramref name="slipGap"/> s, then down.
        /// <paramref name="keep"/>: the button goes up once letting go would keep the file (past half way).
        /// Prints the result, the seconds from the grab, the strokes, the re-grips, how it ended and the file's place on the track.
        /// </summary>
        static IEnumerator TugReelRun(string what, float speed, float stroke, bool lift, float pause, float timeout, string shots, float slipAfter = -1f, float slipGap = 0f,
            bool keep = false)
        {
            var wait = WaitFor(() => G != null && G.Conflict.IsFighting, timeout, "a tug");
            while (wait.MoveNext()) yield return wait.Current;
            var g = G;
            if (g == null || !g.Conflict.IsFighting) yield break;
            TugOutcome result = TugOutcome.None;
            Action<DragPayload, TugOutcome> onEnd = (p, o) => result = o;
            g.Conflict.TugEnded += onEnd;
            float last = 0f, done = 0f, rest = 0f;
            int strokes = 0;
            bool back = false, up = false, again = false;
            double start = EditorApplication.timeSinceStartup;
            _input.Steer((pos, dt) =>
            {
                var gg = G;
                if (gg == null || !gg.Conflict.IsFighting || EditorApplication.timeSinceStartup - start > timeout + 4f) return null;
                last = gg.Conflict.Elapsed;
                if (slipAfter >= 0f)
                {
                    float since = last - ConflictSystem.ReadySeconds;
                    if (!up && since >= slipAfter) { up = true; _input.ButtonNow(false); }
                    else if (up && !again && since >= slipAfter + slipGap) { again = true; _input.ButtonNow(true); }
                }
                if (keep && gg.Conflict.PlayerKeepsOnRelease)
                {
                    _input.ButtonNow(false);
                    return null;
                }
                if (speed <= 0f || dt <= 0f) return pos;
                Vector2 axis = gg.Conflict.PullDirection.normalized;
                if (back && rest > 0f)
                {
                    // A lift: the pointer rests while the finger goes back.
                    if ((rest -= dt) > 0f) return pos;
                    back = false;
                    done = 0f;
                }
                if (back)
                {
                    float step = Mathf.Min(0.8f * speed * dt, done);
                    done -= step;
                    if (done <= 0f) back = false;
                    return ScreenRig.ClampToScreen(pos - axis * step);
                }
                Vector2 next = pos + axis * speed * dt;
                bool blocked = next.x < 2f || next.x > ScreenRig.Width - 3f || next.y < WindowManager.TaskbarHeight + 2f || next.y > ScreenRig.Height - 3f;
                if (blocked || (done += speed * dt) >= stroke)
                {
                    strokes++;
                    back = true;
                    // At the screen's edge every pattern swings back to make room (a lift there would leave the pointer stuck).
                    rest = lift && !blocked ? pause : 0f;
                    if (!blocked) return next;
                    done = Mathf.Max(done, 0.8f * stroke);
                    return pos;
                }
                return next;
            });
            // Phase P-b: with a shot prefix, also the first frame of each cue (her warning, the surge, the latch past half way, GRAB IT) and the
            // frame after the end (a loss's whip lasts 0.12 s).
            bool warnShot = false, surgeShot = false, latchShot = false, regripShot = false, endShot = false;
            try
            {
                int nextShot = 0;
                while (_input.Pending > 0 && EditorApplication.timeSinceStartup - start < timeout + 6f)
                {
                    var cr = g.Conflict.Reel;
                    if (shots != null && g.Conflict.IsFighting && nextShot < HaulShotTimes.Length && g.Conflict.Elapsed >= HaulShotTimes[nextShot])
                        SaveScreen(g, shots + "_" + HaulShotTimes[nextShot++].ToString("0.0", CultureInfo.InvariantCulture));
                    if (shots != null && g.Conflict.IsFighting && cr != null)
                    {
                        if (!warnShot && cr.Telegraph) { warnShot = true; SaveScreen(g, shots + "_warn"); }
                        if (!surgeShot && cr.Surging) { surgeShot = true; SaveScreen(g, shots + "_surge"); }
                        if (!latchShot && g.Conflict.PlayerKeepsOnRelease) { latchShot = true; SaveScreen(g, shots + "_latch"); }
                        if (!regripShot && g.Conflict.InRegrip && !g.Conflict.InReady) { regripShot = true; SaveScreen(g, shots + "_grabit"); }
                    }
                    if (shots != null && !endShot && result != TugOutcome.None) { endShot = true; SaveScreen(g, shots + "_end"); }
                    yield return null;
                }
                if (shots != null && !endShot && result != TugOutcome.None) SaveScreen(g, shots + "_end");
                var drain = Drain();
                while (drain.MoveNext()) yield return drain.Current;
            }
            finally
            {
                g.Conflict.TugEnded -= onEnd;
            }
            if (shots != null) SaveScreen(g, shots + "_result");
            var c = g.Conflict;
            var r = c.Reel;
            string how = result == TugOutcome.None ? "still fighting"
                : result == TugOutcome.Released ? "released"
                : c.LastOutcomeForced ? "FORCED"
                : result == TugOutcome.PlayerWins ? (c.LastWonIntoBin ? "in the bin" : c.LastKeptOnRelease ? "kept on release" : r != null ? "torn loose" : "bar")
                : c.LastLostByRelease ? "let go (" + c.LastLossReason + ")" : (r != null ? "dragged back (" : "taken (") + c.LastLossReason + ")";
            float seconds = r != null && c.CurrentSettings.model == TugModel.Reel ? r.Elapsed : last;
            Say(what + (speed > 0f ? " " + speed.ToString("0", CultureInfo.InvariantCulture) + " px/s " + stroke.ToString("0", CultureInfo.InvariantCulture) + " px "
                    + (lift ? "lift " + pause.ToString("0.00", CultureInfo.InvariantCulture) : keep ? "swing, let go past half way" : "swing") : "")
                + (slipAfter >= 0f ? " slip at " + slipAfter.ToString("0.00", CultureInfo.InvariantCulture) + " for " + slipGap.ToString("0.00", CultureInfo.InvariantCulture) + " s" : "")
                + ": " + result + " at " + seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s from the grab, " + how + ", strokes " + strokes
                + (r != null ? ", regrips " + r.Regrips + ", s " + r.S.ToString("0", CultureInfo.InvariantCulture) + "/" + r.Finish.ToString("0", CultureInfo.InvariantCulture) + (r.FinishIsBin ? " bin" : " tear") : "")
                + " (" + c.CurrentSettings.model + "; " + AssistLine(g) + ")");
        }
    }
}
