using System;

namespace SecondCursor.Core.Entity
{
    /// <summary>Why the player lost a tug-of-war, from what their pointer actually did (Phase K).</summary>
    public enum TugLossReason
    {
        /// <summary>Let go of the button before the bar was theirs.</summary>
        LetGo,
        /// <summary>Hardly moved at all.</summary>
        HeldStill,
        /// <summary>Moved, but mostly not the way the arrow pointed.</summary>
        WrongWay,
        /// <summary>Pulled the right way, then stopped before the bar was theirs.</summary>
        Stopped,
        /// <summary>Pulled the right way, too slowly.</summary>
        TooSlow,
        /// <summary>Pulled the right way and fast enough: she was simply stronger this time.</summary>
        Overpowered,
    }

    /// <summary>
    /// Phase K (fourth blind playtest): watches the player's pointer during one tug-of-war and, if the fight is lost, says what to
    /// change: "you let go", "you held still", "you pulled LEFT, the arrow pointed DOWN", "you stopped", "too slowly", or that she
    /// was stronger. Engine-free and unit-tested; the runtime feeds it every frame of the fight.
    /// </summary>
    public sealed class TugCoach
    {
        /// <summary>Less than this much pointer travel (px) in the whole fight is holding still.</summary>
        public const float StillPath = 14f;
        /// <summary>Under this share of the path along the arrow, the pull went the wrong way.</summary>
        public const float WrongWayShare = 0.5f;
        /// <summary>A pull that paused this long (s) at the end had stopped.</summary>
        public const float StoppedFor = 0.3f;
        /// <summary>Along-the-arrow speed under this share of the full-strength pull speed was too slow.</summary>
        public const float SlowShare = 0.5f;
        /// <summary>Pointer speed (px/s) along the arrow under which a frame counts as not pulling.</summary>
        const float PullingSpeed = 60f;
        /// <summary>
        /// Phase N: a person needs this long to start moving once the fight is on (the runtime feeds the coach only after GET READY),
        /// so "too slowly" is judged on the pull after it, never on the reaction.
        /// </summary>
        public const float ReactionSeconds = 0.3f;
        /// <summary>
        /// Phase P, the reel: under this share of the path toward the bin the pull went the wrong way (hand over hand moves toward the bin
        /// about half the time, so it is never wrong); a smoothed stroke under <see cref="ReelStoppedSpeed"/> for <see cref="ReelStoppedFor"/>
        /// at the end had stopped (a return stroke or a trackpad lift is shorter); her mean pull beat the mean reel by less than
        /// <see cref="ReelSlowMargin"/>: too slow.
        /// </summary>
        public const float ReelWrongWayShare = 0.3f, ReelStoppedSpeed = 40f, ReelStoppedFor = 0.6f, ReelSlowMargin = 30f;

        readonly Vec2 _arrow;
        Vec2 _start, _last;
        bool _started;
        float _held, _along, _path, _still, _forward, _reelStill;

        /// <param name="arrow">The way to drag (need not be normalised).</param>
        public TugCoach(Vec2 arrow)
        {
            _arrow = arrow.SqrLength > 1e-6f ? arrow.Normalized : new Vec2(0f, 1f);
        }

        /// <summary>Seconds the button was held during the fight.</summary>
        public float HeldSeconds => _held;
        /// <summary>Pointer travel along the arrow (px, negative = against it).</summary>
        public float Along => _along;
        /// <summary>Total pointer travel (px).</summary>
        public float Path => _path;
        /// <summary>Where the pointer went overall (end minus start).</summary>
        public Vec2 Net => _last - _start;

        /// <summary>One frame of the fight: where the player's pointer is and whether the button is down.</summary>
        public void Step(float dt, Vec2 pointer, bool holding)
        {
            if (!_started)
            {
                _started = true;
                _start = _last = pointer;
            }
            Vec2 delta = pointer - _last;
            _last = pointer;
            if (!holding || dt <= 0f) return;
            _held += dt;
            _path += delta.Length;
            float along = Vec2.Dot(delta, _arrow);
            _along += along;
            _forward += Math.Max(0f, along);
            _still = along / dt < PullingSpeed ? _still + dt : 0f;
        }

        /// <summary>Phase P: one frame of a reel fight, with the reel's smoothed stroke toward the bin (<see cref="TugReel.ReelSpeed"/>).</summary>
        public void Step(float dt, Vec2 pointer, bool holding, float reelSpeed)
        {
            Step(dt, pointer, holding);
            if (holding && dt > 0f) _reelStill = reelSpeed < ReelStoppedSpeed ? _reelStill + dt : 0f;
        }

        /// <summary>Phase P: why a reel fight was lost, from the pointer and the reel's means (<see cref="TugReel.MeanReel"/>, MeanPull).</summary>
        public TugLossReason ClassifyReel(bool letGo, float meanReel, float meanPull)
        {
            if (letGo) return TugLossReason.LetGo;
            if (_path < StillPath) return TugLossReason.HeldStill;
            if (_forward < _path * ReelWrongWayShare) return TugLossReason.WrongWay;
            if (_reelStill >= ReelStoppedFor && _held > _reelStill) return TugLossReason.Stopped;
            if (meanReel < meanPull + ReelSlowMargin) return TugLossReason.TooSlow;
            return TugLossReason.Overpowered;
        }

        /// <param name="letGo">The fight ended because the player released the button below the line.</param>
        /// <param name="fullStrengthSpeed">The contest's pull speed for full strength (px/s).</param>
        public TugLossReason Classify(bool letGo, float fullStrengthSpeed)
        {
            if (letGo) return TugLossReason.LetGo;
            if (_path < StillPath) return TugLossReason.HeldStill;
            if (_along < _path * WrongWayShare) return TugLossReason.WrongWay;
            if (_still >= StoppedFor && _held > _still) return TugLossReason.Stopped;
            float speed = _along / Math.Max(0.1f, _held - ReactionSeconds);
            if (speed < fullStrengthSpeed * SlowShare) return TugLossReason.TooSlow;
            return TugLossReason.Overpowered;
        }

        /// <summary>
        /// The eight-way name of a direction on screen (x right, y up): "UP", "DOWN-LEFT"... Plain hyphens only, so it reads in
        /// the pixel font and in a notice.
        /// </summary>
        public static string DirectionName(Vec2 d)
        {
            if (d.SqrLength < 1e-6f) return "NOWHERE";
            double deg = Math.Atan2(d.y, d.x) * 180.0 / Math.PI;
            int sector = (int)Math.Round(((deg % 360.0) + 360.0) % 360.0 / 45.0) % 8;
            switch (sector)
            {
                case 0: return "RIGHT";
                case 1: return "UP-RIGHT";
                case 2: return "UP";
                case 3: return "UP-LEFT";
                case 4: return "LEFT";
                case 5: return "DOWN-LEFT";
                case 6: return "DOWN";
                default: return "DOWN-RIGHT";
            }
        }
    }
}
