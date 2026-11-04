using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;

namespace Crucible.Editor;

/// <summary>
/// The user's editor settings (ADR 0066), one file for every project:
/// <c>editor.json</c> in the platform's preferences folder, holding the
/// recent projects and the window layout. A missing or unreadable file gives
/// the defaults (the unreadable one is reported, and replaced on save).
/// </summary>
public sealed class EditorSettings
{
    public const int MaxRecentProjects = 10;

    /// <summary>Project files, most recent first.</summary>
    public List<string> RecentProjects { get; } = [];

    /// <summary>The windows' layout as last saved (<see cref="Layout"/>'s format); null for the default.</summary>
    public JsonObject? Layout { get; set; }

    /// <summary>Where the settings live; null when the platform has no preferences folder.</summary>
    public static string? DefaultPath
    {
        get
        {
            string folder = Platform.Api.PreferencesPath();
            return folder.Length == 0 ? null : Path.Combine(folder, "editor.json");
        }
    }

    public static EditorSettings Load(string? path)
    {
        EditorSettings settings = new();
        if (path is null || !File.Exists(path))
        {
            return settings;
        }
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root)
            {
                throw new JsonException("not an object");
            }
            if (root["RecentProjects"] is JsonArray recent)
            {
                foreach (JsonNode? entry in recent)
                {
                    if (entry?.GetValueKind() == JsonValueKind.String)
                    {
                        settings.RecentProjects.Add(entry.GetValue<string>());
                    }
                }
            }
            settings.Layout = root["Layout"]?.DeepClone() as JsonObject;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Crucible.Editor: {path} can't be read ({exception.Message}); using the defaults");
        }
        return settings;
    }

    /// <summary>Writes the settings (atomically: a crash never leaves half a file).</summary>
    public void Save(string? path)
    {
        if (path is null)
        {
            return;
        }
        JsonObject root = new()
        {
            ["RecentProjects"] = new JsonArray([.. RecentProjects.Select(file => (JsonNode)JsonValue.Create(file))]),
        };
        if (Layout is not null)
        {
            root["Layout"] = Layout.DeepClone();
        }
        Files.WriteAtomically(path, Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
    }

    /// <summary>Puts a project first in the recent list (once), keeping at most <see cref="MaxRecentProjects"/>.</summary>
    public void AddRecentProject(string projectFile)
    {
        string full = Path.GetFullPath(projectFile);
        RecentProjects.RemoveAll(entry => string.Equals(entry, full, StringComparison.Ordinal));
        RecentProjects.Insert(0, full);
        if (RecentProjects.Count > MaxRecentProjects)
        {
            RecentProjects.RemoveRange(MaxRecentProjects, RecentProjects.Count - MaxRecentProjects);
        }
    }
}
