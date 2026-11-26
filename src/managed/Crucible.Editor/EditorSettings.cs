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

    /// <summary>
    /// Always Open With (ADR 0120): the editor chosen for a kind of asset (its
    /// importer, or its file's extension) or a component (its name), by ID.
    /// </summary>
    public Dictionary<string, string> OpenWith { get; } = new(StringComparer.Ordinal);

    public const float DefaultInspectorNameWidth = 92;

    /// <summary>The Inspector's name column, as last dragged (ADR 0130).</summary>
    public float InspectorNameWidth { get; set; } = DefaultInspectorNameWidth;

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
            if (root["InspectorNameWidth"] is JsonValue nameWidth && nameWidth.TryGetValue(out double width) &&
                double.IsFinite(width))
            {
                settings.InspectorNameWidth = Math.Clamp((float)width, InspectorPanel.MinNameWidth, InspectorPanel.MaxNameWidth);
            }
            if (root["OpenWith"] is JsonObject openWith)
            {
                foreach ((string kind, JsonNode? editor) in openWith)
                {
                    if (editor?.GetValueKind() == JsonValueKind.String)
                    {
                        settings.OpenWith[kind] = editor.GetValue<string>();
                    }
                }
            }
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
        if (InspectorNameWidth != DefaultInspectorNameWidth)
        {
            root["InspectorNameWidth"] = InspectorNameWidth;
        }
        if (OpenWith.Count > 0)
        {
            JsonObject openWith = [];
            foreach ((string kind, string editor) in OpenWith.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                openWith[kind] = editor;
            }
            root["OpenWith"] = openWith;
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
