using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The play toolbar (ADR 0088), a strip under the main window's menu bar:
/// Play/Stop, Pause (on while paused, before Play too), and Step (one fixed
/// tick, while paused), centered, as Lucide icons (ADR 0124). Each button's
/// shortcut shows in its tooltip, and in the status bar while the pointer is on it.
/// </summary>
public sealed class PlayToolbar : UIElement
{
    private readonly Shell _shell;

    internal PlayToolbar(Shell shell)
    {
        _shell = shell;
        Classes.Add("play-toolbar");
        PlayButton = Make("play", () => _shell.IsPlaying ? "Stop (Ctrl+P)" : "Play (Ctrl+P)", "Play.Toggle");
        PauseButton = Make("pause", () => "Pause (Ctrl+Shift+P)", "Play.Pause");
        StepButton = Make("step-forward", () => "Step one tick (Ctrl+Alt+P)", "Play.Step");
        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        buttons.Add(PlayButton);
        buttons.Add(PauseButton);
        buttons.Add(StepButton);
        AddChild(buttons);
    }

    public PlayToolButton PlayButton { get; }

    public PlayToolButton PauseButton { get; }

    public PlayToolButton StepButton { get; }

    private PlayToolButton Make(string icon, Func<string> tooltip, string command)
    {
        PlayToolButton button = new(icon) { ToolTip = tooltip() };
        button.Classes.Add("tool");
        button.Clicked += _ => _shell.Commands.Find(command)?.Execute();
        button.PointerEnter += (_, _) => _shell.Status(tooltip());
        return button;
    }

    /// <summary>Each frame: the buttons show the play state.</summary>
    internal void Update()
    {
        PlayButton.IconName = _shell.IsPlaying ? "square" : "play";
        PlayButton.ToolTip = _shell.IsPlaying ? "Stop (Ctrl+P)" : "Play (Ctrl+P)";
        PlayButton.Classes.Set("on", _shell.IsPlaying);
        PauseButton.Classes.Set("on", _shell.IsPaused);
        UIState disabled = _shell.IsPlaying && _shell.IsPaused ? 0 : UIState.Disabled;
        StepButton.State = (StepButton.State & ~UIState.Disabled) | disabled;
    }
}

/// <summary>A play toolbar button (ADR 0088), showing a Lucide icon (ADR 0124): play, stop, pause, or step.</summary>
public sealed class PlayToolButton : ButtonBase
{
    private readonly Icon _icon;

    internal PlayToolButton(string icon)
    {
        _icon = EditorIcons.Icon(icon);
        Content = _icon;
        IconName = icon;
        Width = 32;
        Height = 22;
        VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Its icon's Lucide name: "play", "square" (Stop), "pause", "step-forward".</summary>
    public string IconName
    {
        get;
        set
        {
            field = value ?? "";
            _icon.Glyph = EditorIcons.Glyph(field);
        }
    } = "";
}
