using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Ecs;
using Crucible.Physics2D;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Edits a Chain collider's points over the viewports (ADR 0095), while Edit
/// Points (P) is on for the selected entity: drag a point to move it, drag a
/// segment's middle dot to insert one there, Delete removes the selected
/// point. Each edit is one undo step that writes the .crchain and imports it
/// at once, so every collider using the chain follows; the scene isn't
/// marked modified. The transform handles hide meanwhile. Points move in the
/// entity's XY plane; Ctrl snaps them to 0.25 m in its space.
/// </summary>
internal sealed class ChainHandles
{
    private const float PointPixels = 10, DotPixels = 7, HitPixels = 8, Snap = 0.25f;

    private static readonly Vector4 _line = new(0.92f, 0.92f, 0.94f, 0.9f), _point = new(0.92f, 0.92f, 0.94f, 1);
    private static readonly Vector4 _dot = new(0.6f, 0.6f, 0.64f, 1), _hot = new(1f, 0.85f, 0.2f, 1);
    private static readonly Vector4 _selected = new(0.32f, 0.52f, 0.98f, 1);
    private static readonly Vector4 _edge = new(0.08f, 0.08f, 0.1f, 0.85f); // under everything, so it shows on light and dark

    private readonly Shell _shell;
    private Entity _entity;
    private AssetId _chain;
    private List<Vector2> _points = [];
    private bool _loop;
    // A drag: the point dragged, the points before it, and what it's called.
    private int _dragging = -1;
    private List<Vector2>? _before;
    private string _title = "";

    public ChainHandles(Shell shell)
    {
        _shell = shell;
        _shell.Selection.Changed += selection =>
        {
            if (selection.Selected.Value != _entity.Value)
            {
                Stop();
            }
        };
        _shell.AssetsChanged += () =>
        {
            if (IsEditing && _dragging < 0)
            {
                Read(); // changed outside, or by undo
            }
        };
    }

    /// <summary>Whether Edit Points is on.</summary>
    public bool IsEditing => _entity.Value != 0;

    /// <summary>The entity whose chain is edited; Null when off.</summary>
    public Entity Entity => _entity;

    public IReadOnlyList<Vector2> Points => _points;

    /// <summary>The point Delete removes; -1 for none.</summary>
    public int Selected { get; private set; } = -1;

    /// <summary>The point (0 up) or middle dot (-1 down, for the segment after point 0) under the pointer; null for none.</summary>
    internal int? Hot { get; private set; }

    internal bool IsDragging => _dragging >= 0;

    /// <summary>Whether an entity's collider is a chain with an asset this editor can edit.</summary>
    public bool CanEdit(Entity entity) => ChainOf(entity) is not null;

    /// <summary>Edit Points for the selection: on if it has a chain to edit, off if it was on.</summary>
    public void Toggle()
    {
        if (IsEditing)
        {
            Stop();
        }
        else
        {
            Start(_shell.Selection.Selected);
        }
    }

    public bool Start(Entity entity)
    {
        Stop();
        if (ChainOf(entity) is not { } chain)
        {
            return false;
        }
        _entity = entity;
        _chain = chain;
        if (!Read())
        {
            Stop();
            return false;
        }
        return true;
    }

    public void Stop()
    {
        if (_dragging >= 0 && _before is not null)
        {
            _points = _before; // a drag cut short changes nothing
        }
        _dragging = -1;
        _before = null;
        _entity = Entity.Null;
        _chain = AssetId.None;
        _points = [];
        Selected = -1;
        Hot = null;
    }

    // The chain asset of an entity's Chain collider, if it's one the database imports as a chain.
    private AssetId? ChainOf(Entity entity)
    {
        World? world = _shell.HasWorld ? _shell.World : null;
        if (world is null || entity.Value == 0 || !world.IsAlive(entity) || !world.Has(entity, Components.Of<Collider2D>()))
        {
            return null;
        }
        Collider2D collider = world.Read<Collider2D>(entity);
        return collider.Shape == ShapeKind2D.Chain && !collider.Chain.IsNone &&
               _shell.Database?.Find(collider.Chain) is { Importer: "Chain" }
            ? collider.Chain
            : null;
    }

    private string? FileOf(AssetId chain) =>
        _shell.Project is { } project && _shell.Database?.Find(chain) is { } entry
            ? Path.Combine(project.AssetsFolder, entry.Path)
            : null;

    // The points from the file; false (said) if it can't be read.
    private bool Read()
    {
        if (FileOf(_chain) is not { } file)
        {
            return false;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            (_points, _loop) = ChainImporter.Read(document.RootElement);
            Selected = Math.Min(Selected, _points.Count - 1);
            return true;
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            _shell.Log($"Can't edit {Path.GetFileName(file)}'s points: {exception.Message}");
            return false;
        }
    }

    // Each frame, before drawing: off if the collider stopped being that chain.
    internal void Update()
    {
        if (IsEditing && ChainOf(_entity) != _chain)
        {
            Stop();
        }
    }

    // The entity's world matrix and its collider's offset: the points' space.
    private bool Frame(out Matrix4x4 world, out Vector2 offset)
    {
        world = Matrix4x4.Identity;
        offset = default;
        if (!IsEditing || !_shell.World.IsAlive(_entity))
        {
            return false;
        }
        world = _shell.World.ComputeWorldTransform(_entity);
        offset = _shell.World.Read<Collider2D>(_entity).Offset;
        return true;
    }

    private static Vector3 ToWorld(Vector2 point, Matrix4x4 world, Vector2 offset) =>
        Vector3.Transform(new Vector3(point + offset, 0), world);

    // Where the pointer's ray meets the entity's plane, in the points' space.
    private static Vector2? FromScreen(EditorCamera camera, Vector2 size, Vector2 at, Matrix4x4 world, Vector2 offset)
    {
        (Vector3 origin, Vector3 direction) = camera.Ray(at, size);
        Vector3 normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
        float facing = Vector3.Dot(direction, normal);
        if (MathF.Abs(facing) < 1e-5f || !Matrix4x4.Invert(world, out Matrix4x4 inverse))
        {
            return null;
        }
        float t = Vector3.Dot(world.Translation - origin, normal) / facing;
        if (t <= 0)
        {
            return null;
        }
        Vector3 local = Vector3.Transform(origin + direction * t, inverse);
        return new Vector2(local.X, local.Y) - offset;
    }

    private int Segments => _loop ? _points.Count : _points.Count - 1;

    private Vector2 Middle(int segment) => (_points[segment] + _points[(segment + 1) % _points.Count]) * 0.5f;

    /// <summary>The point (0 up) or middle dot (-1 down) at a place in a viewport; null for none.</summary>
    internal int? HitTest(EditorCamera camera, Vector2 size, Vector2 at)
    {
        if (!Frame(out Matrix4x4 world, out Vector2 offset))
        {
            return null;
        }
        // Points first: a dot is only taken where no point is near.
        int? best = null;
        float bestDistance = HitPixels;
        void Consider(int part, Vector2 point)
        {
            if (camera.Project(ToWorld(point, world, offset), size) is { } shown &&
                Vector2.Distance(shown, at) is var distance && distance < bestDistance)
            {
                bestDistance = distance;
                best = part;
            }
        }
        for (int i = 0; i < _points.Count; ++i)
        {
            Consider(i, _points[i]);
        }
        if (best is null)
        {
            for (int i = 0; i < Segments; ++i)
            {
                Consider(-1 - i, Middle(i));
            }
        }
        return best;
    }

    /// <summary>Tracks what's under the pointer; true if that changed.</summary>
    internal bool Hover(EditorCamera camera, Vector2 size, Vector2 at)
    {
        int? hot = HitTest(camera, size, at);
        bool changed = hot != Hot;
        Hot = hot;
        return changed;
    }

    /// <summary>Starts dragging the point or dot under the pointer; false if there's none.</summary>
    internal bool BeginDrag(EditorCamera camera, Vector2 size, Vector2 at)
    {
        if (HitTest(camera, size, at) is not { } part)
        {
            return false;
        }
        _before = [.. _points];
        if (part >= 0)
        {
            _dragging = part;
            _title = "Move Point";
        }
        else
        {
            int segment = -1 - part;
            _points.Insert(segment + 1, Middle(segment));
            _dragging = segment + 1;
            _title = "Insert Point";
        }
        Selected = _dragging;
        Hot = _dragging;
        return true;
    }

    internal void Drag(EditorCamera camera, Vector2 size, Vector2 at, bool snap)
    {
        if (_dragging < 0 || !Frame(out Matrix4x4 world, out Vector2 offset) ||
            FromScreen(camera, size, at, world, offset) is not { } point)
        {
            return;
        }
        if (snap)
        {
            point = new Vector2(MathF.Round(point.X / Snap) * Snap, MathF.Round(point.Y / Snap) * Snap);
        }
        _points[_dragging] = Rounded(point);
    }

    /// <summary>Ends a drag: its change saved as one undo step, unless it changed nothing or put points too close.</summary>
    internal void EndDrag()
    {
        if (_dragging < 0 || _before is null)
        {
            return;
        }
        List<Vector2> before = _before;
        _dragging = -1;
        _before = null;
        if (before.SequenceEqual(_points))
        {
            return;
        }
        if (TooClose(_points) is { } close)
        {
            _shell.Log($"Points {close.A + 1} and {close.B + 1} would be too close together; the drag is undone");
            _points = before;
            Selected = Math.Min(Selected, _points.Count - 1);
            return;
        }
        Commit(_title, before, [.. _points]);
    }

    /// <summary>Removes the selected point (Delete); false if there's none to remove.</summary>
    public bool DeleteSelected()
    {
        if (!IsEditing || _dragging >= 0 || Selected < 0 || Selected >= _points.Count)
        {
            return false;
        }
        int fewest = _loop ? 3 : 2;
        if (_points.Count <= fewest)
        {
            _shell.Log($"A {(_loop ? "loop" : "chain")} keeps at least {fewest} points");
            return true;
        }
        List<Vector2> after = [.. _points];
        after.RemoveAt(Selected);
        if (TooClose(after) is { } close)
        {
            _shell.Log($"Without it, points {close.A + 1} and {close.B + 1} would be too close together");
            return true;
        }
        List<Vector2> before = [.. _points];
        _points = after;
        Selected = Math.Min(Selected, _points.Count - 1);
        Commit("Delete Point", before, after);
        return true;
    }

    private (int A, int B)? TooClose(List<Vector2> points)
    {
        int segments = _loop ? points.Count : points.Count - 1;
        for (int i = 0; i < segments; ++i)
        {
            int next = (i + 1) % points.Count;
            if (Vector2.Distance(points[i], points[next]) <= ChainImporter.MinPointSpacing)
            {
                return (i, next);
            }
        }
        return null;
    }

    // Millimeters: what's saved is what's shown.
    private static Vector2 Rounded(Vector2 point) => new(MathF.Round(point.X, 3), MathF.Round(point.Y, 3));

    private void Commit(string title, List<Vector2> before, List<Vector2> after)
    {
        if (Write(_chain, after))
        {
            _shell.History.Push(new ChainEdit(this, title, _chain, before, after));
        }
        else
        {
            _points = before;
        }
    }

    // Undo and redo: the points written, and shown if they're the chain being edited.
    internal void Apply(AssetId chain, List<Vector2> points)
    {
        if (Write(chain, points) && IsEditing && chain == _chain && _dragging < 0)
        {
            _points = [.. points];
            Selected = Math.Min(Selected, _points.Count - 1);
        }
    }

    // The .crchain's Points replaced (everything else kept), and imported at once.
    private bool Write(AssetId chain, List<Vector2> points)
    {
        if (FileOf(chain) is not { } file)
        {
            return false;
        }
        try
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? [];
            File.WriteAllText(file, Format(root, points));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _shell.Log($"Can't save {Path.GetFileName(file)}: {exception.Message}");
            return false;
        }
        _shell.Reimport(chain, quiet: true);
        return true;
    }

    // One point a line, the file's other keys as they were.
    internal static string Format(JsonObject root, IReadOnlyList<Vector2> points)
    {
        static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        StringBuilder text = new("{\n");
        bool wrotePoints = false;
        List<string> entries = [];
        foreach ((string key, JsonNode? value) in root)
        {
            if (key == "Points")
            {
                entries.Add(PointsEntry());
                wrotePoints = true;
            }
            else
            {
                entries.Add($"  {JsonValue.Create(key).ToJsonString()}: {value?.ToJsonString() ?? "null"}");
            }
        }
        if (!wrotePoints)
        {
            entries.Add(PointsEntry());
        }
        text.AppendJoin(",\n", entries);
        text.Append("\n}\n");
        return text.ToString();

        string PointsEntry() =>
            "  \"Points\": [\n" + string.Join(",\n", points.Select(point => $"    [{Number(point.X)}, {Number(point.Y)}]")) + "\n  ]";
    }

    /// <summary>The chain's segments, its middle dots, and its points, over a viewport.</summary>
    internal void Draw(Canvas canvas, EditorCamera camera, Vector2 size)
    {
        if (!Frame(out Matrix4x4 world, out Vector2 offset))
        {
            return;
        }
        Vector2? Shown(Vector2 point) => camera.Project(ToWorld(point, world, offset), size);
        for (int i = 0; i < Segments; ++i)
        {
            if (Shown(_points[i]) is { } a && Shown(_points[(i + 1) % _points.Count]) is { } b)
            {
                canvas.DrawLine(a, b, 3.5f, _edge);
                canvas.DrawLine(a, b, 1.5f, _line);
            }
        }
        if (_dragging < 0)
        {
            for (int i = 0; i < Segments; ++i)
            {
                if (Shown(Middle(i)) is { } middle)
                {
                    canvas.FillRect(middle - new Vector2(DotPixels / 2 + 1.5f), new Vector2(DotPixels + 3), _edge,
                        DotPixels / 2 + 1.5f);
                    canvas.FillRect(middle - new Vector2(DotPixels / 2), new Vector2(DotPixels), Hot == -1 - i ? _hot : _dot,
                        DotPixels / 2);
                }
            }
        }
        for (int i = 0; i < _points.Count; ++i)
        {
            if (Shown(_points[i]) is { } point)
            {
                Vector4 color = Hot == i ? _hot : Selected == i ? _selected : _point;
                canvas.FillRect(point - new Vector2(PointPixels / 2 + 1.5f), new Vector2(PointPixels + 3), _edge,
                    PointPixels / 2 + 1.5f);
                canvas.FillRect(point - new Vector2(PointPixels / 2), new Vector2(PointPixels), color, PointPixels / 2);
            }
        }
    }

    // One drag, insert, or delete: the points before and after, written back on undo and redo.
    private sealed class ChainEdit(ChainHandles handles, string title, AssetId chain, List<Vector2> before, List<Vector2> after)
        : UndoStep(title)
    {
        public override bool ChangesScene => false;

        public override void Undo(World world) => handles.Apply(chain, before);

        public override void Redo(World world) => handles.Apply(chain, after);
    }
}
