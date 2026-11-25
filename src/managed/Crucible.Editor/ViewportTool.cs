using System.Numerics;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Text;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// A viewport tool (ADR 0121): what clicks, drags, the wheel, and keys do in
/// a scene view while it's the view's tool, and what it draws over the view.
/// One tool is on in a view at a time, chosen by its button or key; Escape
/// goes back to Move. Input it doesn't take falls back to the view (the
/// camera: right-drag looks, middle-drag pans, Alt and the left button
/// orbit, the wheel dollies) and to the editor's keys.
/// </summary>
public abstract class ViewportTool
{
    protected ViewportTool(string id, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Id = id;
        Title = title ?? id;
    }

    /// <summary>Unique among tools: "Scene.Move", "Paths.Draw".</summary>
    public string Id { get; }

    /// <summary>Its button's text, and its tooltip when it has an icon.</summary>
    public string Title { get; }

    /// <summary>
    /// Its button's icon (ADR 0124), by its Lucide name ("move"); null shows
    /// <see cref="Title"/> as text instead.
    /// </summary>
    public virtual string? Icon => null;

    /// <summary>Turned on in a view.</summary>
    public virtual void Activated(ToolView view)
    {
    }

    /// <summary>Turned off in a view (another tool chosen, Escape, the view gone).</summary>
    public virtual void Deactivated(ToolView view)
    {
    }

    /// <summary>
    /// A button pressed in the view. True takes it: the moves and the release
    /// until it's let go come here, not to the camera.
    /// </summary>
    public virtual bool PointerDown(ToolView view, ToolPointer pointer) => false;

    /// <summary>The pointer moved: during a press it took, or hovering (<see cref="ToolPointer.Button"/> none).</summary>
    public virtual void PointerMove(ToolView view, ToolPointer pointer)
    {
    }

    /// <summary>The press it took let go.</summary>
    public virtual void PointerUp(ToolView view, ToolPointer pointer)
    {
    }

    /// <summary>The wheel turned over the view. True takes it from the camera.</summary>
    public virtual bool Wheel(ToolView view, ToolPointer pointer) => false;

    /// <summary>A key pressed while the view has focus, before anything else sees it. True takes it.</summary>
    public virtual bool KeyDown(ToolView view, KeyEvent key) => false;

    /// <summary>Its overlay, each time the view draws.</summary>
    public virtual void Draw(ToolView view, ToolCanvas canvas)
    {
    }
}

/// <summary>Where the pointer is in the view (in its units, from its top left), its button, keys held, and the wheel.</summary>
public readonly record struct ToolPointer(Vector2 Position, PointerButton Button, KeyModifiers Modifiers, Vector2 Wheel = default)
{
    public bool Ctrl => (Modifiers & KeyModifiers.Ctrl) != 0;

    public bool Shift => (Modifiers & KeyModifiers.Shift) != 0;

    public bool Alt => (Modifiers & KeyModifiers.Alt) != 0;
}

/// <summary>
/// What a tool can ask of its view (ADR 0121): its camera and size, the ray
/// through a point and where it meets a plane, and, a frame or two later,
/// the entity or the surface drawn there.
/// </summary>
public sealed class ToolView
{
    internal ToolView(SceneView view) => View = view;

    /// <summary>The view itself.</summary>
    public SceneView View { get; }

    public EditorCamera Camera => View.Camera;

    /// <summary>The view's size, in the same units as pointer positions.</summary>
    public Vector2 Size => View.ActualSize;

    /// <summary>The camera's ray through a point of the view.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(Vector2 at) => Camera.Ray(at, Size);

    /// <summary>Where the ray through a point meets a plane, in front of the camera; null if it doesn't.</summary>
    public Vector3? OnPlane(Vector2 at, Plane plane)
    {
        (Vector3 origin, Vector3 direction) = Ray(at);
        float facing = Vector3.Dot(plane.Normal, direction);
        if (MathF.Abs(facing) < 1e-6f)
        {
            return null;
        }
        float t = -(Vector3.Dot(plane.Normal, origin) + plane.D) / facing;
        return t > 0 ? origin + direction * t : null;
    }

    /// <summary>Where a world point is in the view; null behind the camera.</summary>
    public Vector2? Project(Vector3 point) => Camera.Project(point, Size);

    /// <summary>The entity drawn at a point (none where nothing is), answered once the GPU has drawn it.</summary>
    public void PickEntity(Vector2 at, Action<Entity> then)
    {
        ArgumentNullException.ThrowIfNull(then);
        View.Pick(at, result => then(result.Hit ? result.Entity : Entity.Null));
    }

    /// <summary>The world position of what's drawn at a point (null where nothing is), answered as <see cref="PickEntity"/> is.</summary>
    public void PickSurface(Vector2 at, Action<Vector3?> then)
    {
        ArgumentNullException.ThrowIfNull(then);
        View.Pick(at, result =>
        {
            if (!result.Hit || result.Depth <= 0)
            {
                then(null);
                return;
            }
            // Reverse-Z with no far plane: the view-axis distance is near / depth (ADR 0071).
            (Vector3 origin, Vector3 direction) = Ray(at);
            float along = Camera.NearPlane / result.Depth;
            then(origin + direction * (along / MathF.Max(Vector3.Dot(direction, Camera.Forward), 1e-4f)));
        });
    }

    /// <summary>Draws the view again (its overlay changed).</summary>
    public void Redraw() => View.InvalidateVisual();
}

/// <summary>
/// A tool's overlay (ADR 0121): drawn over the view's image, always on top,
/// in the view's units from its top left, or at world points through the
/// camera. Colors are linear RGBA.
/// </summary>
public sealed class ToolCanvas
{
    private readonly ToolView _view;
    private readonly Dictionary<string, ShapedText> _texts;
    private readonly HashSet<string> _used;

    internal ToolCanvas(Canvas canvas, ToolView view, Dictionary<string, ShapedText> texts, HashSet<string> used)
    {
        Canvas = canvas;
        _view = view;
        _texts = texts;
        _used = used;
    }

    /// <summary>The UI canvas underneath, for anything else it draws.</summary>
    public Canvas Canvas { get; }

    public void Line(Vector2 from, Vector2 to, float width, Vector4 color) => Canvas.DrawLine(from, to, width, color);

    /// <summary>A rectangle: filled, or outlined <paramref name="stroke"/> wide.</summary>
    public void Rect(Vector2 position, Vector2 size, Vector4 color, float stroke = 0, float radius = 0)
    {
        if (stroke > 0)
        {
            Canvas.StrokeRect(position, size, color, stroke, new Vector4(radius));
        }
        else
        {
            Canvas.FillRect(position, size, color, radius);
        }
    }

    /// <summary>A circle: filled, or outlined <paramref name="stroke"/> wide.</summary>
    public void Circle(Vector2 center, float radius, Vector4 color, float stroke = 0) =>
        Rect(center - new Vector2(radius), new Vector2(radius * 2), color, stroke, radius);

    public void Image(TextureHandle texture, Vector2 position, Vector2 size) => Canvas.DrawImage(texture, position, size);

    /// <summary>Text, in the view's font, its top left at <paramref name="position"/>.</summary>
    public void Text(string text, Vector2 position, Vector4 color)
    {
        if (!_texts.TryGetValue(text, out ShapedText? shaped))
        {
            shaped = new ShapedText();
            _texts[text] = shaped;
        }
        _used.Add(text);
        if (shaped.Shape(_view.View, GlyphRendering.Exact, text, 0) is { } layout)
        {
            Canvas.DrawText(layout, position, color);
        }
    }

    /// <summary>A line between world points, where both are in front of the camera.</summary>
    public void WorldLine(Vector3 from, Vector3 to, float width, Vector4 color)
    {
        if (WorldPoint(from) is { } a && WorldPoint(to) is { } b)
        {
            Line(a, b, width, color);
        }
    }

    /// <summary>Where a world point is in the view, to draw at; null behind the camera.</summary>
    public Vector2? WorldPoint(Vector3 point) => _view.Project(point);
}

/// <summary>
/// An extension's scene tools (ADR 0121): registered with a button on the
/// Viewport's toolbar, after Move, Rotate, Scale, and Edit Points, and a key
/// if asked (the first registered keeps it); turned on from its own commands too.
/// </summary>
public sealed class EditorTools
{
    private readonly Shell _shell;
    private readonly EditorContext _context;

    internal EditorTools(Shell shell, EditorContext context)
    {
        _shell = shell;
        _context = context;
    }

    /// <summary>Registers a tool; false (with a warning in the Log) if its ID is taken.</summary>
    public bool Add(ViewportTool tool, Shortcut? shortcut = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return _shell.AddTool(tool, shortcut, _context);
    }

    /// <summary>Turns a registered tool on in the Viewport; false for one not registered.</summary>
    public bool Activate(string id) => _shell.ActivateTool(id);
}
