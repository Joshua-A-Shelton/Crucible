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

/// <summary>A painted cell: where, which tile, and its flips (TileFlip bits).</summary>
public readonly record struct TileCell(int X, int Y, int Tile, int Flags);

/// <summary>A tilemap as its file holds it (ADR 0108): its tileset and its painted cells.</summary>
public sealed class TilemapData
{
    public AssetId Id;
    public AssetId Tileset;

    /// <summary>The painted cells by position; at most one each.</summary>
    public Dictionary<(int X, int Y), TileCell> Cells = [];
}

/// <summary>
/// Imports tilemaps (ADR 0108): an authored <c>.crtilemap</c> (JSON with its
/// GUID inside) into a <c>.crtmap</c>: a TMAP file (TilemapFile.h) with each
/// cell's place in its tileset's texture, and collision baked from the
/// tiles' shapes. The tileset's file and its texture are dependencies, so
/// changing either imports the map again.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Tileset": "…",                     the tileset's asset ID
///   "Cells": [ [0, 0, 5, 0], … ]        [x, y, tile, flips]: y up; flips 1 for X, 2 for Y
/// }
/// </code>
/// Collision: every painted cell's shapes, mirrored with the cell, placed on
/// the map in whole pixels; edges two shapes share cancel (split where
/// another shape's corner lies on them, so a slope meeting a block still
/// does), and what's left is linked into outlines, counterclockwise around
/// solid ground.
/// </remarks>
public sealed class TilemapImporter : Importer<TilemapImportSettings>
{
    public static readonly uint Kind = Tag.Of("TMAP");
    public const uint KindVersion = 1; // as tilemapFileVersion
    public const string FileExtension = ".crtilemap";
    public const string ImportedExtension = ".crtmap";

    public override string Name => "Tilemap";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ImportedExtension;

    public override void Import(ImportContext context, TilemapImportSettings settings)
    {
        TilemapData map = Read(context.ReadSource());
        string tilesetPath = (map.Tileset.IsNone ? null : context.PathOf(map.Tileset)) ??
                             throw new InvalidDataException("its Tileset isn't a tileset in the project");
        TilesetData tileset;
        try
        {
            tileset = TilesetImporter.Read(context.ReadDependency(tilesetPath));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"its tileset {tilesetPath} is wrong: {exception.Message}", exception);
        }
        (TextureInfo texture, float pixelsPerUnit) = TilesetImporter.ReadTexture(context, tileset.Texture);
        (int columns, int rows) = tileset.Grid((int)texture.Width, (int)texture.Height);
        foreach (TileCell cell in map.Cells.Values.Where(cell => cell.Tile >= columns * rows))
        {
            throw new InvalidDataException($"the cell at ({cell.X}, {cell.Y}) is tile {cell.Tile}, but the tileset has {columns * rows}");
        }
        context.Reference(map.Tileset);
        context.Reference(tileset.Texture);

        TileCell[] cells = [.. map.Cells.Values.OrderBy(cell => cell.Y).ThenBy(cell => cell.X)];
        TilemapCell[] baked = new TilemapCell[cells.Length];
        for (int i = 0; i < cells.Length; ++i)
        {
            (int px, int py) = tileset.PixelOf(cells[i].Tile, columns);
            baked[i] = new TilemapCell
            {
                X = cells[i].X,
                Y = cells[i].Y,
                PixelX = (uint)px,
                PixelY = (uint)py,
                Flags = (uint)cells[i].Flags,
            };
        }
        List<List<Vector2>> outlines = Outlines(tileset, cells);
        List<TilemapLoop> loops = [];
        List<Vector2> points = [];
        foreach (List<Vector2> outline in outlines)
        {
            loops.Add(new TilemapLoop { First = (uint)points.Count, Count = (uint)outline.Count });
            points.AddRange(outline.Select(p => p / pixelsPerUnit));
        }
        TilemapInfo info = new()
        {
            Texture = tileset.Texture,
            TileWidth = (uint)tileset.TileWidth,
            TileHeight = (uint)tileset.TileHeight,
            CellWidth = tileset.TileWidth / pixelsPerUnit,
            CellHeight = tileset.TileHeight / pixelsPerUnit,
            CellCount = (uint)baked.Length,
            LoopCount = (uint)loops.Count,
            PointCount = (uint)points.Count,
        };
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        output.Add(Tag.Of("INFO"), 1, MemoryMarshal.AsBytes(new ReadOnlySpan<TilemapInfo>(in info)));
        output.Add(Tag.Of("CELL"), 1, MemoryMarshal.AsBytes(baked.AsSpan()));
        if (loops.Count > 0)
        {
            output.Add(Tag.Of("LOOP"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(loops)));
            output.Add(Tag.Of("PNTS"), 1, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(points)));
        }
    }

    /// <summary>
    /// The map's collision outlines, in pixels from its origin, y up: closed,
    /// counterclockwise around solid ground, clockwise around holes, without
    /// points in the middle of a straight run.
    /// </summary>
    public static List<List<Vector2>> Outlines(TilesetData tileset, IEnumerable<TileCell> cells)
    {
        // Every shape on the map, counterclockwise, in whole pixels, by cell.
        int w = tileset.TileWidth, h = tileset.TileHeight;
        Dictionary<(int, int), List<(long X, long Y)[]>> byCell = [];
        foreach (TileCell cell in cells)
        {
            if (!tileset.Shapes.TryGetValue(cell.Tile, out List<List<Vector2>>? shapes))
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
            Tileset = root.TryGetProperty("Tileset", out JsonElement set) && set.ValueKind == JsonValueKind.String &&
                      AssetId.TryParse(set.GetString(), out AssetId tileset)
                ? tileset
                : AssetId.None,
        };
        if (root.TryGetProperty("Cells", out JsonElement cells))
        {
            foreach (JsonElement cell in cells.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() != 4 || !cell[0].TryGetInt32(out int x) ||
                    !cell[1].TryGetInt32(out int y) || !cell[2].TryGetInt32(out int tile) || !cell[3].TryGetInt32(out int flags) ||
                    tile < 0 || flags is < 0 or > 3)
                {
                    throw new InvalidDataException($"cell {map.Cells.Count + 1} isn't [x, y, tile, flips (0 to 3)]");
                }
                map.Cells[(x, y)] = new TileCell(x, y, tile, flags);
            }
        }
        return map;
    }

    /// <summary>The map as its file holds it: a row of cells a line.</summary>
    public static string Format(TilemapData map)
    {
        StringBuilder text = new();
        text.Append("{\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"$guid\": \"{map.Id}\",\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"Tileset\": {(map.Tileset.IsNone ? "null" : $"\"{map.Tileset}\"")},\n");
        text.Append("  \"Cells\": [");
        bool first = true;
        foreach (IGrouping<int, TileCell> row in map.Cells.Values.OrderByDescending(c => c.Y).ThenBy(c => c.X).GroupBy(c => c.Y))
        {
            text.Append(first ? "\n    " : ",\n    ");
            first = false;
            text.Append(string.Join(", ", row.Select(c => string.Create(CultureInfo.InvariantCulture, $"[{c.X}, {c.Y}, {c.Tile}, {c.Flags}]"))));
        }
        text.Append(first ? "]\n}\n" : "\n  ]\n}\n");
        return text.ToString();
    }
}
