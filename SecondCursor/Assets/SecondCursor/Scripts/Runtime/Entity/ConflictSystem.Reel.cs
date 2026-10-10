using System.Collections;
using SecondCursor.Core;
using SecondCursor.Core.Entity;
using SecondCursor.Input;
using SecondCursor.OS;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Entity
{
    /// <summary>
    /// Phase P, the reel (<see cref="TugModel.Reel"/>, review board E1 to E6): "haul it to the bin". The track runs from the grabbed file to
    /// the Disposal bin; each stroke toward the bin reels the file along it and her pointer rides the far end, dragging it back. The file
    /// at the bin drops in as the player's own drop; at a tear line it snaps to the player's pointer. The player's pointer is never moved.
    /// Phase P-b: the rope and track (<see cref="HaulView"/>), the surge warning and surge, the sounds of plan 2.9 and the win and loss beats
    /// of plan 2.8.
    /// </summary>
    public sealed partial class ConflictSystem
    {
        /// <summary>A file torn loose (or lost) flies from the track to the winner's hand in this long.</summary>
        const float TearSnapSeconds = 0.12f;
        /// <summary>A smoothed stroke crossing this (px/s, upward) is heard as a yank, at most every <see cref="YankGap"/>.</summary>
        const float YankStroke = 220f, YankGap = 0.15f;
        /// <summary>Her pointer's tremble and the file's shake are re-rolled every 1/60 s, so they look the same at any frame rate.</summary>
        StepTimer _feelStep;
        Vector2 _tremble, _shake;
        bool _ropeTaut, _wasTelegraph, _wasSurging, _wasAhead, _wasRegrip, _strokeHigh;
        float _lastYank;

        void BeginReel(DragPayload p)
        {
            CurrentSettings.reel.seed = UnityEngine.Random.Range(1, int.MaxValue);
            var reel = new TugReel(CurrentSettings);
            reel.Begin((p.GhostPosition + new Vector2(16f, -16f)).ToCore(), _g.EntityAgent.Position.ToCore(), _g.Desktop.DisposalIcon.Hit.Center.ToCore(),
                new ScreenBounds(ScreenRig.Width, ScreenRig.Height, WindowManager.TaskbarHeight));
            _model = reel;
            _coach = new TugCoach(reel.Axis);
            _feelStep.Prime();
            _ropeTaut = _wasTelegraph = _wasSurging = _wasAhead = _wasRegrip = _strokeHigh = false;
            _lastYank = -1f;
            bool slid = reel.Origin != reel.GrabOrigin;
            if (slid)
            {
                // Grabbed by the bin: she yanks it back to the start of the track (one flash event with the start glitch).
                _g.Audio?.Play("glitch_burst", 0.35f);
                _g.Fx?.Shake(0.1f, 1.5f);
            }
            GameLog.Info(LogChannel.Entity, "Tug haul " + ArrowDirection + " " + reel.Finish.ToString("0") + " px to " + (reel.FinishIsBin ? "the bin" : "a tear line")
                + ", her line " + reel.HerLine.ToString("0") + " px" + (slid ? " (grabbed by the bin: slid back)" : "") + ", " + reel.Variant);
        }

        TugOutcome StepReel(float dt, bool playerGrips, float grip)
        {
            var reel = (TugReel)_model;
            var player = _g.Player;
            var entity = _g.EntityAgent;
            // The coach judges from the end of GET READY on, on the reel's smoothed stroke (hand over hand is not stopping).
            if (!reel.InReady) _coach.Step(dt, player.Position.ToCore(), playerGrips, reel.ReelSpeed);
            var outcome = reel.Step(dt, player.Position.ToCore(), playerGrips, Vec2.Zero, grip);
            float strain = reel.Strain;
            if (_feelStep.Tick(dt))
            {
                _tremble = UnityEngine.Random.insideUnitCircle * (1.5f + strain * 3f);
                // The file shakes with the strain; in a surge its label jitters a pixel more.
                _shake = UnityEngine.Random.insideUnitCircle * (strain * 4f) + (reel.Surging ? UnityEngine.Random.insideUnitCircle : Vector2.zero);
            }
            // Her pointer rides the far end of the file: hauled toward the bin when the player gains, walking it back when she does.
            entity.Position = ScreenRig.ClampToScreen(reel.EntityPosition.ToUnity() + _tremble);
            Vector2 obj = reel.ObjectPosition.ToUnity();
            _lastObject = obj;
            _payload.GhostPosition = obj + new Vector2(-16f, 16f) + _shake;
            if (outcome == TugOutcome.None)
            {
                _haul.Show(reel, player.Position, entity.Position, strain);
                Vector2 axis = reel.Axis.ToUnity();
                float mid = (reel.Finish - reel.HerLine) * 0.5f;
                Dim.Show(reel.Origin.ToUnity() + axis * mid, Mathf.Max(120f, (reel.Finish + reel.HerLine) * 0.5f + 70f));
                Cues(reel, strain);
            }
            // The player's arrow leans toward her by at most 3 px (the hotspot never moves); hers shakes with effort.
            if (_g.PlayerView != null) _g.PlayerView.VisualOffset = (entity.Position - player.Position).normalized * Mathf.Min(3f, strain * 5f);
            if (_g.EntityView != null) _g.EntityView.Jitter = 0.5f + strain * 2f;
            return outcome;
        }

        /// <summary>Plan 2.9: the rope snapping taut, the player's yanks, her warning and surge, the latch past half way, a slip and its re-grip.</summary>
        void Cues(TugReel reel, float strain)
        {
            var audio = _g.Audio;
            float now = Time.unscaledTime;
            if (!_ropeTaut && reel.Elapsed >= HaulView.TautAt)
            {
                _ropeTaut = true;
                audio?.Play("grab_snap", 0.3f, 1.4f);
            }
            bool high = reel.ReelSpeed >= YankStroke;
            if (high && !_strokeHigh && now - _lastYank >= YankGap)
            {
                _lastYank = now;
                audio?.Play("grab_snap", 0.25f, 1.15f + UnityEngine.Random.Range(-0.1f, 0.1f));
            }
            _strokeHigh = high;
            if (reel.Telegraph && !_wasTelegraph)
            {
                audio?.Play("ui_select", 0.3f, 0.5f, Audio.AudioManager.PanFor(_g.EntityAgent.Position.x));
                SurgeWarned?.Invoke();
            }
            _wasTelegraph = reel.Telegraph;
            if (reel.Surging && !_wasSurging)
            {
                // A small glitch (under the flash budget's 0.5): Reduce flashing softens or drops it, the rope's red stays.
                audio?.Play("glitch_burst", 0.3f + 0.4f * strain);
                _g.Fx?.Glitch(0.06f, Mathf.Min(0.45f, 0.25f + 0.2f * strain));
                Surged?.Invoke();
            }
            _wasSurging = reel.Surging;
            bool ahead = reel.KeepsOnRelease;
            if (ahead && !_wasAhead) audio?.Play("ui_click", 0.4f, 1.3f);
            _wasAhead = ahead;
            bool regrip = reel.InRegrip && !reel.InReady;
            if (regrip && !_wasRegrip) audio?.Play("mouse_release", 0.6f, 0.8f);
            else if (!regrip && _wasRegrip) audio?.Play("grab_snap", 0.5f, 1f);
            _wasRegrip = regrip;
        }

        /// <summary>
        /// Plan 2.8 and 2.9: into the bin the lid thumps and the drive seeks (the jolt and the drop follow once the fight is recorded); a loss
        /// flies the file into her hand shrinking and back, and the rope's whip snaps.
        /// </summary>
        void EndBeatsReel(DragPayload p, TugOutcome outcome, bool transfer, bool released)
        {
            if (outcome == TugOutcome.PlayerWins && LastWonIntoBin)
            {
                _g.Audio?.Play("low_thump", 0.35f, 1.5f);
                _g.Audio?.Play("hdd_seek", 0.25f);
            }
            else if (outcome == TugOutcome.EntityWins && transfer)
            {
                _g.Audio?.Play("mouse_release", 0.5f, 0.6f);
                if (p != null) _g.DragDrop.PulseGhost(p, 0.6f, 0.32f);
            }
        }

        /// <summary>The bin jolts 2 px as the file lands in it.</summary>
        IEnumerator JoltBin()
        {
            var bin = _g.Desktop != null ? _g.Desktop.DisposalIcon : null;
            if (bin == null || bin.Rect == null) yield break;
            var rt = bin.Rect;
            Vector2 at = rt.anchoredPosition;
            rt.anchoredPosition = at + new Vector2(0f, -2f);
            yield return new WaitForSecondsRealtime(0.15f);
            if (rt != null && rt.anchoredPosition == at + new Vector2(0f, -2f)) rt.anchoredPosition = at;
        }
    }
}
