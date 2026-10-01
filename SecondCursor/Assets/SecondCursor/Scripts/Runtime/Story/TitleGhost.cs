using SecondCursor.Core;
using SecondCursor.Core.Game;
using SecondCursor.Game;
using SecondCursor.Rendering;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase Q2 (board A T5 title part, C V9): the second pointer on the title. Before any Night 3 ending it rests 90 px right of the
    /// first menu button at 60% and slides 30 px away (over 0.4 s) when your pointer comes within 80 px. After a Night 3 ending it
    /// loops your own Night 1 archive drag (saved with the night), scaled into an empty corner of the title. It never covers a button
    /// and never clicks.
    /// </summary>
    public sealed class TitleGhost
    {
        const float Near = 80f, Step = 30f, SlideSpeed = Step / 0.4f, Alpha = 0.6f;
        /// <summary>Where a replayed path is drawn (world px, bottom-left origin): right of the menu column, under the tagline.</summary>
        static readonly Rect Area = new Rect(640f, 90f, 260f, 150f);

        readonly GameServices _g;
        readonly Vector2 _rest;
        readonly Vector2[] _path;
        float _t;
        bool _logged;

        public TitleGhost(GameServices g, Vector2 rest, int[] path, bool replay)
        {
            _g = g;
            _rest = rest;
            _path = replay ? Fit(path) : null;
            var e = g.Entity;
            e.Interrupt();
            e.Brain.Enabled = false;
            e.MaxAlpha = Alpha;
            e.Teleport(_path != null ? _path[0] : rest);
            g.CoroutineHost.StartCoroutine(e.Appear(null, 1.2f, false));
            GameLog.Info(LogChannel.Entity, "Title: the second pointer " + (_path != null ? "replays your Night 1 drag (" + _path.Length + " points)" : "rests by the menu"));
        }

        public void Tick(float dt)
        {
            var me = _g.EntityAgent.Position;
            var you = _g.Player.Position;
            Vector2 target;
            if (_path != null)
            {
                _t += dt;
                int i = Mathf.FloorToInt(_t * SaveData.GhostRate) % _path.Length;
                target = _path[i];
                // Your pointer near it: it keeps its distance, as before.
                if (Vector2.Distance(you, target) < Near) target += (target - you).normalized * Step;
                _g.Entity.Teleport(Vector2.Distance(me, target) > 40f ? Vector2.MoveTowards(me, target, SlideSpeed * 4f * dt) : target);
                return;
            }
            target = _rest;
            if (Vector2.Distance(you, _rest) < Near || Vector2.Distance(you, me) < Near)
            {
                float side = you.x <= _rest.x ? 1f : -1f;
                target = _rest + new Vector2(side * Step, 0f);
                if (!_logged)
                {
                    _logged = true;
                    GameLog.Info(LogChannel.Entity, "Title: the second pointer slides away from yours");
                }
            }
            _g.Entity.Teleport(Vector2.MoveTowards(me, target, SlideSpeed * dt));
        }

        public void Hide()
        {
            _g.Entity.SetPresent(false, 0.2f);
            _g.Entity.MaxAlpha = 1f;
        }

        /// <summary>The saved x,y pairs scaled (keeping their shape) into <see cref="Area"/>; null if there are fewer than two points.</summary>
        static Vector2[] Fit(int[] path)
        {
            if (path == null || path.Length < 4) return null;
            int n = path.Length / 2;
            var pts = new Vector2[n];
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                pts[i] = new Vector2(path[i * 2], path[i * 2 + 1]);
                minX = Mathf.Min(minX, pts[i].x);
                maxX = Mathf.Max(maxX, pts[i].x);
                minY = Mathf.Min(minY, pts[i].y);
                maxY = Mathf.Max(maxY, pts[i].y);
            }
            float w = Mathf.Max(1f, maxX - minX), h = Mathf.Max(1f, maxY - minY);
            float k = Mathf.Min(1f, Mathf.Min(Area.width / w, Area.height / h));
            var offset = new Vector2(Area.x + (Area.width - w * k) * 0.5f, Area.y + (Area.height - h * k) * 0.5f);
            for (int i = 0; i < n; i++) pts[i] = offset + new Vector2((pts[i].x - minX) * k, (pts[i].y - minY) * k);
            return pts;
        }
    }
}
