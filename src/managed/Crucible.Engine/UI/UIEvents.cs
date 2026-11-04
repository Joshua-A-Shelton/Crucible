using System.Numerics;
using Crucible.Loop;
using Crucible.Platform;

namespace Crucible.UI;

/// <summary>
/// A UI event (ADR 0062), raised on its element and then each parent in
/// turn (it bubbles) until one marks it <see cref="Handled"/>. The root
/// reuses one object per kind: don't keep it past the handler.
/// </summary>
public class UIEvent
{
    /// <summary>Stops it bubbling further, and tells the game the UI used the input.</summary>
    public bool Handled { get; set; }

    /// <summary>The element it was first raised on.</summary>
    public UIElement? Source { get; internal set; }

    internal void Reset(UIElement? source)
    {
        Handled = false;
        Source = source;
    }
}

public enum PointerButton
{
    None,
    Left,
    Middle,
    Right,
    Back,
    Forward,
}

public sealed class PointerEvent : UIEvent
{
    /// <summary>Where the pointer is, in the root's logical units.</summary>
    public Vector2 Position { get; internal set; }

    /// <summary>The button pressed or released; None for moves, wheels, entering, and leaving.</summary>
    public PointerButton Button { get; internal set; }

    /// <summary>Wheel notches (y up); zero unless it's a wheel event.</summary>
    public Vector2 Wheel { get; internal set; }

    public KeyModifiers Modifiers { get; internal set; }
}

public sealed class KeyEvent : UIEvent
{
    /// <summary>The key's position on the keyboard (SDL's scancode), whatever the layout.</summary>
    public int Scancode { get; internal set; }

    public KeyModifiers Modifiers { get; internal set; }

    /// <summary>A key held down, repeating.</summary>
    public bool IsRepeat { get; internal set; }
}

public enum NavigationDirection
{
    Up,
    Down,
    Left,
    Right,
    Next,     // Tab
    Previous, // Shift+Tab
}

/// <summary>
/// A move of focus asked for by arrows, Tab, the D-pad, or a stick. An
/// element that handles it keeps focus where it is (a slider moving its
/// value, say); unhandled, focus moves.
/// </summary>
public sealed class NavigationEvent : UIEvent
{
    public NavigationDirection Direction { get; internal set; }
}

/// <summary>What gamepad buttons do in the UI.</summary>
public enum UIAction
{
    None,
    Up,
    Down,
    Left,
    Right,
    Next,
    Previous,
    Activate,
    Cancel,
}

/// <summary>
/// A root's gamepad mapping (ADR 0062): which button does what, the left
/// stick's threshold, and how a held direction repeats. The defaults: the
/// D-pad and left stick navigate, South activates, East cancels, the
/// shoulders move like Shift+Tab and Tab.
/// </summary>
public sealed class UIInputMap
{
    public Dictionary<GamepadButton, UIAction> Buttons { get; } = new()
    {
        [GamepadButton.DpadUp] = UIAction.Up,
        [GamepadButton.DpadDown] = UIAction.Down,
        [GamepadButton.DpadLeft] = UIAction.Left,
        [GamepadButton.DpadRight] = UIAction.Right,
        [GamepadButton.South] = UIAction.Activate,
        [GamepadButton.East] = UIAction.Cancel,
        [GamepadButton.LeftShoulder] = UIAction.Previous,
        [GamepadButton.RightShoulder] = UIAction.Next,
    };

    /// <summary>How far the left stick tilts before it navigates (0 to 1); 0 turns the stick off.</summary>
    public float StickThreshold { get; set; } = 0.5f;

    /// <summary>Seconds a direction is held before it repeats, and between repeats.</summary>
    public float RepeatDelay { get; set; } = 0.4f;

    public float RepeatInterval { get; set; } = 0.1f;
}

/// <summary>
/// Gives every UI root attached to a window that window's events (ADR
/// 0062), in the Input phase right after they're polled, so game systems
/// can see what the UI used (<see cref="UIRoot.HandledPointer"/>).
/// </summary>
public sealed class UIInputSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
        // Indexed: a foreach over the interface would allocate its enumerator every frame.
        IReadOnlyList<UIRoot> roots = UIRoot.Attached;
        for (int i = 0; i < roots.Count; ++i)
        {
            roots[i].ProcessInput(context.Events);
        }
    }
}
