using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>A project file's contents (ADR 0040).</summary>
[Serialized]
public partial class ProjectFile
{
    public string Name = "";
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

    private Project(string projectFile, string name)
    {
        ProjectFilePath = Path.GetFullPath(projectFile);
        Root = Path.GetDirectoryName(ProjectFilePath)!;
        Name = name;
    }

    public string Name { get; }
    public string ProjectFilePath { get; }
    public string Root { get; }
    public string AssetsFolder => Path.Combine(Root, "Assets");
    public string CodeFolder => Path.Combine(Root, "Code");
    public string PluginsFolder => Path.Combine(Root, "Plugins");
    public string CacheFolder => Path.Combine(Root, "Cache");

    public static Project Open(string projectFile)
    {
        ProjectFile file = Json.Deserialize<ProjectFile>(File.ReadAllBytes(projectFile));
        Project project = new(projectFile, file.Name);
        Directory.CreateDirectory(project.AssetsFolder);
        Directory.CreateDirectory(project.CacheFolder);
        return project;
    }

    /// <summary>Makes a new project in <paramref name="folder"/>, with a .gitignore keeping the cache out.</summary>
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
            File.WriteAllText(ignore, "/Cache/\n");
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
