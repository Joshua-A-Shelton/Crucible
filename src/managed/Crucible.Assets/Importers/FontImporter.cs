using System.Buffers.Binary;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Assets.Importers;

/// <summary>A font's import settings: none yet.</summary>
[Serialized]
public partial class FontImportSettings;

/// <summary>
/// Imports TrueType and OpenType fonts (ADR 0057): one face per asset, its
/// file kept whole in the <c>.crfont</c>'s <c>DATA</c> section, since
/// glyphs are made from it at run time, at whatever size and kind text
/// asks for. Collections (<c>.ttc</c>) aren't fonts here: import each face
/// as its own file.
/// </summary>
public sealed class FontImporter : Importer<FontImportSettings>
{
    public static readonly uint Kind = Tag.Of("FONT");
    public static readonly uint DataSection = Tag.Of("DATA");
    public const uint KindVersion = 1;

    public override string Name => "Font";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [".ttf", ".otf"];

    public override string OutputExtension => ".crfont";

    // Big-endian sfnt versions.
    private const uint TrueTypeVersion = 0x00010000;
    private const uint AppleTrueTypeVersion = 0x74727565; // 'true'
    private const uint CffVersion = 0x4F54544F;           // 'OTTO'
    private const uint CollectionTag = 0x74746366;        // 'ttcf'

    public override void Import(ImportContext context, FontImportSettings settings)
    {
        byte[] bytes = context.ReadSource();
        // TrueType or CFF outlines; FreeType checks the rest when the font is
        // first used.
        uint version = bytes.Length >= 12 ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : 0;
        if (version == CollectionTag)
        {
            throw new InvalidDataException("A font collection: import each face as its own .ttf or .otf.");
        }
        if (version is not (TrueTypeVersion or AppleTrueTypeVersion or CffVersion))
        {
            throw new InvalidDataException("Not a TrueType or OpenType font.");
        }
        context.CreateOutput(Kind, KindVersion).Add(DataSection, 1, bytes);
    }
}
