using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Assets.Importers;

/// <summary>A tilemap has no import settings: everything is in the file.</summary>
[Serialized]
public partial class TilemapImportSettings;

/// <summary>
/// A painted cell: where, which tile of which of the map's tilesets
/// (<see cref="Set"/>, by its place in <see cref="TilemapData.Tilesets"/>),
/// and its flips (TileFlip bits).
/// </summary>
public readonly record struct TileCell(int X, int Y, int Tile, int Flags, int Set = 0);

/// <summary>
/// One layer of a map (ADR 0112): its <see cref="Id"/>, kept while it's
/// renamed or moved (a Tilemap component names the layer it draws by it),
/// its name, and its painted cells.
/// </summary>
public sealed class TileLayer
{
    public int Id;
    public string Name = "Layer 1";

    /// <summary>The painted cells by position; at most one each.</summary>
    public Dictionary<(int X, int Y), TileCell> Cells = [];

    public TileLayer Copy() => new() { Id = Id, Name = Name, Cells = new Dictionary<(int, int), TileCell>(Cells) };
}

/// <summary>
/// A tilemap as its file holds it (ADR 0108, 0112): its tilesets, all of one
/// tile size, and its layers, bottom first. A new one has one empty layer.
/// </summary>
public sealed class TilemapData
{
    public AssetId Id;

    public List<AssetId> Tilesets = [];

    public List<TileLayer> Layers = [new TileLayer()];

    /// <summary>The layer with an id; null if there's none.</summary>
    public TileLayer? Layer(int id) => Layers.Find(layer => layer.Id == id);

    /// <summary>An id no layer has: one more than the highest.</summary>
    public int NewLayerId => Layers.Count == 0 ? 0 : Layers.Max(layer => layer.Id) + 1;
}

/// <summary>
/// Imports tilemaps (ADR 0108, 0112): an authored <c>.crtilemap</c> (JSON
/// with its GUID inside) into a <c>.crtmap</c>: a TMAP file (TilemapFile.h)
/// with, layer by layer, each cell's place in its tileset's texture, and
/// collision baked from the tiles' shapes. The tilesets' files and their
/// textures are dependencies, so changing any imports the map again.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Tilesets": [ "…", … ],              the tilesets' asset IDs; a cell names one by its place here
///   "Layers": [                          bottom first
///     { "Id": 0, "Name": "Ground",
///       "Cells": [ [0, 0, 0, 5, 0], … ] } [x, y, tileset, tile, flips]: y up; flips 1 for X, 2 for Y
///   ]
/// }
/// </code>
/// A map from before layers ("Tileset" and "Cells" of [x, y, tile, flips])
/// reads as one layer, "Layer 1", of id 0. Every tileset of a map has its
/// tile size, and every texture its pixels per unit, so the map is one grid.
/// Collision, layer by layer: every painted cell's shapes, mirrored with the
/// cell, placed on the map in whole pixels; edges two shapes share cancel
/// (split where another shape's corner lies on them, so a slope meeting a
/// block still does), and what's left is linked into outlines,
/// counterclockwise around solid ground.
/// </remarks>
public sealed class TilemapImporter : Importer<TilemapImportSettings>
{
    public static readonly uint Kind = Tag.Of("TMAP");
    public const uint KindVersion = 2; // as tilemapFileVersion
    public const string FileExtension = ".crtilemap";
    public const string ImportedExtension = ".crtmap";

    public override string Name => "Tilemap";

    public override int Version => 2;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ImportedExtension;

    public override void Import(ImportContext context, TilemapImportSettings settings)
    {
        TilemapData map = Read(context.ReadSource());
        // Each tileset, read with its texture: the map's one tile size and
        // pixels per unit checked, and each texture listed once.
        List<(TilesetData Data, int Columns, int Count, int Texture)> sets = [];
        List<AssetId> textures = [];
        (int Width, int Height, float PixelsPerUnit)? grid = null;
        string? gridFrom = null;
        foreach (AssetId id in map.Tilesets)
        {
            string path = (id.IsNone ? null : context.PathOf(id)) ??
                          throw new InvalidDataException($"its tileset {sets.Count + 1} isn't a tileset in the project");
            TilesetData tileset;
            try
            {
                tileset = TilesetImporter.Read(context.ReadDependency(path));
            }
            catch (InvalidDataException exception)
            {
                throw new InvalidDataException($"its tileset {path} is wrong: {exception.Message}", exception);
            }
            (TextureInfo texture, float pixelsPerUnit) = TilesetImporter.ReadTexture(context, tileset.Texture);
            (int Width, int Height, float PixelsPerUnit) size = (tileset.TileWidth, tileset.TileHeight, pixelsPerUnit);
            if (grid is { } first && first != size)
            {
                throw new InvalidDataException(first.Width != size.Width || first.Height != size.Height
                    ? $"its tilesets' tiles aren't one size: {gridFrom}'s are {first.Width}x{first.Height}, {path}'s {size.Width}x{size.Height}"
                    : $"its tilesets' textures aren't one pixels per unit: {gridFrom}'s is {first.PixelsPerUnit}, {path}'s {size.PixelsPerUnit}");
            }
            grid ??= size;
            gridFrom ??= path;
            (int columns, int rows) = tileset.Grid((int)texture.Width, (int)texture.Height);
            int index = textures.IndexOf(tileset.Texture);
            if (index < 0)
            {
                index = textures.Count;
                textures.Add(tileset.Texture);
            }
            sets.Add((tileset, columns, columns * rows, index));
            context.Reference(id);
            context.Reference(tileset.Texture);
        }
        foreach (TileLayer layer in map.Layers)
        {
            foreach (TileCell cell in layer.Cells.Values)
            {
                if (cell.Set >= sets.Count)
                {
                    throw new InvalidDataException(sets.Count == 0
                        ? $"layer {layer.Name}'s cell at ({cell.X}, {cell.Y}) has a tile, but the map has no tileset"
                        : $"layer {layer.Name}'s cell at ({cell.X}, {cell.Y}) is from tileset {cell.Set + 1}, but the map has {sets.Count}");
                }
                if (cell.Tile >= sets[cell.Set].Count)
                {
                    throw new InvalidDataException(
                        $"layer {layer.Name}'s cell at ({cell.X}, {cell.Y}) is tile {cell.Tile}, but its tileset has {sets[cell.Set].Count}");
                }
            }
        }

        // Layer by layer: its cells, then its outlines.
        List<TilemapLayer> layers = [];
        List<TilemapCell> baked = [];
        List<TilemapLoop> loops = [];
        List<Vector2> points = [];
        float perUnit = grid?.PixelsPerUnit ?? 16;
        foreach (TileLayer layer in map.Layers)
        {
            TileCell[] cells = [.. layer.Cells.Values.OrderBy(cell => cell.Y).ThenBy(cell => cell.X)];
            TilemapLayer info = new() { Id = (uint)layer.Id, FirstCell = (uint)baked.Count, CellCount = (uint)cells.Length, FirstLoop = (uint)loops.Count };
            foreach (TileCell cell in cells)
            {
                (TilesetData data, int columns, _, int texture) = sets[cell.Set];
                (int px, int py) = data.PixelOf(cell.Tile, columns);
                baked.Add(new TilemapCell
                {
                    X = cell.X,
                    Y = cell.Y,
                    PixelX = (uint)px,
                    PixelY = (uint)py,
                    Flags = (uint)cell.Flags,
                    Texture = (uint)texture,
                });
            }
            if (grid is { } size)
            {
                foreach (List<Vector2> outline in Outlines(size.Width, size.Height, cell => sets[cell.Set].Data, cells))
                {
                    loops.Add(new TilemapLoop { First = (uint)points.Count, Count = (uint)outline.Count });
                    points.AddRange(outline.Select(p => p / perUnit));
                }
            }
            info.LoopCount = (uint)loops.Count - info.FirstLoop;
            layers.Add(info);
        }
        TilemapInfo header = new()
        {
            TileWidth = (uint)(grid?.Width ?? 0),
            TileHeight = (uint)(grid?.Height ?? 0),
            CellWidth = (grid?.Width ?? 0) / perUnit,
            CellHeight = (grid?.Height ?? 0) / perUnit,
            TextureCount = (uint)textures.Count,
            LayerCount = (uint)layers.Count,
            CellCount = (uint)baked.Count,
            LoopCount = (uint)loops.Count,
            PointCount = (uint)points.Count,
        };
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        output.Add(Tag.Of("INFO"), 1, MemoryMarshal.AsBytes(new ReadOnlySpan<TilemapInfo>(in header)));
        output.Add(Tag.Of("TXTR"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(textures)));
        output.Add(Tag.Of("LAYR"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(layers)));
        output.Add(Tag.Of("CELL"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(baked)));
        if (loops.Count > 0)
        {
            output.Add(Tag.Of("LOOP"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(loops)));
            output.Add(Tag.Of("PNTS"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(points)));
        }
    }

    /// <summary>One tileset's cells' collision outlines (see the other overload).</summary>
    public static List<List<Vector2>> Outlines(TilesetData tileset, IEnumerable<TileCell> cells) =>
        Outlines(tileset.TileWidth, tileset.TileHeight, _ => tileset, cells);

    /// <summary>
    /// The map's collision outlines, in pixels from its origin, y up: closed,
    /// counterclockwise around solid ground, clockwise around holes, without
    /// points in the middle of a straight run.
    /// </summary>
    /// <remarks>Each cell's shapes come from its own tileset (<paramref name="tilesetOf"/>); all have tiles of <paramref name="w"/> by <paramref name="h"/>.</remarks>
    public static List<List<Vector2>> Outlines(int w, int h, Func<TileCell, TilesetData> tilesetOf, IEnumerable<TileCell> cells)
    {
        // Every shape on the map, counterclockwise, in whole pixels, by cell.
        Dictionary<(int, int), List<(long X, long Y)[]>> byCell = [];
        foreach (TileCell cell in cells)
        {
            if (!tilesetOf(cell).Shapes.TryGetValue(cell.Tile, out List<List<Vector2>>? shapes))
            {
                continue;
            }
            foreach (List<Vector2> shape in shapes)
            {
                (long X, long Y)[] placed = [.. shape.Select(p =>
                {
                    long x = (long)MathF.Round(p.X), y = h - (long)MathF.Round(p.Y); // y up within the tile
                    x = (cell.Flags & 1) != 0 ? w - x : x;
                    y = (cell.Flags & 2) != 0 ? h - y : y;
                    return ((long)cell.X * w + x, (long)cell.Y * h + y);
                })];
                long area = 0;
                for (int i = 0; i < placed.Length; ++i)
                {
                    (long X, long Y) a = placed[i], b = placed[(i + 1) % placed.Length];
                    area += a.X * b.Y - b.X * a.Y;
                }
                if (area == 0)
                {
                    continue;
                }
                if (area < 0)
                {
                    Array.Reverse(placed);
                }
                (byCell.TryGetValue((cell.X, cell.Y), out var list) ? list : byCell[(cell.X, cell.Y)] = []).Add(placed);
            }
        }

        // Edges, split where a corner of a shape here or next door lies on
        // them, then cancelled against their reverses.
        Dictionary<((long, long) A, (long, long) B), int> edges = [];
        foreach (((int cx, int cy), List<(long X, long Y)[]> shapes) in byCell)
        {
            List<(long X, long Y)> nearby = [];
            for (int dy = -1; dy <= 1; ++dy)
            {
                for (int dx = -1; dx <= 1; ++dx)
                {
                    if (byCell.TryGetValue((cx + dx, cy + dy), out var near))
                    {
                        nearby.AddRange(near.SelectMany(shape => shape));
                    }
                }
            }
            foreach ((long X, long Y)[] shape in shapes)
            {
                for (int i = 0; i < shape.Length; ++i)
                {
                    (long X, long Y) a = shape[i], b = shape[(i + 1) % shape.Length];
                    if (a == b)
                    {
                        continue;
                    }
                    List<(long X, long Y)> stops = [.. nearby.Where(p => p != a && p != b && On(a, b, p)).Distinct()
                        .OrderBy(p => (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y))];
                    (long X, long Y) from = a;
                    foreach ((long X, long Y) to in stops.Append(b))
                    {
                        if (edges.TryGetValue((to, from), out int reverse) && reverse > 0)
                        {
                            edges[(to, from)] = reverse - 1;
                        }
                        else
                        {
                            edges[(from, to)] = edges.GetValueOrDefault((from, to)) + 1;
                        }
                        from = to;
                    }
                }
            }
        }

        // What's left, linked into loops: at a corner where two outlines
        // touch, the sharpest left turn, so each stays its own.
        Dictionary<(long, long), List<(long X, long Y)>> outgoing = [];
        foreach ((((long, long) a, (long, long) b), int count) in edges)
        {
            for (int i = 0; i < count; ++i)
            {
                (outgoing.TryGetValue(a, out var list) ? list : outgoing[a] = []).Add(b);
            }
        }
        List<List<Vector2>> loops = [];
        foreach ((long, long) start in outgoing.Keys.OrderBy(p => p).ToList())
        {
            while (outgoing.TryGetValue(start, out var first) && first.Count > 0)
            {
                List<(long X, long Y)> loop = [start];
                (long X, long Y) previous = start, at = first[0];
                first.RemoveAt(0);
                while (at != start && outgoing.TryGetValue(at, out var next) && next.Count > 0)
                {
                    int pick = 0;
                    double best = -1;
                    for (int i = 0; i < next.Count; ++i)
                    {
                        // The way out, counterclockwise from straight back:
                        // a right turn is under half a turn, a left one over.
                        double back = Math.Atan2(previous.Y - at.Y, previous.X - at.X);
                        double turn = Math.Atan2(next[i].Y - at.Y, next[i].X - at.X) - back;
                        turn = turn <= 0 ? turn + 2 * Math.PI : turn;
                        if (turn > best)
                        {
                            best = turn;
                            pick = i;
                        }
                    }
                    loop.Add(at);
                    previous = at;
                    at = next[pick];
                    next.RemoveAt(pick);
                }
                List<(long X, long Y)> simplified = Simplify(loop);
                if (at == start && simplified.Count >= 3)
                {
                    loops.Add([.. simplified.Select(p => new Vector2(p.X, p.Y))]);
                }
            }
        }
        return loops;
    }

    // Whether p lies on the segment a-b (exactly: whole pixels).
    private static bool On((long X, long Y) a, (long X, long Y) b, (long X, long Y) p) =>
        (b.X - a.X) * (p.Y - a.Y) == (b.Y - a.Y) * (p.X - a.X) &&
        Math.Min(a.X, b.X) <= p.X && p.X <= Math.Max(a.X, b.X) && Math.Min(a.Y, b.Y) <= p.Y && p.Y <= Math.Max(a.Y, b.Y);

    // A closed loop without points in the middle of a straight run.
    private static List<(long X, long Y)> Simplify(List<(long X, long Y)> loop)
    {
        List<(long X, long Y)> kept = [.. loop];
        for (bool removed = true; removed && kept.Count > 3;)
        {
            removed = false;
            for (int i = 0; i < kept.Count && kept.Count > 3; ++i)
            {
                (long X, long Y) a = kept[(i + kept.Count - 1) % kept.Count], p = kept[i], b = kept[(i + 1) % kept.Count];
                if ((b.X - a.X) * (p.Y - a.Y) == (b.Y - a.Y) * (p.X - a.X))
                {
                    kept.RemoveAt(i--);
                    removed = true;
                }
            }
        }
        return kept;
    }

    /// <summary>A tilemap from its file's JSON; throws, saying where, if it's wrong.</summary>
    public static TilemapData Read(ReadOnlySpan<byte> json)
    {
        using JsonDocument document = JsonDocument.Parse(json.ToArray());
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("a tilemap is a JSON object");
        }
        TilemapData map = new()
        {
            Id = root.TryGetProperty("$guid", out JsonElement guid) && AssetId.TryParse(guid.GetString(), out AssetId id) ? id : AssetId.None,
            Layers = [],
        };
        if (!root.TryGetProperty("Layers", out JsonElement layers))
        {
            // From before layers (ADR 0108): one tileset, one layer.
            if (root.TryGetProperty("Tileset", out JsonElement set) && set.ValueKind == JsonValueKind.String &&
                AssetId.TryParse(set.GetString(), out AssetId tileset))
            {
                map.Tilesets.Add(tileset);
            }
            TileLayer only = new();
            if (root.TryGetProperty("Cells", out JsonElement old))
            {
                ReadCells(old, only, legacy: true);
            }
            map.Layers.Add(only);
            return map;
        }
        if (root.TryGetProperty("Tilesets", out JsonElement tilesets))
        {
            if (tilesets.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Tilesets is an array of tilesets' IDs");
            }
            foreach (JsonElement tileset in tilesets.EnumerateArray())
            {
                map.Tilesets.Add(tileset.ValueKind == JsonValueKind.String && AssetId.TryParse(tileset.GetString(), out AssetId set)
                    ? set
                    : throw new InvalidDataException($"tileset {map.Tilesets.Count + 1} isn't an asset ID"));
            }
        }
        if (layers.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Layers is an array");
        }
        foreach (JsonElement layer in layers.EnumerateArray())
        {
            int number = map.Layers.Count + 1;
            TileLayer read = new()
            {
                Id = layer.TryGetProperty("Id", out JsonElement layerId) && layerId.TryGetInt32(out int value) && value >= 0
                    ? value
                    : throw new InvalidDataException($"layer {number} needs Id, a whole number (0 or more)"),
                Name = layer.TryGetProperty("Name", out JsonElement name) && name.ValueKind == JsonValueKind.String
                    ? name.GetString() ?? ""
                    : $"Layer {number}",
            };
            if (map.Layer(read.Id) is not null)
            {
                throw new InvalidDataException($"two layers have the Id {read.Id}");
            }
            if (layer.TryGetProperty("Cells", out JsonElement cells))
            {
                ReadCells(cells, read, legacy: false);
            }
            map.Layers.Add(read);
        }
        return map;
    }

    // A layer's cells: [x, y, tileset, tile, flips], or before layers [x, y, tile, flips].
    private static void ReadCells(JsonElement cells, TileLayer into, bool legacy)
    {
        if (cells.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"layer {into.Name}'s Cells is an array");
        }
        int length = legacy ? 4 : 5;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            int[] values = new int[length];
            bool good = cell.ValueKind == JsonValueKind.Array && cell.GetArrayLength() == length;
            for (int i = 0; good && i < length; ++i)
            {
                good = cell[i].TryGetInt32(out values[i]);
            }
            (int x, int y, int set, int tile, int flags) = legacy
                ? (values[0], values[1], 0, values[2], values[3])
                : (values[0], values[1], values[2], values[3], values[4]);
            if (!good || set < 0 || tile < 0 || flags is < 0 or > 3)
            {
                throw new InvalidDataException(legacy
                    ? $"cell {into.Cells.Count + 1} isn't [x, y, tile, flips (0 to 3)]"
                    : $"layer {into.Name}'s cell {into.Cells.Count + 1} isn't [x, y, tileset, tile, flips (0 to 3)]");
            }
            into.Cells[(x, y)] = new TileCell(x, y, tile, flags, set);
        }
    }

    /// <summary>The map as its file holds it: a row of cells a line.</summary>
    public static string Format(TilemapData map)
    {
        StringBuilder text = new();
        text.Append("{\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"$guid\": \"{map.Id}\",\n");
        text.Append("  \"Tilesets\": [");
        text.Append(string.Join(", ", map.Tilesets.Select(id => $"\"{id}\"")));
        text.Append("],\n");
        text.Append("  \"Layers\": [");
        for (int i = 0; i < map.Layers.Count; ++i)
        {
            TileLayer layer = map.Layers[i];
            text.Append(i == 0 ? "\n" : ",\n");
            text.Append(CultureInfo.InvariantCulture, $"    {{ \"Id\": {layer.Id}, \"Name\": \"{JsonEncodedText.Encode(layer.Name)}\",\n");
            text.Append("      \"Cells\": [");
            bool first = true;
            foreach (IGrouping<int, TileCell> row in layer.Cells.Values.OrderByDescending(c => c.Y).ThenBy(c => c.X).GroupBy(c => c.Y))
            {
                text.Append(first ? "\n        " : ",\n        ");
                first = false;
                text.Append(string.Join(", ", row.Select(c =>
                    string.Create(CultureInfo.InvariantCulture, $"[{c.X}, {c.Y}, {c.Set}, {c.Tile}, {c.Flags}]"))));
            }
            text.Append(first ? "] }" : "\n      ] }");
        }
        text.Append(map.Layers.Count == 0 ? "]\n}\n" : "\n  ]\n}\n");
        return text.ToString();
    }
}
