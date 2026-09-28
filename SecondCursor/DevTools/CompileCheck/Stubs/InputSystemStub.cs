// Harness-only stub of the parts of com.unity.inputsystem used by SecondCursor. Signatures mirror
// the real package (1.7+); bodies are meaningless. NOT part of the game.
using System;
namespace UnityEngine.InputSystem.Controls
{
    public abstract class InputControl { public string name => null; }
    public abstract class InputControl<TValue> : InputControl where TValue : struct { public TValue ReadValue() => default; }
    public class AxisControl : InputControl<float> { }
    public class ButtonControl : AxisControl
    {
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
    }
    public class KeyControl : ButtonControl { public UnityEngine.InputSystem.Key keyCode => default; }
    public class Vector2Control : InputControl<Vector2> { }
    public class DeltaControl : Vector2Control { }
}
namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;
    public class InputDevice { public bool added => true; }
    public class Pointer : InputDevice { public Vector2Control position => null; public Vector2Control delta => null; }
    public class Mouse : Pointer
    {
        public static Mouse current => null;
        public ButtonControl leftButton => null;
        public ButtonControl rightButton => null;
        public ButtonControl middleButton => null;
        public DeltaControl scroll => null;
    }
    public class Keyboard : InputDevice
    {
        public static Keyboard current => null;
        public event Action<char> onTextInput { add { } remove { } }
        public KeyControl this[Key key] => null;
        public KeyControl escapeKey => null;
        public KeyControl enterKey => null;
        public KeyControl numpadEnterKey => null;
        public KeyControl backspaceKey => null;
        public KeyControl deleteKey => null;
        public KeyControl tabKey => null;
        public KeyControl spaceKey => null;
        public KeyControl leftShiftKey => null;
        public KeyControl rightShiftKey => null;
        public KeyControl leftCtrlKey => null;
        public KeyControl rightCtrlKey => null;
        public KeyControl upArrowKey => null;
        public KeyControl downArrowKey => null;
        public KeyControl leftArrowKey => null;
        public KeyControl rightArrowKey => null;
        public KeyControl f1Key => null; public KeyControl f2Key => null; public KeyControl f3Key => null;
        public KeyControl f4Key => null; public KeyControl f5Key => null; public KeyControl f6Key => null;
        public KeyControl f7Key => null; public KeyControl f8Key => null; public KeyControl f9Key => null;
        public KeyControl f10Key => null; public KeyControl f11Key => null; public KeyControl f12Key => null;
        public KeyControl backquoteKey => null;
    }
    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket,
        Minus, Equals, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu,
        Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete,
        CapsLock, NumLock, PrintScreen, ScrollLock, Pause, NumpadEnter,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
    }
}
