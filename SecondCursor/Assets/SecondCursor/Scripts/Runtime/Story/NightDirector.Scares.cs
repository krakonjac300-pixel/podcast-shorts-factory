using System.Collections;
using SecondCursor.CameraFeed;
using SecondCursor.Core;
using SecondCursor.Core.Audio;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase M: the story side of the scares. Ambient scares are placed with <see cref="Scare"/> (the scheduler decides whether and
    /// when). Every climax has one shape (SoundDesign.md 5.1): build, cut to true silence, one pre-mixed hit synced to the picture,
    /// ringing aftermath. Reduce flashing swaps in the soft hit and drops the flash and the glitch; the timing stays the same.
    /// </summary>
    public abstract partial class NightDirector
    {
        /// <summary>The hit lands this far into scare_hit (its pre-roll): the clip starts this much before the picture cuts.</summary>
        protected const float StingerPreRoll = 0.10f;

        /// <summary>A committed capture: accelerating footsteps, a visible rush, then the existing shriek and impact.</summary>
        protected IEnumerator CaptureRush()
        {
            var rig = _g.CameraRig;
            if (rig == null) yield break;
            _g.Player.Visible = false;
            _g.Entity.SetPresent(false, 0.05f);
            _g.Gary?.SetPresent(false, 0.05f);
            // Capture is committed and input is already released. Keep the attack large until the tube dies.
            EndFullView();
            var camera = _g.Apps.Find<Apps.CameraApp>();
            if (camera != null && camera.IsOpen)
            {
                camera.BeginFullView(null);
                if (!camera.Window.IsMaximized) camera.Window.ToggleMaximize(null);
                _g.Windows.Front(camera.Window);
                _fullCam = camera;
            }
            rig.BeginCaptureRush();
            GameLog.Info(LogChannel.Story, "Capture: rush toward CAM 03");
            const float seconds = 0.95f;
            float elapsed = 0f;
            int step = 0;
            float[] steps = { 0f, 0.25f, 0.44f, 0.59f };
            while (elapsed < seconds - StingerPreRoll)
            {
                rig.SetCaptureRush(elapsed / seconds);
                if (step < steps.Length && elapsed >= steps[step])
                {
                    _g.Audio.Play("step_near", 0.65f + step * 0.08f, 1f + step * 0.07f);
                    step++;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            // The shriek is already mixed into scare_hit. Soft sounds uses its quieter alternate.
            _g.Audio.PlayStinger(1f, Game.AccessSettings.SoftSounds(_g.Fx.ReduceFlashing), "crt_off", "ear_ring");
            float impact = Time.time + StingerPreRoll;
            while (Time.time < impact)
            {
                rig.SetCaptureRush(Mathf.Lerp((seconds - StingerPreRoll) / seconds, 1f,
                    1f - (impact - Time.time) / StingerPreRoll));
                yield return null;
            }
            rig.SetCaptureRush(1f);
            rig.FreezeFeed = true;
            if (_g.Fx.ReduceFlashing) _g.Fx.Shake(0.2f, 2f);
            else
            {
                var verdict = _g.Fx.Decide("capture impact");
                _g.Fx.Shake(0.3f, 6f);
                _g.Fx.Glitch(0.15f, 0.7f, verdict);
            }
            GameLog.Info(LogChannel.Story, "Capture: impact");
        }

        /// <summary>An ambient scare at a story moment: tried after <paramref name="delay"/> for up to <paramref name="window"/> seconds.</summary>
        protected void Scare(string id, float volume, float pan, float delay, float window, ScareGate ignore = ScareGate.None) =>
            _g.Scares.Slot(id, volume, pan, delay, window, ignore);

        /// <summary>The room tone falls away (fan, ballast, hum) and comes back: the absence is the sound.</summary>
        protected IEnumerator RoomDropout(float fadeOut, float hold, float fadeIn)
        {
            _g.Audio.SetAmbienceLevel(0f, fadeOut);
            yield return Wait(fadeOut + hold);
            _g.Audio.SetAmbienceLevel(1f, fadeIn);
        }

        /// <summary>A climax's dropout (Review M9: one helper for the three): the room and the tension drone fade to nothing.</summary>
        protected void DropRoom(float fade)
        {
            _g.Audio.SetAmbienceLevel(0f, fade);
            _g.Audio.StopLoop("drone_tension", fade);
        }

        /// <summary>A climax starts: nothing ambient any more, and no toast chimes (the toasts still show).</summary>
        protected void BeginClimax()
        {
            _g.Scares.CancelAll();
            _g.Scares.ClimaxRunning = true;
            _g.Audio.UiMuted = true;
            GameLog.Info(LogChannel.Story, "Climax: begin");
        }

        /// <summary>A mid-night climax is over (an ending's dark screen clears the mute itself; the next root starts clean).</summary>
        protected void EndClimax()
        {
            _g.Scares.ClimaxRunning = false;
            _g.Audio.UiMuted = false;
            var rig = _g.CameraRig;
            if (rig == null) return;
            rig.Quiet = false;
            rig.FreezeFeed = false;
        }

        /// <summary>The cut before an ending's hit: every loop gone in 30 ms, the feed frozen on a clean, still frame. True silence.</summary>
        protected void CutToSilence(float untilHit)
        {
            _g.Audio.StopAllLoops(0.03f);
            _g.Audio.Exclusive(untilHit);
            var rig = _g.CameraRig;
            if (rig != null)
            {
                rig.FreezeFeed = true;
                rig.ExtraNoise = 0f;
            }
            GameLog.Info(LogChannel.Story, "Climax: silence");
        }

        /// <summary>
        /// The hit: the stinger starts <see cref="StingerPreRoll"/> before the picture reacts. Full effects: glitch, shake and flash;
        /// Reduce flashing: the soft hit and a small shake only. Nothing but <paramref name="aftermath"/> sounds for 1.3 s.
        /// </summary>
        protected IEnumerator Hit(float volume, float glitch, float shakePx, float flash, params string[] aftermath)
        {
            bool reduced = _g.Fx.ReduceFlashing;
            // Phase Q4 (A10): the softer stinger is its own option (it follows Reduce flashing until it is set).
            _g.Audio.PlayStinger(volume, Game.AccessSettings.SoftSounds(reduced), aftermath);
            yield return Wait(StingerPreRoll);
            GameLog.Info(LogChannel.Story, "Climax: hit" + (reduced ? " (reduced)" : ""));
            if (reduced)
            {
                if (shakePx > 0f) _g.Fx.Shake(0.2f, 2f);
                yield break;
            }
            // The glitch and the flash are one flash event (and one verdict from the budget).
            var verdict = _g.Fx.Decide("hit");
            if (glitch > 0f) _g.Fx.Glitch(0.25f, glitch, verdict);
            if (shakePx > 0f) _g.Fx.Shake(0.3f, shakePx);
            if (flash > 0f) _g.Fx.Flash(flash, verdict);
        }

        /// <summary>
        /// An ending's aftermath, counted from the hit: NO SIGNAL (black with Reduce flashing), the tube dies (crt_off and its collapse),
        /// black, then the ringing in your ears. The ending that follows skips its own power down (<see cref="EndingSpec.AfterHit"/>).
        /// </summary>
        protected IEnumerator TubeDies(bool signalLost, float crtVolume, float earAt, float earVolume)
        {
            var g = _g;
            var rig = g.CameraRig;
            bool reduced = g.Fx.ReduceFlashing;
            yield return Wait(0.12f);
            if (reduced) g.Fx.SetBlack(true);
            else if (signalLost && rig != null)
            {
                g.Fx.Mark("signal lost");   // scripted: always plays, but counts against the flash budget
                rig.SignalLost = true;
            }
            yield return Wait(0.08f);
            g.Audio.Play("crt_off", crtVolume);
            if (!reduced)
            {
                g.Fx.Mark("tube collapse");
                g.CoroutineHost.StartCoroutine(g.Fx.PowerOff(0.5f, false));
            }
            yield return Wait(0.5f);
            g.Fx.SetBlack(true);
            if (rig != null)
            {
                rig.SignalLost = false;
                rig.LightsOn = false;
            }
            yield return Wait(Mathf.Max(0f, earAt - 0.7f));
            g.Audio.Play("ear_ring", earVolume);
            GameLog.Info(LogChannel.Story, "Climax: aftermath");
        }

        /// <summary>
        /// Night 1's last image as a climax (5.3): "you" turn to the camera on CAM 03 while the monitor's whine climbs and the drone swells
        /// and bends a semitone; the drone stops and the frame freezes; the hit; the tube dies; the ring. The demo ends on it.
        /// </summary>
        protected IEnumerator RevealClimax(float droneVolume)
        {
            var g = _g;
            var rig = g.CameraRig;
            BeginClimax();
            // Review M5: the feed stays up through the build (the player's pointer is still until the card).
            g.Player.Enabled = false;
            rig.Figure = FigureStage.BehindChair;
            rig.SeatedMimicsPlayer = false;
            rig.LightFlicker = 1f;
            g.Audio.Play("crt_whine_rise", 0.7f);
            GameLog.Info(LogChannel.Story, "Climax: build (reveal)");
            float t = 0f;
            while (t < RevealTurnToCut)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 3.3f);
                rig.SeatedHeadTurn = Mathf.SmoothStep(0f, 1f, t / 3.2f);
                rig.ExtraNoise = Mathf.Min(1f, t / 3.2f) * 0.5f;
                g.Audio.SetLoopVolume("drone_tension", Mathf.Lerp(droneVolume, droneVolume * 1.4f, k), 0.05f);
                g.Audio.SetLoopPitch("drone_tension", Mathf.Lerp(1f, 0.9439f, k));
                if (t < RevealTurnToCut - 0.4f) GlitchNowAndThen(1.2f);
                yield return null;
            }
            CutToSilence(RevealSilence);
            yield return Wait(RevealSilence - StingerPreRoll);
            yield return Hit(0.75f, 1f, 4f, 0.4f, "crt_off", "ear_ring");
            yield return TubeDies(false, 0.5f, 0.6f, 0.6f);
        }

        /// <summary>Review M2: a build's random glitches, about <paramref name="perSecond"/> a second at any frame rate, and none with Reduce flashing.</summary>
        protected void GlitchNowAndThen(float perSecond)
        {
            if (!_g.Fx.ReduceFlashing && Random.value < perSecond * Time.deltaTime) _g.Fx.Glitch(0.05f, 0.6f);
        }

        /// <summary>Night 1: the turn and hold before the cut (the whine is exactly this long), and the silence before the hit.</summary>
        const float RevealTurnToCut = 3.5f, RevealSilence = 0.4f;
    }
}
