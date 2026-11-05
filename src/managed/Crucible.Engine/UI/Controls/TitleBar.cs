using System.Drawing;
using System.Numerics;
using Crucible.Platform;

namespace Crucible.UI;

/// <summary>What a window button does (ADR 0066).</summary>
public enum WindowButtonKind
{
    Minimize,
    Maximize, // and restores a maximized window
    Close,
}

// The window calls a title bar makes: the platform's, or a test's.
internal interface IWindowControls
{
    bool StartMove(WindowHandle window);
    void Minimize(WindowHandle window);
    void ToggleMaximized(WindowHandle window);
    bool IsMaximized(WindowHandle window);
    void ShowMenu(WindowHandle window, Vector2 pixels);
}

internal sealed class PlatformWindowControls : IWindowControls
{
    public bool StartMove(WindowHandle window) => Platform.Api.StartWindowMove(window);

    public void Minimize(WindowHandle window) => Platform.Api.MinimizeWindow(window);

    public void ToggleMaximized(WindowHandle window)
    {
        if (Platform.Api.WindowMaximized(window))
        {
            Platform.Api.RestoreWindow(window);
        }
        else
        {
            Platform.Api.MaximizeWindow(window);
        }
    }

    public bool IsMaximized(WindowHandle window) => Platform.Api.WindowMaximized(window);

    public void ShowMenu(WindowHandle window, Vector2 pixels) => Platform.Api.ShowWindowMenu(window, pixels);
}

/// <summary>
/// A window's minimize, maximize (restore), or close button (ADR 0066),
/// for windows drawing their own frame; classes <c>minimize</c>,
/// <c>maximize</c>, <c>close</c>. Minimize and maximize act on the root's
/// window; close does nothing itself: its owner handles <see cref="UIElement.Activated"/>.
/// </summary>
public sealed class WindowButton : UIElement
{
    public WindowButton(WindowButtonKind kind)
    {
        Kind = kind;
        Classes.Add(kind switch
        {
            WindowButtonKind.Minimize => "minimize",
            WindowButtonKind.Maximize => "maximize",
            _ => "close",
        });
    }

    public WindowButtonKind Kind { get; }

    protected override Vector2 MeasureCore(Vector2 available) => new(12, 12);

    protected override void OnActivate(UIEvent e)
    {
        if (Root is not { } root || Kind == WindowButtonKind.Close)
        {
            return;
        }
        e.Handled = true;
        if (Kind == WindowButtonKind.Minimize)
        {
            TitleBar.Controls.Minimize(root.Window);
        }
        else
        {
            TitleBar.Controls.ToggleMaximized(root.Window);
        }
    }

    // A line, a square (two overlapping while maximized), or a cross, 10 units across.
    protected override void OnDraw(Canvas canvas)
    {
        Vector2 c = ActualSize * 0.5f;
        float s = 5, width = MathF.Max(1, 1 / (Root?.Scale ?? 1));
        Vector4 color = Foreground;
        switch (Kind)
        {
            case WindowButtonKind.Minimize:
                canvas.DrawLine(c + new Vector2(-s, 0), c + new Vector2(s, 0), width, color);
                break;
            case WindowButtonKind.Maximize:
                if (Root is { } root && TitleBar.Controls.IsMaximized(root.Window))
                {
                    canvas.StrokeRect(c + new Vector2(-s, -s + 2), new Vector2(2 * s - 2), color, width);
                    canvas.DrawPolyline([c + new Vector2(-s + 2, -s + 2), c + new Vector2(-s + 2, -s),
                        c + new Vector2(s, -s), c + new Vector2(s, s - 2), c + new Vector2(s - 2, s - 2)], width, color);
                }
                else
                {
                    canvas.StrokeRect(c - new Vector2(s), new Vector2(2 * s), color, width);
                }
                break;
            default:
                canvas.DrawLine(c - new Vector2(s), c + new Vector2(s), width, color);
                canvas.DrawLine(c + new Vector2(-s, s), c + new Vector2(s, -s), width, color);
                break;
        }
    }
}

/// <summary>
/// A window's own title bar (ADR 0066), for windows without the system's
/// frame: <see cref="Leading"/> content on the left (a menu bar), the
/// <see cref="Title"/> centered, and minimize, maximize, and close buttons
/// on the right. Dragging its empty parts moves the window (the system's
/// move, with its snapping), a double-click maximizes or restores it, and a
/// right-click shows the system's window menu.
/// </summary>
public class TitleBar : UIElement
{
    private const float MoveThreshold = 4; // units moved before a press moves the window

    private readonly Label _title = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal };
    private UIElement? _leading;
    private bool _pressed;
    private Vector2 _pressedAt;

    public TitleBar()
    {
        _title.Classes.Add("title");
        MinimizeButton = new WindowButton(WindowButtonKind.Minimize);
        MaximizeButton = new WindowButton(WindowButtonKind.Maximize);
        CloseButton = new WindowButton(WindowButtonKind.Close);
        _buttons.Add(MinimizeButton);
        _buttons.Add(MaximizeButton);
        _buttons.Add(CloseButton);
        AddChild(_title);
        AddChild(_buttons);
    }

    // Tests: window calls they can see.
    internal static IWindowControls Controls { get; set; } = new PlatformWindowControls();

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>Content at the left end: the main window's menu bar.</summary>
    public UIElement? Leading
    {
        get => _leading;
        set
        {
            if (_leading == value)
            {
                return;
            }
            if (_leading is not null)
            {
                RemoveChild(_leading);
            }
            _leading = value;
            if (value is not null)
            {
                InsertChild(0, value);
            }
        }
    }

    public WindowButton MinimizeButton { get; }

    public WindowButton MaximizeButton { get; }

    /// <summary>Its owner closes the window when it's activated.</summary>
    public WindowButton CloseButton { get; }

    protected override Vector2 MeasureCore(Vector2 available)
    {
        Vector2 infinite = new(float.PositiveInfinity, available.Y);
        _leading?.Measure(infinite);
        _title.Measure(infinite);
        _buttons.Measure(infinite);
        Vector2 leading = _leading?.DesiredSize ?? Vector2.Zero;
        return new Vector2(leading.X + _title.DesiredSize.X + _buttons.DesiredSize.X,
            MathF.Max(MathF.Max(leading.Y, _title.DesiredSize.Y), _buttons.DesiredSize.Y));
    }

    protected override void ArrangeCore(RectangleF content)
    {
        float leading = _leading?.DesiredSize.X ?? 0, buttons = _buttons.DesiredSize.X, title = _title.DesiredSize.X;
        _leading?.Arrange(new RectangleF(content.X, content.Y, leading, content.Height));
        _buttons.Arrange(new RectangleF(content.Right - buttons, content.Y, buttons, content.Height));
        // Centered on the whole bar (the window) if it fits between the two
        // ends, else in the room between them.
        float left = content.X + leading, right = content.Right - buttons;
        float x = Math.Clamp(Bounds.X + (Bounds.Width - title) * 0.5f, left, MathF.Max(right - title, left));
        _title.Arrange(new RectangleF(x, content.Y, MathF.Min(title, MathF.Max(right - x, 0)), content.Height));
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (Root is not { } root || (e.Source != this && e.Source != _title))
        {
            return; // a press on the menu bar or a button is theirs
        }
        e.Handled = true;
        if (e.Button == PointerButton.Right)
        {
            Controls.ShowMenu(root.Window, e.Position * root.Scale);
            return;
        }
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        if (e.ClickCount == 2)
        {
            _pressed = false;
            Controls.ToggleMaximized(root.Window);
            InvalidateVisual();
            return;
        }
        _pressed = true;
        _pressedAt = e.Position;
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (!_pressed || Vector2.Distance(e.Position, _pressedAt) < MoveThreshold)
        {
            return;
        }
        e.Handled = true;
        _pressed = false;
        OnMoveStarted(_pressedAt);
    }

    protected override void OnPointerUp(PointerEvent e) => _pressed = false;

    /// <summary>
    /// Its empty part dragged: by default, the system moves the window. A
    /// window that moves otherwise (an editor's floating window, dragged
    /// so it can dock) does its own.
    /// </summary>
    protected virtual void OnMoveStarted(Vector2 pressedAt)
    {
        if (Root is { } root)
        {
            Controls.StartMove(root.Window);
        }
    }
}
