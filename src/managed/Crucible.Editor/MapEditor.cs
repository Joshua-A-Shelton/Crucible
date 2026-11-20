using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Ecs;
using Crucible.Importers;
using Crucible.Render;
using Crucible.Render2D;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>Map mode's tools (ADR 0112), each with its key.</summary>
public enum MapTool
{
    Stamp,     // B: the brush's tiles where it's pressed and dragged
    Rectangle, // R: the rectangle dragged out, the brush repeated over it
    Fill,      // F: the cells joined to the one pressed that have its tile (or none), the brush repeated over them
    Eraser,    // E: the cells under the brush's footprint emptied
    Select,    // S: a rectangle of cells to copy, cut, delete, or drag somewhere else
}

/// <summary>A brush's tile: where in the brush (x right, y up from its bottom-left), from which tileset, its flips.</summary>
public readonly record struct BrushTile(int Dx, int Dy, int Set, int Tile, int Flags = 0);

/// <summary>One of the map's tilesets as map mode shows it: its file read, its grid, and its texture.</summary>
internal sealed class MapTileset
{
    public AssetId Id;
    public string Path = "";
    public TilesetData Data = new();
    public int Columns;
    public int Count;
    public float PixelsPerUnit = 16;
    public TextureHandle Texture;
}

/// <summary>
/// Map mode's editing (ADR 0112): the map an entity's children are the
/// layers of (<see cref="TilemapLayers"/>), its file and its tilesets read,
/// a layer being edited, and the tools applied in the viewports cell by cell
/// on the map's XY plane. The left button uses the tool; the right button
/// dragged over painted cells picks them up as the brush; while the pointer
/// is over the map, the brush shows faintly under it and the grid around
/// the map. Each change saves the .crtilemap and imports it, so the map and
/// its collision follow; a stroke, a move, a paste is one undo step.
/// </summary>
internal sealed class MapEditor
{
    private static readonly Vector4 _edge = new(0.08f, 0.08f, 0.1f, 0.85f), _brush = new(0.32f, 0.52f, 0.98f, 1);
    private static readonly Vector4 _erase = new(0.95f, 0.35f, 0.3f, 1), _gridLine = new(1, 1, 1, 0.12f);
    private static readonly Vector4 _ghost = new(1, 1, 1, 0.55f), _region = new(1f, 0.85f, 0.2f, 1);
    private static readonly Vector4 _collision = new(0.35f, 0.95f, 0.45f, 0.95f);

    private readonly Shell _shell;
    private string? _before; // the file before a stroke
    private (int X, int Y)? _last, _from;
    private (int X, int Y)? _moveFrom; // a Select drag moving the region from here
    private Dictionary<(int X, int Y), TileCell>? _lifted; // the cells being moved
    private (int X, int Y)? _pickFrom;
    private List<List<Vector2>>? _outlines; // Show Collision's, by the colliding layers; null when stale

    public MapEditor(Shell shell)
    {
        _shell = shell;
        _shell.AssetsChanged += () =>
        {
            if (IsOpen && _before is null)
            {
                Read(); // changed outside, or by undo
            }
        };
    }

    /// <summary>The map entity: its children are the layers.</summary>
    public Entity Root { get; private set; }

    /// <summary>The .crtilemap's asset ID.</summary>
    public AssetId Map { get; private set; }

    public TilemapData Data { get; private set; } = new();

    public List<MapTileset> Tilesets { get; } = [];

    /// <summary>A map is open in map mode.</summary>
    public bool IsOpen => Root.Value != 0 && _shell.HasWorld && _shell.World.IsAlive(Root);

    /// <summary>The layer the tools apply to, by its id.</summary>
    public int ActiveLayer { get; set; }

    public TileLayer? Layer => Data.Layer(ActiveLayer);

    public MapTool Tool { get; set; } = MapTool.Stamp;

    public IReadOnlyList<BrushTile> Brush { get; private set; } = [];

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    /// <summary>The colliding layers' outlines over the map.</summary>
    public bool ShowCollision { get; set; }

    /// <summary>The Select tool's rectangle of cells, corners inclusive; null for none.</summary>
    public (int X0, int Y0, int X1, int Y1)? Region { get; set; }

    /// <summary>The cell under the pointer; null off the map's plane.</summary>
    public (int X, int Y)? Hover { get; private set; }

    /// <summary>A cell's size in meters: the map's tiles' pixels over their textures' pixels per unit.</summary>
    public Vector2 CellSize => Tilesets.Count > 0
        ? new Vector2(Tilesets[0].Data.TileWidth, Tilesets[0].Data.TileHeight) / Tilesets[0].PixelsPerUnit
        : Vector2.One;

    /// <summary>The map's tile size in pixels, from its first tileset; none before it has one.</summary>
    public (int Width, int Height)? TileSize => Tilesets.Count > 0 ? (Tilesets[0].Data.TileWidth, Tilesets[0].Data.TileHeight) : null;

    /// <summary>Raised when the brush or the tool changes other than by the toolbar (a pick, a copy, a key).</summary>
    public event Action? BrushChanged;

    /// <summary>Opens a map entity's map; false if its file can't be read.</summary>
    public bool Open(Entity root, AssetId map)
    {
        Close();
        Root = root;
        Map = map;
        if (!Read())
        {
            Close();
            return false;
        }
        ActiveLayer = Data.Layers.Count > 0 ? Data.Layers[^1].Id : 0;
        Brush = [];
        BrushChanged?.Invoke();
        return true;
    }

    public void Close()
    {
        if (_before is not null)
        {
            EndStroke();
        }
        foreach (MapTileset tileset in Tilesets)
        {
            ReleaseTexture(tileset);
        }
        Tilesets.Clear();
        Root = Entity.Null;
        Map = AssetId.None;
        Data = new TilemapData();
        Region = null;
        Hover = null;
        _outlines = null;
    }

    private string? FileOf(AssetId id) =>
        _shell.Project is { } project && _shell.Database?.Find(id) is { } entry ? Path.Combine(project.AssetsFolder, entry.Path) : null;

    /// <summary>The map's file and its tilesets read again; false (said) if the map can't be read.</summary>
    public bool Read()
    {
        if (FileOf(Map) is not { } file)
        {
            return false;
        }
        try
        {
            Data = TilemapImporter.Read(File.ReadAllBytes(file));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _shell.Log($"Can't edit {Path.GetFileName(file)}: {exception.Message}");
            return false;
        }
        _outlines = null;
        // The tilesets kept, read again, or let go.
        Dictionary<AssetId, MapTileset> had = Tilesets.ToDictionary(tileset => tileset.Id);
        Tilesets.Clear();
        foreach (AssetId id in Data.Tilesets)
        {
            MapTileset tileset = had.Remove(id, out MapTileset? kept) ? kept : new MapTileset { Id = id };
            ReadTileset(tileset);
            Tilesets.Add(tileset);
        }
        foreach (MapTileset gone in had.Values)
        {
            ReleaseTexture(gone);
        }
        if (Data.Layer(ActiveLayer) is null && Data.Layers.Count > 0)
        {
            ActiveLayer = Data.Layers[^1].Id;
        }
        return true;
    }

    // A tileset's file, grid, and texture; left empty (said) if they can't be read.
    private void ReadTileset(MapTileset tileset)
    {
        tileset.Path = _shell.Database?.Find(tileset.Id)?.Path ?? "";
        tileset.Columns = tileset.Count = 0;
        try
        {
            if (FileOf(tileset.Id) is { } path)
            {
                tileset.Data = TilesetImporter.Read(File.ReadAllBytes(path));
                (int width, int height, float pixelsPerUnit) = TextureOf(tileset.Data.Texture);
                (int columns, int rows) = tileset.Data.Grid(width, height);
                tileset.Columns = columns;
                tileset.Count = columns * rows;
                tileset.PixelsPerUnit = pixelsPerUnit;
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            _shell.Log($"Can't read the tileset {tileset.Path}: {exception.Message}");
        }
    }

    /// <summary>A texture's size, from its imported file, and its pixels per unit, from its settings.</summary>
    internal (int Width, int Height, float PixelsPerUnit) TextureOf(AssetId texture)
    {
        if (texture.IsNone || _shell.Database?.ImportedFile(texture) is not { } path)
        {
            return (0, 0, 16);
        }
        try
        {
            using ImportedFile file = ImportedFile.Open(path, TextureImporter.Kind, TextureImporter.KindVersion);
            TextureInfo info = MemoryMarshal.Read<TextureInfo>(file.Read(file.Find(TextureImporter.InfoSection)));
            float pixelsPerUnit = _shell.Database.SettingsOf(texture)?["PixelsPerUnit"]?.GetValue<float>() ?? 16;
            return ((int)info.Width, (int)info.Height, pixelsPerUnit > 0 ? pixelsPerUnit : 16);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return (0, 0, 16);
        }
    }

    /// <summary>Each frame: each tileset's texture acquired once it's imported, and let go if it changed.</summary>
    internal void Tick()
    {
        if (!IsOpen)
        {
            if (Root.Value != 0)
            {
                Close();
            }
            return;
        }
        foreach (MapTileset tileset in Tilesets)
        {
            if (tileset.Texture.Generation == 0 && !tileset.Data.Texture.IsNone && _shell.Database?.ImportedFile(tileset.Data.Texture) is not null)
            {
                tileset.Texture = RenderAssets.AcquireTexture(tileset.Data.Texture);
                if (tileset.Columns == 0)
                {
                    ReadTileset(tileset);
                }
            }
        }
    }

    private static void ReleaseTexture(MapTileset tileset)
    {
        if (tileset.Texture.Generation != 0)
        {
            RenderAssets.ReleaseTexture(tileset.Texture);
            tileset.Texture = default;
        }
    }

    /// <summary>The brush from a palette selection: a tileset's tiles by column and row, corners inclusive.</summary>
    public void SetBrush(int set, int x0, int y0, int x1, int y1)
    {
        if (set < 0 || set >= Tilesets.Count || Tilesets[set].Columns == 0)
        {
            return;
        }
        int columns = Tilesets[set].Columns;
        List<BrushTile> brush = [];
        for (int y = y0; y <= y1; ++y)
        {
            for (int x = x0; x <= x1; ++x)
            {
                brush.Add(new BrushTile(x - x0, y1 - y, set, y * columns + x)); // the palette's rows run down; the map's up
            }
        }
        Brush = brush;
        if (Tool is MapTool.Eraser or MapTool.Select)
        {
            Tool = MapTool.Stamp;
        }
        Region = null;
    }

    private bool Frame(out Matrix4x4 world)
    {
        world = Matrix4x4.Identity;
        if (!IsOpen)
        {
            return false;
        }
        world = _shell.World.ComputeWorldTransform(Root);
        return true;
    }

    /// <summary>The map's middle and size in the world, to frame it: its painted cells (or a few around the origin).</summary>
    public (Vector3 Center, float Radius) Bounds()
    {
        (int minX, int minY, int maxX, int maxY) = PaintedBounds() ?? (-8, -5, 8, 5);
        Frame(out Matrix4x4 world);
        Vector3 a = Vector3.Transform(new Vector3(minX * CellSize.X, minY * CellSize.Y, 0), world);
        Vector3 b = Vector3.Transform(new Vector3((maxX + 1) * CellSize.X, (maxY + 1) * CellSize.Y, 0), world);
        return ((a + b) * 0.5f, MathF.Max(Vector3.Distance(a, b) * 0.5f, 1));
    }

    // Every layer's painted cells' bounds; null for an empty map.
    private (int MinX, int MinY, int MaxX, int MaxY)? PaintedBounds()
    {
        IEnumerable<(int X, int Y)> cells = Data.Layers.SelectMany(layer => layer.Cells.Keys);
        return cells.Any() ? (cells.Min(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.X), cells.Max(c => c.Y)) : null;
    }

    // The cell under a place in a viewport, where the pointer's ray meets the map's plane.
    internal (int X, int Y)? CellAt(EditorCamera camera, Vector2 size, Vector2 at)
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
    internal bool Track((int X, int Y)? cell)
    {
        bool changed = cell != Hover;
        Hover = cell;
        return changed;
    }

    // -----------------------------------------------------------------------
    // Strokes: the left button
    // -----------------------------------------------------------------------

    /// <summary>
    /// Starts a stroke at a cell; false (said) if there's nothing to do it
    /// with: no layer, no tileset, or no brush for a tool that paints.
    /// </summary>
    internal bool BeginStroke((int X, int Y) cell)
    {
        if (!IsOpen || Layer is not { } layer)
        {
            return false;
        }
        if (Tool is MapTool.Stamp or MapTool.Rectangle or MapTool.Fill && Brush.Count == 0)
        {
            _shell.Status(Tilesets.Count == 0
                ? "Add a tileset first: the + tab in the Palette"
                : "Choose a tile in the Palette first (or right-drag over painted tiles to pick them up)");
            return false;
        }
        _before = TilemapImporter.Format(Data);
        _last = null;
        Hover = cell;
        switch (Tool)
        {
            case MapTool.Rectangle:
                _from = cell;
                break;
            case MapTool.Select when Region is { } region && Inside(region, cell):
                // Dragging the region: its cells lifted, to be put down where it's let go.
                _moveFrom = cell;
                _lifted = [];
                for (int y = region.Y0; y <= region.Y1; ++y)
                {
                    for (int x = region.X0; x <= region.X1; ++x)
                    {
                        if (layer.Cells.Remove((x, y), out TileCell lifted))
                        {
                            _lifted[(x, y)] = lifted;
                        }
                    }
                }
                break;
            case MapTool.Select:
                _from = cell;
                Region = (cell.X, cell.Y, cell.X, cell.Y);
                break;
            default:
                ApplyAt(cell);
                break;
        }
        return true;
    }

    private static bool Inside((int X0, int Y0, int X1, int Y1) region, (int X, int Y) cell) =>
        cell.X >= region.X0 && cell.X <= region.X1 && cell.Y >= region.Y0 && cell.Y <= region.Y1;

    /// <summary>A stroke dragged on to a cell.</summary>
    internal void Stroke((int X, int Y) cell)
    {
        if (_before is null)
        {
            return;
        }
        Hover = cell;
        if (Tool is MapTool.Stamp or MapTool.Eraser)
        {
            ApplyAt(cell);
        }
        else if (Tool == MapTool.Select && _moveFrom is null && _from is { } from)
        {
            Region = (Math.Min(from.X, cell.X), Math.Min(from.Y, cell.Y), Math.Max(from.X, cell.X), Math.Max(from.Y, cell.Y));
        }
    }

    /// <summary>Ends a stroke (a Rectangle filled, a moved region put down): its changes one undo step, if it changed anything.</summary>
    internal void EndStroke()
    {
        if (_before is not { } before)
        {
            return;
        }
        if (Layer is { } layer)
        {
            if (Tool == MapTool.Rectangle && _from is { } from && Hover is { } to)
            {
                FillRectangle(layer, from, to);
            }
            else if (Tool == MapTool.Select && _moveFrom is { } start && _lifted is { } lifted && Region is { } region)
            {
                (int dx, int dy) = Hover is { } end ? (end.X - start.X, end.Y - start.Y) : (0, 0);
                foreach (TileCell cell in lifted.Values)
                {
                    layer.Cells[(cell.X + dx, cell.Y + dy)] = cell with { X = cell.X + dx, Y = cell.Y + dy };
                }
                Region = (region.X0 + dx, region.Y0 + dy, region.X1 + dx, region.Y1 + dy);
            }
        }
        _from = _moveFrom = _last = null;
        _lifted = null;
        _before = null;
        string title = Tool switch
        {
            MapTool.Eraser => "Erase Tiles",
            MapTool.Fill => "Fill Tiles",
            MapTool.Rectangle => "Paint Rectangle",
            MapTool.Select => "Move Tiles",
            _ => "Paint Tiles",
        };
        Commit(before, title);
    }

    /// <summary>The map as it is now saved and imported, one undo step from <paramref name="before"/>; false if nothing changed.</summary>
    internal bool Commit(string before, string title)
    {
        string after = TilemapImporter.Format(Data);
        if (after == before || _shell.Database?.Find(Map)?.Path is not { } path)
        {
            return false;
        }
        Write(after);
        _shell.PushFileEdit(title, path, before, after);
        return true;
    }

    // The tool at a cell: once a cell, so a drag doesn't apply to a cell again.
    private void ApplyAt((int X, int Y) cell)
    {
        if (_last == cell || Layer is not { } layer)
        {
            return;
        }
        _last = cell;
        bool changed = Tool switch
        {
            MapTool.Stamp => Stamp(layer, cell),
            MapTool.Eraser => Erase(layer, cell),
            MapTool.Fill => Fill(layer, cell),
            _ => false,
        };
        if (changed)
        {
            Write(TilemapImporter.Format(Data));
        }
    }

    private int Flags(BrushTile tile) => tile.Flags ^ ((FlipX ? 1 : 0) | (FlipY ? 2 : 0));

    // The brush's width and height in cells.
    private (int Width, int Height) BrushSize => Brush.Count == 0 ? (1, 1) : (Brush.Max(b => b.Dx) + 1, Brush.Max(b => b.Dy) + 1);

    // A brush tile's place, mirrored with the flips so a flipped brush reads as flipped art.
    private (int X, int Y) Placed((int X, int Y) cell, BrushTile tile)
    {
        (int width, int height) = BrushSize;
        return (cell.X + (FlipX ? width - 1 - tile.Dx : tile.Dx), cell.Y + (FlipY ? height - 1 - tile.Dy : tile.Dy));
    }

    private bool Stamp(TileLayer layer, (int X, int Y) cell)
    {
        bool changed = false;
        foreach (BrushTile tile in Brush)
        {
            (int x, int y) = Placed(cell, tile);
            changed |= Put(layer, new TileCell(x, y, tile.Tile, Flags(tile), tile.Set));
        }
        return changed;
    }

    private static bool Put(TileLayer layer, TileCell cell)
    {
        if (layer.Cells.TryGetValue((cell.X, cell.Y), out TileCell was) && was == cell)
        {
            return false;
        }
        layer.Cells[(cell.X, cell.Y)] = cell;
        return true;
    }

    private bool Erase(TileLayer layer, (int X, int Y) cell)
    {
        bool changed = false;
        foreach (BrushTile tile in Brush.Count > 0 ? Brush : [new BrushTile(0, 0, 0, 0)])
        {
            changed |= layer.Cells.Remove(Placed(cell, tile));
        }
        return changed;
    }

    // The brush's tile for a cell when it's repeated from `origin`, mirrored with the flips.
    private BrushTile? PatternAt((int X, int Y) origin, int x, int y)
    {
        (int width, int height) = BrushSize;
        int dx = ((x - origin.X) % width + width) % width, dy = ((y - origin.Y) % height + height) % height;
        dx = FlipX ? width - 1 - dx : dx;
        dy = FlipY ? height - 1 - dy : dy;
        foreach (BrushTile tile in Brush)
        {
            if (tile.Dx == dx && tile.Dy == dy)
            {
                return tile;
            }
        }
        return null;
    }

    // Rectangle: the cells between two corners, the brush repeated over them from the first.
    private bool FillRectangle(TileLayer layer, (int X, int Y) a, (int X, int Y) b)
    {
        bool changed = false;
        for (int y = Math.Min(a.Y, b.Y); y <= Math.Max(a.Y, b.Y); ++y)
        {
            for (int x = Math.Min(a.X, b.X); x <= Math.Max(a.X, b.X); ++x)
            {
                if (PatternAt(a, x, y) is { } tile)
                {
                    changed |= Put(layer, new TileCell(x, y, tile.Tile, Flags(tile), tile.Set));
                }
            }
        }
        return changed;
    }

    // The cells joined to this one (left, right, up, down) with its tile (or
    // none), the brush repeated over them; an empty area only as far as one
    // cell beyond the map's painted bounds (every layer's), so it can't run
    // on forever.
    private bool Fill(TileLayer layer, (int X, int Y) start)
    {
        if (PaintedBounds() is not var (minX, minY, maxX, maxY))
        {
            _shell.Status("Fill fills around painted tiles: paint some first, or drag out a Rectangle (R)");
            return false;
        }
        (minX, minY, maxX, maxY) = (minX - 1, minY - 1, maxX + 1, maxY + 1);
        if (start.X < minX || start.X > maxX || start.Y < minY || start.Y > maxY)
        {
            return false;
        }
        Dictionary<(int, int), TileCell> before = new(layer.Cells);
        (int Set, int Tile)? target = before.TryGetValue(start, out TileCell first) ? (first.Set, first.Tile) : null;
        HashSet<(int, int)> seen = [start];
        Queue<(int X, int Y)> queue = new([start]);
        bool changed = false;
        while (queue.Count > 0)
        {
            (int x, int y) = queue.Dequeue();
            if (PatternAt(start, x, y) is { } tile)
            {
                changed |= Put(layer, new TileCell(x, y, tile.Tile, Flags(tile), tile.Set));
            }
            foreach ((int X, int Y) next in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (next.X < minX || next.X > maxX || next.Y < minY || next.Y > maxY || !seen.Add(next))
                {
                    continue;
                }
                (int, int)? there = before.TryGetValue(next, out TileCell cell) ? (cell.Set, cell.Tile) : null;
                if (there == target)
                {
                    queue.Enqueue(next);
                }
            }
        }
        return changed;
    }

    // -----------------------------------------------------------------------
    // Picking up: the right button
    // -----------------------------------------------------------------------

    internal void BeginPick((int X, int Y) cell)
    {
        _pickFrom = cell;
        Hover = cell;
    }

    internal bool IsPicking => _pickFrom is not null;

    /// <summary>The picked rectangle's painted cells (of the layer being edited) become the brush, Stamp the tool.</summary>
    internal void EndPick()
    {
        if (_pickFrom is not { } from || Hover is not { } to || Layer is not { } layer)
        {
            _pickFrom = null;
            return;
        }
        _pickFrom = null;
        if (TakeBrush(layer, (Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Max(from.X, to.X), Math.Max(from.Y, to.Y))))
        {
            Tool = MapTool.Stamp;
            FlipX = FlipY = false;
            Region = null;
            BrushChanged?.Invoke();
        }
    }

    // A rectangle's painted cells as the brush, by their place in it; false if it has none.
    private bool TakeBrush(TileLayer layer, (int X0, int Y0, int X1, int Y1) area)
    {
        List<BrushTile> brush = [];
        for (int y = area.Y0; y <= area.Y1; ++y)
        {
            for (int x = area.X0; x <= area.X1; ++x)
            {
                if (layer.Cells.TryGetValue((x, y), out TileCell cell))
                {
                    brush.Add(new BrushTile(x - area.X0, y - area.Y0, cell.Set, cell.Tile, cell.Flags));
                }
            }
        }
        if (brush.Count == 0)
        {
            return false;
        }
        // Its bottom-left at the brush's: the pointer's cell is its first painted one.
        int left = brush.Min(b => b.Dx), bottom = brush.Min(b => b.Dy);
        Brush = [.. brush.Select(b => b with { Dx = b.Dx - left, Dy = b.Dy - bottom })];
        return true;
    }

    // -----------------------------------------------------------------------
    // The Select tool's region: copy, cut, delete; paste is the brush
    // -----------------------------------------------------------------------

    /// <summary>The region's painted cells become the brush (Ctrl+C), Stamp the tool so they can be put down.</summary>
    public bool Copy()
    {
        if (Region is not { } region || Layer is not { } layer || !TakeBrush(layer, region))
        {
            return false;
        }
        Tool = MapTool.Stamp;
        FlipX = FlipY = false;
        Region = null;
        BrushChanged?.Invoke();
        return true;
    }

    /// <summary>Copy, then the region's cells emptied (Ctrl+X), as one undo step.</summary>
    public bool Cut()
    {
        (int, int, int, int)? region = Region;
        if (!Copy())
        {
            return false;
        }
        Region = region;
        DeleteRegion("Cut Tiles");
        Region = null;
        return true;
    }

    /// <summary>The region's cells emptied (Delete), as one undo step.</summary>
    public bool DeleteRegion(string title = "Delete Tiles")
    {
        if (Region is not { } region || Layer is not { } layer)
        {
            return false;
        }
        string before = TilemapImporter.Format(Data);
        for (int y = region.Y0; y <= region.Y1; ++y)
        {
            for (int x = region.X0; x <= region.X1; ++x)
            {
                layer.Cells.Remove((x, y));
            }
        }
        return Commit(before, title);
    }

    // -----------------------------------------------------------------------
    // Saving
    // -----------------------------------------------------------------------

    // The .crtilemap written, and imported at once so the map and its collision follow.
    private void Write(string text)
    {
        if (FileOf(Map) is not { } file)
        {
            return;
        }
        try
        {
            File.WriteAllText(file, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _shell.Log($"Can't save {Path.GetFileName(file)}: {exception.Message}");
            return;
        }
        _outlines = null;
        _shell.Reimport(Map, quiet: true);
    }

    /// <summary>A change to the map other than its cells (its tilesets, its layers): made to the data, saved, one undo step.</summary>
    internal bool Change(string title, Action<TilemapData> change)
    {
        if (!IsOpen)
        {
            return false;
        }
        string before = TilemapImporter.Format(Data);
        change(Data);
        if (!Commit(before, title))
        {
            return false;
        }
        Read();
        return true;
    }

    // -----------------------------------------------------------------------
    // Drawing over a viewport
    // -----------------------------------------------------------------------

    /// <summary>
    /// Over a viewport: the grid around the map; the brush's tiles faintly at
    /// the cell under the pointer with its outline (red for the Eraser; a
    /// Rectangle's whole rectangle while it's dragged out); the Select tool's
    /// region and a picked rectangle; the colliding layers' outlines if
    /// they're shown.
    /// </summary>
    internal void Draw(Canvas canvas, EditorCamera camera, Vector2 size)
    {
        if (!Frame(out Matrix4x4 world))
        {
            return;
        }
        Vector2? Shown(float x, float y) => camera.Project(Vector3.Transform(new Vector3(x * CellSize.X, y * CellSize.Y, 0), world), size);
        DrawGrid(canvas, Shown);
        if (ShowCollision)
        {
            DrawCollision(canvas, camera, size, world);
        }
        if (Region is { } region)
        {
            (int dx, int dy) = _moveFrom is { } start && Hover is { } at ? (at.X - start.X, at.Y - start.Y) : (0, 0);
            Outline(canvas, Shown, region.X0 + dx, region.Y0 + dy, region.X1 + dx, region.Y1 + dy, _region);
            if (_lifted is { } lifted)
            {
                foreach (TileCell cell in lifted.Values)
                {
                    Ghost(canvas, Shown, cell.X + dx, cell.Y + dy, cell.Set, cell.Tile, cell.Flags);
                }
            }
        }
        if (Hover is not { } hover)
        {
            return;
        }
        if (_pickFrom is { } pick)
        {
            Outline(canvas, Shown, Math.Min(pick.X, hover.X), Math.Min(pick.Y, hover.Y), Math.Max(pick.X, hover.X),
                Math.Max(pick.Y, hover.Y), _region);
            return;
        }
        List<(int X, int Y, BrushTile? Tile)> cells = [];
        if (Tool == MapTool.Rectangle && _from is { } from)
        {
            for (int y = Math.Min(from.Y, hover.Y); y <= Math.Max(from.Y, hover.Y); ++y)
            {
                for (int x = Math.Min(from.X, hover.X); x <= Math.Max(from.X, hover.X); ++x)
                {
                    cells.Add((x, y, PatternAt(from, x, y)));
                }
            }
        }
        else if (Tool is MapTool.Stamp or MapTool.Rectangle or MapTool.Eraser or MapTool.Fill && Brush.Count > 0)
        {
            foreach (BrushTile tile in Tool == MapTool.Fill ? [Brush[0] with { Dx = 0, Dy = 0 }] : Brush)
            {
                (int x, int y) = Placed(hover, tile);
                cells.Add((x, y, Tool == MapTool.Eraser ? null : tile));
            }
        }
        else if (Tool != MapTool.Select)
        {
            cells.Add((hover.X, hover.Y, null));
        }
        Vector4 color = Tool == MapTool.Eraser ? _erase : _brush;
        foreach ((int x, int y, BrushTile? tile) in cells)
        {
            if (tile is { } t)
            {
                Ghost(canvas, Shown, x, y, t.Set, t.Tile, Flags(t));
            }
            Outline(canvas, Shown, x, y, x, y, color);
        }
    }

    private static void Outline(Canvas canvas, Func<float, float, Vector2?> shown, int x0, int y0, int x1, int y1, Vector4 color)
    {
        if (shown(x0, y0) is not { } a || shown(x1 + 1, y0) is not { } b || shown(x1 + 1, y1 + 1) is not { } c ||
            shown(x0, y1 + 1) is not { } d)
        {
            return;
        }
        canvas.DrawPolyline([a, b, c, d, a], 3.5f, _edge);
        canvas.DrawPolyline([a, b, c, d, a], 1.5f, color);
    }

    // A tile drawn faintly in a cell, mirrored with its flips.
    private void Ghost(Canvas canvas, Func<float, float, Vector2?> shown, int x, int y, int set, int tile, int flags)
    {
        if (UvOf(set, tile, flags) is not { } uv || shown(x, y) is not { } a || shown(x + 1, y) is not { } b ||
            shown(x + 1, y + 1) is not { } c || shown(x, y + 1) is not { } d)
        {
            return;
        }
        Vector2 min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)), max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        canvas.DrawImage(Tilesets[set].Texture, min, max - min, uv, _ghost, default, pixelated: true);
    }

    /// <summary>A tile's place in its texture as uv, mirrored with flips; null before the texture loads.</summary>
    internal Vector4? UvOf(int set, int tile, int flags)
    {
        if (set < 0 || set >= Tilesets.Count || Tilesets[set] is not { Texture.Generation: not 0, Columns: > 0 } tileset)
        {
            return null;
        }
        TextureSize size = Render.Api.TextureSize(tileset.Texture);
        if (size.Width == 0 || size.Height == 0)
        {
            return null;
        }
        (int px, int py) = tileset.Data.PixelOf(tile, tileset.Columns);
        float u0 = px / (float)size.Width, v0 = py / (float)size.Height;
        float u1 = (px + tileset.Data.TileWidth) / (float)size.Width, v1 = (py + tileset.Data.TileHeight) / (float)size.Height;
        bool x = (flags & 1) != 0, y = (flags & 2) != 0;
        return new Vector4(x ? u1 : u0, y ? v1 : v0, x ? u0 : u1, y ? v0 : v1);
    }

    // Faint lines along the cells over the painted area and eight cells around it.
    private void DrawGrid(Canvas canvas, Func<float, float, Vector2?> shown)
    {
        (int minX, int minY, int maxX, int maxY) = PaintedBounds() is var (x0, y0, x1, y1) ? (x0 - 8, y0 - 8, x1 + 9, y1 + 9) : (-8, -8, 8, 8);
        if ((long)(maxX - minX) + (maxY - minY) > 2000)
        {
            return;
        }
        for (int x = minX; x <= maxX; ++x)
        {
            if (shown(x, minY) is { } a && shown(x, maxY) is { } b)
            {
                canvas.DrawLine(a, b, 1, _gridLine);
            }
        }
        for (int y = minY; y <= maxY; ++y)
        {
            if (shown(minX, y) is { } a && shown(maxX, y) is { } b)
            {
                canvas.DrawLine(a, b, 1, _gridLine);
            }
        }
    }

    /// <summary>The layers that collide: those whose entity has a Collider2D.</summary>
    internal IEnumerable<TileLayer> CollidingLayers() =>
        Data.Layers.Where(layer => _shell.LayerEntity(Root, layer.Id) is { } entity &&
                                   _shell.World.Has(entity, Components.Of<Crucible.Physics2D.Collider2D>()));

    // The colliding layers' outlines, as their maps' import merges them.
    private void DrawCollision(Canvas canvas, EditorCamera camera, Vector2 size, Matrix4x4 world)
    {
        if (TileSize is not var (width, height))
        {
            return;
        }
        _outlines ??= [.. CollidingLayers().SelectMany(layer => TilemapImporter.Outlines(width, height,
            cell => cell.Set < Tilesets.Count ? Tilesets[cell.Set].Data : new TilesetData(), layer.Cells.Values))];
        Vector2 perPixel = CellSize / new Vector2(width, height);
        foreach (List<Vector2> outline in _outlines)
        {
            List<Vector2> points = [];
            foreach (Vector2 p in outline.Append(outline[0]))
            {
                if (camera.Project(Vector3.Transform(new Vector3(p * perPixel, 0), world), size) is { } shown)
                {
                    points.Add(shown);
                }
            }
            if (points.Count > 1)
            {
                canvas.DrawPolyline([.. points], 2, _collision);
            }
        }
    }

    /// <summary>Show Collision's outlines are stale (a layer's collider changed).</summary>
    internal void CollisionChanged() => _outlines = null;
}
