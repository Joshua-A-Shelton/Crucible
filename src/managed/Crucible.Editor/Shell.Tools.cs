using System.Numerics;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Viewport tools (ADR 0121): the scene's tools, the editor's own (Move,
/// Rotate, Scale, Edit Points) and extensions', each a button on the
/// Viewport's toolbar and a key; the tool on in the Viewport; and their
/// code run guarded.
/// </summary>
internal sealed partial class Shell
{
    private readonly List<(ViewportTool Tool, Shortcut? Shortcut)> _tools = [];
    private ViewportTool? _move;

    /// <summary>The scene's tools, the editor's first, in the order registered.</summary>
    internal IReadOnlyList<(ViewportTool Tool, Shortcut? Shortcut)> Tools => _tools;

    /// <summary>Raised when tools are registered or taken out.</summary>
    internal event Action? ToolsChanged;

    /// <summary>Move: the scene views' tool when no other is on.</summary>
    internal ViewportTool DefaultTool => _move ??= new TransformTool(this, "Scene.Move", "Move", HandleMode.Move);

    private void RegisterTools()
    {
        AddTool(DefaultTool, new Shortcut(KeyModifiers.None, Keycode.W), null);
        AddTool(new TransformTool(this, "Scene.Rotate", "Rotate", HandleMode.Rotate), new Shortcut(KeyModifiers.None, Keycode.E), null);
        AddTool(new TransformTool(this, "Scene.Scale", "Scale", HandleMode.Scale), new Shortcut(KeyModifiers.None, Keycode.R), null);
        AddTool(new EditPointsTool(this), new Shortcut(KeyModifiers.None, Keycode.P), null);
    }

    internal bool AddTool(ViewportTool tool, Shortcut? shortcut, EditorContext? owner)
    {
        if (_tools.Any(known => known.Tool.Id == tool.Id))
        {
            LogWarning($"{owner?.Extension ?? "The editor"}: a tool \"{tool.Id}\" is already registered");
            return false;
        }
        if (shortcut is { } wanted && _tools.FirstOrDefault(known => known.Shortcut == wanted) is { Tool: { } taken })
        {
            LogWarning($"{tool.Id} wants {wanted}, already used by {taken.Id}; it has no shortcut");
            shortcut = null;
        }
        _tools.Add((tool, shortcut));
        owner?.OnStop(() =>
        {
            _tools.RemoveAll(known => known.Tool == tool);
            foreach (SceneView view in SceneViews().Where(view => view.Tool == tool))
            {
                view.Tool = DefaultTool;
            }
            ToolsChanged?.Invoke();
        });
        ToolsChanged?.Invoke();
        return true;
    }

    /// <summary>Turns a registered tool on in the Viewport; false for one not registered or no Viewport.</summary>
    internal bool ActivateTool(string id)
    {
        if (_tools.FirstOrDefault(known => known.Tool.Id == id).Tool is not { } tool ||
            _open.GetValueOrDefault("Viewport") is not ViewportPanel viewport)
        {
            return false;
        }
        viewport.View.Tool = tool;
        return true;
    }

    /// <summary>The tool a key chooses in a scene's view, if any.</summary>
    internal ViewportTool? ToolForKey(KeyEvent e) => _tools.FirstOrDefault(known => known.Shortcut is { } key && key.Matches(e)).Tool;

    // Every scene view open: the Viewport's and workspaces'.
    private IEnumerable<SceneView> SceneViews()
    {
        foreach (EditorPanel panel in _open.Values)
        {
            if (panel is ViewportPanel viewport)
            {
                yield return viewport.View;
            }
            else if (panel is Workspace workspace)
            {
                foreach (SceneView view in ViewsIn(workspace))
                {
                    yield return view;
                }
            }
        }
    }

    private static IEnumerable<SceneView> ViewsIn(UIElement element)
    {
        foreach (UIElement child in element.Children)
        {
            if (child is SceneView view)
            {
                yield return view;
            }
            else
            {
                foreach (SceneView inner in ViewsIn(child))
                {
                    yield return inner;
                }
            }
        }
    }

    // Each frame: the views' picks answered; Edit Points ended (the
    // selection moved on) gives the Viewport back to Move.
    private void TickTools()
    {
        foreach (SceneView view in SceneViews())
        {
            view.PollPick();
            if (view.Tool is EditPointsTool && !Chains.IsEditing)
            {
                view.Tool = DefaultTool;
            }
        }
    }

    /// <summary>A tool's code, guarded: an exception is logged, not the editor's end.</summary>
    internal void GuardTool(ViewportTool tool, string what, Action run)
    {
        try
        {
            run();
        }
        catch (Exception exception)
        {
            LogError($"{tool.Id}: {what} failed: {Message(exception)}");
        }
    }

    internal T GuardTool<T>(ViewportTool tool, string what, Func<T> run, T fallback)
    {
        try
        {
            return run();
        }
        catch (Exception exception)
        {
            LogError($"{tool.Id}: {what} failed: {Message(exception)}");
            return fallback;
        }
    }

    /// <summary>What a click picks: the topmost entity above what's drawn (a prefab instance's root).</summary>
    internal Entity Pickable(Entity entity) => entity.IsNull || !_context.HasWorld ? Entity.Null : TopOf(World, entity);
}

/// <summary>
/// Move, Rotate, Scale (ADR 0067, 0121): the selection's handles in that
/// mode, dragged as one undo step; a click off them selects what's under it.
/// </summary>
internal sealed class TransformTool(Shell shell, string id, string title, HandleMode mode) : ViewportTool(id, title)
{
    private HandlePart _hot;
    private bool _dragging;

    public HandleMode Mode => mode;

    public override void Activated(ToolView view) => view.View.Handles.Mode = mode;

    public override bool PointerDown(ToolView view, ToolPointer pointer)
    {
        if (pointer.Button != PointerButton.Left || pointer.Alt)
        {
            return false;
        }
        TransformHandles handles = view.View.Handles;
        handles.Mode = mode;
        if (shell.SelectedTransform() is { } world &&
            handles.HitTest(view.Camera, view.Size, world, pointer.Position) is var part && part != HandlePart.None)
        {
            handles.BeginDrag(part, view.Camera, view.Size, world, pointer.Position);
            _dragging = true;
            // The drag's moves undo as one (ADR 0068).
            shell.History.BeginGroup(mode.ToString());
            return true;
        }
        view.PickEntity(pointer.Position, picked => shell.Selection.Selected = shell.Pickable(picked));
        return true;
    }

    public override void PointerMove(ToolView view, ToolPointer pointer)
    {
        TransformHandles handles = view.View.Handles;
        if (_dragging)
        {
            shell.SetSelectedTransform(handles.Drag(view.Camera, view.Size, pointer.Position, pointer.Ctrl));
            return;
        }
        HandlePart hot = shell.SelectedTransform() is { } world ? handles.HitTest(view.Camera, view.Size, world, pointer.Position) : HandlePart.None;
        if (hot != _hot)
        {
            _hot = hot;
            view.Redraw();
        }
    }

    public override void PointerUp(ToolView view, ToolPointer pointer)
    {
        if (_dragging)
        {
            view.View.Handles.EndDrag();
            shell.History.EndGroup();
            _dragging = false;
        }
    }

    public override void Draw(ToolView view, ToolCanvas canvas)
    {
        if (shell.SelectedTransform() is { } world)
        {
            shell.DrawLight2D(canvas.Canvas, view.Camera, view.Size);
            view.View.Handles.Draw(canvas.Canvas, view.Camera, view.Size, world, _hot);
        }
    }
}

/// <summary>
/// Edit Points (ADR 0095, 0121): a Chain collider's points, for the selected
/// entity: drag a point, drag a segment's dot to insert one, Delete removes
/// the selected one; a click away selects what's under it, ending it.
/// </summary>
internal sealed class EditPointsTool(Shell shell) : ViewportTool("Scene.EditPoints", "Edit Points")
{
    private bool _dragging;

    public override void Activated(ToolView view)
    {
        if (!shell.Chains.IsEditing)
        {
            shell.Chains.Start(shell.Selection.Selected);
        }
    }

    public override void Deactivated(ToolView view) => shell.Chains.Stop();

    public override bool PointerDown(ToolView view, ToolPointer pointer)
    {
        if (pointer.Button != PointerButton.Left || pointer.Alt)
        {
            return false;
        }
        if (shell.Chains.IsEditing && shell.Chains.BeginDrag(view.Camera, view.Size, pointer.Position))
        {
            _dragging = true;
            return true;
        }
        view.PickEntity(pointer.Position, picked => shell.Selection.Selected = shell.Pickable(picked));
        return true;
    }

    public override void PointerMove(ToolView view, ToolPointer pointer)
    {
        if (_dragging)
        {
            shell.Chains.Drag(view.Camera, view.Size, pointer.Position, pointer.Ctrl);
        }
        else if (shell.Chains.IsEditing && shell.Chains.Hover(view.Camera, view.Size, pointer.Position))
        {
            view.Redraw();
        }
    }

    public override void PointerUp(ToolView view, ToolPointer pointer)
    {
        if (_dragging)
        {
            shell.Chains.EndDrag();
            _dragging = false;
        }
    }

    public override bool KeyDown(ToolView view, KeyEvent key) =>
        key.Keycode == Keycode.Delete && (key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Gui)) == 0 &&
        shell.Chains.DeleteSelected();

    public override void Draw(ToolView view, ToolCanvas canvas)
    {
        shell.Chains.Update();
        if (shell.Chains.IsEditing)
        {
            shell.Chains.Draw(canvas.Canvas, view.Camera, view.Size);
        }
    }
}

/// <summary>
/// Map mode's painting (ADR 0112) as its view's tool (ADR 0121): the left
/// button uses the map tool, the right picks up painted tiles (taken from
/// the camera here); Ctrl+C, Ctrl+X, and Delete on the Select tool's region,
/// Escape letting go of it. Rebuilt on the public API in step 7.
/// </summary>
internal sealed class MapPaintTool(Shell shell) : ViewportTool("Map.Paint", "Paint")
{
    private enum Press
    {
        None,
        Stroke,
        Pick,
    }

    private Press _press;

    private MapEditor Map => shell.Map;

    public override bool PointerDown(ToolView view, ToolPointer pointer)
    {
        if (!Map.IsOpen || pointer.Alt || pointer.Button is not (PointerButton.Left or PointerButton.Right))
        {
            return false;
        }
        if (Map.CellAt(view.Camera, view.Size, pointer.Position) is { } cell)
        {
            if (pointer.Button == PointerButton.Right)
            {
                Map.BeginPick(cell);
                _press = Press.Pick;
            }
            else if (Map.BeginStroke(cell))
            {
                _press = Press.Stroke;
            }
        }
        return true;
    }

    public override void PointerMove(ToolView view, ToolPointer pointer)
    {
        (int X, int Y)? cell = Map.IsOpen ? Map.CellAt(view.Camera, view.Size, pointer.Position) : null;
        switch (_press)
        {
            case Press.Stroke when cell is { } at:
                Map.Stroke(at);
                break;
            case Press.Pick:
                Map.Track(cell ?? Map.Hover);
                break;
            case Press.None when Map.IsOpen && Map.Track(cell):
                view.Redraw();
                break;
        }
    }

    public override void PointerUp(ToolView view, ToolPointer pointer)
    {
        if (_press == Press.Stroke)
        {
            Map.EndStroke();
        }
        else if (_press == Press.Pick)
        {
            Map.EndPick();
        }
        _press = Press.None;
    }

    public override bool KeyDown(ToolView view, KeyEvent key)
    {
        const int ScancodeEscape = 41;
        if (!Map.IsOpen || (key.Modifiers & KeyModifiers.Alt) != 0)
        {
            return false;
        }
        if ((key.Modifiers & (KeyModifiers.Ctrl | KeyModifiers.Gui)) != 0)
        {
            bool done = key.Keycode switch
            {
                Keycode.C => Map.Copy(),
                Keycode.X => Map.Cut(),
                _ => false,
            };
            if (done)
            {
                shell.RaiseMapToolChanged();
            }
            return done;
        }
        if (key.Keycode == Keycode.Delete)
        {
            return Map.DeleteRegion();
        }
        if (key.Scancode != ScancodeEscape || Map.Region is null)
        {
            return false;
        }
        Map.Region = null;
        return true;
    }

    public override void Draw(ToolView view, ToolCanvas canvas)
    {
        if (Map.IsOpen)
        {
            Map.Draw(canvas.Canvas, view.Camera, view.Size);
        }
    }
}
