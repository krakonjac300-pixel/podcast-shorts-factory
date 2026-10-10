using SecondCursor.Core;
using SecondCursor.Game;
using UnityEngine;

namespace SecondCursor.Input
{
    /// <summary>A window the player can type into (the Steam Deck's floating keyboard opens for it).</summary>
    public interface ITextEntryTarget
    {
        /// <summary>The player can type here now.</summary>
        bool WantsTextEntry { get; }
        TextEntryMode EntryMode { get; }
        /// <summary>The text field in virtual OS pixels (origin bottom-left): the keyboard avoids covering it.</summary>
        Rect EntryRectVirtual { get; }
    }

    /// <summary>
    /// Steam Deck: shows the floating keyboard while the active window wants typing (a conversation in Jotter, an
    /// editable file, the Restricted code prompt) and dismisses it when that stops. After the player closes it on the
    /// Steam side, a click inside the window brings it back. Only on a Deck (or the bridge's simulated one), never
    /// while paused. Without Steamworks the calls only log, so the flow can be tested in the Editor.
    /// </summary>
    public sealed class DeckKeyboard
    {
        readonly GameServices _g;
        ITextEntryTarget _shown;
        TextEntryMode _shownMode;

        public DeckKeyboard(GameServices g)
        {
            _g = g;
        }

        public void Tick()
        {
            if (!SteamBridge.OnDeck || Game.PauseMenu.IsPaused)
            {
                Dismiss();
                return;
            }
            var target = CurrentTarget(out var window);
            if (target == null)
            {
                Dismiss();
                return;
            }
            if (target != _shown || target.EntryMode != _shownMode)
            {
                Show(target);
                return;
            }
            // Closed from the Steam side: a click inside the window asks for it again.
            var player = _g.Player;
            if (SteamBridge.TextEntryDismissedBySteam && player.PressedThisFrame && window.WorldRect.Contains(player.Position)) Show(target);
        }

        ITextEntryTarget CurrentTarget(out OS.OSWindow window)
        {
            window = _g.Windows != null ? _g.Windows.Active : null;
            if (window == null || window.IsClosed || window.IsMinimized) return null;
            return window.Owner is ITextEntryTarget t && t.WantsTextEntry ? t : null;
        }

        void Show(ITextEntryTarget target)
        {
            _shown = target;
            _shownMode = target.EntryMode;
            var r = target.EntryRectVirtual;
            Vector2 bottomLeft = _g.Screen.VirtualToScreen(new Vector2(r.xMin, r.yMin));
            Vector2 topRight = _g.Screen.VirtualToScreen(new Vector2(r.xMax, r.yMax));
            // Steam wants the field in window pixels from the top-left.
            var field = new RectInt(Mathf.RoundToInt(bottomLeft.x), Mathf.RoundToInt(Screen.height - topRight.y),
                Mathf.RoundToInt(topRight.x - bottomLeft.x), Mathf.RoundToInt(topRight.y - bottomLeft.y));
            SteamBridge.ShowTextEntry(target.EntryMode, field);
            GameLog.Info(LogChannel.System, "Deck keyboard: show " + target.EntryMode);
        }

        public void Dismiss()
        {
            if (_shown == null) return;
            _shown = null;
            SteamBridge.DismissTextEntry();
            GameLog.Info(LogChannel.System, "Deck keyboard: dismiss");
        }
    }
}
