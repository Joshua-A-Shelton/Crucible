using System.Numerics;
using Crucible.Ecs;
using Crucible.Editor;
using Crucible.Platform;
using Crucible.UI;

namespace Crucible.Tilemaps.Editor;

/// <summary>
/// Map mode's painting (ADR 0112) as its view's tool (ADR 0121): the left
/// button uses the map tool, the right picks up painted tiles (taken from
/// the camera here); Ctrl+C, Ctrl+X, and Delete on the Select tool's region,
/// Escape letting go of it. On the public API (ADR 0123).
/// </summary>
internal sealed class MapPaintTool(MapMode maps) : ViewportTool("Map.Paint", "Paint")
{
    private enum Press
    {
        None,
        Stroke,
        Pick,
    }

    private Press _press;

    private MapEditor Map => maps.Map;

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
                maps.RaiseMapToolChanged();
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
