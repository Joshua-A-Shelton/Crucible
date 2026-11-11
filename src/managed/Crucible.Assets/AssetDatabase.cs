using System.Text;
using Crucible.Binary;
using Crucible.Scenes;
using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// One asset the database knows: its identity, where it is, and how it
/// imports. A sub-asset (ADR 0078) has its parent's path and importer, and
/// is made by importing its parent.
/// </summary>
public sealed record AssetEntry(AssetId Id, string Path, string Importer)
{
    /// <summary>The asset whose import makes this one, or <see cref="AssetId.None"/>.</summary>
    public AssetId Parent { get; init; }

    /// <summary>Its name among its parent's sub-assets ("Walk"); empty for an asset of its own.</summary>
    public string SubAssetName { get; init; } = "";

    // A parent's sub-assets, by name, as its sidecar lists them.
    internal IReadOnlyDictionary<string, AssetId> SubAssets { get; init; } = new Dictionary<string, AssetId>();

    // The canonical settings JSON, and the source's content hash.
    internal byte[] Settings { get; init; } = [];
    internal string Hash { get; init; } = "";
}

/// <summary>Something the database noticed while scanning, for the editor to show.</summary>
public sealed record AssetWarning(string Path, string Message);

public enum ImportOutcome
{
    Imported,
    UpToDate,
    NoImporter,
    Failed,
}

public sealed record ImportResult(AssetId Id, string Path, ImportOutcome Outcome, string? OutputPath, string? Error)
{
    /// <summary>What the importer left out or worked around, for the editor to show.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

// What the database keeps in Cache/ between runs.
[Serialized]
internal partial class Stamp
{
    public long Size;
    public long Modified;
    public string Hash = "";
}

[Serialized]
internal partial class StampFile
{
    public Dictionary<string, Stamp> Files = [];
}

[Serialized]
internal partial class ImportRecord
{
    public string Key = "";
    public Dictionary<string, string> Dependencies = [];
    public string Extension = ""; // a sub-asset's (its importer's is the parent's)
}

[Serialized]
internal partial class ImportRecordFile
{
    public Dictionary<string, ImportRecord> Assets = [];
}

/// <summary>
/// The asset database (ADR 0021, 0040). Opening it scans <c>Assets/</c>:
/// every file gets a sidecar with a GUID if it lacks one, sidecars are the
/// source of truth, and a stamp file lets unchanged files skip hashing.
/// Importing writes each asset's output to <c>Cache/Imported/</c>, named by a
/// hash of everything that went into it, so unchanged assets are never
/// reimported and deleting the cache is always safe. An import can make
/// sub-assets (ADR 0078), each an asset of its own whose ID its parent's
/// sidecar keeps by name; they aren't in <see cref="Assets"/> but are found by
/// ID. Names are placeholders (docs/naming.md).
/// </summary>
/// <remarks>Not thread-safe: the editor uses it from one thread.</remarks>
public sealed class AssetDatabase
{
    // Raising it invalidates every cached import, for changes to how keys
    // are made.
    private const int KeyFormat = 1;

    private readonly Dictionary<string, Importer> _importersByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Importer> _importersByExtension = new(StringComparer.Ordinal);
    private readonly Dictionary<AssetId, AssetEntry> _byId = [];
    private readonly Dictionary<string, AssetEntry> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);
    private readonly List<AssetWarning> _warnings = [];
    private ImportRecordFile _records = new();

    private AssetDatabase(Project project, IEnumerable<Importer> importers)
    {
        Project = project;
        foreach (Importer importer in importers)
        {
            if (!_importersByName.TryAdd(importer.Name, importer))
            {
                throw new ArgumentException($"Two importers are named {importer.Name}.", nameof(importers));
            }
            foreach (string extension in importer.Extensions)
            {
                if (!_importersByExtension.TryAdd(extension, importer))
                {
                    throw new ArgumentException($"Two importers take {extension} files.", nameof(importers));
                }
            }
        }
    }

    /// <summary>Opens the project's database and scans its assets.</summary>
    public static AssetDatabase Open(Project project, IEnumerable<Importer> importers)
    {
        AssetDatabase database = new(project, importers);
        database._records = ReadOrDefault<ImportRecordFile>(database.RecordsPath);
        database.Refresh();
        return database;
    }

    public Project Project { get; }

    public IReadOnlyList<AssetWarning> Warnings => _warnings;

    public IEnumerable<AssetEntry> Assets => _byPath.Values.OrderBy(entry => entry.Path, StringComparer.Ordinal);

    /// <summary>How many files the last scan hashed (the others' stamps were unchanged).</summary>
    public int FilesHashed { get; private set; }

    public AssetEntry? Find(AssetId id) => _byId.GetValueOrDefault(id);

    /// <summary>An asset's sub-assets, by name.</summary>
    public IEnumerable<AssetEntry> SubAssetsOf(AssetId id) =>
        Find(id) is { } entry
            ? entry.SubAssets.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => _byId[pair.Value])
            : [];

    public AssetEntry? Find(string assetPath) => _byPath.GetValueOrDefault(assetPath);

    private string StampsPath => Path.Combine(Project.CacheFolder, "stamps.json");
    private string RecordsPath => Path.Combine(Project.CacheFolder, "imports.json");
    private string ImportedFolder => Path.Combine(Project.CacheFolder, "Imported");

    // ---------------------------------------------------------------------
    // Scanning
    // ---------------------------------------------------------------------

    /// <summary>
    /// Scans <c>Assets/</c> again: new files get sidecars, changed files are
    /// rehashed, and the warnings are replaced.
    /// </summary>
    public void Refresh()
    {
        _byId.Clear();
        _byPath.Clear();
        _hashes.Clear();
        _warnings.Clear();
        FilesHashed = 0;

        string root = Project.AssetsFolder;
        List<string> files = [];
        HashSet<string> sidecars = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string path = AssetPath(root, file);
            if (path.Split('/').Any(part => part.StartsWith('.')) || path.Contains(".tmp-", StringComparison.Ordinal))
            {
                continue; // hidden files and folders, and writes in progress
            }
            if (path.EndsWith(Sidecar.Extension, StringComparison.Ordinal))
            {
                sidecars.Add(path);
            }
            else
            {
                files.Add(path);
            }
        }
        // Ordinal order makes every scan the same, and decides which copy of
        // a duplicated GUID keeps it.
        files.Sort(StringComparer.Ordinal);

        StampFile stamps = ReadOrDefault<StampFile>(StampsPath);
        StampFile newStamps = new();
        foreach (string path in files)
        {
            string fullPath = FullPath(root, path);
            sidecars.Remove(path + Sidecar.Extension);
            _hashes[path] = HashWithStamp(fullPath, path, stamps, newStamps);
            AddAsset(path, fullPath);
        }
        foreach (string orphan in sidecars.Order(StringComparer.Ordinal))
        {
            _warnings.Add(new AssetWarning(orphan,
                "this sidecar's file is missing: if it was moved, move the sidecar with it to keep its GUID"));
        }
        Files.WriteAtomically(StampsPath, Json.Serialize(newStamps));
    }

    private string HashWithStamp(string fullPath, string path, StampFile old, StampFile updated)
    {
        FileInfo info = new(fullPath);
        long modified = info.LastWriteTimeUtc.Ticks;
        if (!old.Files.TryGetValue(path, out Stamp? stamp) || stamp.Size != info.Length || stamp.Modified != modified)
        {
            stamp = new Stamp { Size = info.Length, Modified = modified, Hash = ContentHash.OfFile(fullPath) };
            FilesHashed++;
        }
        updated.Files[path] = stamp;
        return stamp.Hash;
    }

    private void AddAsset(string path, string fullPath)
    {
        if (path.EndsWith(Scene.Extension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(Importers.MaterialImporter.FileExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(Importers.StyleImporter.FileExtension, StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(Importers.StateMachineImporter.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            AddOwnFile(path, fullPath);
            return;
        }
        string sidecarPath = fullPath + Sidecar.Extension;
        Importer? byExtension = _importersByExtension.GetValueOrDefault(Path.GetExtension(path).ToLowerInvariant());
        Sidecar sidecar;
        bool rewrite = false;
        if (File.Exists(sidecarPath))
        {
            try
            {
                sidecar = Sidecar.Read(File.ReadAllBytes(sidecarPath));
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or FormatException)
            {
                // Never overwrite a sidecar someone may be fixing by hand.
                _warnings.Add(new AssetWarning(path + Sidecar.Extension, $"can't be read, so the asset is skipped: {exception.Message}"));
                return;
            }
            if (sidecar.Importer.Length == 0 && byExtension is not null)
            {
                sidecar = sidecar with { Importer = byExtension.Name }; // an importer for it arrived
                rewrite = true;
            }
        }
        else
        {
            sidecar = new Sidecar(AssetId.New(), byExtension?.Name ?? "", byExtension?.DefaultSettings(path) ?? []);
            rewrite = true;
        }

        if (_byId.TryGetValue(sidecar.Id, out AssetEntry? original))
        {
            // Copied with its sidecar: the copy gets an identity of its own.
            _warnings.Add(new AssetWarning(path,
                $"had the same GUID as {original.Path} (copied with its sidecar?), so it was given a new one"));
            sidecar = sidecar with { Id = AssetId.New() };
            rewrite = true;
        }
        if (sidecar.SubAssets.Values.Any(_byId.ContainsKey) ||
            sidecar.SubAssets.Values.Distinct().Count() != sidecar.SubAssets.Count)
        {
            // Copied too: new ones, kept by name.
            sidecar = sidecar with
            {
                SubAssets = sidecar.SubAssets.ToDictionary(pair => pair.Key, _ => AssetId.New(), StringComparer.Ordinal),
            };
            rewrite = true;
        }

        Importer? importer = null;
        if (sidecar.Importer.Length > 0 && !_importersByName.TryGetValue(sidecar.Importer, out importer))
        {
            _warnings.Add(new AssetWarning(path, $"its importer, {sidecar.Importer}, isn't installed"));
        }
        byte[] settings = [];
        if (importer is not null)
        {
            try
            {
                settings = importer.CanonicalSettings(sidecar.SettingsJson);
            }
            catch (System.Text.Json.JsonException exception)
            {
                _warnings.Add(new AssetWarning(path + Sidecar.Extension, $"has settings {importer.Name} can't read: {exception.Message}"));
                importer = null;
            }
        }
        if (rewrite)
        {
            Files.WriteAtomically(sidecarPath, sidecar.Write(importer));
        }

        AssetEntry entry = new(sidecar.Id, path, sidecar.Importer)
        {
            Settings = settings,
            Hash = _hashes[path],
            SubAssets = sidecar.SubAssets,
        };
        _byId[entry.Id] = entry;
        _byPath[path] = entry;
        AddSubAssets(entry);
    }

    private void AddSubAssets(AssetEntry parent)
    {
        foreach ((string name, AssetId id) in parent.SubAssets)
        {
            _byId[id] = new AssetEntry(id, parent.Path, parent.Importer) { Parent = parent.Id, SubAssetName = name };
        }
    }

    // Crucible's own files keep their GUID inside (ADR 0021), as their first
    // property, and have no sidecar.
    private void AddOwnFile(string path, string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        AssetId id = Scene.ReadId(bytes);
        if (id.IsNone)
        {
            _warnings.Add(new AssetWarning(path, $"has no {Scene.GuidProperty} as its first property, so it is skipped"));
            return;
        }
        if (_byId.TryGetValue(id, out AssetEntry? original))
        {
            // A copy: a new GUID, replaced as text so nothing else in the file moves.
            AssetId fresh = AssetId.New();
            string text = Encoding.UTF8.GetString(bytes);
            int at = text.IndexOf(id.ToString(), StringComparison.OrdinalIgnoreCase);
            Files.WriteAtomically(fullPath, Encoding.UTF8.GetBytes(text.Remove(at, 36).Insert(at, fresh.ToString())));
            _warnings.Add(new AssetWarning(path, $"had the same GUID as {original.Path} (a copy?), so it was given a new one"));
            _hashes[path] = ContentHash.OfFile(fullPath);
            id = fresh;
        }
        // An importer that takes the extension imports it, without settings
        // (materials, ADR 0052); scenes and prefabs are read as they are.
        Importer? importer = _importersByExtension.GetValueOrDefault(Path.GetExtension(path).ToLowerInvariant());
        AssetEntry entry = new(id, path, importer?.Name ?? "") { Hash = _hashes[path] };
        _byId[id] = entry;
        _byPath[path] = entry;
    }

    // ---------------------------------------------------------------------
    // Importing
    // ---------------------------------------------------------------------

    /// <summary>Imports every asset that needs it.</summary>
    public IReadOnlyList<ImportResult> ImportAll()
    {
        List<ImportResult> results = [.. Assets.Select(entry => Import(entry))];
        Files.WriteAtomically(RecordsPath, Json.Serialize(_records));
        return results;
    }

    /// <summary>Imports one asset if it needs it, or with <paramref name="force"/> anyway (the editor's Reimport, ADR 0071).</summary>
    public ImportResult Import(AssetId id, bool force = false)
    {
        AssetEntry entry = Find(id) ?? throw new ArgumentException($"There is no asset {id}.", nameof(id));
        ImportResult result = Import(entry, force);
        Files.WriteAtomically(RecordsPath, Json.Serialize(_records));
        return result;
    }

    /// <summary>
    /// Prefabs for instancing (ADR 0045): <c>.crprefab</c> files as they are on
    /// disk, and models as imported, importing them first if needed.
    /// </summary>
    public PrefabLibrary Prefabs() => new(ReadPrefab);

    private byte[]? ReadPrefab(AssetId id)
    {
        if (Find(id) is not { } entry)
        {
            return null;
        }
        if (entry.Path.EndsWith(Prefab.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllBytes(FullPath(Project.AssetsFolder, entry.Path));
        }
        string? output = ImportedFile(id) ?? Import(id).OutputPath;
        return output is null ? null : File.ReadAllBytes(output);
    }

    /// <summary>The asset's imported file, if its import is up to date; null otherwise.</summary>
    public string? ImportedFile(AssetId id)
    {
        AssetEntry? entry = Find(id);
        if (entry is { Parent.IsNone: false })
        {
            // Up to date with its parent: its key is made from the parent's.
            if (ImportedFile(entry.Parent) is null ||
                !_records.Assets.TryGetValue(entry.Parent.ToString(), out ImportRecord? parent) ||
                !_records.Assets.TryGetValue(id.ToString(), out ImportRecord? sub) ||
                sub.Key != SubKey(parent.Key, entry.SubAssetName))
            {
                return null;
            }
            string subOutput = OutputPath(sub.Key, sub.Extension);
            return File.Exists(subOutput) ? subOutput : null;
        }
        if (entry is null || !_records.Assets.TryGetValue(id.ToString(), out ImportRecord? record) ||
            !_importersByName.TryGetValue(entry.Importer, out Importer? importer))
        {
            return null;
        }
        string? key = KeyOf(entry, importer, record.Dependencies.Keys);
        string output = OutputPath(record.Key, importer.OutputExtension);
        return key == record.Key && File.Exists(output) ? output : null;
    }

    private ImportResult Import(AssetEntry entry, bool force = false)
    {
        if (!entry.Parent.IsNone)
        {
            // Made by its parent's import.
            ImportResult parent = Import(_byId[entry.Parent], force);
            string? output = parent.Outcome is ImportOutcome.Imported or ImportOutcome.UpToDate
                ? ImportedFile(entry.Id)
                : null;
            ImportOutcome outcome = output is not null ? parent.Outcome : ImportOutcome.Failed;
            string? error = output is not null ? null : parent.Error ?? $"its import no longer makes {entry.SubAssetName}";
            return new ImportResult(entry.Id, entry.Path, outcome, output, error) { Warnings = parent.Warnings };
        }
        if (!_importersByName.TryGetValue(entry.Importer, out Importer? importer))
        {
            return new ImportResult(entry.Id, entry.Path, ImportOutcome.NoImporter, null, null);
        }
        string? cached = force ? null : ImportedFile(entry.Id);
        if (cached is not null)
        {
            return new ImportResult(entry.Id, entry.Path, ImportOutcome.UpToDate, cached, null);
        }

        using ImportContext context = new(entry.Id, entry.Path, Project.AssetsFolder,
            path => Find(path)?.Id ?? AssetId.None, ImportedFor);
        try
        {
            importer.Run(context, entry.Settings);
            if (context.Output is null)
            {
                throw new InvalidOperationException($"{importer.Name} wrote no output.");
            }
            // The key uses the dependencies as they were read during the
            // import, so a file changed meanwhile is caught next time.
            string key = Key(entry, importer, context.Dependencies);
            string output = OutputPath(key, importer.OutputExtension);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            context.Output.Write(output, Compression.Fast);
            _records.Assets[entry.Id.ToString()] = new ImportRecord
            {
                Key = key,
                Dependencies = new Dictionary<string, string>(context.Dependencies, StringComparer.Ordinal),
            };
            WriteSubAssets(entry, importer, key, context.SubAssets);
            return new ImportResult(entry.Id, entry.Path, ImportOutcome.Imported, output, null)
            {
                Warnings = [.. context.Warnings],
            };
        }
        catch (Exception exception)
        {
            _records.Assets.Remove(entry.Id.ToString());
            foreach (AssetId sub in entry.SubAssets.Values)
            {
                _records.Assets.Remove(sub.ToString());
            }
            return new ImportResult(entry.Id, entry.Path, ImportOutcome.Failed, null, exception.Message)
            {
                Warnings = [.. context.Warnings],
            };
        }
    }

    // Writes an import's sub-assets, each keyed by its parent's key and its
    // name, and keeps their IDs in the parent's sidecar: the same name keeps
    // its ID, a new one gets one, and one no longer made is forgotten.
    private void WriteSubAssets(AssetEntry entry, Importer importer, string key,
        IReadOnlyList<(string Name, string Extension, ImportedFileWriter Output)> made)
    {
        Dictionary<string, AssetId> ids = new(StringComparer.Ordinal);
        foreach ((string name, string extension, ImportedFileWriter writer) in made)
        {
            AssetId id = entry.SubAssets.TryGetValue(name, out AssetId kept) ? kept : AssetId.New();
            ids[name] = id;
            string subKey = SubKey(key, name);
            string output = OutputPath(subKey, extension);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            writer.Write(output, Compression.Fast);
            _records.Assets[id.ToString()] = new ImportRecord { Key = subKey, Extension = extension };
        }
        bool changed = ids.Count != entry.SubAssets.Count || ids.Any(pair =>
            !entry.SubAssets.TryGetValue(pair.Key, out AssetId old) || old != pair.Value);
        if (!changed)
        {
            return;
        }
        foreach ((string name, AssetId stale) in entry.SubAssets)
        {
            if (!ids.ContainsKey(name))
            {
                _records.Assets.Remove(stale.ToString());
                _byId.Remove(stale);
            }
        }
        AssetEntry updated = entry with { SubAssets = ids };
        _byId[entry.Id] = updated;
        _byPath[entry.Path] = updated;
        AddSubAssets(updated);
        string sidecarPath = FullPath(Project.AssetsFolder, entry.Path) + Sidecar.Extension;
        Sidecar sidecar = new(entry.Id, entry.Importer, entry.Settings) { SubAssets = ids };
        Files.WriteAtomically(sidecarPath, sidecar.Write(importer));
    }

    private static string SubKey(string parentKey, string name) =>
        ContentHash.Of(Encoding.UTF8.GetBytes(parentKey + "\n" + name));

    // Another asset's imported file and what it depends on, importing it
    // first if it needs it (a material's shader), or null.
    private (string Path, string Imported, IReadOnlyCollection<string> Dependencies)? ImportedFor(AssetId id)
    {
        AssetEntry? entry = Find(id);
        if (entry is null)
        {
            return null;
        }
        string? output = ImportedFile(id) ?? Import(entry).OutputPath;
        if (output is null)
        {
            return null;
        }
        IReadOnlyCollection<string> dependencies =
            _records.Assets.TryGetValue(id.ToString(), out ImportRecord? record) ? record.Dependencies.Keys : [];
        return (entry.Path, output, dependencies);
    }

    // The key with the dependencies' hashes now, or null if one is gone.
    private string? KeyOf(AssetEntry entry, Importer importer, IEnumerable<string> dependencies)
    {
        SortedDictionary<string, string> hashes = new(StringComparer.Ordinal);
        foreach (string dependency in dependencies)
        {
            string fullPath = FullPath(Project.AssetsFolder, dependency);
            if (_hashes.TryGetValue(dependency, out string? hash))
            {
                hashes[dependency] = hash;
            }
            else if (File.Exists(fullPath))
            {
                hashes[dependency] = ContentHash.OfFile(fullPath); // outside the scan (a sidecar, say)
            }
            else
            {
                return null;
            }
        }
        return Key(entry, importer, hashes);
    }

    // XXH128 of everything an import depends on: change any of it and the
    // key changes, so the cached output is no longer found.
    private static string Key(AssetEntry entry, Importer importer, IReadOnlyDictionary<string, string> dependencies)
    {
        StringBuilder text = new();
        text.Append("crucible-import ").Append(KeyFormat).Append('\n')
            .Append(importer.Name).Append(' ').Append(importer.Version).Append('\n')
            .Append(entry.Hash).Append('\n');
        foreach ((string path, string hash) in dependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append(path).Append('=').Append(hash).Append('\n');
        }
        byte[] header = Encoding.UTF8.GetBytes(text.ToString());
        return ContentHash.Of([.. header, .. entry.Settings]);
    }

    private string OutputPath(string key, string extension) =>
        Path.Combine(ImportedFolder, key[..2], key + extension);

    // ---------------------------------------------------------------------

    internal static string AssetPath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    internal static string FullPath(string root, string assetPath) =>
        Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar));

    // The database's own files are a cache too: unreadable means start over.
    private static T ReadOrDefault<T>(string path) where T : class, IJsonSerialized<T>, new()
    {
        try
        {
            return File.Exists(path) ? Json.Deserialize<T>(File.ReadAllBytes(path)) : new T();
        }
        catch (System.Text.Json.JsonException)
        {
            return new T();
        }
    }
}
