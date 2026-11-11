using System.Numerics;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The play toolbar (ADR 0088), a strip under the main window's menu bar:
/// Play/Stop, Pause (on while paused, before Play too), and Step (one fixed
/// tick, while paused), centered. Each button's shortcut shows in the status
/// bar while the pointer is on it.
/// </summary>
public sealed class PlayToolbar : UIElement
{
    private readonly Shell _shell;

    internal PlayToolbar(Shell shell)
    {
        _shell = shell;
        Classes.Add("play-toolbar");
        PlayButton = Make(PlayGlyph.Play, () => _shell.IsPlaying ? "Stop (Ctrl+P)" : "Play (Ctrl+P)", "Play.Toggle");
        PauseButton = Make(PlayGlyph.Pause, () => "Pause (Ctrl+Shift+P)", "Play.Pause");
        StepButton = Make(PlayGlyph.Step, () => "Step one tick (Ctrl+Alt+P)", "Play.Step");
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

    private PlayToolButton Make(PlayGlyph glyph, Func<string> tooltip, string command)
    {
        PlayToolButton button = new(glyph);
        button.Classes.Add("tool");
        button.Clicked += _ => _shell.Commands.Find(command)?.Execute();
        button.PointerEnter += (_, _) => _shell.Status(tooltip());
        return button;
    }

    /// <summary>Each frame: the buttons show the play state.</summary>
    internal void Update()
    {
        PlayButton.Glyph = _shell.IsPlaying ? PlayGlyph.Stop : PlayGlyph.Play;
        PlayButton.Classes.Set("on", _shell.IsPlaying);
        PauseButton.Classes.Set("on", _shell.IsPaused);
        UIState disabled = _shell.IsPlaying && _shell.IsPaused ? 0 : UIState.Disabled;
        StepButton.State = (StepButton.State & ~UIState.Disabled) | disabled;
    }
}

public enum PlayGlyph
{
    Play,
    Stop,
    Pause,
    Step,
}

/// <summary>A toolbar button drawn as a play, stop, pause, or step symbol in its <c>Foreground</c>.</summary>
public sealed class PlayToolButton : ButtonBase
{
    private PlayGlyph _glyph;

    internal PlayToolButton(PlayGlyph glyph)
    {
        _glyph = glyph;
        Width = 32;
        Height = 22;
        VerticalAlignment = VerticalAlignment.Center;
    }

    public PlayGlyph Glyph
    {
        get => _glyph;
        set
        {
            if (_glyph != value)
            {
                _glyph = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        Vector2 c = ActualSize * 0.5f;
        Vector4 color = Foreground;
        switch (_glyph)
        {
            case PlayGlyph.Play:
                Triangle(canvas, c + new Vector2(-4, -5), 10, color);
                break;
            case PlayGlyph.Stop:
                canvas.FillRect(c - new Vector2(4.5f), new Vector2(9), color, 1);
                break;
            case PlayGlyph.Pause:
                canvas.FillRect(c + new Vector2(-4.5f, -5), new Vector2(3, 10), color);
                canvas.FillRect(c + new Vector2(1.5f, -5), new Vector2(3, 10), color);
                break;
            case PlayGlyph.Step:
                Triangle(canvas, c + new Vector2(-5, -5), 10, color);
                canvas.FillRect(c + new Vector2(3, -5), new Vector2(2.5f, 10), color);
                break;
        }
    }

    // A right-pointing triangle, `height` tall, from its top-left corner:
    // filled column by column (the canvas draws lines and rectangles).
    private static void Triangle(Canvas canvas, Vector2 at, float height, Vector4 color)
    {
        float width = height * 0.85f;
        for (float x = 0; x <= width; x += 0.5f)
        {
            float half = height * 0.5f * (1 - x / width);
            canvas.DrawLine(at + new Vector2(x, height * 0.5f - half), at + new Vector2(x, height * 0.5f + half), 0.75f, color);
        }
    }
}
