using System.Globalization;
using Crucible.Platform;

namespace Crucible.UI;

/// <summary>
/// A number typed into a <see cref="TextField"/> (ADR 0065), kept between
/// <see cref="Minimum"/> and <see cref="Maximum"/>. Typed text becomes the
/// value when committed (Enter or focus leaving); text that isn't a number
/// puts the value back. Up and down, and the wheel while focused, step it by
/// <see cref="Step"/> (ten steps with Shift). A press that gives it focus
/// can drag sideways to scrub the value; released without moving, it's
/// ready to type.
/// </summary>
public class NumberField : TextField
{
    private const int ScancodeDown = 81, ScancodeUp = 82;
    private const float ScrubThreshold = 3; // logical units moved before a press scrubs
    private const float ScrubUnitsPerStep = 4;

    private double _value;
    private double _minimum = double.NegativeInfinity;
    private double _maximum = double.PositiveInfinity;
    private double _step = 1;
    private bool _settingText;
    private bool _focusedByPress; // focus came in this input update, so a press now is the one that focused it
    private bool _scrubCandidate;
    private bool _scrubbing;
    private float _scrubStartX;
    private double _scrubStartValue;

    public NumberField()
    {
        ShowValue();
    }

    public double Value
    {
        get => _value;
        set
        {
            if (double.IsNaN(value))
            {
                return;
            }
            double clamped = Math.Max(_minimum, Math.Min(_maximum, value));
            if (clamped != _value)
            {
                _value = clamped;
                ShowValue();
                ValueChanged?.Invoke(this);
            }
            else
            {
                ShowValue();
            }
        }
    }

    public double Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            Value = _value;
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            Value = _value;
        }
    }

    /// <summary>What up, down, the wheel, and scrubbing move it by; its decimals are the ones shown.</summary>
    public double Step
    {
        get => _step;
        set
        {
            _step = value > 0 ? value : 1;
            ShowValue();
        }
    }

    public event Action<NumberField>? ValueChanged;

    /// <summary>A value as the field shows it: with at least as many decimals as <see cref="Step"/> has, up to six.</summary>
    public string Format(double value)
    {
        int decimals = 0;
        for (double step = _step; decimals < 6 && Math.Abs(step - Math.Round(step)) > 1e-9; step *= 10)
        {
            ++decimals;
        }
        string format = decimals == 0 ? "0.######" : "0." + new string('0', decimals) + new string('#', 6 - decimals);
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    protected override void Commit()
    {
        if (_settingText || !IsEdited)
        {
            base.Commit();
            return;
        }
        double before = _value;
        if (double.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double typed) &&
            double.IsFinite(typed))
        {
            Value = typed;
        }
        ShowValue(); // tidied, or put back if it wasn't a number
        if (_value != before)
        {
            MarkCommitted();
        }
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        if (e.Scancode is ScancodeUp or ScancodeDown)
        {
            e.Handled = true;
            Commit();
            Nudge(e.Scancode == ScancodeUp ? 1 : -1, e.Modifiers);
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPointerWheel(PointerEvent e)
    {
        // Only while focused: a wheel scrolling a panel past it leaves it alone.
        if (IsFocused && e.Wheel.Y != 0)
        {
            e.Handled = true;
            Commit();
            Nudge(MathF.Sign(e.Wheel.Y), e.Modifiers);
        }
    }

    protected override void OnGotFocus(UIEvent e)
    {
        _focusedByPress = true;
        base.OnGotFocus(e);
    }

    protected internal override void OnFocusedTick(double now)
    {
        _focusedByPress = false;
        base.OnFocusedTick(now);
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (e.Button == PointerButton.Left && _focusedByPress && e.ClickCount == 1)
        {
            // Keep everything selected (from focusing): a release without a
            // drag leaves it ready to type over.
            e.Handled = true;
            _focusedByPress = false;
            _scrubCandidate = true;
            _scrubStartX = e.Position.X;
            _scrubStartValue = _value;
            return;
        }
        base.OnPointerDown(e);
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        if (_scrubCandidate)
        {
            e.Handled = true;
            float moved = e.Position.X - _scrubStartX;
            _scrubbing |= MathF.Abs(moved) >= ScrubThreshold;
            if (_scrubbing)
            {
                double steps = MathF.Round(moved / ScrubUnitsPerStep) * ((e.Modifiers & KeyModifiers.Shift) != 0 ? 10 : 1);
                Value = _scrubStartValue + steps * _step;
                SelectAll();
            }
            return;
        }
        base.OnPointerMove(e);
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (_scrubCandidate && e.Button == PointerButton.Left)
        {
            _scrubCandidate = false;
            if (_scrubbing)
            {
                _scrubbing = false;
                if (_value != _scrubStartValue)
                {
                    MarkCommitted();
                }
            }
            return;
        }
        base.OnPointerUp(e);
    }

    protected override void OnLostFocus(UIEvent e)
    {
        _scrubCandidate = _scrubbing = false;
        base.OnLostFocus(e);
    }

    // Each step is an edit of its own, committed at once.
    private void Nudge(int direction, KeyModifiers modifiers)
    {
        double before = _value;
        Value = _value + direction * _step * ((modifiers & KeyModifiers.Shift) != 0 ? 10 : 1);
        SelectAll();
        if (_value != before)
        {
            MarkCommitted();
        }
    }

    // The text shows the value, without counting as typing.
    private void ShowValue()
    {
        string text = Format(_value);
        if (text != Text)
        {
            _settingText = true;
            bool focused = IsFocused;
            Text = text;
            if (focused)
            {
                SelectAll();
            }
            _settingText = false;
        }
    }
}
