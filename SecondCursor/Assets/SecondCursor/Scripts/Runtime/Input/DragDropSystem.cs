using System;
using System.Collections.Generic;
using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;
using UnityEngine.UI;

namespace SecondCursor.Input
{
    /// <summary>
    /// Carries dragged files around the fake OS: creates the translucent ghost that follows the holding
    /// cursor, lets the OTHER cursor grab that ghost (which starts a contest), animates refused drops back
    /// to where they came from, and hands payloads between cursors when a contest is decided.
    /// </summary>
    public sealed class DragDropSystem
    {
        sealed class Returning
        {
            public DragPayload Payload;
            public RectTransform Ghost;
            public Vector2 From;
            public Vector2 To;
            public float T;
        }

        readonly RectTransform _layer;
        readonly List<DragPayload> _active = new List<DragPayload>();
        readonly List<Returning> _returning = new List<Returning>();
        readonly Dictionary<DragPayload, RectTransform> _ghosts = new Dictionary<DragPayload, RectTransform>();

        /// <summary>A second cursor grabbed a payload someone else is carrying: (payload, contender).</summary>
        public event Action<DragPayload, CursorAgent> ContestStarted;
        /// <summary>A cursor released a payload while it was contested: (payload, releasing agent).</summary>
        public event Action<DragPayload, CursorAgent> ContestReleased;
        /// <summary>Payload finished (dropped somewhere or returned): (payload, accepted).</summary>
        public event Action<DragPayload, bool> PayloadFinished;

        public DragDropSystem(RectTransform layer, PointerRouter router)
        {
            _layer = layer;
            router.PayloadReleased += OnReleased;
        }

        public IReadOnlyList<DragPayload> Active => _active;

        public DragPayload FindByFile(string fileId)
        {
            foreach (var p in _active) if (p.FileId == fileId) return p;
            return null;
        }

        /// <summary>Starts carrying a file. <paramref name="iconTopLeft"/> is where the source icon's top-left sits (virtual px).</summary>
        public DragPayload BeginFileDrag(CursorAgent agent, string fileId, string label, string iconSprite, Interactable source, Vector2 iconTopLeft)
        {
            if (agent.Payload != null) return agent.Payload;
            var p = new DragPayload
            {
                Kind = PayloadKind.File,
                FileId = fileId,
                Label = label,
                IconSprite = iconSprite,
                Source = source,
                Origin = iconTopLeft,
                GrabOffset = agent.Position - iconTopLeft,
                Holder = agent,
                GhostPosition = iconTopLeft,
            };
            // Keep the grab point inside the 32x32 ghost icon so it reads as "held".
            p.GrabOffset = new Vector2(Mathf.Clamp(p.GrabOffset.x, 4f, 28f), Mathf.Clamp(p.GrabOffset.y, -28f, -4f));
            agent.Payload = p;
            _active.Add(p);
            _ghosts[p] = CreateGhost(p);
            return p;
        }

        RectTransform CreateGhost(DragPayload p)
        {
            var rt = UIBuilder.Rect("Drag Ghost " + p.Label, _layer);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(32f, 32f);
            var icon = UIBuilder.Icon(rt, p.IconSprite, 2);
            icon.color = new Color(1f, 1f, 1f, 0.8f);
            var label = UIBuilder.Text(rt, p.Label, Palette.DesktopLabel);
            label.Shadow = true;
            label.ShadowColor = Palette.DesktopLabelShadow;
            var lrt = label.rectTransform;
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(96f, 12f);
            lrt.anchoredPosition = new Vector2(0f, -2f);
            label.Align = TextAlign.Center;
            label.color = new Color(1f, 1f, 1f, 0.85f);

            var it = rt.gameObject.AddComponent<Interactable>();
            it.Tag = p;
            it.cursor = CursorShape.Grab;
            it.elementId = "ghost:" + p.FileId;
            it.PointerDown += a =>
            {
                if (a == p.Holder || p.Contender != null || p.Holder == null) return;
                p.Contender = a;
                GameLog.Info(a.IsEntity ? LogChannel.Entity : LogChannel.Player, a.Name + " grabbed " + p.Label + " from " + p.Holder.Name);
                ContestStarted?.Invoke(p, a);
            };
            p.Ghost = it;
            Place(rt, p.GhostPosition);
            return rt;
        }

        static void Place(RectTransform rt, Vector2 topLeft)
        {
            rt.anchoredPosition = new Vector2(Mathf.Round(topLeft.x), Mathf.Round(topLeft.y));
        }

        /// <summary>Per-frame: ghosts follow holders (contested ghosts are positioned by the conflict system).</summary>
        public void Update(float dt)
        {
            foreach (var p in _active)
            {
                if (!_ghosts.TryGetValue(p, out var g) || g == null) continue;
                if (!p.Contested && p.Holder != null) p.GhostPosition = p.Holder.Position - p.GrabOffset;
                Place(g, p.GhostPosition);
            }

            for (int i = _returning.Count - 1; i >= 0; i--)
            {
                var r = _returning[i];
                r.T += dt / 0.18f;
                if (r.Ghost == null || r.T >= 1f)
                {
                    if (r.Ghost != null) UnityEngine.Object.Destroy(r.Ghost.gameObject);
                    _returning.RemoveAt(i);
                    continue;
                }
                float e = 1f - (1f - r.T) * (1f - r.T);
                Place(r.Ghost, Vector2.Lerp(r.From, r.To, e));
            }
        }

        /// <summary>Contest decided: the winner now carries the payload.</summary>
        public void TransferTo(DragPayload p, CursorAgent winner)
        {
            if (p.Holder != null && p.Holder != winner && p.Holder.Payload == p) p.Holder.Payload = null;
            if (p.Contender != null && p.Contender != winner && p.Contender.Payload == p) p.Contender.Payload = null;
            p.Holder = winner;
            p.Contender = null;
            winner.Payload = p;
            p.GrabOffset = winner.Position - p.GhostPosition;
            p.GrabOffset = new Vector2(Mathf.Clamp(p.GrabOffset.x, 4f, 28f), Mathf.Clamp(p.GrabOffset.y, -28f, -4f));
        }

        /// <summary>Stops the drag and flies the ghost back to its origin.</summary>
        public void Cancel(DragPayload p)
        {
            if (p.Holder != null && p.Holder.Payload == p) p.Holder.Payload = null;
            if (p.Contender != null && p.Contender.Payload == p) p.Contender.Payload = null;
            Finish(p, false);
        }

        void OnReleased(CursorAgent a, DragPayload p, bool accepted)
        {
            if (p.Contested && !accepted)
            {
                ContestReleased?.Invoke(p, a);
                return;
            }
            if (p.Holder != a) return;
            a.Payload = null;
            Finish(p, accepted);
        }

        void Finish(DragPayload p, bool accepted)
        {
            _active.Remove(p);
            p.Holder = null;
            p.Contender = null;
            if (_ghosts.TryGetValue(p, out var g))
            {
                _ghosts.Remove(p);
                if (g != null)
                {
                    var it = g.GetComponent<Interactable>();
                    if (it != null) it.interactable = false;
                    if (accepted) UnityEngine.Object.Destroy(g.gameObject);
                    else _returning.Add(new Returning { Payload = p, Ghost = g, From = p.GhostPosition, To = p.Origin });
                }
            }
            PayloadFinished?.Invoke(p, accepted);
        }

        public RectTransform GhostOf(DragPayload p) => _ghosts.TryGetValue(p, out var g) ? g : null;
    }
}
