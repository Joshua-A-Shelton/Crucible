using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Crucible.Binary;
using Crucible.Importers;
using Crucible.Serialization;

namespace Crucible.Assets.Importers;

/// <summary>A tileset has no import settings: everything is in the file.</summary>
[Serialized]
public partial class TilesetImportSettings;

/// <summary>A tileset as its file holds it (ADR 0108): a texture cut into tiles, and each tile's shapes.</summary>
public sealed class TilesetData
{
    public AssetId Id;
    public AssetId Texture;
    public int TileWidth = 16;
    public int TileHeight = 16;
    public int Margin;
    public int Spacing;

    /// <summary>Collision shapes, by tile number: outlines in the tile's pixels from its top-left, y down.</summary>
    public SortedDictionary<int, List<List<Vector2>>> Shapes = [];

    /// <summary>Tiles across and down a texture of this size.</summary>
    public (int Columns, int Rows) Grid(int width, int height) =>
        (Math.Max((width - 2 * Margin + Spacing) / Math.Max(TileWidth + Spacing, 1), 0),
            Math.Max((height - 2 * Margin + Spacing) / Math.Max(TileHeight + Spacing, 1), 0));

    /// <summary>A tile's top-left corner in the texture.</summary>
    public (int X, int Y) PixelOf(int tile, int columns) =>
        (Margin + tile % columns * (TileWidth + Spacing), Margin + tile / columns * (TileHeight + Spacing));
}

/// <summary>
/// Imports tilesets (ADR 0108): an authored <c>.crtileset</c> (JSON with its
/// GUID inside), checked so a mistake fails the import saying where, into a
/// <c>.crtls</c>: a TSET file (TilemapFile.h). Maps read the tiles' shapes
/// from the authored file when they import.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Texture": "…",                    the texture's asset ID
///   "TileWidth": 16, "TileHeight": 16, a tile's size in pixels
///   "Margin": 0, "Spacing": 0,         pixels around the grid and between tiles
///   "Tiles": [                         only tiles with something set
///     { "Tile": 3, "Shapes": [ [ [0, 0], [16, 0], [16, 16], [0, 16] ] ] }
///   ]
/// }
/// </code>
/// A shape is an outline of whole pixels from the tile's top-left corner,
/// y down, of any shape; a tile may have several.
/// </remarks>
public sealed class TilesetImporter : Importer<TilesetImportSettings>
{
    public static readonly uint Kind = Tag.Of("TSET");
    public const uint KindVersion = 1; // as tilesetFileVersion
    public const string FileExtension = ".crtileset";
    public const string ImportedExtension = ".crtls";

    public override string Name => "Tileset";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ImportedExtension;

    public override void Import(ImportContext context, TilesetImportSettings settings)
    {
        TilesetData tileset = Read(context.ReadSource());
        (TextureInfo texture, _) = ReadTexture(context, tileset.Texture);
        (int columns, int rows) = tileset.Grid((int)texture.Width, (int)texture.Height);
        if (columns == 0 || rows == 0)
        {
            throw new InvalidDataException(
                $"its {tileset.TileWidth}x{tileset.TileHeight} tiles don't fit the {texture.Width}x{texture.Height} texture");
        }
        foreach (int tile in tileset.Shapes.Keys.Where(tile => tile >= columns * rows))
        {
            throw new InvalidDataException($"tile {tile} has shapes, but the texture has {columns * rows} tiles");
        }
        context.Reference(tileset.Texture);
        TilesetInfo info = new()
        {
            Texture = tileset.Texture,
            TileWidth = (uint)tileset.TileWidth,
            TileHeight = (uint)tileset.TileHeight,
            Columns = (uint)columns,
            TileCount = (uint)(columns * rows),
        };
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        output.Add(Tag.Of("INFO"), 1, MemoryMarshal.AsBytes(new ReadOnlySpan<TilesetInfo>(in info)));
    }

    /// <summary>A texture's size and pixels per unit, from its imported file; throws if it can't be read.</summary>
    internal static (TextureInfo Info, float PixelsPerUnit) ReadTexture(ImportContext context, AssetId texture)
    {
        byte[] bytes = (texture.IsNone ? null : context.ReadImported(texture)) ??
                       throw new InvalidDataException("its Texture isn't a texture that imports");
        using ImportedFile file = ImportedFile.Parse(bytes, TextureImporter.Kind, TextureImporter.KindVersion);
        TextureInfo info = MemoryMarshal.Read<TextureInfo>(file.Read(file.Find(TextureImporter.InfoSection)));
        int sprite = file.Find(TextureImporter.SpriteSection);
        float pixelsPerUnit = sprite >= 0 ? MemoryMarshal.Read<float>(file.Read(sprite)) : 16;
        return (info, pixelsPerUnit > 0 ? pixelsPerUnit : 16);
    }

    /// <summary>A tileset from its file's JSON; throws, saying where, if it's wrong.</summary>
    public static TilesetData Read(ReadOnlySpan<byte> json)
    {
        using JsonDocument document = JsonDocument.Parse(json.ToArray());
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("a tileset is a JSON object");
        }
        TilesetData tileset = new()
        {
            Id = root.TryGetProperty("$guid", out JsonElement guid) && AssetId.TryParse(guid.GetString(), out AssetId id) ? id : AssetId.None,
            Texture = root.TryGetProperty("Texture", out JsonElement texture) && AssetId.TryParse(texture.GetString(), out AssetId t)
                ? t
                : AssetId.None,
            TileWidth = Whole(root, "TileWidth", 16, 1),
            TileHeight = Whole(root, "TileHeight", 16, 1),
            Margin = Whole(root, "Margin", 0, 0),
            Spacing = Whole(root, "Spacing", 0, 0),
        };
        if (!root.TryGetProperty("Tiles", out JsonElement tiles))
        {
            return tileset;
        }
        if (tiles.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Tiles is an array");
        }
        foreach (JsonElement tile in tiles.EnumerateArray())
        {
            int number = tile.TryGetProperty("Tile", out JsonElement n) && n.TryGetInt32(out int value) && value >= 0
                ? value
                : throw new InvalidDataException("each of Tiles needs Tile, its number (0 or more)");
            List<List<Vector2>> shapes = [];
            if (tile.TryGetProperty("Shapes", out JsonElement list))
            {
                foreach (JsonElement shape in list.EnumerateArray())
                {
                    List<Vector2> points = [];
                    foreach (JsonElement point in shape.EnumerateArray())
                    {
                        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 ||
                            !point[0].TryGetInt32(out int x) || !point[1].TryGetInt32(out int y))
                        {
                            throw new InvalidDataException($"tile {number}: a shape's points are [x, y] in whole pixels");
                        }
                        if (x < 0 || y < 0 || x > tileset.TileWidth || y > tileset.TileHeight)
                        {
                            throw new InvalidDataException(
                                $"tile {number}: the point [{x}, {y}] is outside the {tileset.TileWidth}x{tileset.TileHeight} tile");
                        }
                        points.Add(new Vector2(x, y));
                    }
                    if (points.Count < 3)
                    {
                        throw new InvalidDataException($"tile {number}: a shape needs at least 3 points");
                    }
                    shapes.Add(points);
                }
            }
            tileset.Shapes[number] = shapes;
        }
        return tileset;
    }

    private static int Whole(JsonElement root, string name, int fallback, int least)
    {
        if (!root.TryGetProperty(name, out JsonElement element))
        {
            return fallback;
        }
        return element.TryGetInt32(out int value) && value >= least
            ? value
            : throw new InvalidDataException($"{name} is a whole number, {least} or more");
    }

    /// <summary>The tileset as its file holds it, indented, shapes on one line each.</summary>
    public static string Format(TilesetData tileset)
    {
        StringBuilder text = new();
        text.Append("{\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"$guid\": \"{tileset.Id}\",\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"Texture\": {(tileset.Texture.IsNone ? "null" : $"\"{tileset.Texture}\"")},\n");
        text.Append(CultureInfo.InvariantCulture,
            $"  \"TileWidth\": {tileset.TileWidth},\n  \"TileHeight\": {tileset.TileHeight},\n");
        text.Append(CultureInfo.InvariantCulture, $"  \"Margin\": {tileset.Margin},\n  \"Spacing\": {tileset.Spacing},\n");
        text.Append("  \"Tiles\": [");
        bool first = true;
        foreach ((int tile, List<List<Vector2>> shapes) in tileset.Shapes.Where(entry => entry.Value.Count > 0))
        {
            text.Append(first ? "\n" : ",\n");
            first = false;
            IEnumerable<string> outlines = shapes.Select(shape =>
                "[" + string.Join(", ", shape.Select(p => string.Create(CultureInfo.InvariantCulture, $"[{(int)p.X}, {(int)p.Y}]"))) + "]");
            text.Append(CultureInfo.InvariantCulture, $"    {{ \"Tile\": {tile}, \"Shapes\": [ {string.Join(", ", outlines)} ] }}");
        }
        text.Append(first ? "]\n}\n" : "\n  ]\n}\n");
        return text.ToString();
    }
}
