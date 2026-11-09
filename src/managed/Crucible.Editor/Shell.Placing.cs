using System.Numerics;
using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Render;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

// Placing assets (ADR 0071): a model or prefab dragged into a viewport is
// instanced at once and follows the pointer, landing on the surface under it
// (picking looks through it); leaving removes it, dropping keeps it as one
// undo step. Dropped on the hierarchy, it's placed under the row. A drag
// leaving its window is carried on as a platform drag to the others.
internal sealed partial class Shell
{
    private const float MissDistance = 10; // meters ahead when nothing's under the pointer

    private SceneView? _placeView;
    private Entity _placing;
    private Vector2 _placePixel;
    // The pixel each recent frame's pick was asked for, by frame: answers come
    // a frame or two late, each for the pixel asked then.
    private readonly Dictionary<ulong, Vector2> _placeAsked = [];
    private ulong _placeApplied;
    // A UI drag carried between windows by a platform drag.
    private (object Payload, UIElement? Preview)? _carried;

    /// <summary>Whether an asset can be placed in the scene: a model or a prefab the database knows.</summary>
    public bool CanPlace(object payload) =>
        payload is AssetDrag drag && Database?.Find(drag.Id) is { } asset && Prefabs is not null &&
        (asset.Importer is "Mesh" or "Fbx" || drag.Path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>The entity being placed by a drag over a viewport, or <see cref="Entity.Null"/>.</summary>
    public Entity Placing => _placing;

    // A drag entered a viewport: the asset instanced, picking told to look through it.
    internal void BeginPlacement(SceneView view, AssetDrag drag, Vector2 pixel)
    {
        CancelPlacement();
        if (!_context.HasWorld || !CanPlace(drag))
        {
            Debug($"  not placing {drag.Path}: world {_context.HasWorld}, can place {CanPlace(drag)}");
            return;
        }
        try
        {
            _placing = Prefab.Instantiate(World, drag.Id, Prefabs!);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
        {
            Status($"Can't place {drag.Path}: {exception.Message}");
            return;
        }
        _placeView = view;
        _placeApplied = 0;
        Render.Api.SetSurfacePickExclusion(view.Surface, Subtree(_placing));
        MovePlacement(pixel);
    }

    internal void MovePlacement(Vector2 pixel)
    {
        if (_placeView is not { } view)
        {
            return;
        }
        _placePixel = pixel;
        Render.Api.RequestPick(view.Surface, (uint)MathF.Max(pixel.X, 0), (uint)MathF.Max(pixel.Y, 0));
        ulong frame = Render.Api.FramesSubmitted(view.Surface) + 1;
        _placeAsked[frame] = pixel; // a later ask in the same frame replaces it
        if (_placeAsked.Count > 16)
        {
            foreach (ulong old in _placeAsked.Keys.Where(asked => asked + 8 < frame).ToArray())
            {
                _placeAsked.Remove(old);
            }
        }
    }

    // Each frame: the newest answer moves the instance to what's under the pointer.
    private void UpdatePlacement()
    {
        if (_placeView is not { } view || _placing.IsNull || !World.IsAlive(_placing))
        {
            return;
        }
        // Asked every frame, so a still pointer follows a scene that moves.
        MovePlacement(_placePixel);
        // Every new answer is used, with the pixel it was asked for.
        PickResult result = Render.Api.PickResult(view.Surface);
        if (result.Frame <= _placeApplied || !_placeAsked.TryGetValue(result.Frame, out Vector2 pixel))
        {
            return;
        }
        _placeApplied = result.Frame;
        Vector3 at = PlacementPoint(view.Camera, view.ActualSize, pixel / (view.Root?.Scale ?? 1), result);
        LocalTransform local = World.Read<LocalTransform>(_placing);
        World.Set(_placing, local with { Position = at });
        Debug($"  placement at {at} (pixel {pixel}, hit {result.Hit}, depth {result.Depth})");
    }

    /// <summary>
    /// Where a placed asset lands under a point of a viewport: on what's drawn
    /// there (from the pick's depth), else on the ground plane (y = 0), else
    /// <see cref="MissDistance"/> ahead.
    /// </summary>
    internal static Vector3 PlacementPoint(EditorCamera camera, Vector2 size, Vector2 point, PickResult pick)
    {
        (Vector3 origin, Vector3 direction) = camera.Ray(point, size);
        if (pick.Hit && pick.Depth > 0)
        {
            // Reverse-Z with no far plane: the view-axis distance is near / depth.
            float along = camera.NearPlane / pick.Depth;
            return origin + direction * (along / MathF.Max(Vector3.Dot(direction, camera.Forward), 1e-4f));
        }
        if (direction.Y < -1e-4f)
        {
            float t = -origin.Y / direction.Y;
            if (t > 0)
            {
                return origin + direction * t;
            }
        }
        return origin + direction * MissDistance;
    }

    /// <summary>The drag left the viewport, or was canceled: the instance goes, unrecorded.</summary>
    internal void CancelPlacement()
    {
        if (_placeView is { } view)
        {
            Render.Api.SetSurfacePickExclusion(view.Surface, []);
        }
        if (!_placing.IsNull && _context.HasWorld && World.IsAlive(_placing))
        {
            World.Destroy(_placing);
        }
        _placing = Entity.Null;
        _placeView = null;
        _placeAsked.Clear();
    }

    /// <summary>Dropped: the instance stays, as one undo step, and is selected.</summary>
    internal void FinishPlacement()
    {
        if (_placeView is { } view)
        {
            Render.Api.SetSurfacePickExclusion(view.Surface, []);
        }
        Entity placed = _placing;
        _placing = Entity.Null;
        _placeView = null;
        _placeAsked.Clear();
        if (placed.IsNull || !World.IsAlive(placed))
        {
            return;
        }
        RecordCreated($"Place {LabelOf(placed)}", placed);
        Selection.Selected = placed;
        Debug($"  placed {LabelOf(placed)} at {World.Read<LocalTransform>(placed).Position}");
    }

    /// <summary>Places an asset under <paramref name="parent"/> (or at the top) at its origin: a drop on the hierarchy.</summary>
    public Entity Place(AssetDrag drag, Entity parent)
    {
        if (!_context.HasWorld || !CanPlace(drag))
        {
            return Entity.Null;
        }
        Entity placed;
        try
        {
            placed = Prefab.Instantiate(World, drag.Id, Prefabs!, HasLive(parent) ? parent : Entity.Null);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
        {
            Status($"Can't place {drag.Path}: {exception.Message}");
            return Entity.Null;
        }
        RecordCreated($"Place {LabelOf(placed)}", placed);
        Selection.Selected = placed;
        return placed;
    }

    private Entity[] Subtree(Entity root)
    {
        List<Entity> found = [];
        Stack<Entity> pending = new([root]);
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            found.Add(entity);
            foreach (Entity child in SceneIds.ChildrenOf(World, entity))
            {
                pending.Push(child);
            }
        }
        return [.. found];
    }

    // -----------------------------------------------------------------------
    // Drags between windows
    // -----------------------------------------------------------------------

    // A UI drag left its window: carried on by the platform, if it can.
    private void DragLeftWindow(UIRoot root, Vector2 position)
    {
        // A carried drag no window holds any more ended without the platform saying so.
        if (_carried is { } stale && !_windows.Exists(window => window.Root.DragPayload == stale.Payload))
        {
            Debug("forgot a carried drag that no window holds");
            _carried = null;
        }
        Debug($"a UI drag left window {_windows.Find(window => window.Root == root)?.Window.Value} at {position}" +
              $" (support {Platform.Api.WindowDragSupport()}, carrying {_carried is not null})");
        if (_carried is not null || root.DragPayload is not { } payload ||
            Platform.Api.WindowDragSupport() == WindowDragSupport.None ||
            _windows.Find(window => window.Root == root) is not { } source)
        {
            return;
        }
        if (Platform.Api.StartWindowDrag(source.Window, default, Vector2.Zero))
        {
            _carried = (payload, null);
            Debug("carrying a UI drag between windows");
        }
        else
        {
            Debug("the platform didn't start a drag to carry it");
        }
    }

    private void CarriedDragMoved(WindowHandle over, Vector2 pixels)
    {
        if (_carried is not { } carried)
        {
            return;
        }
        EditorWindow? target = _windows.Find(window => window.Window.Value == over.Value);
        Debug($"carried drag over window {over.Value} at {pixels} ({(target is null ? "not ours" : target.IsMain ? "main" : "floating")})");
        foreach (EditorWindow window in _windows)
        {
            if (window != target && window.Root.DragPayload == carried.Payload)
            {
                window.Root.CancelDrag();
            }
        }
        target?.Root.ContinueDrag(carried.Payload, carried.Preview, pixels / target.Root.Scale);
        if (target is not null)
        {
            Vector2 at = pixels / target.Root.Scale;
            Debug($"  in window {over.Value}: scale {target.Root.Scale}, size {target.Root.Size}, at {at}, over " +
                  $"{target.Root.HitTest(at)?.GetType().Name}, dragging {target.Root.IsDragging}, placing {_placing.Value}");
        }
    }

    private void CarriedDragEnded(WindowHandle over, Vector2 pixels, bool dropped)
    {
        if (_carried is not { } carried)
        {
            return;
        }
        _carried = null;
        Debug($"carried drag {(dropped ? "dropped on" : "canceled over")} window {over.Value} at {pixels}");
        foreach (EditorWindow window in _windows)
        {
            if (window.Root.DragPayload != carried.Payload)
            {
                continue;
            }
            if (dropped && window.Window.Value == over.Value)
            {
                bool taken = window.Root.DropAt(pixels / window.Root.Scale);
                Debug($"  dropped in window {over.Value}: taken {taken}, placing {_placing.Value}");
            }
            else
            {
                window.Root.CancelDrag();
            }
        }
    }
}
