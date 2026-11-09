using System.Diagnostics;
using System.Numerics;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Render;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// A viewport's image (ADR 0067): the world rendered into an offscreen
/// surface the element's size in pixels, from its own <see cref="Camera"/>,
/// drawn as a texture, with the selection's <see cref="Handles"/> over it.
/// Unity's controls: the right button held looks around and flies (WASD, Q
/// and E; the wheel sets the speed), Alt and the left button orbit, the
/// middle button pans, the wheel dollies, F frames the selection, W, E, and
/// R pick the handles. A click picks what's under it.
/// </summary>
public sealed class SceneView : UIElement
{
    // SDL scancodes: flying keys stay where they are on any layout.
    private const int ScancodeA = 4, ScancodeD = 7, ScancodeE = 8, ScancodeQ = 20, ScancodeS = 22, ScancodeW = 26;
    private const int ScancodeEscape = 41;

    private readonly Shell _shell;
    private SurfaceHandle _surface;
    private TextureHandle _texture;
    private bool _drawn;
    private uint _lastRun;
    private Mode _mode;
    private Vector2 _last;
    private HandlePart _hot;
    private Vector3 _flying; // x right, y up, z forward, from the keys held
    private double _lastTick = double.NaN;

    private enum Mode
    {
        None,
        Look,
        Orbit,
        Pan,
        Handle,
    }

    internal SceneView(Shell shell)
    {
        _shell = shell;
        IsFocusable = true;
        ClipToBounds = true;
        AllowDrop = true;
    }

    // An asset dragged in (ADR 0071): placed under the pointer as it moves.
    private Vector2 DragPixel(DragEvent e) =>
        (e.Position - new Vector2(Bounds.X, Bounds.Y)) * (Root?.Scale ?? 1);

    protected override void OnDragEnter(DragEvent e)
    {
        if (e.Payload is AssetDrag drag && _shell.CanPlace(drag))
        {
            _shell.BeginPlacement(this, drag, DragPixel(e));
        }
    }

    protected override void OnDragOver(DragEvent e)
    {
        if (!_shell.Placing.IsNull)
        {
            e.Accepted = true;
            _shell.MovePlacement(DragPixel(e));
        }
    }

    protected override void OnDragLeave(DragEvent e) => _shell.CancelPlacement();

    protected override void OnDrop(DragEvent e) => _shell.FinishPlacement();

    public EditorCamera Camera { get; } = new();

    public TransformHandles Handles { get; } = new();

    public bool ShowsGrid { get; set; } = true;

    /// <summary>Its offscreen surface; none until first rendered.</summary>
    public SurfaceHandle Surface => _surface;

    /// <summary>How long its last frame took to record and submit, on the CPU.</summary>
    public double LastFrameMilliseconds { get; private set; }

    // Each frame, before the window's: its surface sized to it in pixels, the
    // world rendered from its camera, the selection outlined.
    internal void Render(World world, ReadOnlySpan<Entity> outlined)
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
                _shell.Log("The viewport's image can't be drawn");
                return;
            }
        }
        else if (Crucible.Render.Api.ResizeOffscreenSurface(_surface, width, height) is var resized && resized < 0)
        {
            _shell.Log($"The viewport can't be resized ({resized})");
            return;
        }
        Crucible.Render.Api.SetSurfaceCamera(_surface, Camera.Position, Camera.Rotation, Camera.Camera);
        Crucible.Render.Api.SetSurfaceGrid(_surface, ShowsGrid);
        Crucible.Render.Api.SetSurfaceSelection(_surface, outlined);
        Stopwatch watch = Stopwatch.StartNew();
        Status status = Crucible.Render.Api.RenderFrame(_surface, world.Handle, _lastRun, 0.05f, 0.05f, 0.06f, 1);
        LastFrameMilliseconds = watch.Elapsed.TotalMilliseconds;
        _lastRun = world.CurrentVersion;
        if (status < 0)
        {
            _shell.Log($"The viewport failed to render ({status})");
            return;
        }
        _drawn = true;
        InvalidateVisual();
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (!_drawn)
        {
            return;
        }
        canvas.DrawImage(_texture, Vector2.Zero, ActualSize);
        if (_shell.SelectedTransform() is { } world)
        {
            Handles.Draw(canvas, Camera, ActualSize, world, _hot);
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        // Torn off mid-drag: the drag's group ends, so undo isn't stuck open.
        if (_mode == Mode.Handle)
        {
            Handles.EndDrag();
            _shell.History.EndGroup();
            _mode = Mode.None;
        }
        if (_surface.Value != 0)
        {
            Crucible.Render.Api.DestroySurface(_surface);
            _surface = default;
            _drawn = false;
        }
    }

    private Vector2 Local(PointerEvent e) => e.Position - new Vector2(Bounds.X, Bounds.Y);

    protected override void OnPointerDown(PointerEvent e)
    {
        Focus();
        e.Handled = true;
        _last = e.Position;
        Vector2 at = Local(e);
        switch (e.Button)
        {
            case PointerButton.Right:
                _mode = Mode.Look;
                _flying = Vector3.Zero;
                break;
            case PointerButton.Middle:
                _mode = Mode.Pan;
                break;
            case PointerButton.Left when (e.Modifiers & KeyModifiers.Alt) != 0:
                _mode = Mode.Orbit;
                break;
            case PointerButton.Left:
                if (_shell.SelectedTransform() is { } world &&
                    Handles.HitTest(Camera, ActualSize, world, at) is var part && part != HandlePart.None)
                {
                    Handles.BeginDrag(part, Camera, ActualSize, world, at);
                    _mode = Mode.Handle;
                    // The drag's moves undo as one (ADR 0068).
                    _shell.History.BeginGroup(Handles.Mode.ToString());
                }
                else if (_surface.Value != 0 && Root is { } root)
                {
                    _shell.RequestPick(this, at * root.Scale);
                }
                break;
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        Vector2 moved = e.Position - _last;
        _last = e.Position;
        switch (_mode)
        {
            case Mode.Look:
                Camera.Look(moved);
                break;
            case Mode.Orbit:
                Camera.Orbit(moved);
                break;
            case Mode.Pan:
                Camera.Pan(moved, ActualSize.Y);
                break;
            case Mode.Handle:
                _shell.SetSelectedTransform(Handles.Drag(Camera, ActualSize, Local(e), (e.Modifiers & KeyModifiers.Ctrl) != 0));
                break;
            default:
                HandlePart hot = _shell.SelectedTransform() is { } world
                    ? Handles.HitTest(Camera, ActualSize, world, Local(e))
                    : HandlePart.None;
                if (hot != _hot)
                {
                    _hot = hot;
                    InvalidateVisual();
                }
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (_mode == Mode.Handle)
        {
            Handles.EndDrag();
            _shell.History.EndGroup();
        }
        _mode = Mode.None;
        _flying = Vector3.Zero;
    }

    protected override void OnPointerWheel(PointerEvent e)
    {
        e.Handled = true;
        if (_mode == Mode.Look)
        {
            Camera.FlySpeed = Math.Clamp(Camera.FlySpeed * MathF.Pow(1.25f, e.Wheel.Y), 0.5f, 500);
        }
        else
        {
            Camera.Dolly(e.Wheel.Y);
        }
    }

    protected override void OnKeyDown(KeyEvent e)
    {
        if (_mode == Mode.Look && Fly(e.Scancode, true))
        {
            e.Handled = true;
            return;
        }
        if ((e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Gui)) != 0)
        {
            return;
        }
        switch (e.Keycode)
        {
            case Keycode.W:
                Handles.Mode = HandleMode.Move;
                break;
            case Keycode.E:
                Handles.Mode = HandleMode.Rotate;
                break;
            case Keycode.R:
                Handles.Mode = HandleMode.Scale;
                break;
            case Keycode.F:
                _shell.FrameSelection(this);
                break;
            default:
                if (e.Scancode == ScancodeEscape && _shell.Selection.Selected.Value != 0)
                {
                    _shell.Selection.Selected = Entity.Null;
                    break;
                }
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnKeyUp(KeyEvent e)
    {
        if (Fly(e.Scancode, false))
        {
            e.Handled = true;
        }
    }

    // WASD, Q, E: the flying direction while the right button is held.
    private bool Fly(int scancode, bool down)
    {
        float sign = down ? 1 : 0;
        switch (scancode)
        {
            case ScancodeW:
                _flying.Z = sign;
                break;
            case ScancodeS:
                _flying.Z = -sign;
                break;
            case ScancodeD:
                _flying.X = sign;
                break;
            case ScancodeA:
                _flying.X = -sign;
                break;
            case ScancodeE:
                _flying.Y = sign;
                break;
            case ScancodeQ:
                _flying.Y = -sign;
                break;
            default:
                return false;
        }
        return true;
    }

    protected internal override void OnFocusedTick(double now)
    {
        if (_mode == Mode.Look && _flying != Vector3.Zero && !double.IsNaN(_lastTick))
        {
            Camera.Fly(_flying, (float)Math.Min(now - _lastTick, 0.1));
            InvalidateVisual();
        }
        _lastTick = now;
    }
}
