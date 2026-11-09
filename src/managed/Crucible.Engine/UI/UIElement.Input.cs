namespace Crucible.UI;

// Input (ADR 0062): what an element does with pointer, key, navigation,
// and action events, and how focus treats it. Controls override the On…
// methods; game code subscribes to the events, raised after them.
public partial class UIElement
{
    internal enum EventKind
    {
        PointerEnter,
        PointerLeave,
        PointerMove,
        PointerDown,
        PointerUp,
        PointerWheel,
        KeyDown,
        KeyUp,
        Navigate,
        Activate,
        Cancel,
        GotFocus,
        LostFocus,
        TextInput,
        DragEnter,
        DragOver,
        DragLeave,
        Drop,
    }

    /// <summary>Whether it can hold focus: Tab, navigation, and clicks give it focus.</summary>
    public bool IsFocusable { get; set; }

    /// <summary>Tab order: lower first, ties in tree order.</summary>
    public int TabIndex { get; set; }

    /// <summary>Keeps focus inside: Tab and navigation from within it don't leave (a modal dialog, a menu).</summary>
    public bool IsFocusScope { get; set; }

    /// <summary>Where navigation goes from here, before looking spatially.</summary>
    public UIElement? NavigateUp { get; set; }

    public UIElement? NavigateDown { get; set; }

    public UIElement? NavigateLeft { get; set; }

    public UIElement? NavigateRight { get; set; }

    public bool IsFocused => (State & UIState.Focus) != 0;

    /// <summary>
    /// Takes part in drags (ADR 0071): it's told when a drag enters, moves
    /// over, and leaves it, and gets the drop if it accepted the drag.
    /// </summary>
    public bool AllowDrop { get; set; }

    /// <summary>
    /// Takes typed text: while it has focus, the root turns the platform's
    /// text input on (showing an on-screen keyboard where there is one).
    /// </summary>
    protected internal virtual bool AcceptsTextInput => false;

    /// <summary>Not disabled, nor inside a disabled element: input reaches it.</summary>
    public bool IsEnabled
    {
        get
        {
            for (UIElement? element = this; element is not null; element = element.Parent)
            {
                if ((element.State & UIState.Disabled) != 0)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>Gives it focus, if it can take it now (focusable, enabled, shown, in a root).</summary>
    public bool Focus() => Root?.SetFocus(this) ?? false;

    public event Action<UIElement, PointerEvent>? PointerEnter;
    public event Action<UIElement, PointerEvent>? PointerLeave;
    public event Action<UIElement, PointerEvent>? PointerMove;
    public event Action<UIElement, PointerEvent>? PointerDown;
    public event Action<UIElement, PointerEvent>? PointerUp;
    public event Action<UIElement, PointerEvent>? PointerWheel;
    public event Action<UIElement, KeyEvent>? KeyDown;
    public event Action<UIElement, KeyEvent>? KeyUp;
    public event Action<UIElement, NavigationEvent>? Navigate;
    public event Action<UIElement, UIEvent>? Activated;
    public event Action<UIElement, UIEvent>? Canceled;
    public event Action<UIElement, UIEvent>? GotFocus;
    public event Action<UIElement, UIEvent>? LostFocus;
    public event Action<UIElement, TextInputEvent>? TextInput;
    public event Action<UIElement, DragEvent>? DragEnter;
    public event Action<UIElement, DragEvent>? DragOver;
    public event Action<UIElement, DragEvent>? DragLeave;
    public event Action<UIElement, DragEvent>? Drop;

    /// <summary>The pointer came over it or one of its children (doesn't bubble).</summary>
    protected virtual void OnPointerEnter(PointerEvent e)
    {
    }

    protected virtual void OnPointerLeave(PointerEvent e)
    {
    }

    protected virtual void OnPointerMove(PointerEvent e)
    {
    }

    /// <summary>A press over it; it captures the pointer until the release.</summary>
    protected virtual void OnPointerDown(PointerEvent e)
    {
    }

    protected virtual void OnPointerUp(PointerEvent e)
    {
    }

    protected virtual void OnPointerWheel(PointerEvent e)
    {
    }

    protected virtual void OnKeyDown(KeyEvent e)
    {
    }

    protected virtual void OnKeyUp(KeyEvent e)
    {
    }

    /// <summary>Navigation from the focused element; handle it to keep focus here.</summary>
    protected virtual void OnNavigate(NavigationEvent e)
    {
    }

    /// <summary>A click, Enter, Space, or the gamepad's activate button.</summary>
    protected virtual void OnActivate(UIEvent e)
    {
    }

    /// <summary>Escape, or the gamepad's cancel button.</summary>
    protected virtual void OnCancel(UIEvent e)
    {
    }

    protected virtual void OnGotFocus(UIEvent e)
    {
    }

    protected virtual void OnLostFocus(UIEvent e)
    {
    }

    /// <summary>Typed text, while it (or a child) has focus.</summary>
    protected virtual void OnDragEnter(DragEvent e)
    {
    }

    /// <summary>The pointer moved over it during a drag: set <see cref="DragEvent.Accepted"/> to take a drop here.</summary>
    protected virtual void OnDragOver(DragEvent e)
    {
    }

    protected virtual void OnDragLeave(DragEvent e)
    {
    }

    protected virtual void OnDrop(DragEvent e)
    {
    }

    protected virtual void OnTextInput(TextInputEvent e)
    {
    }

    /// <summary>
    /// Every UI update while it has focus, with the root's clock: for what
    /// changes with time alone, such as a blinking caret.
    /// </summary>
    protected internal virtual void OnFocusedTick(double now)
    {
    }

    internal void Raise(EventKind kind, UIEvent e)
    {
        switch (kind)
        {
            case EventKind.PointerEnter:
                OnPointerEnter((PointerEvent)e);
                PointerEnter?.Invoke(this, (PointerEvent)e);
                break;
            case EventKind.PointerLeave:
                OnPointerLeave((PointerEvent)e);
                PointerLeave?.Invoke(this, (PointerEvent)e);
                break;
            case EventKind.PointerMove:
                OnPointerMove((PointerEvent)e);
                PointerMove?.Invoke(this, (PointerEvent)e);
                break;
            case EventKind.PointerDown:
                OnPointerDown((PointerEvent)e);
                PointerDown?.Invoke(this, (PointerEvent)e);
                break;
            case EventKind.PointerUp:
                OnPointerUp((PointerEvent)e);
                PointerUp?.Invoke(this, (PointerEvent)e);
                break;
            case EventKind.PointerWheel:
                OnPointerWheel((PointerEvent)e);
                PointerWheel?.Invoke(this, (PointerEvent)e);
                break;
            case EventKind.KeyDown:
                OnKeyDown((KeyEvent)e);
                KeyDown?.Invoke(this, (KeyEvent)e);
                break;
            case EventKind.KeyUp:
                OnKeyUp((KeyEvent)e);
                KeyUp?.Invoke(this, (KeyEvent)e);
                break;
            case EventKind.Navigate:
                OnNavigate((NavigationEvent)e);
                Navigate?.Invoke(this, (NavigationEvent)e);
                break;
            case EventKind.Activate:
                OnActivate(e);
                Activated?.Invoke(this, e);
                break;
            case EventKind.Cancel:
                OnCancel(e);
                Canceled?.Invoke(this, e);
                break;
            case EventKind.GotFocus:
                OnGotFocus(e);
                GotFocus?.Invoke(this, e);
                break;
            case EventKind.LostFocus:
                OnLostFocus(e);
                LostFocus?.Invoke(this, e);
                break;
            case EventKind.TextInput:
                OnTextInput((TextInputEvent)e);
                TextInput?.Invoke(this, (TextInputEvent)e);
                break;
            case EventKind.DragEnter:
                OnDragEnter((DragEvent)e);
                DragEnter?.Invoke(this, (DragEvent)e);
                break;
            case EventKind.DragOver:
                OnDragOver((DragEvent)e);
                DragOver?.Invoke(this, (DragEvent)e);
                break;
            case EventKind.DragLeave:
                OnDragLeave((DragEvent)e);
                DragLeave?.Invoke(this, (DragEvent)e);
                break;
            case EventKind.Drop:
                OnDrop((DragEvent)e);
                Drop?.Invoke(this, (DragEvent)e);
                break;
        }
    }

    // Shown and drawn, all the way up: what focus and input can reach.
    internal bool IsShown
    {
        get
        {
            for (UIElement? element = this; element is not null; element = element.Parent)
            {
                if (element.Visibility != Visibility.Visible)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
