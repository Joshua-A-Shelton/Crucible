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
/// R pick the handles, P edits a chain's points instead (ADR 0095). A click
/// picks what's under it.
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
        Chain,
        Tiles,
        Pick,
    }

    internal SceneView(Shell shell)
    {
        _shell = shell;
        IsFocusable = true;
        ClipToBounds = true;
        AllowDrop = true;
    }

    /// <summary>A view of the open scene for a workspace (ADR 0119), rendered while its tab is shown.</summary>
    public SceneView() : this(Shell.Current ?? throw new InvalidOperationException("A SceneView needs the editor running"))
    {
    }

    /// <summary>Map mode's view (ADR 0112, 0119): the map's tools and grid instead of handles and the floor's.</summary>
    internal bool IsMapView { get; init; }

    // Map mode's view with its map open.
    private bool OnMap => IsMapView && _shell.Map.IsOpen;

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
        // Files from other programs (ADR 0115): placed where dropped once
        // imported; picking under the pointer meanwhile, for where that is.
        if (e.Payload is FileDrag files)
        {
            e.Accepted = true;
            files.View = this;
            files.ViewPoint = e.Position - new Vector2(Bounds.X, Bounds.Y);
            Vector2 pixel = DragPixel(e);
            Crucible.Render.Api.RequestPick(Surface, (uint)MathF.Max(pixel.X, 0), (uint)MathF.Max(pixel.Y, 0));
            return;
        }
        // Started here too, not only on entering: a drag handed over from
        // another window can arrive already over the view (ADR 0071).
        if (_shell.Placing.IsNull && e.Payload is AssetDrag drag)
        {
            _shell.BeginPlacement(this, drag, DragPixel(e)); // says why when it can't
        }
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
        // Map mode (ADR 0112) draws the map's own grid instead of the floor's.
        Crucible.Render.Api.SetSurfaceGrid(_surface, ShowsGrid && !IsMapView);
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
        _shell.Chains.Update();
        if (_shell.Chains.IsEditing)
        {
            _shell.Chains.Draw(canvas, Camera, ActualSize);
        }
        else if (OnMap)
        {
            _shell.Map.Draw(canvas, Camera, ActualSize);
        }
        else if (_shell.SelectedTransform() is { } world)
        {
            _shell.DrawLight2D(canvas, Camera, ActualSize);
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
        if (_mode == Mode.Chain)
        {
            _shell.Chains.EndDrag();
            _mode = Mode.None;
        }
        if (_mode == Mode.Tiles)
        {
            _shell.Map.EndStroke();
            _mode = Mode.None;
        }
        if (_mode == Mode.Pick)
        {
            _shell.Map.EndPick();
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
        // Map mode (ADR 0112): the left button uses the tool, the right picks
        // up painted tiles, the middle pans, the wheel zooms.
        if (OnMap && e.Button is PointerButton.Left or PointerButton.Right &&
            (e.Modifiers & KeyModifiers.Alt) == 0)
        {
            if (_shell.Map.CellAt(Camera, ActualSize, at) is { } cell)
            {
                if (e.Button == PointerButton.Right)
                {
                    _shell.Map.BeginPick(cell);
                    _mode = Mode.Pick;
                }
                else if (_shell.Map.BeginStroke(cell))
                {
                    _mode = Mode.Tiles;
                }
                InvalidateVisual();
            }
            return;
        }
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
            case PointerButton.Left when _shell.Chains.IsEditing && _shell.Chains.BeginDrag(Camera, ActualSize, at):
                _mode = Mode.Chain;
                InvalidateVisual();
                break;
            case PointerButton.Left:
                if (!_shell.Chains.IsEditing && _shell.SelectedTransform() is { } world &&
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
            case Mode.Chain:
                _shell.Chains.Drag(Camera, ActualSize, Local(e), (e.Modifiers & KeyModifiers.Ctrl) != 0);
                break;
            case Mode.Tiles:
                if (_shell.Map.CellAt(Camera, ActualSize, Local(e)) is { } cell)
                {
                    _shell.Map.Stroke(cell);
                }
                break;
            case Mode.Pick:
                _shell.Map.Track(_shell.Map.CellAt(Camera, ActualSize, Local(e)) ?? _shell.Map.Hover);
                break;
            default:
                if (OnMap)
                {
                    if (_shell.Map.Track(_shell.Map.CellAt(Camera, ActualSize, Local(e))))
                    {
                        InvalidateVisual();
                    }
                    return;
                }
                if (_shell.Chains.IsEditing)
                {
                    if (_shell.Chains.Hover(Camera, ActualSize, Local(e)))
                    {
                        InvalidateVisual();
                    }
                    return;
                }
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
        else if (_mode == Mode.Chain)
        {
            _shell.Chains.EndDrag();
            InvalidateVisual();
        }
        else if (_mode == Mode.Tiles)
        {
            _shell.Map.EndStroke();
            InvalidateVisual();
        }
        else if (_mode == Mode.Pick)
        {
            _shell.Map.EndPick();
            InvalidateVisual();
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
        if (OnMap)
        {
            if (MapKey(e))
            {
                e.Handled = true;
                InvalidateVisual();
            }
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
            case Keycode.P:
                _shell.Chains.Toggle();
                break;
            case Keycode.Delete when _shell.Chains.DeleteSelected():
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

    // Map mode's keys here (ADR 0112): Ctrl+C, Ctrl+X, and Delete on the
    // Select tool's region, and Escape lets go of it. The tools' and flips'
    // keys are the map workspace's commands (ADR 0119), anywhere in its tab.
    private bool MapKey(KeyEvent e)
    {
        MapEditor map = _shell.Map;
        bool ctrl = (e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Gui)) != 0;
        if ((e.Modifiers & KeyModifiers.Alt) != 0)
        {
            return false;
        }
        if (ctrl)
        {
            bool done = e.Keycode switch
            {
                Keycode.C => map.Copy(),
                Keycode.X => map.Cut(),
                _ => false,
            };
            if (done)
            {
                _shell.RaiseMapToolChanged();
            }
            return done;
        }
        if (e.Keycode == Keycode.Delete)
        {
            return map.DeleteRegion();
        }
        if (e.Scancode != ScancodeEscape || map.Region is null)
        {
            return false;
        }
        map.Region = null;
        return true;
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
