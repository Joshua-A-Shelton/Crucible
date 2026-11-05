namespace Crucible.UI;

/// <summary>
/// A question with buttons (ADR 0068): a modal popup centered on the root,
/// its message above a row of buttons, the first focused. Choosing one
/// closes it and raises <see cref="Chosen"/> with its index; Escape (or the
/// gamepad's cancel) closes it with -1.
/// </summary>
public sealed class Dialog : Popup
{
    private readonly Label _message = new() { Wrap = true };
    private bool _answered;

    public Dialog(string message, params string[] buttons)
    {
        IsModal = true;
        _message.Text = message;
        _message.Classes.Add("dialog-message");
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.End };
        for (int i = 0; i < buttons.Length; ++i)
        {
            int index = i;
            Button button = new(buttons[i]);
            button.Clicked += _ => Answer(index);
            row.Add(button);
        }
        StackPanel body = new() { Spacing = 16 };
        body.Classes.Add("dialog");
        body.Add(_message);
        body.Add(row);
        Content = body;
        Closed += _ =>
        {
            if (!_answered)
            {
                _answered = true;
                Chosen?.Invoke(this, -1);
            }
        };
    }

    public string Message
    {
        get => _message.Text;
        set => _message.Text = value;
    }

    /// <summary>The button chosen, by index; -1 when it was canceled.</summary>
    public event Action<Dialog, int>? Chosen;

    private void Answer(int index)
    {
        if (_answered)
        {
            return;
        }
        _answered = true;
        Close();
        Chosen?.Invoke(this, index);
    }
}
