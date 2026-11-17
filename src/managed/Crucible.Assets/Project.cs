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
        BuildSettings? build = null)
    {
        ProjectFilePath = Path.GetFullPath(projectFile);
        Root = Path.GetDirectoryName(ProjectFilePath)!;
        Name = name;
        PhysicsLayers = physicsLayers ?? [];
        Build = build ?? new BuildSettings();
    }

    public string Name { get; }

    /// <summary>The physics layers' names, by layer (ADR 0075): what layer masks show.</summary>
    public IReadOnlyList<string> PhysicsLayers { get; }

    /// <summary>What a cook packs (ADR 0098); <see cref="SaveBuild"/> changes it.</summary>
    public BuildSettings Build { get; private set; }

    /// <summary>The default folder a cook writes to: <c>Build/Cooked</c> in the project.</summary>
    public string CookedFolder => Path.Combine(Root, "Build", "Cooked");
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
        Project project = new(projectFile, file.Name, [.. file.PhysicsLayers.Take(32)], file.Build);
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
