using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>A project file's contents (ADR 0040).</summary>
[Serialized]
public partial class ProjectFile
{
    public string Name = "";

    /// <summary>The physics layers' names, by layer, up to 32 (ADR 0075); "" for an unnamed layer.</summary>
    public List<string> PhysicsLayers = [];

    /// <summary>What a cook packs (ADR 0098).</summary>
    public BuildSettings Build = new();

    /// <summary>The sorting layers, in drawing order (ADR 0104); "Default" when there are none.</summary>
    public List<SortingLayer> SortingLayers = [];
}

/// <summary>What cooking packs (ADR 0098): the project file's "Build" section.</summary>
[Serialized]
public partial class BuildSettings
{
    /// <summary>The root scenes, by ID; the first is the one the game starts with.</summary>
    public List<AssetId> Scenes = [];

    /// <summary>
    /// Assets, or folders of them, always packed, by their paths inside
    /// <c>Assets/</c>: what game code loads by ID, which no walk sees.
    /// </summary>
    public List<string> Include = [];

    /// <summary>Whether a package's assemblies are precompiled with ReadyToRun (ADR 0001, 0100); JIT only.</summary>
    public bool ReadyToRun = true;

    /// <summary>How a package runs its C# (ADR 0001, 0101): on .NET's JIT, or compiled ahead with NativeAOT.</summary>
    public PackageRuntime Runtime = PackageRuntime.Jit;

    /// <summary>
    /// Whether a package is a Shipping one, information messages compiled
    /// out, from the shipping preset's build, or a Development one, every
    /// message kept, from the editor's own (ADR 0103).
    /// </summary>
    public PackageConfiguration Configuration = PackageConfiguration.Shipping;
}

/// <summary>A package's configuration (ADR 0103).</summary>
public enum PackageConfiguration
{
    /// <summary>Information messages compiled out; from the shipping preset's build.</summary>
    Shipping,

    /// <summary>Every message kept; from the editor's own build.</summary>
    Development,
}

/// <summary>A package's runtime (ADR 0001, 0101).</summary>
public enum PackageRuntime
{
    /// <summary>.NET with its JIT, shipped in the package: loads mods.</summary>
    Jit,

    /// <summary>Compiled ahead with NativeAOT into one executable: no mods.</summary>
    NativeAot,
}

/// <summary>
/// A Crucible project (ADR 0040): a folder holding <c>&lt;Name&gt;.crproject</c>,
/// <c>Assets/</c> (content and sidecars), <c>Code/</c> (the game's C#),
/// <c>Plugins/</c>, and <c>Cache/</c> (imported data and the database's own
/// files, never in source control). Names are placeholders (docs/naming.md).
/// </summary>
public sealed class Project
{
    public const string Extension = ".crproject";

    private Project(string projectFile, string name, IReadOnlyList<string>? physicsLayers = null,
        BuildSettings? build = null, IReadOnlyList<SortingLayer>? sortingLayers = null)
    {
        ProjectFilePath = Path.GetFullPath(projectFile);
        Root = Path.GetDirectoryName(ProjectFilePath)!;
        Name = name;
        PhysicsLayers = physicsLayers ?? [];
        Build = build ?? new BuildSettings();
        SortingLayers = sortingLayers is { Count: > 0 } ? sortingLayers : [new SortingLayer { Name = "Default" }];
    }

    public string Name { get; }

    /// <summary>The physics layers' names, by layer (ADR 0075): what layer masks show.</summary>
    public IReadOnlyList<string> PhysicsLayers { get; }

    /// <summary>What a cook packs (ADR 0098); <see cref="SaveBuild"/> changes it.</summary>
    public BuildSettings Build { get; private set; }

    /// <summary>The sorting layers, in drawing order (ADR 0104): "Default" alone when the file lists none.</summary>
    public IReadOnlyList<SortingLayer> SortingLayers { get; }

    /// <summary>What a running game needs of the project (ADR 0104): the sorting layers.</summary>
    public RuntimeSettings RuntimeSettings => new() { SortingLayers = [.. SortingLayers] };

    /// <summary>
    /// The folder Build writes the package to (ADR 0100, 0101, 0103):
    /// <c>Build/Linux/JIT</c>, <c>Build/Windows/AOT-Development</c>, and so on
    /// in the project, for the platform it runs on and the build settings'
    /// runtime and configuration.
    /// </summary>
    public string PackageFolder => Path.Combine(Root, "Build", OperatingSystem.IsWindows() ? "Windows" : "Linux",
        (Build.Runtime == PackageRuntime.NativeAot ? "AOT" : "JIT") +
        (Build.Configuration == PackageConfiguration.Development ? "-Development" : ""));

    /// <summary>Where a package keeps its archive, assemblies, and runtime: its <c>Data</c> folder.</summary>
    public string PackageDataFolder => Path.Combine(PackageFolder, "Data");
    public string ProjectFilePath { get; }
    public string Root { get; }
    public string AssetsFolder => Path.Combine(Root, "Assets");
    public string CodeFolder => Path.Combine(Root, "Code");
    public string NativeFolder => Path.Combine(Root, "Native");
    public string PluginsFolder => Path.Combine(Root, "Plugins");
    public string CacheFolder => Path.Combine(Root, "Cache");

    public static Project Open(string projectFile)
    {
        ProjectFile file = Json.Deserialize<ProjectFile>(File.ReadAllBytes(projectFile));
        Project project = new(projectFile, file.Name, [.. file.PhysicsLayers.Take(32)], file.Build,
            [.. file.SortingLayers.Take(32)]);
        Directory.CreateDirectory(project.AssetsFolder);
        Directory.CreateDirectory(project.CacheFolder);
        return project;
    }

    /// <summary>
    /// Saves new build settings into the project file, keeping the rest of
    /// what it holds as it is.
    /// </summary>
    public void SaveBuild(BuildSettings build)
    {
        ProjectFile file = Json.Deserialize<ProjectFile>(File.ReadAllBytes(ProjectFilePath));
        file.Build = build;
        Files.WriteAtomically(ProjectFilePath, Json.Serialize(file));
        Build = build;
    }

    /// <summary>Makes a new project in <paramref name="folder"/>, with a .gitignore keeping the cache and cooked builds out.</summary>
    public static Project Create(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string projectFile = Path.Combine(folder, name + Extension);
        Files.WriteAtomically(projectFile, Json.Serialize(new ProjectFile { Name = name }));
        Project project = new(projectFile, name);
        foreach (string child in (string[])[project.AssetsFolder, project.CodeFolder, project.PluginsFolder, project.CacheFolder])
        {
            Directory.CreateDirectory(child);
        }
        string ignore = Path.Combine(folder, ".gitignore");
        if (!File.Exists(ignore))
        {
            File.WriteAllText(ignore, "/Cache/\n/Build/\n");
        }
        return project;
    }
}

internal static class Files
{
    // A temporary file beside the target, then a rename over it: readers see
    // the old file or the new one, never half of one.
    public static void WriteAtomically(string path, ReadOnlySpan<byte> contents)
    {
        string temporary = $"{path}.tmp-{Environment.ProcessId}-{Guid.NewGuid():N}";
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write))
            {
                stream.Write(contents);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
