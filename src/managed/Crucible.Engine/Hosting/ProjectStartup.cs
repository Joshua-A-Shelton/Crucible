using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Render;
using Crucible.Scenes;

namespace Crucible.Hosting;

/// <summary>
/// A project run as a game (ADR 0073): given the game arguments
/// <c>project=</c> and <c>scene=</c> (the launcher's --project and --scene),
/// the host opens the project's imported assets, points the renderer at
/// them, and loads the scene into the main world before the game's systems
/// start. A project needs no setup code of its own. The editor opens
/// projects itself, so the editor launcher skips this. Given
/// <c>archive=</c> instead (the launcher's --archive, ADR 0099), the assets
/// come from a cooked archive and the scene is its start scene, or the one
/// <c>scene=</c> names by ID.
/// </summary>
public static class ProjectStartup
{
    /// <summary>The archive a run reads its assets from, once opened; open while the run lasts.</summary>
    public static Archive? OpenedArchive { get; private set; }

    /// <summary>Opens the project and loads the scene; null if the arguments name no project and scene.</summary>
    public static AssetCache? Open(World world, IReadOnlyDictionary<string, string> arguments)
    {
        if (arguments.TryGetValue("archive", out string? archive))
        {
            OpenArchive(world, archive, arguments.GetValueOrDefault("scene"));
            return null;
        }
        if (!arguments.TryGetValue("project", out string? project) ||
            !arguments.TryGetValue("scene", out string? scene))
        {
            return null;
        }
        AssetCache assets = AssetCache.Open(project);
        // The project's imported files, unless a native module installed its
        // own source (pack files, ADR 0076); prefabs come through it too.
        AssetSource.InstallDefault(assets.Read);
        Load(world, File.ReadAllBytes(scene), Path.GetFileName(scene));
        return assets;
    }

    // The archive as the asset source (replacing any), and its scene.
    private static void OpenArchive(World world, string path, string? scene)
    {
        Archive archive;
        try
        {
            archive = Archive.Open(path);
        }
        catch (CrucibleException exception)
        {
            Console.Error.WriteLine(exception.Status == Status.Unsupported
                ? $"Crucible.Hosting: the archive {path} was cooked by another version of Crucible: cook it again"
                : $"Crucible.Hosting: can't open the archive {path}: {exception.Status}");
            return;
        }
        OpenedArchive = archive;
        archive.Install();
        AssetId start = archive.StartScene;
        if (scene is not null && !AssetId.TryParse(scene, out start))
        {
            Console.Error.WriteLine($"Crucible.Hosting: --scene names {scene}, which isn't an asset ID");
            return;
        }
        if (AssetSource.Read(start) is not { } bytes)
        {
            Console.Error.WriteLine($"Crucible.Hosting: {Path.GetFileName(path)} has no scene {start}");
            return;
        }
        Console.WriteLine($"Crucible.Hosting: reading {archive.Count} assets from {Path.GetFileName(path)}");
        Load(world, bytes, start.ToString());
    }

    private static void Load(World world, byte[] scene, string name)
    {
        SceneLoad loaded = Scene.Load(world, scene, prefabs: PrefabLibrary.FromAssetSource());
        Console.WriteLine($"Crucible.Hosting: opened {name}: {loaded.Roots.Count} entities at the top");
        foreach (string skipped in loaded.SkippedComponents)
        {
            Console.WriteLine($"Crucible.Hosting: {name} has {skipped}, which nothing registered");
        }
    }
}
