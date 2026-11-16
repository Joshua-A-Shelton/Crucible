using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Assets.Importers;

/// <summary>A chain has no import settings: everything is in the file.</summary>
[Serialized]
public partial class ChainImportSettings;

/// <summary>
/// Imports chains (ADR 0095): an authored <c>.crchain</c> (JSON with its GUID
/// inside, like materials), checked so a mistake fails the import saying
/// where, into a <c>.crchn</c>: a CHAN file (ChainFile.h) 2D physics makes a
/// Chain collider's segments from.
/// </summary>
/// <remarks>
/// <code>
/// {
///   "$guid": "…",
///   "Points": [ [6, 0], [2, 1], [-2, 1], [-6, 0] ],   in the entity's space, meters
///   "Loop": false                                       true joins the last point to the first
/// }
/// </code>
/// The solid side is on the right of the direction the points run: ground
/// runs right to left, a solid island counterclockwise, a cave clockwise.
/// </remarks>
public sealed class ChainImporter : Importer<ChainImportSettings>
{
    public static readonly uint Kind = Tag.Of("CHAN");
    public const uint KindVersion = 1; // as chainFileVersion
    public const string FileExtension = ".crchain";

    /// <summary>The closest two neighboring points may be, in meters (as minChainPointSpacing).</summary>
    public const float MinPointSpacing = 0.005f;

    public override string Name => "Chain";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ".crchn";

    public override void Import(ImportContext context, ChainImportSettings settings)
    {
        using JsonDocument document = JsonDocument.Parse(context.ReadSource());
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        foreach ((string tag, byte[] bytes) in Build(document.RootElement))
        {
            output.Add(Tag.Of(tag), 1, bytes);
        }
    }

    /// <summary>The points and whether it loops, from a .crchain's JSON; throws, saying where, if it's wrong.</summary>
    public static (List<Vector2> Points, bool Loop) Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Points", out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("a chain needs Points: an array of [x, y]");
        }
        bool loop = false;
        if (root.TryGetProperty("Loop", out JsonElement flag))
        {
            loop = flag.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException("Loop is true or false"),
            };
        }
        List<Vector2> points = [];
        foreach (JsonElement point in list.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2 ||
                point[0].ValueKind != JsonValueKind.Number || point[1].ValueKind != JsonValueKind.Number)
            {
                throw new InvalidDataException($"point {points.Count + 1} isn't [x, y]");
            }
            points.Add(new Vector2(point[0].GetSingle(), point[1].GetSingle()));
        }
        int fewest = loop ? 3 : 2;
        if (points.Count < fewest)
        {
            throw new InvalidDataException($"a {(loop ? "loop" : "chain")} needs at least {fewest} points, not {points.Count}");
        }
        int segments = loop ? points.Count : points.Count - 1;
        for (int i = 0; i < segments; ++i)
        {
            int next = (i + 1) % points.Count;
            if (Vector2.Distance(points[i], points[next]) <= MinPointSpacing)
            {
                throw new InvalidDataException(
                    $"points {i + 1} and {next + 1} are too close together (more than {MinPointSpacing} m apart, please)");
            }
        }
        return (points, loop);
    }

    /// <summary>The file's sections, by tag, from its JSON; throws, saying where, if it's wrong.</summary>
    public static IEnumerable<(string Tag, byte[] Bytes)> Build(JsonElement root)
    {
        (List<Vector2> points, bool loop) = Read(root);
        ChainInfo info = new() { PointCount = (uint)points.Count, Loop = loop ? 1u : 0u };
        yield return ("INFO", MemoryMarshal.AsBytes(new Span<ChainInfo>(ref info)).ToArray());
        yield return ("PNTS", MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(points)).ToArray());
    }
}
