// Nights 2 and 3 are not in the free demo (SC_DEMO): their code stays out of its build, like their content.
#if !SC_DEMO
using System.Collections;
using SecondCursor.Apps;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Content;
using SecondCursor.Core.Entity;
using SecondCursor.Core.Story;
using SecondCursor.Entity;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>Night 2's ending (split from Night2Director.Gary.cs, which reached the 800 line ceiling): the empty chair and the end card.</summary>
    public sealed partial class Night2Director
    {
        // ------------------------------------------------------------------ ENDING

        /// <summary>
        /// Phase Q3 (D1): after "Session suspended" session 017 opens CAM 03 and nobody sits in the chair (the door log, Denise and Personnel all said
        /// so). The feed fills the desktop under the CROURKE / WS-04 tag, still, and she types LOOK AT YOU (Night 1's own camera line). About 8 s, both
        /// branches. A click ends the full view; nothing here scares (no hit, no flash, the room tone dips).
        /// </summary>
        IEnumerator EmptyChair()
        {
            var g = _g;
            var rig = g.CameraRig;
            if (rig == null) yield break;
            BeginClimax();
            rig.SignalLost = false;
            rig.Figure = FigureStage.None;
            rig.DoorOpen = Mathf.Max(rig.DoorOpen, 0.15f);
            rig.LightsOn = true;
            rig.LightFlicker = 0.1f;
            rig.SeatedVisible = false;
            E.Brain.Enabled = false;
            E.Interrupt();
            if (!E.IsVisible) yield return E.Appear(new Vector2(ScreenRig.Width * 0.62f, ScreenRig.Height * 0.55f), 0.5f, false);
            E.State = EntityState.Observing;
            var cam = g.Apps.Find<CameraApp>();
            if (cam == null)
            {
                yield return E.OpenApp(AppIds.Camera, MovementProfiles.HumanLike);
                cam = g.Apps.Find<CameraApp>() ?? g.Apps.Launch(AppIds.Camera, E.Agent) as CameraApp;
            }
            else cam.Window.Restore(null);
            if (cam == null) { EndClimax(); yield break; }
            var button = cam.Window.Element("camera:" + ContentIds.Cam03);
            if (button != null && cam.CurrentCamera != ContentIds.Cam03) yield return E.ClickElement(button, MovementProfiles.HumanLike, null, 3f);
            if (cam.CurrentCamera != ContentIds.Cam03) cam.Select(ContentIds.Cam03, E.Agent);
            g.Windows.Front(cam.Window);
            g.Audio.SetAmbienceLevel(0.4f, 1f);
            // The empty chair in its own small window first, then the whole desktop.
            yield return Wait(1.5f);
            yield return FullSizeFeed(Lines("n2_empty_chair")[0], null, EmptyChairSeconds);
            yield return Wait(1f);
            yield return SayDirect(_ellen, Lines("n2_empty_chair"), 3.5f);
            yield return Wait(2f);
            EndFullView();
            EndClimax();
            g.Audio.SetAmbienceLevel(1f, 1f);
        }

        const float EmptyChairSeconds = 9f;

        IEnumerator EndingBeat()
        {
            var g = _g;
            StopSideRoutines();
            yield return Wait(3f);
            g.Notifications.Show(g.Content.Text("os.name"), g.Content.Text("session.suspended"), "icon_warning", null, "sys_warning");
            yield return Wait(1f);
            yield return EmptyChair();
            bool finished = g.Flags.Has(MemoryFlags.N2FinishedGary);
            int late = g.Flags.Get(LateShredFlag);
            string id = finished ? ContentIds.EndingN2Finished : ContentIds.EndingN2Kept;
            CompleteNight(id);
            var spec = new EndingSpec
            {
                Id = id,
                Lines = Lines(finished ? "n2_end_finished" : "n2_end_kept"),
                GaryLines = finished ? null : Lines("g2_goodnight"),
                TitleKey = "end.n2.title",
                SubtitleKey = finished ? "end.n2.subtitle.finished" : "end.n2.subtitle.kept",
                // Phase J: like Night 1's card, one line says what the player's choice about 209 was (Phase K: a late shred too).
                Outcome = finished ? g.Content.Text("end.n2.outcome.finished")
                    : g.Flags.Has(MemoryFlags.N2ArchivedGary) ? g.Content.Text("end.n2.outcome.archived")
                    : late > 0 ? g.Content.Format(late > LateShredMinute ? "end.n2.outcome.late" : "end.n2.outcome.late0", GameClock.Format12(late), late - LateShredMinute)
                    : g.Content.Text("end.n2.outcome.missed"),
                DemoCard = false,
                ContinueNight = 3,
                // Phase Q2 (T2): tonight's Retention Record rows.
                RecordRows = CardRows(),
            };
            _ending = new EndingSequence(g, spec);
            yield return _ending.Run();
        }
    }
}
#endif
