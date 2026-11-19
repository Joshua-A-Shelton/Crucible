using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Importers;
using Crucible.Render2D;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>The Tile Palette's tools (ADR 0108).</summary>
public enum TileTool
{
    Paint, // the brush's tiles where it's pressed and dragged
    Erase, // the cells under the brush's footprint emptied
    Fill,  // the cells joined to the one pressed that have its tile (or none), the brush repeated over them
    Pick,  // the tile pressed becomes the brush, and Paint the tool
}

/// <summary>
/// Paints a tilemap over the viewports (ADR 0108), while Edit Tiles (T) is on
/// for the selected entity: the Tile Palette's tool and brush apply where
/// the left button presses and drags, cell by cell in the entity's XY plane.
/// Each change saves the .crtilemap and imports it, so the map and its
/// collision follow as it's painted; a stroke is one undo step. The scene
/// isn't marked modified, as with chains.
/// </summary>
internal sealed class TilePainter
{
    private static readonly Vector4 _edge = new(0.08f, 0.08f, 0.1f, 0.85f), _brush = new(0.32f, 0.52f, 0.98f, 1);
    private static readonly Vector4 _erase = new(0.95f, 0.35f, 0.3f, 1);

    private readonly Shell _shell;
    private Entity _entity;
    private AssetId _map;
    private TilemapData _data = new();
    private Dictionary<(int X, int Y), TileCell>? _before;
    private (int X, int Y)? _last;

    public TilePainter(Shell shell)
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
            if (IsEditing && _before is null)
            {
                Read(); // changed outside, or by undo
            }
        };
    }

    public bool IsEditing => _entity.Value != 0;

    public Entity Entity => _entity;

    public AssetId Map => _map;

    public TilemapData Data => _data;

    /// <summary>The map's tileset, read from its file; null if it has none that reads.</summary>
    public TilesetData? Tileset { get; private set; }

    /// <summary>Tiles across the tileset's texture; 0 if it isn't imported.</summary>
    public int Columns { get; private set; }

    /// <summary>A cell's size in meters (its tile's pixels over the texture's pixels per unit).</summary>
    public Vector2 CellSize { get; private set; } = Vector2.One;

    public TileTool Tool { get; set; } = TileTool.Paint;

    /// <summary>The brush: tiles by their place in it, x right and y up from its bottom-left.</summary>
    public IReadOnlyList<(int Dx, int Dy, int Tile)> Brush { get; private set; } = [(0, 0, 0)];

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    /// <summary>The cell under the pointer; null off the map's plane.</summary>
    public (int X, int Y)? Hover { get; private set; }

    /// <summary>Raised when Pick changes the brush or the tool.</summary>
    public event Action? BrushChanged;

    public bool CanEdit(Entity entity) => MapOf(entity) is not null;

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
        if (MapOf(entity) is not { } map)
        {
            return false;
        }
        _entity = entity;
        _map = map;
        if (!Read())
        {
            Stop();
            return false;
        }
        _shell.ShowTilePalette();
        return true;
    }

    public void Stop()
    {
        if (_before is not null)
        {
            EndStroke();
        }
        _entity = Entity.Null;
        _map = AssetId.None;
        _data = new TilemapData();
        Tileset = null;
        Columns = 0;
        Hover = null;
    }

    /// <summary>The brush from the palette's selection: tiles by column and row, corners inclusive.</summary>
    public void SetBrush(int x0, int y0, int x1, int y1)
    {
        if (Columns == 0)
        {
            return;
        }
        List<(int, int, int)> brush = [];
        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                brush.Add((x - x0, y1 - y, y * Columns + x)); // the palette's rows run down; the map's up
            }
        }
        Brush = brush;
    }

    // The tilemap asset of an entity's Tilemap, if the database imports it as one.
    private AssetId? MapOf(Entity entity)
    {
        World? world = _shell.HasWorld ? _shell.World : null;
        if (world is null || entity.Value == 0 || !world.IsAlive(entity) || !world.Has(entity, Components.Of<Tilemap>()))
        {
            return null;
        }
        AssetId map = world.Read<Tilemap>(entity).Map;
        return !map.IsNone && _shell.Database?.Find(map) is { Importer: "Tilemap" } ? map : null;
    }

    private string? FileOf(AssetId id) =>
        _shell.Project is { } project && _shell.Database?.Find(id) is { } entry ? Path.Combine(project.AssetsFolder, entry.Path) : null;

    // The map and its tileset from their files; false (said) if the map can't be read.
    private bool Read()
    {
        if (FileOf(_map) is not { } file)
        {
            return false;
        }
        try
        {
            _data = TilemapImporter.Read(File.ReadAllBytes(file));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _shell.Log($"Can't paint {Path.GetFileName(file)}: {exception.Message}");
            return false;
        }
        Tileset = null;
        Columns = 0;
        try
        {
            if (FileOf(_data.Tileset) is { } tilesetFile)
            {
                Tileset = TilesetImporter.Read(File.ReadAllBytes(tilesetFile));
                (int width, int height, float pixelsPerUnit) = TextureOf(Tileset.Texture);
                Columns = Tileset.Grid(width, height).Columns;
                CellSize = new Vector2(Tileset.TileWidth, Tileset.TileHeight) / pixelsPerUnit;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _shell.Log($"The map's tileset can't be read: {exception.Message}");
        }
        return true;
    }

    // A texture's size, from its imported file, and its pixels per unit, from its settings.
    private (int Width, int Height, float PixelsPerUnit) TextureOf(AssetId texture)
    {
        if (texture.IsNone || _shell.Database?.ImportedFile(texture) is not { } path)
        {
            return (0, 0, 16);
        }
        using ImportedFile file = ImportedFile.Open(path, TextureImporter.Kind, TextureImporter.KindVersion);
        TextureInfo info = MemoryMarshal.Read<TextureInfo>(file.Read(file.Find(TextureImporter.InfoSection)));
        float pixelsPerUnit = _shell.Database.SettingsOf(texture)?["PixelsPerUnit"]?.GetValue<float>() ?? 16;
        return ((int)info.Width, (int)info.Height, pixelsPerUnit > 0 ? pixelsPerUnit : 16);
    }

    // Each frame, before drawing: off if the entity stopped having that map.
    internal void Update()
    {
        if (IsEditing && MapOf(_entity) != _map)
        {
            Stop();
        }
    }

    private bool Frame(out Matrix4x4 world)
    {
        world = Matrix4x4.Identity;
        if (!IsEditing || !_shell.World.IsAlive(_entity))
        {
            return false;
        }
        world = _shell.World.ComputeWorldTransform(_entity);
        return true;
    }

    // The cell under a place in a viewport, where the pointer's ray meets the map's plane.
    private (int X, int Y)? CellAt(EditorCamera camera, Vector2 size, Vector2 at)
    {
        if (!Frame(out Matrix4x4 world))
        {
            return null;
        }
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
        return ((int)MathF.Floor(local.X / CellSize.X), (int)MathF.Floor(local.Y / CellSize.Y));
    }

    /// <summary>Tracks the cell under the pointer; true if that changed.</summary>
    internal bool Track(EditorCamera camera, Vector2 size, Vector2 at)
    {
        (int, int)? hover = CellAt(camera, size, at);
        bool changed = hover != Hover;
        Hover = hover;
        return changed;
    }

    /// <summary>Starts a stroke where the pointer is; false if it's off the map's plane or there's no tileset.</summary>
    internal bool BeginStroke(EditorCamera camera, Vector2 size, Vector2 at) =>
        CellAt(camera, size, at) is { } cell && BeginStroke(cell);

    /// <summary>Starts a stroke at a cell (the checks paint by cell); false without a tileset.</summary>
    internal bool BeginStroke((int X, int Y) cell)
    {
        if (!IsEditing || Tileset is null || Columns == 0)
        {
            return false;
        }
        _before = new Dictionary<(int, int), TileCell>(_data.Cells);
        _last = null;
        Hover = cell;
        ApplyAt(cell);
        return true;
    }

    internal void Stroke(EditorCamera camera, Vector2 size, Vector2 at)
    {
        if (CellAt(camera, size, at) is { } cell)
        {
            Stroke(cell);
        }
    }

    /// <summary>A stroke dragged on to a cell: Paint and Erase apply there too.</summary>
    internal void Stroke((int X, int Y) cell)
    {
        if (_before is not null)
        {
            Hover = cell;
            if (Tool is TileTool.Paint or TileTool.Erase)
            {
                ApplyAt(cell);
            }
        }
    }

    /// <summary>Ends a stroke: its changes one undo step, if it changed anything.</summary>
    internal void EndStroke()
    {
        if (_before is not { } before)
        {
            return;
        }
        _before = null;
        _last = null;
        if (before.Count == _data.Cells.Count && before.All(pair => _data.Cells.TryGetValue(pair.Key, out TileCell now) && now == pair.Value))
        {
            return;
        }
        string title = Tool switch { TileTool.Erase => "Erase Tiles", TileTool.Fill => "Fill Tiles", _ => "Paint Tiles" };
        _shell.History.Push(new TileEdit(this, title, _map, before, new Dictionary<(int, int), TileCell>(_data.Cells)));
    }

    // The tool at a cell: once a cell, so a drag doesn't paint a cell again.
    private void ApplyAt((int X, int Y) cell)
    {
        if (_last == cell)
        {
            return;
        }
        _last = cell;
        bool changed = Tool switch
        {
            TileTool.Paint => Paint(cell),
            TileTool.Erase => Erase(cell),
            TileTool.Fill => Fill(cell),
            _ => Pick(cell),
        };
        if (changed)
        {
            Write(_map, _data.Cells);
        }
    }

    private int Flags => (FlipX ? 1 : 0) | (FlipY ? 2 : 0);

    // The brush's width and height in cells.
    private (int Width, int Height) BrushSize => (Brush.Max(b => b.Dx) + 1, Brush.Max(b => b.Dy) + 1);

    // A brush entry's place, mirrored with the flips so a flipped brush reads as flipped art.
    private (int X, int Y) Placed((int X, int Y) cell, (int Dx, int Dy, int Tile) entry)
    {
        (int width, int height) = BrushSize;
        return (cell.X + (FlipX ? width - 1 - entry.Dx : entry.Dx), cell.Y + (FlipY ? height - 1 - entry.Dy : entry.Dy));
    }

    private bool Paint((int X, int Y) cell)
    {
        bool changed = false;
        foreach ((int Dx, int Dy, int Tile) entry in Brush)
        {
            (int x, int y) = Placed(cell, entry);
            TileCell painted = new(x, y, entry.Tile, Flags);
            if (!_data.Cells.TryGetValue((x, y), out TileCell was) || was != painted)
            {
                _data.Cells[(x, y)] = painted;
                changed = true;
            }
        }
        return changed;
    }

    private bool Erase((int X, int Y) cell)
    {
        bool changed = false;
        foreach ((int Dx, int Dy, int Tile) entry in Brush)
        {
            changed |= _data.Cells.Remove(Placed(cell, entry));
        }
        return changed;
    }

    // The cells joined to this one (left, right, up, down) with its tile (or
    // none), the brush repeated over them; an empty area only as far as the
    // map's painted bounds, so it can't run on forever.
    private bool Fill((int X, int Y) start)
    {
        if (_data.Cells.Count == 0)
        {
            _shell.Log("Fill needs painted cells around the area to fill");
            return false;
        }
        int minX = _data.Cells.Keys.Min(c => c.X), maxX = _data.Cells.Keys.Max(c => c.X);
        int minY = _data.Cells.Keys.Min(c => c.Y), maxY = _data.Cells.Keys.Max(c => c.Y);
        if (start.X < minX || start.X > maxX || start.Y < minY || start.Y > maxY)
        {
            return false;
        }
        int? target = _data.Cells.TryGetValue(start, out TileCell first) ? first.Tile : null;
        (int width, int height) = BrushSize;
        Dictionary<(int, int), int> pattern = Brush.ToDictionary(b => (b.Dx, b.Dy), b => b.Tile);
        HashSet<(int, int)> seen = [start];
        Queue<(int X, int Y)> queue = new([start]);
        bool changed = false;
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            int dx = ((x - start.X) % width + width) % width, dy = ((y - start.Y) % height + height) % height;
            dx = FlipX ? width - 1 - dx : dx;
            dy = FlipY ? height - 1 - dy : dy;
            if (pattern.TryGetValue((dx, dy), out int tile))
            {
                TileCell painted = new(x, y, tile, Flags);
                changed |= !_data.Cells.TryGetValue((x, y), out TileCell was) || was != painted;
                _data.Cells[(x, y)] = painted;
            }
            foreach ((int X, int Y) next in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY || !seen.Add(next))
                {
                    continue;
                }
                int? there = _before!.TryGetValue(next, out TileCell cell) ? cell.Tile : null;
                if (there == target)
                {
                    queue.Enqueue(next);
                }
            }
        }
        return changed;
    }

    private bool Pick((int X, int Y) cell)
    {
        if (_data.Cells.TryGetValue(cell, out TileCell picked))
        {
            Brush = [(0, 0, picked.Tile)];
            FlipX = (picked.Flags & 1) != 0;
            FlipY = (picked.Flags & 2) != 0;
            Tool = TileTool.Paint;
            BrushChanged?.Invoke();
        }
        return false;
    }

    // Undo and redo: the cells written, and shown if it's the map being painted.
    internal void Apply(AssetId map, Dictionary<(int X, int Y), TileCell> cells)
    {
        if (Write(map, cells) && IsEditing && map == _map && _before is null)
        {
            _data.Cells = new Dictionary<(int, int), TileCell>(cells);
        }
    }

    // The .crtilemap's cells replaced (its tileset kept), and imported at once.
    private bool Write(AssetId map, Dictionary<(int X, int Y), TileCell> cells)
    {
        if (FileOf(map) is not { } file)
        {
            return false;
        }
        try
        {
            TilemapData data = TilemapImporter.Read(File.ReadAllBytes(file));
            data.Cells = new Dictionary<(int, int), TileCell>(cells);
            File.WriteAllText(file, TilemapImporter.Format(data));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or
                                              System.Text.Json.JsonException)
        {
            _shell.Log($"Can't save {Path.GetFileName(file)}: {exception.Message}");
            return false;
        }
        _shell.Reimport(map, quiet: true);
        return true;
    }

    /// <summary>The brush's footprint at the cell under the pointer, over a viewport.</summary>
    internal void Draw(Canvas canvas, EditorCamera camera, Vector2 size)
    {
        if (Hover is not { } cell || !Frame(out Matrix4x4 world))
        {
            return;
        }
        Vector2? Shown(float x, float y) => camera.Project(Vector3.Transform(new Vector3(x * CellSize.X, y * CellSize.Y, 0), world), size);
        Vector4 color = Tool == TileTool.Erase ? _erase : _brush;
        IEnumerable<(int X, int Y)> cells = Tool is TileTool.Paint or TileTool.Erase
            ? Brush.Select(entry => Placed(cell, entry))
            : [cell];
        foreach ((int x, int y) in cells)
        {
            if (Shown(x, y) is { } a && Shown(x + 1, y) is { } b && Shown(x + 1, y + 1) is { } c && Shown(x, y + 1) is { } d)
            {
                canvas.DrawPolyline([a, b, c, d, a], 3.5f, _edge);
                canvas.DrawPolyline([a, b, c, d, a], 1.5f, color);
            }
        }
    }

    // One stroke: the cells before and after, written back on undo and redo.
    private sealed class TileEdit(TilePainter painter, string title, AssetId map, Dictionary<(int X, int Y), TileCell> before,
        Dictionary<(int X, int Y), TileCell> after) : UndoStep(title)
    {
        public override bool ChangesScene => false;

        public override void Undo(World world) => painter.Apply(map, before);

        public override void Redo(World world) => painter.Apply(map, after);
    }
}
