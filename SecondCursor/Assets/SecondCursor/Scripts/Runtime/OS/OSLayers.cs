using SecondCursor.Rendering;
using SecondCursor.UI;
using UnityEngine;

namespace SecondCursor.OS
{
    /// <summary>
    /// Draw-order layers of the fake screen, bottom to top. Hit-testing follows the same order, so e.g.
    /// menus and drag ghosts always sit above windows and both cursors are drawn above everything.
    /// </summary>
    public sealed class OSLayers
    {
        public readonly RectTransform Root;
        public readonly RectTransform Desktop;
        public readonly RectTransform Windows;
        public readonly RectTransform Taskbar;
        public readonly RectTransform Popups;
        public readonly RectTransform Notifications;
        public readonly RectTransform Effects;
        public readonly RectTransform Drag;
        public readonly RectTransform Fullscreen;
        public readonly RectTransform Cursors;

        public OSLayers(RectTransform root)
        {
            Root = root;
            Desktop = Layer("Desktop Layer");
            Windows = Layer("Window Layer");
            Taskbar = Layer("Taskbar Layer");
            Popups = Layer("Popup Layer");
            Notifications = Layer("Notification Layer");
            Effects = Layer("Effects Layer");
            Drag = Layer("Drag Layer");
            Fullscreen = Layer("Fullscreen Layer");
            Cursors = Layer("Cursor Layer");
        }

        RectTransform Layer(string name)
        {
            var rt = UIBuilder.Rect(name, Root);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = Vector2.zero;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        /// <summary>Converts desktop coordinates (x from left, y from top) to world/virtual coordinates (y up).</summary>
        public static Vector2 DesktopToWorld(Vector2 desktop) => new Vector2(desktop.x, ScreenRig.Height - desktop.y);

        public static Vector2 WorldToDesktop(Vector2 world) => new Vector2(world.x, ScreenRig.Height - world.y);
    }
}
