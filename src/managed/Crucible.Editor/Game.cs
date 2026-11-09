using System.Numerics;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The game as it will look (ADR 0072): the world drawn through its own
/// camera, with its post-processing, beside the Viewport's editor camera.
/// While playing, a click on it gives the game the keyboard, mouse, and
/// gamepad; Escape hands them back to the editor.
/// </summary>
public sealed class GamePanel : EditorPanel
{
    internal GamePanel(Shell shell) : base("Game", "Game")
    {
        View = new GameView(shell);
        AddChild(View);
    }

    public GameView View { get; }
}

/// <summary>The Game panel's image (ADR 0072): an offscreen surface the panel's size, drawn from the world's camera.</summary>
public sealed class GameView : UIElement
{
    private const int ScancodeEscape = 41;

    private readonly Shell _shell;
    private SurfaceHandle _surface;
    private TextureHandle _texture;
    private bool _drawn;
    private uint _lastRun;

    internal GameView(Shell shell)
    {
        _shell = shell;
        IsFocusable = true;
        ClipToBounds = true;
    }

    public SurfaceHandle Surface => _surface;

    /// <summary>Whether the game gets the input now.</summary>
    public bool HasInput => _shell.GameInput == this;

    /// <summary>Gives the game the input (while playing), or takes it back.</summary>
    public void TakeInput(bool take)
    {
        if (take && _shell.IsPlaying)
        {
            Focus();
            _shell.GameInput = this;
        }
        else if (_shell.GameInput == this)
        {
            _shell.GameInput = null;
        }
        InvalidateVisual();
    }

    internal void Render(World world)
    {
        if (Root is not { } root || ActualSize.X < 1 || ActualSize.Y < 1)
        {
            return;
        }
        uint width = (uint)MathF.Max(MathF.Round(ActualSize.X * root.Scale), 1);
        uint height = (uint)MathF.Max(MathF.Round(ActualSize.Y * root.Scale), 1);
        if (_surface.Value == 0)
        {
            _surface = Crucible.Render.Api.CreateOffscreenSurface(width, height);
            if (Crucible.Render.Api.SurfaceTexture(_surface, out _texture) < 0)
            {
                _shell.Log("The game's image can't be drawn");
                return;
            }
        }
        else if (Crucible.Render.Api.ResizeOffscreenSurface(_surface, width, height) is var resized && resized < 0)
        {
            return;
        }
        // No camera of its own: the world's, as the game draws.
        Status status = Crucible.Render.Api.RenderFrame(_surface, world.Handle, _lastRun, 0.05f, 0.05f, 0.06f, 1);
        _lastRun = world.CurrentVersion;
        if (status < 0)
        {
            _shell.Log($"The game view failed to render ({status})");
            return;
        }
        _drawn = true;
        InvalidateVisual();
    }

    protected override void OnPointerDown(PointerEvent e)
    {
        if (_shell.IsPlaying && !HasInput)
        {
            e.Handled = true;
            TakeInput(true);
        }
    }

    // While the game has the input, keys are its own, not the editor's shortcuts.
    protected override void OnKeyDown(KeyEvent e)
    {
        if (!HasInput)
        {
            return;
        }
        e.Handled = true;
        if (e.Scancode == ScancodeEscape)
        {
            TakeInput(false);
        }
    }

    protected override void OnLostFocus(UIEvent e) => TakeInput(false);

    protected override void OnDraw(Canvas canvas)
    {
        if (_drawn)
        {
            canvas.DrawImage(_texture, Vector2.Zero, ActualSize);
        }
        if (HasInput)
        {
            canvas.StrokeRect(Vector2.Zero, ActualSize, new Vector4(0.3f, 0.55f, 0.95f, 1), 2);
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        TakeInput(false);
        if (_surface.Value != 0)
        {
            Crucible.Render.Api.DestroySurface(_surface);
            _surface = default;
            _drawn = false;
        }
    }
}
