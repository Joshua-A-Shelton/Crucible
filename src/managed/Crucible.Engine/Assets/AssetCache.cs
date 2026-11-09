using Crucible.Scenes;
using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// A project's imported assets as a game reads them (ADR 0073): what the
/// editor's asset database left in the project's Cache folder, read-only,
/// with no importers. <c>Cache/imports.json</c> names each asset's import by
/// its key, and the file is <c>Cache/Imported/&lt;first two&gt;/&lt;key&gt;.*</c>.
/// Prefab files (.crprefab) aren't imported: they're found in Assets/ by
/// their sidecars. Imports are trusted as they are: the editor imports what
/// changed before it runs a project.
/// </summary>
public sealed class AssetCache
{
    private readonly Dictionary<AssetId, string> _files = [];

    private AssetCache(string root) => Root = root;

    /// <summary>The project's folder.</summary>
    public string Root { get; }

    public string CacheFolder => Path.Combine(Root, "Cache");

    /// <summary>How many assets it can read.</summary>
    public int Count => _files.Count;

    /// <summary>Opens the cache of the project whose .crproject file this is.</summary>
    public static AssetCache Open(string projectFile)
    {
        AssetCache cache = new(Path.GetDirectoryName(Path.GetFullPath(projectFile))!);
        cache.ReadImports();
        cache.FindPrefabs();
        return cache;
    }

    /// <summary>The asset's file: its imported output, or a prefab file; null if it has none.</summary>
    public string? FileOf(AssetId id) => _files.GetValueOrDefault(id);

    /// <summary>The asset's bytes, or null if it has none (the renderer's asset reader).</summary>
    public byte[]? Read(AssetId id) => FileOf(id) is { } path && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>Prefabs for a scene's instances: imported models and prefab files.</summary>
    public PrefabLibrary Prefabs() => new(Read);

    private void ReadImports()
    {
        string records = Path.Combine(CacheFolder, "imports.json");
        if (!File.Exists(records))
        {
            return;
        }
        CachedImports imports = Json.Deserialize<CachedImports>(File.ReadAllBytes(records));
        foreach ((string id, CachedImport import) in imports.Assets)
        {
            if (!AssetId.TryParse(id, out AssetId parsed) || import.Key.Length < 2)
            {
                continue;
            }
            string folder = Path.Combine(CacheFolder, "Imported", import.Key[..2]);
            // The importer's extension isn't recorded: the key names one file.
            string? file = Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, import.Key + ".*").FirstOrDefault()
                : null;
            if (file is not null)
            {
                _files[parsed] = file;
            }
        }
    }

    private void FindPrefabs()
    {
        string assets = Path.Combine(Root, "Assets");
        if (!Directory.Exists(assets))
        {
            return;
        }
        foreach (string sidecar in Directory.EnumerateFiles(assets, "*" + Prefab.Extension + ".crmeta",
                     SearchOption.AllDirectories))
        {
            CachedSidecar read = Json.Deserialize<CachedSidecar>(File.ReadAllBytes(sidecar));
            if (AssetId.TryParse(read.Guid, out AssetId id))
            {
                _files[id] = sidecar[..^".crmeta".Length];
            }
        }
    }
}

// What AssetCache reads of the asset database's files (Crucible.Assets'
// ImportRecordFile and sidecars); the rest is skipped.
[Serialized]
internal sealed partial class CachedImports
{
    public Dictionary<string, CachedImport> Assets = [];
}

[Serialized]
internal sealed partial class CachedImport
{
    public string Key = "";
}

[Serialized]
internal sealed partial class CachedSidecar
{
    public string Guid = "";
}
