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
    private ImportedFileWriter? _output;

    internal ImportContext(AssetId asset, string assetPath, string assetsFolder)
    {
        Asset = asset;
        AssetPath = assetPath;
        _assetsFolder = assetsFolder;
    }

    public AssetId Asset { get; }

    /// <summary>The source's path inside <c>Assets/</c>, with '/' separators.</summary>
    public string AssetPath { get; }

    public string SourcePath => AssetDatabase.FullPath(_assetsFolder, AssetPath);

    public byte[] ReadSource() => File.ReadAllBytes(SourcePath);

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

    internal IReadOnlyDictionary<string, string> Dependencies => _dependencies;

    internal ImportedFileWriter? Output => _output;

    public void Dispose() => _output?.Dispose();
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
