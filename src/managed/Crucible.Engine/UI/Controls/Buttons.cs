using System.Drawing;
using System.Numerics;

namespace Crucible.UI;

/// <summary>
/// What buttons and toggles share (ADR 0063): focusable, one content
/// element (or text, as a shortcut that makes a centered <see cref="Label"/>),
/// and a click on activate: a click, Enter, Space, or the gamepad's
/// activate button.
/// </summary>
public abstract class ButtonBase : UIElement
{
    private UIElement? _content;
    private Label? _textLabel; // the label Text made

    protected ButtonBase() => IsFocusable = true;

    public UIElement? Content
    {
        get => _content;
        set
        {
            if (_content == value)
            {
                return;
            }
            if (_content is not null)
            {
                RemoveContent(_content);
            }
            _content = value;
            _textLabel = null;
            if (value is not null)
            {
                AddContent(value);
            }
        }
    }

    /// <summary>The content as text: setting it makes the content a centered label.</summary>
    public string? Text
    {
        get => (_content as Label)?.Text;
        set
        {
            if (_textLabel is not null && _content == _textLabel)
            {
                _textLabel.Text = value ?? "";
                return;
            }
            Label label = new(value ?? "")
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Content = label;
            _textLabel = label;
        }
    }

    public event Action<ButtonBase>? Clicked;

    protected override void OnActivate(UIEvent e)
    {
        e.Handled = true;
        OnClicked();
        Clicked?.Invoke(this);
    }

    /// <summary>Before <see cref="Clicked"/> is raised.</summary>
    protected virtual void OnClicked()
    {
    }

    // Where the content goes among the button's children; a toggle puts it beside its box.
    private protected virtual void AddContent(UIElement content) => AddChild(content);

    private protected virtual void RemoveContent(UIElement content) => RemoveChild(content);
}

/// <summary>A push button (ADR 0063): styled as <c>Button</c>, with <c>:hover</c>, <c>:pressed</c>, <c>:focus</c>.</summary>
public class Button : ButtonBase
{
    public Button()
    {
    }

    public Button(string text) => Text = text;
}

/// <summary>A toggle's box (ADR 0063); it and its <see cref="ToggleMark"/> have the class <c>on</c> while the toggle is on.</summary>
public sealed class ToggleBox : UIElement
{
    internal ToggleBox(ToggleMark mark) => AddChild(mark);
}

/// <summary>A toggle's mark, inside its <see cref="ToggleBox"/>: a check, or a switch's knob, as styled.</summary>
public sealed class ToggleMark : UIElement
{
    internal ToggleMark()
    {
    }
}

/// <summary>
/// An on/off button (ADR 0063): a <see cref="ToggleBox"/> holding a
/// <see cref="ToggleMark"/>, then its content. A checkbox or a switch is
/// a matter of style; the toggle, its box, and its mark all have the class
/// <c>on</c> while it's on.
/// </summary>
public class Toggle : ButtonBase
{
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly ToggleMark _mark = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ToggleBox _box;
    private bool _isOn;

    public Toggle()
    {
        _box = new ToggleBox(_mark) { VerticalAlignment = VerticalAlignment.Center };
        _row.Add(_box);
        AddChild(_row);
    }

    public Toggle(string text) : this() => Text = text;

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value)
            {
                return;
            }
            _isOn = value;
            Classes.Set("on", value);
            _box.Classes.Set("on", value);
            _mark.Classes.Set("on", value);
            Changed?.Invoke(this);
        }
    }

    /// <summary>After <see cref="IsOn"/> changes, from a click or code.</summary>
    public event Action<Toggle>? Changed;

    public ToggleBox Box => _box;

    public ToggleMark Mark => _mark;

    protected override void OnClicked() => IsOn = !IsOn;

    private protected override void AddContent(UIElement content)
    {
        content.VerticalAlignment = VerticalAlignment.Center;
        _row.Add(content);
    }

    private protected override void RemoveContent(UIElement content) => _row.Remove(content);
}

/// <summary>A slider's track: the line the thumb moves along (ADR 0063).</summary>
public sealed class SliderTrack : UIElement
{
    internal SliderTrack()
    {
    }
}

/// <summary>A slider's thumb, at its value along the track.</summary>
public sealed class SliderThumb : UIElement
{
    internal SliderThumb()
    {
    }
}

/// <summary>
/// A value between <see cref="Minimum"/> and <see cref="Maximum"/>
/// (ADR 0063), snapped to <see cref="Step"/> when it's above 0. Pressing
/// or dragging along it sets the value; focused, left and right (keys,
/// D-pad, stick) move it by a step (a tenth of the range without one),
/// and up and down navigate away.
/// </summary>
public class Slider : UIElement
{
    private readonly SliderTrack _track = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly SliderThumb _thumb = new();
    private float _minimum, _maximum = 1, _value, _step;
    private bool _dragging;

    public Slider()
    {
        IsFocusable = true;
        AddChild(_track);
        AddChild(_thumb);
    }

    public float Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Value = _value;
            InvalidateArrange();
        }
    }

    public float Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            Value = _value;
            InvalidateArrange();
        }
    }

    /// <summary>Clamped to the range and snapped to <see cref="Step"/>.</summary>
    public float Value
    {
        get => _value;
        set
        {
            float low = MathF.Min(_minimum, _maximum), high = MathF.Max(_minimum, _maximum);
            float clamped = Math.Clamp(float.IsNaN(value) ? low : value, low, high);
            if (_step > 0)
            {
                clamped = Math.Clamp(low + MathF.Round((clamped - low) / _step) * _step, low, high);
            }
            if (clamped == _value)
            {
                return;
            }
            _value = clamped;
            InvalidateArrange();
            ValueChanged?.Invoke(this);
        }
    }

    public float Step
    {
        get => _step;
        set
        {
            _step = MathF.Max(value, 0);
            Value = _value;
        }
    }

    public event Action<Slider>? ValueChanged;

    public SliderTrack Track => _track;

    public SliderThumb Thumb => _thumb;

    // Where the value is, 0 to 1.
    private float Fraction => _maximum == _minimum ? 0 : (_value - _minimum) / (_maximum - _minimum);

    protected override Vector2 MeasureCore(Vector2 available)
    {
        _track.Measure(available);
        _thumb.Measure(available);
        return new Vector2(MathF.Max(_track.DesiredSize.X, _thumb.DesiredSize.X),
            MathF.Max(_track.DesiredSize.Y, _thumb.DesiredSize.Y));
    }

    protected override void ArrangeCore(RectangleF content)
    {
        _track.Arrange(content);
        Vector2 thumb = _thumb.DesiredSize;
        float x = content.X + Fraction * MathF.Max(content.Width - thumb.X, 0);
        _thumb.Arrange(new RectangleF(x, content.Y + (content.Height - thumb.Y) * 0.5f, thumb.X, thumb.Y));
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button != PointerButton.Left)
        {
            return;
        }
        _dragging = true;
        SetFromPointer(e.Position.X);
        e.Handled = true;
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_dragging)
        {
            SetFromPointer(e.Position.X);
            e.Handled = true;
        }
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (_dragging)
        {
            _dragging = false;
            e.Handled = true;
        }
    }

    protected override void OnNavigate(NavigationEvent e)
    {
        float step = _step > 0 ? _step : (_maximum - _minimum) / 10;
        if (e.Direction == NavigationDirection.Left)
        {
            Value -= step;
            e.Handled = true;
        }
        else if (e.Direction == NavigationDirection.Right)
        {
            Value += step;
            e.Handled = true;
        }
    }

    // The thumb's center follows the pointer.
    private void SetFromPointer(float x)
    {
        float thumb = _thumb.DesiredSize.X;
        float travel = MathF.Max(Bounds.Width - Padding.Horizontal - thumb, 1e-3f);
        float fraction = Math.Clamp((x - Bounds.X - Padding.Left - thumb * 0.5f) / travel, 0, 1);
        Value = _minimum + fraction * (_maximum - _minimum);
    }
}
