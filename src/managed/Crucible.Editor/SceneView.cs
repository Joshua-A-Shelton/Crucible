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
/// drawn as a texture, with its <see cref="Tool"/>'s overlay over it (ADR
/// 0121). Its tool gets the input first; what it doesn't take is Unity's
/// camera controls: the right button held looks around and flies (WASD, Q
/// and E; the wheel sets the speed), Alt and the left button orbit, the
/// middle button pans, the wheel dollies. F frames the selection; W, E, R,
/// and P choose Move, Rotate, Scale, and Edit Points (ADR 0095), and
/// Escape goes back to Move.
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
    private Vector2 _lastLocal;
    private PointerButton _toolButton;
    private ViewportTool? _tool;
    private ToolView? _toolView;
    private readonly Dictionary<string, ShapedText> _texts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _textsUsed = new(StringComparer.Ordinal);
    private (ulong Frame, Action<PickResult> Then)? _pick;
    private Vector3 _flying; // x right, y up, z forward, from the keys held
    private double _lastTick = double.NaN;

    private enum Mode
    {
        None,
        Look,
        Orbit,
        Pan,
        Tool, // a press the tool took
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
        // Its tool's overlay (ADR 0121); text no longer drawn is let go.
        if (_tool is { } tool)
        {
            _textsUsed.Clear();
            ToolCanvas overlay = new(canvas, ToolView, _texts, _textsUsed);
            _shell.GuardTool(tool, "Draw", () => tool.Draw(ToolView, overlay));
            foreach (string stale in _texts.Keys.Where(text => !_textsUsed.Contains(text)).ToArray())
            {
                _texts[stale].Detach();
                _texts.Remove(stale);
            }
        }
    }

    protected override void OnDetached(UIRoot root)
    {
        // Torn off mid-drag: the tool's press ends, so undo isn't stuck open.
        EndToolDrag(null);
        _mode = Mode.None;
        foreach (ShapedText text in _texts.Values)
        {
            text.Detach();
        }
        _texts.Clear();
        _pick = null;
        if (_surface.Value != 0)
        {
            Crucible.Render.Api.DestroySurface(_surface);
            _surface = default;
            _drawn = false;
        }
    }

    // -----------------------------------------------------------------------
    // Its tool (ADR 0121)
    // -----------------------------------------------------------------------

    /// <summary>What its tool asks of it.</summary>
    internal ToolView ToolView => _toolView ??= new ToolView(this);

    /// <summary>
    /// The tool on in this view (ADR 0121): what clicks, drags, the wheel, and
    /// keys do, and what's drawn over it; null for only the camera.
    /// </summary>
    public ViewportTool? Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }
            EndToolDrag(null);
            ViewportTool? old = _tool;
            _tool = value;
            if (old is not null)
            {
                _shell.GuardTool(old, "Deactivated", () => old.Deactivated(ToolView));
            }
            if (value is not null)
            {
                _shell.GuardTool(value, "Activated", () => value.Activated(ToolView));
            }
            InvalidateVisual();
        }
    }

    // The tool's press let go (released, or the view torn off or retooled).
    private void EndToolDrag(ToolPointer? at)
    {
        if (_mode != Mode.Tool || _tool is not { } tool)
        {
            return;
        }
        _mode = Mode.None;
        ToolPointer release = at ?? new ToolPointer(_lastLocal, _toolButton, KeyModifiers.None);
        _shell.GuardTool(tool, "PointerUp", () => tool.PointerUp(ToolView, release));
    }

    /// <summary>Asks what's drawn at a point; the answer comes once the GPU has drawn it (one request at a time: a newer replaces it).</summary>
    internal void Pick(Vector2 at, Action<PickResult> then)
    {
        if (_surface.Value == 0 || Root is not { } root)
        {
            then(default);
            return;
        }
        Vector2 pixel = at * root.Scale;
        Crucible.Render.Api.RequestPick(_surface, (uint)MathF.Max(pixel.X, 0), (uint)MathF.Max(pixel.Y, 0));
        _pick = (Crucible.Render.Api.FramesSubmitted(_surface) + 1, then);
    }

    // Each frame after rendering: a pick answered.
    internal void PollPick()
    {
        if (_pick is not { } pending || _surface.Value == 0)
        {
            return;
        }
        PickResult result = Crucible.Render.Api.PickResult(_surface);
        if (result.Frame >= pending.Frame)
        {
            _pick = null;
            try
            {
                pending.Then(result);
            }
            catch (Exception exception)
            {
                _shell.LogError($"A pick's answer failed: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private Vector2 Local(PointerEvent e) => e.Position - new Vector2(Bounds.X, Bounds.Y);

    private static ToolPointer Pointer(Vector2 at, PointerEvent e, PointerButton button) => new(at, button, e.Modifiers, e.Wheel);

    // The tool first; what it doesn't take, the camera: the right button
    // looks and flies, the middle pans, Alt and the left orbit.
    protected override void OnPointerDown(PointerEvent e)
    {
        Focus();
        e.Handled = true;
        _last = e.Position;
        Vector2 at = Local(e);
        _lastLocal = at;
        if (_tool is { } tool && _shell.GuardTool(tool, "PointerDown", () => tool.PointerDown(ToolView, Pointer(at, e, e.Button)), false))
        {
            _mode = Mode.Tool;
            _toolButton = e.Button;
            InvalidateVisual();
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
        }
    }

    protected override void OnPointerMove(PointerEvent e)
    {
        Vector2 moved = e.Position - _last;
        _last = e.Position;
        Vector2 at = Local(e);
        _lastLocal = at;
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
            case Mode.Tool when _tool is { } dragging:
                _shell.GuardTool(dragging, "PointerMove", () => dragging.PointerMove(ToolView, Pointer(at, e, _toolButton)));
                break;
            default:
                // Hovering: the tool's to show what's under the pointer.
                if (_tool is { } hovering)
                {
                    _shell.GuardTool(hovering, "PointerMove", () => hovering.PointerMove(ToolView, Pointer(at, e, PointerButton.None)));
                }
                return;
        }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerUp(PointerEvent e)
    {
        if (_mode == Mode.Tool)
        {
            EndToolDrag(Pointer(Local(e), e, _toolButton));
            InvalidateVisual();
        }
        _mode = Mode.None;
        _flying = Vector3.Zero;
    }

    protected override void OnPointerWheel(PointerEvent e)
    {
        e.Handled = true;
        if (_mode != Mode.Look && _tool is { } tool &&
            _shell.GuardTool(tool, "Wheel", () => tool.Wheel(ToolView, Pointer(Local(e), e, PointerButton.None)), false))
        {
            InvalidateVisual();
            return;
        }
        if (_mode == Mode.Look)
        {
            Camera.FlySpeed = Math.Clamp(Camera.FlySpeed * MathF.Pow(1.25f, e.Wheel.Y), 0.5f, 500);
        }
        else
        {
            Camera.Dolly(e.Wheel.Y);
        }
    }

    // Flying first while looking; then the tool; then, in a scene's view,
    // choosing tools by their keys (Escape back to Move), F to frame, and
    // Escape letting go of the selection. The rest goes on to the editor.
    protected override void OnKeyDown(KeyEvent e)
    {
        if (_mode == Mode.Look && Fly(e.Scancode, true))
        {
            e.Handled = true;
            return;
        }
        if (_tool is { } tool && _shell.GuardTool(tool, "KeyDown", () => tool.KeyDown(ToolView, e), false))
        {
            e.Handled = true;
            InvalidateVisual();
            return;
        }
        if (IsMapView || (e.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Gui)) != 0)
        {
            return;
        }
        if (_shell.ToolForKey(e) is { } chosen)
        {
            // A tool's key while it's on: back to Move (P toggles Edit Points).
            Tool = chosen == _tool ? _shell.DefaultTool : chosen;
        }
        else if (e.Keycode == Keycode.F)
        {
            _shell.FrameSelection(this);
        }
        else if (e.Scancode == ScancodeEscape && _tool != _shell.DefaultTool)
        {
            Tool = _shell.DefaultTool;
        }
        else if (e.Scancode == ScancodeEscape && _shell.Selection.Selected.Value != 0)
        {
            _shell.Selection.Selected = Entity.Null;
        }
        else
        {
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
