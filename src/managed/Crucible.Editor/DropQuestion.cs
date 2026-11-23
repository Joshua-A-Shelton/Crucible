using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// A question about dropped files (ADR 0115): "Grass.png is already there"
/// or "Import Grass.png as", its choices as buttons (the guess first and
/// focused), and "Do this for the rest" when more files would ask the same.
/// Escape cancels: the rest of the drop is skipped.
/// </summary>
public sealed class DropQuestion : Popup
{
    private readonly Toggle _rest = new("Do this for the rest");
    private bool _answered;

    internal DropQuestion(string message, IReadOnlyList<string> choices, int suggested, bool offerRest)
    {
        IsModal = true;
        Message = message;
        Choices = choices;
        Label text = new(message) { Wrap = true };
        text.Classes.Add("dialog-message");
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.End };
        Button? first = null;
        for (int i = 0; i < choices.Count; ++i)
        {
            int index = i;
            Button button = new(choices[i]);
            if (i == suggested)
            {
                button.Classes.Add("primary");
                first = button;
            }
            button.Clicked += _ => Answer(index);
            row.Add(button);
        }
        StackPanel body = new() { Spacing = 16 };
        body.Classes.Add("dialog");
        body.Add(text);
        if (offerRest)
        {
            body.Add(_rest);
        }
        body.Add(row);
        Content = body;
        Suggested = suggested;
        Closed += _ =>
        {
            if (!_answered)
            {
                _answered = true;
                Chosen?.Invoke(this, -1, false);
            }
        };
        _first = first;
    }

    private readonly Button? _first;

    public string Message { get; }

    public IReadOnlyList<string> Choices { get; }

    /// <summary>The guess, by index: drawn as the primary button.</summary>
    public int Suggested { get; }

    /// <summary>"Do this for the rest", shown when more files would ask.</summary>
    public Toggle ForTheRest => _rest;

    /// <summary>The choice by index (-1 when canceled), and whether it's for the rest too.</summary>
    public event Action<DropQuestion, int, bool>? Chosen;

    /// <summary>Opens it with the guess focused.</summary>
    internal void Show(UIRoot root)
    {
        Open(root);
        _first?.Focus();
    }

    /// <summary>Chooses as a click would: the checks answer it this way.</summary>
    public void Answer(int index, bool forTheRest = false)
    {
        if (_answered)
        {
            return;
        }
        if (forTheRest)
        {
            _rest.IsOn = true;
        }
        _answered = true;
        Close();
        Chosen?.Invoke(this, index, _rest.IsOn);
    }

    /// <summary>Chooses by the button's text.</summary>
    public void Answer(string choice, bool forTheRest = false) => Answer(IndexOf(choice), forTheRest);

    private int IndexOf(string choice)
    {
        for (int i = 0; i < Choices.Count; ++i)
        {
            if (Choices[i] == choice)
            {
                return i;
            }
        }
        throw new ArgumentException($"{choice} isn't one of {string.Join(", ", Choices)}", nameof(choice));
    }
}
