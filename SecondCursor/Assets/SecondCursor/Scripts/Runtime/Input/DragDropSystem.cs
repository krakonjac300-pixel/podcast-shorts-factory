using System;
using System.Collections.Generic;
using SecondCursor.Core;
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
        /// <summary>Payload finished (dropped somewhere or returned): (payload, accepted, agent that released it or null).</summary>
        public event Action<DragPayload, bool, CursorAgent> PayloadFinished;
        /// <summary>The player took a payload from a cursor it cannot fight: (payload, previous holder).</summary>
        public event Action<DragPayload, CursorAgent> Snatched;

        /// <summary>
        /// Whether a grab by (holder, grabber) starts a contest. Null = always. A refused grab by the player
        /// takes the payload outright (<see cref="Snatched"/>); any other refused grab does nothing.
        /// </summary>
        public Func<CursorAgent, CursorAgent, bool> CanContest;

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
        public DragPayload BeginFileDrag(CursorAgent agent, string fileId, string label, string iconSprite, Interactable source, Vector2 iconTopLeft) =>
            BeginDrag(agent, PayloadKind.File, fileId, label, iconSprite, source, iconTopLeft);

        /// <summary>Starts carrying anything (a file, or e.g. a desktop shortcut identified by <paramref name="id"/>).</summary>
        public DragPayload BeginDrag(CursorAgent agent, PayloadKind kind, string id, string label, string iconSprite, Interactable source, Vector2 iconTopLeft)
        {
            if (agent.Payload != null) return agent.Payload;
            var p = new DragPayload
            {
                Kind = kind,
                FileId = id,
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
                if (CanContest != null && !CanContest(p.Holder, a))
                {
                    // No fight between these two (Gary is too weak to hold on): the player simply takes it.
                    if (!a.IsPlayer || a.Payload != null) return;
                    var from = p.Holder;
                    TransferTo(p, a);
                    GameLog.Info(LogChannel.Player, a.Name + " took " + p.Label + " from " + from.Name);
                    Snatched?.Invoke(p, from);
                    return;
                }
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
            rt.anchoredPosition = new Vector2(Mathf.Floor(topLeft.x + 0.5f), Mathf.Floor(topLeft.y + 0.5f));
        }

        /// <summary>Per-frame: ghosts follow holders (contested ghosts are positioned by the conflict system).</summary>
        public void Update(float dt)
        {
            foreach (var p in _active)
            {
                if (!_ghosts.TryGetValue(p, out var g) || g == null) continue;
                if (!p.Contested && p.Holder != null)
                {
                    Vector2 hand = p.Holder.Position - p.GrabOffset;
                    if (p.SnapLeft > 0f)
                    {
                        p.SnapLeft -= dt;
                        float k = 1f - Mathf.Clamp01(p.SnapLeft / p.SnapSeconds);
                        hand = Vector2.Lerp(p.SnapFrom, hand, k * (2f - k));
                    }
                    p.GhostPosition = hand;
                }
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

        /// <summary>Phase P: the ghost flies from <paramref name="from"/> (top-left) to its holder's hand over <paramref name="seconds"/> instead of jumping.</summary>
        public void SnapGhost(DragPayload p, Vector2 from, float seconds)
        {
            p.SnapFrom = from;
            p.SnapSeconds = p.SnapLeft = seconds;
        }

        /// <summary>
        /// Phase P: <paramref name="by"/> drops the payload onto <paramref name="target"/> without letting go of the button (a tug hauled into
        /// the Disposal bin), exactly as a release over it would: the target takes it and the drag ends.
        /// </summary>
        public void DropInto(DragPayload p, CursorAgent by, Interactable target)
        {
            if (by.Payload == p) by.Payload = null;
            p.Dropped = true;
            target.RaiseDrop(by, p);
            Finish(p, true, by);
        }

        /// <summary>Stops the drag and flies the ghost back to its origin.</summary>
        public void Cancel(DragPayload p)
        {
            if (p.Holder != null && p.Holder.Payload == p) p.Holder.Payload = null;
            if (p.Contender != null && p.Contender.Payload == p) p.Contender.Payload = null;
            Finish(p, false, null);
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
            Finish(p, accepted, a);
        }

        void Finish(DragPayload p, bool accepted, CursorAgent by)
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
                    if (accepted)
                    {
                        g.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(g.gameObject);
                    }
                    else _returning.Add(new Returning { Payload = p, Ghost = g, From = p.GhostPosition, To = p.Origin });
                }
            }
            PayloadFinished?.Invoke(p, accepted, by);
        }

        public RectTransform GhostOf(DragPayload p) => _ghosts.TryGetValue(p, out var g) ? g : null;
    }
}
