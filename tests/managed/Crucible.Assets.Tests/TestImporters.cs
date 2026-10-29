using System.Text;
using Crucible.Assets;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Tests.Assets;

[Serialized]
public partial class TextSettings
{
    public bool Upper = true;
    public int Repeat = 1;
}

// .txt: the text, uppercased and repeated as its settings say.
public sealed class TextImporter : Importer<TextSettings>
{
    public static readonly uint Kind = Tag.Of("TEXT");
    public static readonly uint Body = Tag.Of("BODY");

    public int Runs { get; private set; }

    public int ImporterVersion { get; set; } = 1;

    public override string Name => "Text";

    public override int Version => ImporterVersion;

    public override IReadOnlyList<string> Extensions => [".txt"];

    public override string OutputExtension => ".crtext";

    public override void Import(ImportContext context, TextSettings settings)
    {
        Runs++;
        string text = Encoding.UTF8.GetString(context.ReadSource());
        text = string.Concat(Enumerable.Repeat(settings.Upper ? text.ToUpperInvariant() : text, settings.Repeat));
        context.CreateOutput(Kind, 1).Add(Body, 1, Encoding.UTF8.GetBytes(text));
    }
}

[Serialized]
public partial class ListSettings;

// .list: each line names another file in Assets/; the output is their
// contents joined, so changing any of them reimports the list.
public sealed class ListImporter : Importer<ListSettings>
{
    public override string OutputExtension => ".crlist";

    public int Runs { get; private set; }

    public override string Name => "List";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [".list"];

    public override void Import(ImportContext context, ListSettings settings)
    {
        Runs++;
        string[] lines = Encoding.UTF8.GetString(context.ReadSource())
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string joined = string.Join("+", lines.Select(line => Encoding.UTF8.GetString(context.ReadDependency(line))));
        context.CreateOutput(TextImporter.Kind, 1).Add(TextImporter.Body, 1, Encoding.UTF8.GetBytes(joined));
    }
}

public sealed class BrokenImporter : Importer<ListSettings>
{
    public override string OutputExtension => ".crlist";

    public override string Name => "Broken";

    public override int Version => 1;

    public override IReadOnlyList<string> Extensions => [".bad"];

    public override void Import(ImportContext context, ListSettings settings) =>
        throw new InvalidDataException("this file is beyond repair");
}
