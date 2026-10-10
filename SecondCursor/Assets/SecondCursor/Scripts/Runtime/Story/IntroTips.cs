using System;
using SecondCursor.Apps;
using SecondCursor.Core.Content;
using SecondCursor.Core.Tasks;
using SecondCursor.Game;
using SecondCursor.OS;
using UnityEngine;

namespace SecondCursor.Story
{
    /// <summary>
    /// Phase L (suggestion 2): the playable introduction. The Quick Start is three lines; every action is taught the first time
    /// the shift needs it, by a one-time <see cref="Tips">tip</see> beside the thing it describes: opening an icon, dragging a
    /// file onto a folder, a work order and its Personnel check, shredding through the Disposal bin (the Nexus menu, the tug of
    /// war and replying in Jotter have their own moments: <see cref="Tick"/>, the tug label, the first remote session's Jotter).
    /// A tip belongs to the first time its task is given, so a night played after a jump shows none for the past.
    /// </summary>
    public sealed class IntroTips
    {
        readonly GameServices _g;
        readonly Func<bool> _preparing;
        bool _openedSomething, _moved, _decided;
        float _nextLook;

        public IntroTips(GameServices g, Func<bool> preparing)
        {
            _g = g;
            _preparing = preparing;
            g.Tasks.TaskActivated += OnTask;
            g.Apps.Launched += (id, by) =>
            {
                if (by == null || !by.IsPlayer) return;
                _openedSomething = true;
                if (id == AppIds.Files) OfferMove();
                else if (id == AppIds.WorkOrders) OfferOrders();
            };
            g.Files.FileMoved += (f, from, to, actor) => _moved |= actor == Core.FileSystem.Actor.Player;
            g.Orders.Decided += (id, decision, by) => _decided |= by != null && by.IsPlayer;
        }

        WorkTask Active(TaskType type)
        {
            foreach (var t in _g.Tasks.Tasks)
                if (t.State == TaskState.Active && t.Type == type && !t.IsEntityAuthored) return t;
            return null;
        }

        void OnTask(WorkTask t)
        {
            if (_preparing() || t.IsEntityAuthored) return;
            switch (t.Type)
            {
                case TaskType.ReadEmail: OfferOpen(); break;
                case TaskType.MoveFile: OfferMove(); break;
                case TaskType.DecideOrder: OfferOrders(); break;
                case TaskType.DeleteFile: OfferShred(t); break;
            }
        }

        static Rect? RectOf(OSWindow w) => w != null && !w.IsClosed && !w.IsMinimized ? w.WorldRect : (Rect?)null;

        /// <summary>The first task is in Mail: a double-click on its desktop icon opens it.</summary>
        void OfferOpen()
        {
            var task = Active(TaskType.ReadEmail);
            if (task == null) return;
            _g.Tips.Offer("open", () => _g.Desktop.IconForApp(AppIds.Mail)?.Hit.WorldRect, () => !_openedSomething && task.State == TaskState.Active, 40f);
        }

        /// <summary>A file to move: File Manager first (the Workstation icon opens it), then the drag onto the folder.</summary>
        void OfferMove()
        {
            var task = Active(TaskType.MoveFile);
            if (task == null) return;
            if (_g.Apps.FindById(AppIds.Files) == null)
            {
                _g.Tips.Offer("files", () => _g.Desktop.IconForApp(AppIds.Workstation)?.Hit.WorldRect,
                    () => _g.Apps.FindById(AppIds.Files) == null && task.State == TaskState.Active, 40f);
                return;
            }
            string key = FilesApp.IsGuided(task) ? "tip.move.guided" : "tip.move";
            _g.Tips.Offer("move", () => RectOf(_g.Apps.FindById(AppIds.Files)?.Window), () => !_moved && task.State == TaskState.Active, 45f, key);
        }

        /// <summary>An order to decide, with Work Orders open: look the owner up in Personnel, then Approve or Reject.</summary>
        void OfferOrders()
        {
            var task = Active(TaskType.DecideOrder);
            if (task == null) return;
            _g.Tips.Offer("orders", () => RectOf(_g.Apps.FindById(AppIds.WorkOrders)?.Window), () => !_decided && task.State == TaskState.Active, 45f);
        }

        /// <summary>A file to shred: drag it onto the Disposal bin and confirm.</summary>
        void OfferShred(WorkTask task)
        {
            _g.Tips.Offer("shred", () => _g.Desktop.DisposalIcon.Hit.WorldRect, () => task.State == TaskState.Active && !_g.Shred.Busy, 45f);
        }

        /// <summary>
        /// The Nexus menu: with four programs open (or two desktop icons hidden under windows) a program may not be on the desktop
        /// any more; the Nexus button lists them all. It is also said once the first chores are done, at a calm moment, so a night that
        /// never crowds the desktop still says it. Looked at twice a second, after log-on.
        /// </summary>
        public void Tick()
        {
            if (Time.time < _nextLook || _g.Tips.Busy || !_g.Flags.Has(Core.Story.Flags.LoggedIn) || _g.Tips.Seen("nexus")) return;
            _nextLook = Time.time + 0.5f;
            int open = 0;
            foreach (var w in _g.Windows.Windows)
                if (w != null && !w.IsClosed && !w.IsMinimized && w.ShowInTaskbar) open++;
            if (open < 4 && HiddenIcons() < 2 && !_g.Flags.Has(Core.Story.Flags.TutorialDone)) return;
            // Only when no other tip waits (it must not hold back a task's tip), briefly, and it may stand over part of a window: the
            // button is always in view, the room beside it usually is not.
            _g.Tips.Offer("nexus", () => _g.Taskbar.StartButton.Hit.WorldRect, () => !_g.Taskbar.StartMenu.IsOpen, 12f, null, 0.7f);
        }

        int HiddenIcons()
        {
            int n = 0;
            foreach (var icon in _g.Desktop.Icons)
            {
                if (icon == null || icon.IsFile || icon.AppId == AppIds.Disposal) continue;
                var over = _g.Router.HitTest(icon.Hit.Center);
                if (over != null && over != icon.Hit && over.GetComponentInParent<OSWindow>() != null) n++;
            }
            return n;
        }
    }
}
