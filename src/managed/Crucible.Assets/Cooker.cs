using System.Text.Json;

namespace Crucible.Assets;

/// <summary>What a cook did (ADR 0098).</summary>
public sealed record CookResult(string ArchivePath, int AssetCount, long Size)
{
    /// <summary>Problems that left an asset out: missing, or failing to import.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool Succeeded => Errors.Count == 0;
}

/// <summary>
/// Cooks a project (ADR 0098): from its build settings' root scenes and
/// include list, walks to everything they need, imports what isn't up to
/// date, and packs it all into one archive, <c>Game.crpak</c>.
/// </summary>
/// <remarks>
/// Scenes and prefabs are JSON: any string in them that is a known asset's
/// ID (a field of an asset kind, a prefab instance's prefab) is followed.
/// Imported files are followed through the references their importers
/// recorded (a material's shader and textures, a state machine's clips).
/// </remarks>
public sealed class Cooker(AssetDatabase database)
{
    public const string ArchiveName = "Game.crpak";

    /// <summary>
    /// Cooks into <paramref name="folder"/> (the project's
    /// <see cref="Project.PackageDataFolder"/> by default). With errors, nothing
    /// is written.
    /// </summary>
    public CookResult Cook(string? folder = null)
    {
        folder ??= database.Project.PackageDataFolder;
        BuildSettings build = database.Project.Build;
        List<string> errors = [];
        if (build.Scenes.Count == 0)
        {
            errors.Add("the build settings name no scenes");
        }

        ArchiveWriter archive = new() { StartScene = build.Scenes.FirstOrDefault() };
        HashSet<AssetId> seen = [];
        // What each packed asset names, for the scenes' asset lists.
        Dictionary<AssetId, List<AssetId>> links = [];
        List<AssetId> scenes = [];
        Queue<(AssetId Id, string From)> queue = new();
        foreach (AssetId scene in build.Scenes)
        {
            queue.Enqueue((scene, "the build settings' scenes"));
        }
        foreach (string included in build.Include)
        {
            string prefix = included.Trim('/') + "/";
            List<AssetEntry> found = [.. database.Assets.Where(entry =>
                entry.Path == included.Trim('/') || entry.Path.StartsWith(prefix, StringComparison.Ordinal))];
            if (found.Count == 0)
            {
                errors.Add($"the build settings include {included}, which isn't an asset or a folder of them");
            }
            foreach (AssetEntry entry in found)
            {
                queue.Enqueue((entry.Id, $"the build settings' include list ({included})"));
                foreach (AssetEntry sub in database.SubAssetsOf(entry.Id))
                {
                    queue.Enqueue((sub.Id, $"the build settings' include list ({included})"));
                }
            }
        }

        while (queue.TryDequeue(out (AssetId Id, string From) next))
        {
            if (!seen.Add(next.Id))
            {
                continue;
            }
            if (database.Find(next.Id) is not { } entry)
            {
                errors.Add($"{next.From} names {next.Id}, which isn't an asset");
                continue;
            }
            string from = entry.SubAssetName.Length > 0 ? $"{entry.Path} ({entry.SubAssetName})" : entry.Path;
            if (entry.Importer.Length == 0 && entry.Parent.IsNone)
            {
                if (!IsReadAsItIs(entry.Path))
                {
                    errors.Add($"{from}, which {next.From} needs, has no importer installed");
                    continue;
                }
                byte[] text = File.ReadAllBytes(AssetDatabase.FullPath(database.Project.AssetsFolder, entry.Path));
                archive.Add(entry.Id, text, database.KindOf(entry.Id));
                if (entry.Path.EndsWith(Crucible.Scenes.Scene.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    scenes.Add(entry.Id);
                }
                List<AssetId> named = [.. IdsIn(text).Where(id => id != entry.Id && database.Find(id) is not null)];
                links[entry.Id] = named;
                foreach (AssetId id in named)
                {
                    queue.Enqueue((id, from));
                }
                continue;
            }
            ImportResult imported = database.Import(entry.Id);
            if (imported.OutputPath is null)
            {
                errors.Add($"{from}, which {next.From} needs, didn't import: {imported.Error ?? imported.Outcome.ToString()}");
                continue;
            }
            archive.Add(entry.Id, File.ReadAllBytes(imported.OutputPath), database.KindOf(entry.Id));
            links[entry.Id] = [.. database.ReferencesOf(entry.Id)];
            foreach (AssetId referenced in database.ReferencesOf(entry.Id))
            {
                queue.Enqueue((referenced, from));
            }
        }

        string path = Path.Combine(folder, ArchiveName);
        if (errors.Count > 0)
        {
            return new CookResult(path, 0, 0) { Errors = errors };
        }
        foreach (AssetId scene in scenes)
        {
            archive.SetSceneAssets(scene, Reached(scene, links, scenes));
        }
        archive.Write(path);
        return new CookResult(path, archive.Count, new FileInfo(path).Length);
    }

    // Everything a scene reaches through the links, but not other scenes (a
    // scene naming another doesn't load it) nor what only they reach.
    private static HashSet<AssetId> Reached(AssetId scene, Dictionary<AssetId, List<AssetId>> links, List<AssetId> scenes)
    {
        HashSet<AssetId> reached = [];
        Stack<AssetId> pending = new([scene]);
        while (pending.TryPop(out AssetId next))
        {
            foreach (AssetId linked in links.GetValueOrDefault(next) ?? [])
            {
                if (linked != scene && !scenes.Contains(linked) && reached.Add(linked))
                {
                    pending.Push(linked);
                }
            }
        }
        return reached;
    }

    private static bool IsReadAsItIs(string path) =>
        path.EndsWith(Crucible.Scenes.Scene.Extension, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(Crucible.Scenes.Prefab.Extension, StringComparison.OrdinalIgnoreCase);

    // Every string in the JSON that is an asset ID.
    private static IEnumerable<AssetId> IdsIn(byte[] json)
    {
        List<AssetId> ids = [];
        Utf8JsonReader reader = new(json, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.String && reader.ValueSpan.Length == 36 &&
                AssetId.TryParse(reader.GetString(), out AssetId id) && !id.IsNone)
            {
                ids.Add(id);
            }
        }
        return ids;
    }
}
