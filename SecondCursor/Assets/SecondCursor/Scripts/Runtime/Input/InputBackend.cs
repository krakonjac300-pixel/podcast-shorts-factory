using System.Text;
using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && SC_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SecondCursor.Input
{
    /// <summary>Keys the game cares about, independent of which Unity input system is active.</summary>
    public enum GameKey
    {
        Escape, Enter, Backspace, Delete, Tab, Space, Up, Down, Left, Right,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, BackQuote, Shift, Ctrl,
    }

    /// <summary>
    /// Reads the REAL mouse/keyboard only while the game window is focused. It never moves the OS cursor
    /// or reads anything outside the game (brief section 3).
    /// </summary>
    public interface IInputBackend : System.IDisposable
    {
        Vector2 MouseScreenPosition { get; }
        bool LeftHeld { get; }
        bool LeftDown { get; }
        bool LeftUp { get; }
        bool RightDown { get; }
        bool RightUp { get; }
        float Scroll { get; }
        bool KeyDown(GameKey key);
        bool KeyHeld(GameKey key);
        /// <summary>Printable characters typed this frame ('\b' backspace, '\n' enter).</summary>
        string TypedText { get; }
        void Poll();
    }

    public static class InputBackendFactory
    {
        public static IInputBackend Create()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return new LegacyInputBackend();
#elif ENABLE_INPUT_SYSTEM && SC_INPUT_SYSTEM
            return new InputSystemBackend();
#else
            Debug.LogError("[SYSTEM] No usable input backend: enable the Input Manager or install the Input System package.");
            return new NullInputBackend();
#endif
        }
    }

    public sealed class NullInputBackend : IInputBackend
    {
        public Vector2 MouseScreenPosition => Vector2.zero;
        public bool LeftHeld => false;
        public bool LeftDown => false;
        public bool LeftUp => false;
        public bool RightDown => false;
        public bool RightUp => false;
        public float Scroll => 0f;
        public bool KeyDown(GameKey key) => false;
        public bool KeyHeld(GameKey key) => false;
        public string TypedText => "";
        public void Poll() { }
        public void Dispose() { }
    }

#if ENABLE_LEGACY_INPUT_MANAGER
    public sealed class LegacyInputBackend : IInputBackend
    {
        string _typed = "";

        public Vector2 MouseScreenPosition => UnityEngine.Input.mousePosition;
        public bool LeftHeld => UnityEngine.Input.GetMouseButton(0);
        public bool LeftDown => UnityEngine.Input.GetMouseButtonDown(0);
        public bool LeftUp => UnityEngine.Input.GetMouseButtonUp(0);
        public bool RightDown => UnityEngine.Input.GetMouseButtonDown(1);
        public bool RightUp => UnityEngine.Input.GetMouseButtonUp(1);
        public float Scroll => UnityEngine.Input.mouseScrollDelta.y;
        public string TypedText => _typed;

        public void Poll()
        {
            string raw = UnityEngine.Input.inputString;
            if (string.IsNullOrEmpty(raw))
            {
                _typed = "";
                return;
            }
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (c == '\r' || c == '\n') sb.Append('\n');
                else if (c == '\b' || c == (char)127) sb.Append('\b');
                else if (c >= 32 && c <= 126) sb.Append(c);
            }
            _typed = sb.ToString();
        }

        public void Dispose() { }

        public bool KeyDown(GameKey key) => UnityEngine.Input.GetKeyDown(Map(key)) || (key == GameKey.Enter && UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))
            || (key == GameKey.Shift && UnityEngine.Input.GetKeyDown(KeyCode.RightShift)) || (key == GameKey.Ctrl && UnityEngine.Input.GetKeyDown(KeyCode.RightControl));

        public bool KeyHeld(GameKey key) => UnityEngine.Input.GetKey(Map(key)) || (key == GameKey.Shift && UnityEngine.Input.GetKey(KeyCode.RightShift))
            || (key == GameKey.Ctrl && UnityEngine.Input.GetKey(KeyCode.RightControl));

        static KeyCode Map(GameKey key)
        {
            switch (key)
            {
                case GameKey.Escape: return KeyCode.Escape;
                case GameKey.Enter: return KeyCode.Return;
                case GameKey.Backspace: return KeyCode.Backspace;
                case GameKey.Delete: return KeyCode.Delete;
                case GameKey.Tab: return KeyCode.Tab;
                case GameKey.Space: return KeyCode.Space;
                case GameKey.Up: return KeyCode.UpArrow;
                case GameKey.Down: return KeyCode.DownArrow;
                case GameKey.Left: return KeyCode.LeftArrow;
                case GameKey.Right: return KeyCode.RightArrow;
                case GameKey.F1: return KeyCode.F1;
                case GameKey.F2: return KeyCode.F2;
                case GameKey.F3: return KeyCode.F3;
                case GameKey.F4: return KeyCode.F4;
                case GameKey.F5: return KeyCode.F5;
                case GameKey.F6: return KeyCode.F6;
                case GameKey.F7: return KeyCode.F7;
                case GameKey.F8: return KeyCode.F8;
                case GameKey.F9: return KeyCode.F9;
                case GameKey.F10: return KeyCode.F10;
                case GameKey.F11: return KeyCode.F11;
                case GameKey.F12: return KeyCode.F12;
                case GameKey.BackQuote: return KeyCode.BackQuote;
                case GameKey.Shift: return KeyCode.LeftShift;
                case GameKey.Ctrl: return KeyCode.LeftControl;
                default: return KeyCode.None;
            }
        }
    }
#endif

#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM && SC_INPUT_SYSTEM
    /// <summary>Backend for projects where only the new Input System is enabled (Unity 6 template default).</summary>
    public sealed class InputSystemBackend : IInputBackend
    {
        readonly StringBuilder _pending = new StringBuilder();
        string _typed = "";
        Keyboard _subscribed;

        // Position and the left button come from the last used pointer (mouse, pen or the Steam Deck's touchscreen);
        // the right button and the wheel only exist on a mouse.
        public Vector2 MouseScreenPosition => Pointer.current != null ? Pointer.current.position.ReadValue() : Vector2.zero;
        public bool LeftHeld => Pointer.current != null && Pointer.current.press.isPressed;
        public bool LeftDown => Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
        public bool LeftUp => Pointer.current != null && Pointer.current.press.wasReleasedThisFrame;
        public bool RightDown => Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        public bool RightUp => Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame;
        public float Scroll
        {
            get
            {
                if (Mouse.current == null) return 0f;
                // Depending on package version/settings scroll is either raw (120 per notch) or normalized (1 per notch).
                float y = Mouse.current.scroll.ReadValue().y;
                return Mathf.Abs(y) >= 10f ? y / 120f : y;
            }
        }
        public string TypedText => _typed;

        public void Poll()
        {
            var kb = Keyboard.current;
            if (kb != _subscribed)
            {
                if (_subscribed != null) _subscribed.onTextInput -= OnText;
                _subscribed = kb;
                if (kb != null) kb.onTextInput += OnText;
            }
            _typed = _pending.ToString();
            _pending.Clear();
        }

        void OnText(char c)
        {
            if (c == '\r' || c == '\n') _pending.Append('\n');
            else if (c == '\b' || c == (char)127) _pending.Append('\b'); // macOS sends DEL for backspace
            else if (c >= 32 && c <= 126) _pending.Append(c);
        }

        public void Dispose()
        {
            if (_subscribed != null) _subscribed.onTextInput -= OnText;
            _subscribed = null;
        }

        public bool KeyDown(GameKey key)
        {
            var k = Control(key);
            bool down = k != null && k.wasPressedThisFrame;
            if (!down && key == GameKey.Enter && Keyboard.current != null) down = Keyboard.current.numpadEnterKey.wasPressedThisFrame;
            if (!down && key == GameKey.Shift && Keyboard.current != null) down = Keyboard.current.rightShiftKey.wasPressedThisFrame;
            if (!down && key == GameKey.Ctrl && Keyboard.current != null) down = Keyboard.current.rightCtrlKey.wasPressedThisFrame;
            return down;
        }

        public bool KeyHeld(GameKey key)
        {
            var k = Control(key);
            bool held = k != null && k.isPressed;
            if (!held && key == GameKey.Shift && Keyboard.current != null) held = Keyboard.current.rightShiftKey.isPressed;
            if (!held && key == GameKey.Ctrl && Keyboard.current != null) held = Keyboard.current.rightCtrlKey.isPressed;
            return held;
        }

        static UnityEngine.InputSystem.Controls.KeyControl Control(GameKey key)
        {
            var kb = Keyboard.current;
            if (kb == null) return null;
            switch (key)
            {
                case GameKey.Escape: return kb.escapeKey;
                case GameKey.Enter: return kb.enterKey;
                case GameKey.Backspace: return kb.backspaceKey;
                case GameKey.Delete: return kb.deleteKey;
                case GameKey.Tab: return kb.tabKey;
                case GameKey.Space: return kb.spaceKey;
                case GameKey.Up: return kb.upArrowKey;
                case GameKey.Down: return kb.downArrowKey;
                case GameKey.Left: return kb.leftArrowKey;
                case GameKey.Right: return kb.rightArrowKey;
                case GameKey.F1: return kb.f1Key;
                case GameKey.F2: return kb.f2Key;
                case GameKey.F3: return kb.f3Key;
                case GameKey.F4: return kb.f4Key;
                case GameKey.F5: return kb.f5Key;
                case GameKey.F6: return kb.f6Key;
                case GameKey.F7: return kb.f7Key;
                case GameKey.F8: return kb.f8Key;
                case GameKey.F9: return kb.f9Key;
                case GameKey.F10: return kb.f10Key;
                case GameKey.F11: return kb.f11Key;
                case GameKey.F12: return kb.f12Key;
                case GameKey.BackQuote: return kb.backquoteKey;
                case GameKey.Shift: return kb.leftShiftKey;
                case GameKey.Ctrl: return kb.leftCtrlKey;
                default: return null;
            }
        }
    }
#endif
}
