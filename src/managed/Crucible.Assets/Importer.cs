using System.IO.Hashing;
using System.Text.Json;
using Crucible.Binary;
using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// Turns one kind of source file into imported data (ADR 0021, 0040).
/// Derive from <see cref="Importer{TSettings}"/>. Names are placeholders
/// (docs/naming.md).
/// </summary>
public abstract class Importer
{
    /// <summary>Stored in sidecars; never change it once assets use it.</summary>
    public abstract string Name { get; }

    /// <summary>Raising it reimports everything this importer made.</summary>
    public abstract int Version { get; }

    /// <summary>The file extensions it takes by default, lowercase with the dot (".png").</summary>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// The extension of the file it writes, named after its kind (".crmesh",
    /// ".crtexture"; docs/naming.md).
    /// </summary>
    public abstract string OutputExtension { get; }

    /// <summary>Its settings type's fields, for the editor's inspector (ADR 0087).</summary>
    public abstract IReadOnlyList<SerializedField> SettingsFields { get; }

    // Settings cross the database as JSON: canonical (re-serialized) bytes,
    // so formatting a sidecar by hand doesn't reimport anything.
    internal abstract byte[] CanonicalSettings(ReadOnlySpan<byte> settingsJson);

    // A new sidecar's settings, as JSON.
    internal abstract byte[] DefaultSettings(string assetPath);

    internal abstract void WriteSettings(Utf8JsonWriter writer, ReadOnlySpan<byte> settingsJson);

    internal abstract void Run(ImportContext context, ReadOnlySpan<byte> settingsJson);
}

/// <summary>An importer whose settings are a <see cref="SerializedAttribute"/> type, kept in the sidecar.</summary>
public abstract class Importer<TSettings> : Importer where TSettings : IJsonSerialized<TSettings>, new()
{
    /// <summary>
    /// Reads the source (and any dependencies) through the context and writes
    /// one output with <see cref="ImportContext.CreateOutput"/>. Throwing fails
    /// this asset's import with the exception's message.
    /// </summary>
    public abstract void Import(ImportContext context, TSettings settings);

    /// <summary>
    /// The settings a new file gets in its sidecar: the type's own defaults
    /// unless an importer knows better from the file (an .hdr image wants HDR).
    /// </summary>
    protected virtual TSettings Defaults(string assetPath) => new();

    public override IReadOnlyList<SerializedField> SettingsFields => TSettings.SerializedFields;

    internal override byte[] CanonicalSettings(ReadOnlySpan<byte> settingsJson) =>
        Json.Serialize(Parse(settingsJson));

    internal override byte[] DefaultSettings(string assetPath) => Json.Serialize(Defaults(assetPath));

    internal override void WriteSettings(Utf8JsonWriter writer, ReadOnlySpan<byte> settingsJson) =>
        TSettings.WriteJson(writer, Parse(settingsJson));

    internal override void Run(ImportContext context, ReadOnlySpan<byte> settingsJson) =>
        Import(context, Parse(settingsJson));

    private static TSettings Parse(ReadOnlySpan<byte> settingsJson) =>
        settingsJson.IsEmpty ? new TSettings() : Json.Deserialize<TSettings>(settingsJson);
}

/// <summary>What an importer sees while it runs.</summary>
public sealed class ImportContext : IDisposable
{
    private readonly string _assetsFolder;
    private readonly SortedDictionary<string, string> _dependencies = new(StringComparer.Ordinal);
    private readonly Func<string, AssetId> _idOf;
    private readonly Func<AssetId, (string Path, string Imported, IReadOnlyCollection<string> Dependencies)?> _imported;
    private readonly List<string> _warnings = [];
    private ImportedFileWriter? _output;
    private readonly List<(string Name, string Extension, ImportedFileWriter Output)> _subAssets = [];

    internal ImportContext(AssetId asset, string assetPath, string assetsFolder, Func<string, AssetId> idOf,
        Func<AssetId, (string Path, string Imported, IReadOnlyCollection<string> Dependencies)?>? imported = null)
    {
        Asset = asset;
        AssetPath = assetPath;
        _assetsFolder = assetsFolder;
        _idOf = idOf;
        _imported = imported ?? (_ => null);
    }

    /// <summary>
    /// Another asset's imported file (a material's shader), imported first if
    /// it needs it, or null if there is no such asset or it doesn't import.
    /// The asset and what it depends on become this import's dependencies:
    /// changing them reimports this one.
    /// </summary>
    public byte[]? ReadImported(AssetId id)
    {
        if (_imported(id) is not { } found)
        {
            return null;
        }
        ReadDependency(found.Path);
        foreach (string dependency in found.Dependencies)
        {
            ReadDependency(dependency);
        }
        return File.ReadAllBytes(found.Imported);
    }

    /// <summary>
    /// Another asset's ID by its path inside <c>Assets/</c> (a glTF's
    /// textures), or <see cref="AssetId.None"/> if there is none.
    /// </summary>
    public AssetId IdOf(string assetPath) => _idOf(assetPath);

    /// <summary>Notes something the import left out or worked around; the import still succeeds.</summary>
    public void Warn(string message) => _warnings.Add(message);

    internal IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// A path inside <c>Assets/</c> for a file the source names relative to
    /// itself ("textures/brick.png" beside "models/wall.gltf" is
    /// "models/textures/brick.png"), with '.' and '..' resolved; throws if it
    /// leaves <c>Assets/</c>.
    /// </summary>
    public string Resolve(string relative)
    {
        List<string> parts = [.. AssetPath.Split('/')[..^1]];
        foreach (string part in relative.Replace('\\', '/').Split('/'))
        {
            if (part is "" or ".")
            {
                continue;
            }
            if (part == "..")
            {
                if (parts.Count == 0)
                {
                    throw new InvalidDataException($"{relative} is outside Assets/");
                }
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }

    public AssetId Asset { get; }

    /// <summary>The source's path inside <c>Assets/</c>, with '/' separators.</summary>
    public string AssetPath { get; }

    public string SourcePath => AssetDatabase.FullPath(_assetsFolder, AssetPath);

    public byte[] ReadSource() => File.ReadAllBytes(SourcePath);

    /// <summary>The project's <c>Assets/</c> folder, for tools that look beside the source (shader imports).</summary>
    public string AssetsFolder => _assetsFolder;

    /// <summary>
    /// Writes C# into the game's code (<c>Code/</c>, at
    /// <paramref name="codePath"/>, '/' separators), unless it's already
    /// there unchanged: what the game builds against, checked in like any
    /// source (the Shader importer's material types, ADR 0052).
    /// </summary>
    public void WriteGeneratedSource(string codePath, string text)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_assetsFolder))!, "Code");
        string path = AssetDatabase.FullPath(root, codePath);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Files.WriteAtomically(path, bytes);
    }

    /// <summary>
    /// Reads another file inside <c>Assets/</c> (a glTF's buffers, say) and
    /// records it: changing it reimports this asset.
    /// </summary>
    /// <param name="assetPath">Its path inside <c>Assets/</c>, with '/' separators.</param>
    public byte[] ReadDependency(string assetPath)
    {
        byte[] bytes = File.ReadAllBytes(AssetDatabase.FullPath(_assetsFolder, assetPath));
        _dependencies[assetPath] = ContentHash.Of(bytes);
        return bytes;
    }

    /// <summary>The import's one output, in Crucible's binary format (ADR 0039).</summary>
    public ImportedFileWriter CreateOutput(uint kind, uint kindVersion)
    {
        if (_output is not null)
        {
            throw new InvalidOperationException("An import writes one output.");
        }
        _output = new ImportedFileWriter(kind, kindVersion);
        return _output;
    }

    /// <summary>
    /// Another output, a sub-asset (ADR 0078): an asset of its own with its
    /// own ID (a model's animation clips), named uniquely within this import.
    /// Its ID stays the same across imports while the name does.
    /// </summary>
    /// <param name="extension">Its file's extension, as <see cref="Importer.OutputExtension"/> (".crclip").</param>
    public ImportedFileWriter AddSubAsset(string name, uint kind, uint kindVersion, string extension)
    {
        if (name.Length == 0 || _subAssets.Any(sub => sub.Name == name))
        {
            throw new InvalidDataException(name.Length == 0
                ? "a sub-asset has no name"
                : $"two sub-assets are named {name}");
        }
        ImportedFileWriter output = new(kind, kindVersion);
        _subAssets.Add((name, extension, output));
        return output;
    }

    internal IReadOnlyDictionary<string, string> Dependencies => _dependencies;

    internal ImportedFileWriter? Output => _output;

    internal IReadOnlyList<(string Name, string Extension, ImportedFileWriter Output)> SubAssets => _subAssets;

    public void Dispose()
    {
        _output?.Dispose();
        foreach ((_, _, ImportedFileWriter output) in _subAssets)
        {
            output.Dispose();
        }
    }
}

/// <summary>XXH128 of file contents (ADR 0040), as 32 lowercase hex digits.</summary>
public static class ContentHash
{
    public static string Of(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(XxHash128.Hash(bytes));

    public static string OfFile(string path)
    {
        XxHash128 hash = new();
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        hash.Append(stream);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
