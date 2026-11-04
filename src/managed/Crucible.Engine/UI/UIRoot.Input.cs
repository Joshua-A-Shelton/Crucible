using System.Numerics;
using Crucible.Platform;

namespace Crucible.UI;

// Input (ADR 0062): pointer routing with capture and hover, focus, Tab
// order and focus scopes, directional navigation, and actions from keys
// and gamepads.
public sealed partial class UIRoot
{
    // SDL scancodes the UI reads (SDL_scancode.h).
    private const int ScancodeReturn = 40, ScancodeEscape = 41, ScancodeTab = 43, ScancodeSpace = 44;
    private const int ScancodeRight = 79, ScancodeLeft = 80, ScancodeDown = 81, ScancodeUp = 82, ScancodeKeypadEnter = 88;

    private static readonly List<UIRoot> _attached = [];

    private WindowHandle _window;
    private readonly List<UIElement> _hovered = []; // the element under the pointer and its ancestors
    private UIElement? _capture;
    private PointerButton _captureButton;
    private UIElement? _focused;
    private UIElement? _pressedByAction; // pressed with Enter, Space, or the gamepad
    private Vector2 _pointer = new(float.NaN);
    private readonly PointerEvent _pointerEvent = new();
    private readonly KeyEvent _keyEvent = new();
    private readonly NavigationEvent _navigationEvent = new();
    private readonly UIEvent _event = new();
    // A held gamepad direction, and when it repeats next.
    private NavigationDirection? _held;
    private object? _heldBy; // the button or stick axis holding it
    private double _nextRepeat;

    /// <summary>The roots attached to windows, which <see cref="UIInputSystem"/> feeds.</summary>
    public static IReadOnlyList<UIRoot> Attached => _attached;

    /// <summary>
    /// The window whose input it takes, and whose display scale it uses
    /// (kept up to date when it changes); default for none.
    /// </summary>
    public WindowHandle Window
    {
        get => _window;
        set
        {
            _window = value;
            _attached.Remove(this);
            if (value.Value != 0)
            {
                _attached.Add(this);
                Scale = Platform.Api.WindowDisplayScale(value);
            }
        }
    }

    public UIInputMap InputMap { get; set; } = new();

    /// <summary>The element with focus, or null.</summary>
    public UIElement? FocusedElement => _focused;

    /// <summary>Whether the UI handled pointer input in the last <see cref="ProcessInput"/>: games ignore those clicks.</summary>
    public bool HandledPointer { get; private set; }

    /// <summary>Whether the UI handled key or gamepad input in the last <see cref="ProcessInput"/>.</summary>
    public bool HandledKeyboard { get; private set; }

    /// <summary>
    /// Routes a frame's events (the window's, if it has one; gamepads'
    /// always) through the tree as last laid out, then repeats held
    /// gamepad directions.
    /// </summary>
    public void ProcessInput(ReadOnlySpan<InputEvent> events)
    {
        HandledPointer = false;
        HandledKeyboard = false;
        foreach (ref readonly InputEvent input in events)
        {
            bool gamepad = input.Type is >= InputEventType.GamepadAdded and <= InputEventType.GamepadAxis;
            if (!gamepad && _window.Value != 0 && input.Window.Value != _window.Value)
            {
                continue;
            }
            switch (input.Type)
            {
                case InputEventType.MouseMotion:
                    PointerMoved(new Vector2(input.X, input.Y) / Scale, (KeyModifiers)input.Modifiers);
                    break;
                case InputEventType.MouseButtonDown:
                case InputEventType.MouseButtonUp:
                    PointerButtonChanged(new Vector2(input.X, input.Y) / Scale, (PointerButton)Math.Clamp(input.Code, 0, 5),
                        input.Type == InputEventType.MouseButtonDown, (KeyModifiers)input.Modifiers);
                    break;
                case InputEventType.MouseWheel:
                    PointerWheeled(new Vector2(input.X, input.Y) / Scale, new Vector2(input.Dx, input.Dy),
                        (KeyModifiers)input.Modifiers);
                    break;
                case InputEventType.KeyDown:
                case InputEventType.KeyUp:
                    KeyChanged(input.Code, input.Type == InputEventType.KeyDown, input.Value != 0, (KeyModifiers)input.Modifiers);
                    break;
                case InputEventType.GamepadButtonDown:
                case InputEventType.GamepadButtonUp:
                    GamepadButtonChanged((GamepadButton)input.Code, input.Type == InputEventType.GamepadButtonDown);
                    break;
                case InputEventType.GamepadAxis:
                    GamepadAxisMoved((GamepadAxis)input.Code, input.X);
                    break;
                case InputEventType.DisplayScaleChanged:
                    if (input.X > 0)
                    {
                        Scale = input.X;
                    }
                    break;
            }
        }
        if (_held is { } direction && Now >= _nextRepeat)
        {
            _nextRepeat = Now + InputMap.RepeatInterval;
            HandledKeyboard |= MoveOrNavigate(direction);
        }
    }

    // -----------------------------------------------------------------------
    // Pointer
    // -----------------------------------------------------------------------

    private void PointerMoved(Vector2 position, KeyModifiers modifiers)
    {
        _pointer = position;
        UpdateHover(position, modifiers);
        UIElement? target = _capture ?? HitEnabled(position);
        if (target is not null)
        {
            HandledPointer |= Bubble(target, UIElement.EventKind.PointerMove, Pointer(target, position, PointerButton.None, default, modifiers));
        }
    }

    private void PointerButtonChanged(Vector2 position, PointerButton button, bool down, KeyModifiers modifiers)
    {
        _pointer = position;
        UpdateHover(position, modifiers);
        if (down)
        {
            UIElement? target = HitEnabled(position);
            // Focus goes to the nearest focusable element pressed, or nowhere.
            UIElement? focusable = target;
            while (focusable is not null && !focusable.IsFocusable)
            {
                focusable = focusable.Parent;
            }
            if (focusable is not null)
            {
                SetFocus(focusable);
            }
            else
            {
                ClearFocus();
            }
            if (target is null)
            {
                return;
            }
            if (_capture is null)
            {
                _capture = target;
                _captureButton = button;
                target.State |= UIState.Pressed;
            }
            HandledPointer |= Bubble(target, UIElement.EventKind.PointerDown, Pointer(target, position, button, default, modifiers));
            return;
        }
        UIElement? released = _capture ?? HitEnabled(position);
        if (released is null)
        {
            return;
        }
        HandledPointer |= Bubble(released, UIElement.EventKind.PointerUp, Pointer(released, position, button, default, modifiers));
        if (_capture is not null && button == _captureButton)
        {
            UIElement captured = _capture;
            _capture = null;
            captured.State &= ~UIState.Pressed;
            // A click: released over what was pressed.
            if (button == PointerButton.Left && captured.Root == this && captured.IsEnabled && IsUnder(captured, position))
            {
                HandledPointer |= Bubble(captured, UIElement.EventKind.Activate, Plain(captured));
            }
        }
    }

    private void PointerWheeled(Vector2 position, Vector2 wheel, KeyModifiers modifiers)
    {
        UIElement? target = HitEnabled(position);
        if (target is not null)
        {
            HandledPointer |= Bubble(target, UIElement.EventKind.PointerWheel, Pointer(target, position, PointerButton.None, wheel, modifiers));
        }
    }

    // Hover is the element under the pointer and its ancestors, as CSS's :hover.
    private void UpdateHover(Vector2 position, KeyModifiers modifiers)
    {
        UIElement? hit = HitEnabled(position);
        List<UIElement> now = [];
        for (UIElement? element = hit; element is not null; element = element.Parent)
        {
            now.Add(element);
        }
        foreach (UIElement left in _hovered)
        {
            if (!now.Contains(left))
            {
                left.State &= ~UIState.Hover;
                left.Raise(UIElement.EventKind.PointerLeave, Pointer(left, position, PointerButton.None, default, modifiers));
            }
        }
        for (int i = now.Count - 1; i >= 0; --i) // outermost first
        {
            UIElement entered = now[i];
            if (!_hovered.Contains(entered))
            {
                entered.State |= UIState.Hover;
                entered.Raise(UIElement.EventKind.PointerEnter, Pointer(entered, position, PointerButton.None, default, modifiers));
            }
        }
        _hovered.Clear();
        _hovered.AddRange(now);
    }

    // The topmost enabled element: a disabled one (or one inside one) passes hits to its parent.
    private UIElement? HitEnabled(Vector2 position)
    {
        UIElement? hit = HitTest(position);
        while (hit is not null && !hit.IsEnabled)
        {
            hit = hit.Parent;
        }
        return hit;
    }

    private bool IsUnder(UIElement element, Vector2 position)
    {
        for (UIElement? hit = HitTest(position); hit is not null; hit = hit.Parent)
        {
            if (hit == element)
            {
                return true;
            }
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // Keys and gamepads
    // -----------------------------------------------------------------------

    private void KeyChanged(int scancode, bool down, bool repeat, KeyModifiers modifiers)
    {
        UIElement? target = KeyTarget();
        _keyEvent.Reset(target);
        _keyEvent.Scancode = scancode;
        _keyEvent.Modifiers = modifiers;
        _keyEvent.IsRepeat = repeat;
        bool handled = target is not null && Bubble(target, down ? UIElement.EventKind.KeyDown : UIElement.EventKind.KeyUp, _keyEvent);
        if (!handled)
        {
            // Unhandled keys navigate and act.
            if (down)
            {
                handled = scancode switch
                {
                    ScancodeTab => MoveOrNavigate((modifiers & KeyModifiers.Shift) != 0
                        ? NavigationDirection.Previous
                        : NavigationDirection.Next),
                    ScancodeUp => MoveOrNavigate(NavigationDirection.Up),
                    ScancodeDown => MoveOrNavigate(NavigationDirection.Down),
                    ScancodeLeft => MoveOrNavigate(NavigationDirection.Left),
                    ScancodeRight => MoveOrNavigate(NavigationDirection.Right),
                    ScancodeReturn or ScancodeKeypadEnter or ScancodeSpace => !repeat && Act(UIAction.Activate, true),
                    ScancodeEscape => !repeat && Act(UIAction.Cancel, true),
                    _ => false,
                };
            }
            else if (scancode is ScancodeReturn or ScancodeKeypadEnter or ScancodeSpace)
            {
                handled = Act(UIAction.Activate, false);
            }
        }
        HandledKeyboard |= handled;
    }

    private void GamepadButtonChanged(GamepadButton button, bool down)
    {
        if (!InputMap.Buttons.TryGetValue(button, out UIAction action) || action == UIAction.None)
        {
            return;
        }
        if (Direction(action) is { } direction)
        {
            if (down)
            {
                Hold(direction, button);
                HandledKeyboard |= MoveOrNavigate(direction);
            }
            else if (Equals(_heldBy, button))
            {
                _held = null;
                _heldBy = null;
            }
            return;
        }
        HandledKeyboard |= Act(action, down);
    }

    // The left stick navigates past the threshold, and lets go a little
    // inside it, so it doesn't flicker at the edge.
    private void GamepadAxisMoved(GamepadAxis axis, float value)
    {
        if (axis is not (GamepadAxis.LeftX or GamepadAxis.LeftY) || InputMap.StickThreshold <= 0)
        {
            return;
        }
        NavigationDirection negative = axis == GamepadAxis.LeftX ? NavigationDirection.Left : NavigationDirection.Up;
        NavigationDirection positive = axis == GamepadAxis.LeftX ? NavigationDirection.Right : NavigationDirection.Down;
        bool holding = Equals(_heldBy, axis);
        if (MathF.Abs(value) >= InputMap.StickThreshold)
        {
            NavigationDirection direction = value < 0 ? negative : positive;
            if (!holding || _held != direction)
            {
                Hold(direction, axis);
                HandledKeyboard |= MoveOrNavigate(direction);
            }
        }
        else if (holding && MathF.Abs(value) < InputMap.StickThreshold * 0.8f)
        {
            _held = null;
            _heldBy = null;
        }
    }

    private void Hold(NavigationDirection direction, object by)
    {
        _held = direction;
        _heldBy = by;
        _nextRepeat = Now + InputMap.RepeatDelay;
    }

    // Activate presses the focused element while held, and fires on press.
    private bool Act(UIAction action, bool down)
    {
        if (action == UIAction.Activate)
        {
            if (!down)
            {
                if (_pressedByAction is null)
                {
                    return false;
                }
                _pressedByAction.State &= ~UIState.Pressed;
                _pressedByAction = null;
                return true;
            }
            if (_focused is null)
            {
                return false;
            }
            _pressedByAction = _focused;
            _focused.State |= UIState.Pressed;
            Bubble(_focused, UIElement.EventKind.Activate, Plain(_focused));
            return true;
        }
        if (action == UIAction.Cancel && down)
        {
            UIElement? target = KeyTarget();
            return target is not null && Bubble(target, UIElement.EventKind.Cancel, Plain(target));
        }
        return Direction(action) is { } direction && down && MoveOrNavigate(direction);
    }

    private static NavigationDirection? Direction(UIAction action) => action switch
    {
        UIAction.Up => NavigationDirection.Up,
        UIAction.Down => NavigationDirection.Down,
        UIAction.Left => NavigationDirection.Left,
        UIAction.Right => NavigationDirection.Right,
        UIAction.Next => NavigationDirection.Next,
        UIAction.Previous => NavigationDirection.Previous,
        _ => null,
    };

    private UIElement? KeyTarget() => _focused ?? _content;

    // -----------------------------------------------------------------------
    // Focus and navigation
    // -----------------------------------------------------------------------

    internal bool SetFocus(UIElement element)
    {
        if (element.Root != this || !element.IsFocusable || !element.IsEnabled || !element.IsShown)
        {
            return false;
        }
        if (_focused == element)
        {
            return true;
        }
        ClearFocus();
        _focused = element;
        element.State |= UIState.Focus;
        element.Raise(UIElement.EventKind.GotFocus, Plain(element));
        // Scroll views around it show it.
        for (UIElement? at = element.Parent; at is not null; at = at.Parent)
        {
            if (at is ScrollView view)
            {
                view.ScrollIntoView(element.Bounds);
            }
        }
        return true;
    }

    /// <summary>Takes focus from whatever has it.</summary>
    public void ClearFocus()
    {
        if (_focused is not { } old)
        {
            return;
        }
        _focused = null;
        old.State &= ~UIState.Focus;
        old.Raise(UIElement.EventKind.LostFocus, Plain(old));
    }

    /// <summary>
    /// Navigation as from the keyboard or a gamepad: raised on the focused
    /// element (bubbling); unhandled, focus moves. True if anything
    /// happened.
    /// </summary>
    public bool MoveOrNavigate(NavigationDirection direction)
    {
        if (_focused is { } focused)
        {
            _navigationEvent.Reset(focused);
            _navigationEvent.Direction = direction;
            if (Bubble(focused, UIElement.EventKind.Navigate, _navigationEvent))
            {
                return true;
            }
        }
        return MoveFocus(direction);
    }

    /// <summary>Moves focus without asking the focused element: in tab order, or spatially.</summary>
    public bool MoveFocus(NavigationDirection direction)
    {
        UIElement? scope = Scope(_focused);
        if (scope is null)
        {
            return false;
        }
        List<UIElement> candidates = [];
        Collect(scope, candidates);
        if (candidates.Count == 0)
        {
            return false;
        }
        UIElement? next;
        if (_focused is null)
        {
            next = TabOrder(candidates)[0];
        }
        else if (direction is NavigationDirection.Next or NavigationDirection.Previous)
        {
            List<UIElement> order = TabOrder(candidates);
            int at = order.IndexOf(_focused);
            int step = direction == NavigationDirection.Next ? 1 : -1;
            next = order[((at < 0 ? -step : at) + step + order.Count) % order.Count];
        }
        else
        {
            next = Explicit(_focused, direction) ?? Nearest(_focused, direction, candidates);
        }
        return next is not null && next != _focused && SetFocus(next);
    }

    // The innermost focus scope around the element, or the whole tree.
    private UIElement? Scope(UIElement? element)
    {
        for (UIElement? at = element; at is not null; at = at.Parent)
        {
            if (at.IsFocusScope)
            {
                return at;
            }
        }
        return _content;
    }

    // The focusable elements in the scope, in tree order, not descending
    // into nested focus scopes (their insides are reached by focusing them).
    private static void Collect(UIElement element, List<UIElement> into, bool top = true)
    {
        if (element.Visibility != Visibility.Visible || (element.State & UIState.Disabled) != 0)
        {
            return;
        }
        if (element.IsFocusable)
        {
            into.Add(element);
        }
        if (!top && element.IsFocusScope)
        {
            return;
        }
        foreach (UIElement child in element.Children)
        {
            Collect(child, into, false);
        }
    }

    private static List<UIElement> TabOrder(List<UIElement> candidates)
    {
        List<UIElement> order = [.. candidates];
        // Stable: ties keep tree order.
        return [.. order.Select((element, index) => (element, index)).OrderBy(p => p.element.TabIndex)
            .ThenBy(p => p.index).Select(p => p.element)];
    }

    private static UIElement? Explicit(UIElement from, NavigationDirection direction)
    {
        UIElement? named = direction switch
        {
            NavigationDirection.Up => from.NavigateUp,
            NavigationDirection.Down => from.NavigateDown,
            NavigationDirection.Left => from.NavigateLeft,
            NavigationDirection.Right => from.NavigateRight,
            _ => null,
        };
        return named is { IsFocusable: true, IsEnabled: true, IsShown: true } && named.Root == from.Root ? named : null;
    }

    // The candidate ahead in the direction scoring lowest: the gap along the
    // direction, plus twice the sideways offset of their centers (zero
    // where they overlap sideways), so the one straight ahead wins over a
    // nearer one off to the side.
    private static UIElement? Nearest(UIElement from, NavigationDirection direction, List<UIElement> candidates)
    {
        System.Drawing.RectangleF a = from.Bounds;
        UIElement? best = null;
        float bestScore = float.PositiveInfinity;
        foreach (UIElement candidate in candidates)
        {
            if (candidate == from)
            {
                continue;
            }
            System.Drawing.RectangleF b = candidate.Bounds;
            (float along, float sideways) = direction switch
            {
                NavigationDirection.Right => (b.Left - a.Right, Offset(a.Top, a.Bottom, b.Top, b.Bottom)),
                NavigationDirection.Left => (a.Left - b.Right, Offset(a.Top, a.Bottom, b.Top, b.Bottom)),
                NavigationDirection.Down => (b.Top - a.Bottom, Offset(a.Left, a.Right, b.Left, b.Right)),
                _ => (a.Top - b.Bottom, Offset(a.Left, a.Right, b.Left, b.Right)),
            };
            // Ahead: past the current element's leading edge (allowing a little overlap).
            bool ahead = direction switch
            {
                NavigationDirection.Right => b.Left + b.Width * 0.5f > a.Right - 0.5f && b.Left >= a.Left + 0.5f,
                NavigationDirection.Left => b.Right - b.Width * 0.5f < a.Left + 0.5f && b.Right <= a.Right - 0.5f,
                NavigationDirection.Down => b.Top + b.Height * 0.5f > a.Bottom - 0.5f && b.Top >= a.Top + 0.5f,
                _ => b.Bottom - b.Height * 0.5f < a.Top + 0.5f && b.Bottom <= a.Bottom - 0.5f,
            };
            if (!ahead)
            {
                continue;
            }
            float score = MathF.Max(along, 0) + 2 * sideways;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    // The distance between two intervals' centers, or zero if they overlap.
    private static float Offset(float a0, float a1, float b0, float b1) =>
        a1 > b0 && b1 > a0 ? 0 : MathF.Abs((a0 + a1) * 0.5f - (b0 + b1) * 0.5f);

    // -----------------------------------------------------------------------
    // Raising
    // -----------------------------------------------------------------------

    // Raises on the element, then each enabled parent, until handled.
    private static bool Bubble(UIElement target, UIElement.EventKind kind, UIEvent e)
    {
        for (UIElement? element = target; element is not null && !e.Handled; element = element.Parent)
        {
            if (element.IsEnabled)
            {
                element.Raise(kind, e);
            }
        }
        return e.Handled;
    }

    private PointerEvent Pointer(UIElement source, Vector2 position, PointerButton button, Vector2 wheel,
        KeyModifiers modifiers)
    {
        _pointerEvent.Reset(source);
        _pointerEvent.Position = position;
        _pointerEvent.Button = button;
        _pointerEvent.Wheel = wheel;
        _pointerEvent.Modifiers = modifiers;
        return _pointerEvent;
    }

    private UIEvent Plain(UIElement source)
    {
        _event.Reset(source);
        return _event;
    }

    // An element leaving the tree loses hover, capture, and focus.
    internal void Detached(UIElement element)
    {
        if (_focused is not null && IsWithin(_focused, element))
        {
            ClearFocus();
        }
        if (_capture is not null && IsWithin(_capture, element))
        {
            _capture.State &= ~UIState.Pressed;
            _capture = null;
        }
        if (_pressedByAction is not null && IsWithin(_pressedByAction, element))
        {
            _pressedByAction.State &= ~UIState.Pressed;
            _pressedByAction = null;
        }
        foreach (UIElement hovered in _hovered)
        {
            if (IsWithin(hovered, element))
            {
                hovered.State &= ~UIState.Hover;
            }
        }
        _hovered.RemoveAll(hovered => IsWithin(hovered, element));
    }

    private static bool IsWithin(UIElement element, UIElement ancestor)
    {
        for (UIElement? at = element; at is not null; at = at.Parent)
        {
            if (at == ancestor)
            {
                return true;
            }
        }
        return false;
    }
}
