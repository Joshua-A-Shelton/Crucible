using System.Numerics;

namespace Crucible.UI;

// A chevron drawn in the element's Foreground, pointing `direction`.
internal static class Chevron
{
    public static void Draw(Canvas canvas, Vector2 size, Vector4 color, NavigationDirection direction)
    {
        float w = size.X, h = size.Y, s = MathF.Min(w, h) * 0.3f, line = MathF.Max(MathF.Min(w, h) * 0.12f, 1.5f);
        Vector2 c = size * 0.5f;
        (Vector2 a, Vector2 tip, Vector2 b) = direction switch
        {
            NavigationDirection.Left => (c + new Vector2(s * 0.5f, -s), c + new Vector2(-s * 0.5f, 0), c + new Vector2(s * 0.5f, s)),
            NavigationDirection.Right => (c + new Vector2(-s * 0.5f, -s), c + new Vector2(s * 0.5f, 0), c + new Vector2(-s * 0.5f, s)),
            NavigationDirection.Up => (c + new Vector2(-s, s * 0.5f), c + new Vector2(0, -s * 0.5f), c + new Vector2(s, s * 0.5f)),
            _ => (c + new Vector2(-s, -s * 0.5f), c + new Vector2(0, s * 0.5f), c + new Vector2(s, -s * 0.5f)),
        };
        canvas.DrawPolyline([a, tip, b], line, color);
    }
}

/// <summary>One of a <see cref="Spinner"/>'s arrows (classes <c>previous</c>, <c>next</c>), a chevron in its <c>Foreground</c>.</summary>
public sealed class SpinnerArrow : UIElement
{
    private readonly int _step;

    internal SpinnerArrow(int step)
    {
        _step = step;
        Classes.Add(step < 0 ? "previous" : "next");
    }

    protected override void OnDraw(Canvas canvas) =>
        Chevron.Draw(canvas, ActualSize, Foreground, _step < 0 ? NavigationDirection.Left : NavigationDirection.Right);

    protected override void OnActivate(UIEvent e)
    {
        if (Parent?.Parent is Spinner spinner)
        {
            e.Handled = true;
            spinner.Step(_step);
        }
    }
}

/// <summary>
/// A game-menu cycler (ADR 0063): "‹ Normal ›". Left and right (keys,
/// D-pad, stick) or the arrows step through <see cref="Options"/>, wrapping
/// unless <see cref="Wraps"/> is off; up and down navigate away.
/// </summary>
public class Spinner : UIElement
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Label _value = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly SpinnerArrow _previous = new(-1) { VerticalAlignment = VerticalAlignment.Center };
    private readonly SpinnerArrow _next = new(1) { VerticalAlignment = VerticalAlignment.Center };
    private IReadOnlyList<string> _options = [];
    private int _selected = -1;

    public Spinner()
    {
        IsFocusable = true;
        _row.Add(_previous);
        _row.Add(_value);
        _row.Add(_next);
        AddChild(_row);
    }

    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            _options = value ?? [];
            SelectedIndex = _options.Count == 0 ? -1 : Math.Clamp(_selected, 0, _options.Count - 1);
            Show();
        }
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int index = _options.Count == 0 ? -1 : Math.Clamp(value, 0, _options.Count - 1);
            if (index == _selected)
            {
                return;
            }
            _selected = index;
            Show();
            SelectionChanged?.Invoke(this);
        }
    }

    public string? SelectedOption => _selected >= 0 ? _options[_selected] : null;

    public bool Wraps { get; set; } = true;

    public event Action<Spinner>? SelectionChanged;

    public Label ValueLabel => _value;

    /// <summary>Moves the selection by <paramref name="step"/> options, wrapping if it wraps.</summary>
    public void Step(int step)
    {
        if (_options.Count == 0)
        {
            return;
        }
        int next = _selected + step;
        SelectedIndex = Wraps ? ((next % _options.Count) + _options.Count) % _options.Count : next;
    }

    protected override void OnNavigate(NavigationEvent e)
    {
        if (e.Direction is NavigationDirection.Left or NavigationDirection.Right)
        {
            Step(e.Direction == NavigationDirection.Left ? -1 : 1);
            e.Handled = true;
        }
    }

    private void Show() => _value.Text = SelectedOption ?? "";
}

/// <summary>A <see cref="ComboBox"/>'s arrow, a downward chevron in its <c>Foreground</c>.</summary>
public sealed class ComboBoxArrow : UIElement
{
    internal ComboBoxArrow()
    {
    }

    protected override void OnDraw(Canvas canvas) => Chevron.Draw(canvas, ActualSize, Foreground, NavigationDirection.Down);
}

/// <summary>
/// A dropdown (ADR 0063): shows the selected option and an arrow; activated,
/// it opens a <see cref="Popup"/> below it holding a <see cref="ListBox"/>
/// of the options, focused on the selection. Activating an option selects
/// it and closes the list; Cancel or a press outside closes it unchanged.
/// </summary>
public class ComboBox : ButtonBase
{
    private readonly DockPanel _row = new();
    private readonly Label _value = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBoxArrow _arrow = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _popup = new();
    private readonly ListBox _list = new();
    private IReadOnlyList<string> _options = [];
    private int _selected = -1;

    public ComboBox()
    {
        DockPanel.SetDock(_arrow, Dock.Right);
        _row.Add(_arrow);
        _row.Add(_value);
        Content = _row;
        _popup.Anchor = this;
        _popup.Content = _list;
        _list.Classes.Add("dropdown"); // the default theme limits its height
        _list.CreateItem = () => new Label();
        _list.BindItem = (element, index) => ((Label)element).Text = _options[index];
        _list.ItemActivated += (_, index) =>
        {
            SelectedIndex = index;
            _popup.Close();
        };
    }

    public IReadOnlyList<string> Options
    {
        get => _options;
        set
        {
            _options = value ?? [];
            _list.ItemCount = _options.Count;
            if (_selected >= _options.Count)
            {
                SelectedIndex = _options.Count - 1;
            }
            Show();
        }
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int index = value >= 0 && value < _options.Count ? value : -1;
            if (index == _selected)
            {
                return;
            }
            _selected = index;
            _list.SelectedIndex = index;
            Show();
            SelectionChanged?.Invoke(this);
        }
    }

    public string? SelectedOption => _selected >= 0 ? _options[_selected] : null;

    public event Action<ComboBox>? SelectionChanged;

    /// <summary>Just before its list opens: the moment to bring its options up to date.</summary>
    public event Action<ComboBox>? Opening;

    public Popup Popup => _popup;

    public ListBox List => _list;

    public bool IsOpen => _popup.IsOpen;

    protected override void OnClicked()
    {
        if (_popup.IsOpen)
        {
            _popup.Close();
            return;
        }
        Opening?.Invoke(this);
        if (Root is not { } root || _options.Count == 0)
        {
            return;
        }
        _popup.Open(root);
        _list.FocusItem(Math.Max(_selected, 0));
    }

    protected override void OnDetached(UIRoot root) => _popup.Close();

    private void Show() => _value.Text = SelectedOption ?? "";
}
