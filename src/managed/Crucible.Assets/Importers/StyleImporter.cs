using Crucible.Serialization;
using Crucible.UI;

namespace Crucible.Assets.Importers;

/// <summary>A theme has no import settings: everything is in the file.</summary>
[Serialized]
public partial class StyleImportSettings;

/// <summary>
/// Imports themes (ADR 0061): an authored <c>.crstyle</c> (JSON with its
/// GUID inside, like materials; the format is <see cref="Theme"/>'s), read
/// as the runtime reads it so a mistake fails the import with where it is,
/// then kept in a <c>.crsty</c>: a STYL file whose JSON section holds the
/// file's text.
/// </summary>
public sealed class StyleImporter : Importer<StyleImportSettings>
{
    public const string FileExtension = ".crstyle";

    public override string Name => "Style";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [FileExtension];

    public override string OutputExtension => ".crsty";

    public override void Import(ImportContext context, StyleImportSettings settings)
    {
        byte[] json = context.ReadSource();
        _ = Theme.Parse(json); // throws, saying where, if it's wrong
        context.CreateOutput(Theme.Kind, Theme.KindVersion).Add(Theme.JsonSection, 1, json);
    }
}
