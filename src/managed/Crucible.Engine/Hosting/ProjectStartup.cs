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
/// projects itself, so the editor launcher skips this.
/// </summary>
public static class ProjectStartup
{
    /// <summary>Opens the project and loads the scene; null if the arguments name no project and scene.</summary>
    public static AssetCache? Open(World world, IReadOnlyDictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("project", out string? project) ||
            !arguments.TryGetValue("scene", out string? scene))
        {
            return null;
        }
        AssetCache assets = AssetCache.Open(project);
        RenderAssets.SetReader(assets.Read);
        SceneLoad loaded = Scene.Load(world, File.ReadAllBytes(scene), prefabs: assets.Prefabs());
        Console.WriteLine($"Crucible.Hosting: opened {Path.GetFileName(scene)}: {loaded.Roots.Count} entities at the top");
        foreach (string skipped in loaded.SkippedComponents)
        {
            Console.WriteLine($"Crucible.Hosting: {Path.GetFileName(scene)} has {skipped}, which nothing registered");
        }
        return assets;
    }
}
