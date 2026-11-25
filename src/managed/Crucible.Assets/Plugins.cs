using Crucible.Serialization;

namespace Crucible.Assets;

/// <summary>
/// A plugin's manifest (ADR 0118), <c>Plugins/&lt;Name&gt;/&lt;Name&gt;.crplugin</c>:
/// its name, its own version, the plugins it uses, and for a compiled
/// plugin the Crucible version it was built with.
/// </summary>
[Serialized]
public partial class PluginManifest
{
    public string Name = "";

    /// <summary>The plugin's own version, as its author numbers it ("1.2.0"); shown, not checked.</summary>
    public string Version = "";

    /// <summary>The plugins it uses, by name: loaded before it, its code may reference theirs.</summary>
    public List<string> Uses = [];

    /// <summary>The Crucible version a compiled plugin was built with ("0.1.0"); empty for a source plugin.</summary>
    public string Crucible = "";
}

/// <summary>
/// A plugin in a project (ADR 0118): a folder in <c>Plugins/</c> with a
/// manifest, holding source (<c>Code/</c>, with <c>Editor/</c> folders as a
/// game's has) or a compiled pair of assemblies, and <c>Assets/</c>.
/// </summary>
public sealed class Plugin
{
    public const string Extension = ".crplugin";

    internal Plugin(string folder, PluginManifest manifest, bool isOfficial = false)
    {
        Folder = folder;
        Manifest = manifest;
        IsOfficial = isOfficial;
    }

    public string Name => Manifest.Name;

    public PluginManifest Manifest { get; }

    /// <summary>Its folder: <c>Plugins/&lt;Name&gt;</c>.</summary>
    public string Folder { get; }

    public string ManifestPath => Path.Combine(Folder, Name + Extension);

    /// <summary>
    /// One of Crucible's own (ADR 0123), shipped built beside the editor and
    /// used by a project unless its file leaves it out.
    /// </summary>
    public bool IsOfficial { get; }

    /// <summary>
    /// Built and shared as assemblies (its manifest names the Crucible it was
    /// built with; an official one is built with the editor), not source.
    /// </summary>
    public bool IsCompiled => IsOfficial || Manifest.Crucible.Length > 0;

    public string AssetsFolder => Path.Combine(Folder, "Assets");

    /// <summary>A source plugin's code; a compiled one has none.</summary>
    public string CodeFolder => Path.Combine(Folder, "Code");

    /// <summary>A compiled plugin's runtime assembly, <c>&lt;Name&gt;.dll</c>.</summary>
    public string Assembly => Path.Combine(Folder, Name + ".dll");

    /// <summary>A compiled plugin's editor assembly, <c>&lt;Name&gt;.Editor.dll</c>, if it has one.</summary>
    public string EditorAssembly => Path.Combine(Folder, Name + ".Editor.dll");

    /// <summary>Whether a compiled plugin has an editor assembly (an official one may have only that).</summary>
    public bool HasEditorAssembly => IsCompiled && File.Exists(EditorAssembly);

    /// <summary>Whether it has game code: .cs files under Code for a source plugin, its assembly for a compiled one.</summary>
    public bool HasCode => IsCompiled
        ? File.Exists(Assembly)
        : Directory.Exists(CodeFolder) && Directory.EnumerateFiles(CodeFolder, "*.cs", SearchOption.AllDirectories).Any();

    /// <summary>The root of its assets' paths, its name and a colon: "Dialogue:" (ADR 0118).</summary>
    public string AssetRoot => Name + AssetPaths.PluginSeparator;

    public override string ToString() => Name;
}

/// <summary>
/// The plugins in a project's <c>Plugins/</c> folder (ADR 0118), in the order
/// they load: each after the plugins it uses, otherwise by name. Plugins that
/// can't be used (a bad manifest, a Crucible version this one doesn't accept,
/// a plugin it uses that's missing or can't be used, plugins using each
/// other) are left out, with why.
/// </summary>
public static class ProjectPlugins
{
    /// <summary>
    /// Reads every plugin folder under <paramref name="pluginsFolder"/>, with
    /// the official plugins the project uses (ADR 0123), which its own may use.
    /// </summary>
    /// <param name="reserved">Names a plugin can't have: the game's assembly's, so the two don't clash.</param>
    /// <param name="official">The official plugins the project uses (<see cref="OfficialPlugins.Read"/>); their names are taken too.</param>
    public static (IReadOnlyList<Plugin> Plugins, IReadOnlyList<string> Problems) Read(string pluginsFolder, string? reserved = null,
        IReadOnlyList<Plugin>? official = null)
    {
        List<string> problems = [];
        Dictionary<string, Plugin> found = new(StringComparer.Ordinal);
        foreach (Plugin plugin in official ?? [])
        {
            found[plugin.Name] = plugin;
        }
        if (Directory.Exists(pluginsFolder))
        {
            foreach (string folder in Directory.EnumerateDirectories(pluginsFolder).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(folder);
                if (name.StartsWith('.'))
                {
                    continue;
                }
                if (found.TryGetValue(name, out Plugin? taken) && taken.IsOfficial)
                {
                    problems.Add($"Plugins/{name} has the name of an official plugin; rename it");
                    continue;
                }
                if (ReadOne(folder, name, reserved, problems) is { } plugin)
                {
                    found[plugin.Name] = plugin;
                }
            }
        }
        return (Order(found, problems), problems);
    }

    internal static Plugin? ReadOne(string folder, string name, string? reserved, List<string> problems, bool official = false)
    {
        string manifestPath = Path.Combine(folder, name + Plugin.Extension);
        if (!File.Exists(manifestPath))
        {
            problems.Add($"Plugins/{name} has no {name}{Plugin.Extension}, so it isn't a plugin");
            return null;
        }
        PluginManifest manifest;
        try
        {
            manifest = Json.Deserialize<PluginManifest>(File.ReadAllBytes(manifestPath));
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or IOException)
        {
            problems.Add($"{name}{Plugin.Extension} can't be read: {exception.Message}");
            return null;
        }
        if (manifest.Name != name)
        {
            problems.Add($"{name}{Plugin.Extension} names the plugin \"{manifest.Name}\": a plugin's name is its folder's");
            return null;
        }
        if (!IsName(name) || string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(IsName(name)
                ? $"{name} can't be a plugin's name: it's the game's"
                : $"{name} can't be a plugin's name: use letters, digits, and _, not starting with a digit");
            return null;
        }
        Plugin plugin = new(folder, manifest, official);
        if (plugin.IsCompiled && !official)
        {
            if (CrucibleVersion.LineOf(manifest.Crucible) is not { } line)
            {
                problems.Add($"{name}{Plugin.Extension} has a Crucible version that isn't one: \"{manifest.Crucible}\"");
                return null;
            }
            if (!CrucibleVersion.Accepts(manifest.Crucible))
            {
                problems.Add($"{name} was built for Crucible {line}; this is {CrucibleVersion.Line}. Rebuild it for {CrucibleVersion.Line}");
                return null;
            }
            if (!File.Exists(plugin.Assembly) && Directory.Exists(plugin.CodeFolder))
            {
                problems.Add($"{name} is a compiled plugin with source in Code/ and no {name}.dll");
                return null;
            }
        }
        return plugin;
    }

    // Each plugin after those it uses; among those ready together, by name.
    // A plugin using one that's missing, left out, or in a cycle is left out too.
    private static List<Plugin> Order(Dictionary<string, Plugin> found, List<string> problems)
    {
        List<Plugin> ordered = [];
        HashSet<string> placed = new(StringComparer.Ordinal);
        HashSet<string> dropped = new(StringComparer.Ordinal);
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (Plugin plugin in found.Values.OrderBy(plugin => plugin.Name, StringComparer.Ordinal))
            {
                if (placed.Contains(plugin.Name) || dropped.Contains(plugin.Name))
                {
                    continue;
                }
                if (plugin.Manifest.Uses.FirstOrDefault(used => !found.ContainsKey(used) || dropped.Contains(used)) is { } missing)
                {
                    problems.Add(found.ContainsKey(missing)
                        ? $"{plugin.Name} uses {missing}, which can't be used"
                        : $"{plugin.Name} uses {missing}, which isn't in Plugins/");
                    dropped.Add(plugin.Name);
                    progress = true;
                    continue;
                }
                // A compiled plugin loads once; a source one reloads with the
                // game, so the compiled one can't be built on it.
                if (plugin.IsCompiled && plugin.Manifest.Uses.FirstOrDefault(used => !found[used].IsCompiled) is { } source)
                {
                    problems.Add($"{plugin.Name} is compiled and uses {source}, which is source: build {source} too");
                    dropped.Add(plugin.Name);
                    progress = true;
                    continue;
                }
                if (plugin.Manifest.Uses.All(placed.Contains))
                {
                    ordered.Add(plugin);
                    placed.Add(plugin.Name);
                    progress = true;
                }
            }
        }
        // What's left uses itself through others.
        List<Plugin> cycle = [.. found.Values.Where(plugin => !placed.Contains(plugin.Name) && !dropped.Contains(plugin.Name))
            .OrderBy(plugin => plugin.Name, StringComparer.Ordinal)];
        if (cycle.Count > 0)
        {
            problems.Add(cycle.Count == 2
                ? $"{cycle[0].Name} and {cycle[1].Name} use each other"
                : $"{string.Join(", ", cycle.SkipLast(1).Select(plugin => plugin.Name))} and {cycle[^1].Name} use each other");
        }
        return ordered;
    }

    /// <summary>A plugin's name: what its assemblies and its assets' paths are named after.</summary>
    public static bool IsName(string name) =>
        name.Length > 0 && !char.IsAsciiDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') &&
        !name.Equals("Crucible", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Crucible's official plugins (ADR 0123): built with the editor into a
/// <c>Plugins</c> folder beside it, read-only, each used by a project unless
/// its file's <c>Official</c> list leaves it out (all of them when the list
/// is <c>["*"]</c>, as it is by default).
/// </summary>
public static class OfficialPlugins
{
    /// <summary>What <see cref="ProjectFile.Official"/> holds to use every official plugin.</summary>
    public const string All = "*";

    /// <summary>
    /// Where they are: set by the editor when it starts (beside its
    /// assembly); null where there are none (the game, the tests).
    /// </summary>
    public static string? Folder { get; set; }

    /// <summary>Every official plugin's name in <paramref name="folder"/>, by name.</summary>
    public static IReadOnlyList<string> Names(string? folder) =>
        folder is not null && Directory.Exists(folder)
            ? [.. Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>()
                .Where(name => !name.StartsWith('.')).Order(StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// The official plugins in <paramref name="folder"/> a project uses: the
    /// names in <paramref name="used"/>, or all of them for <see cref="All"/>.
    /// A name that isn't one is a problem.
    /// </summary>
    public static IReadOnlyList<Plugin> Read(string? folder, IReadOnlyList<string> used, List<string> problems)
    {
        IReadOnlyList<string> names = Names(folder);
        List<Plugin> plugins = [];
        foreach (string name in used.Contains(All) ? names : used.Distinct(StringComparer.Ordinal))
        {
            if (!names.Contains(name))
            {
                problems.Add($"The project uses the official plugin {name}, which this editor doesn't have");
                continue;
            }
            if (ProjectPlugins.ReadOne(Path.Combine(folder!, name), name, reserved: null, problems, official: true) is { } plugin)
            {
                plugins.Add(plugin);
            }
        }
        return plugins;
    }
}

/// <summary>
/// Asset paths (ADR 0040, 0118): a game's asset by its path inside
/// <c>Assets/</c> ("Models/Walker.gltf"), a plugin's by its name, a colon,
/// and its path inside the plugin's <c>Assets/</c> ("Dialogue:Icons/Talk.png").
/// A colon can't be in a file's name on Windows, so the two never clash.
/// </summary>
public static class AssetPaths
{
    public const char PluginSeparator = ':';

    /// <summary>The plugin a path is in ("Dialogue"), or null for the game's own assets.</summary>
    public static string? PluginOf(string assetPath) =>
        assetPath.IndexOf(PluginSeparator) is var at and > 0 ? assetPath[..at] : null;

    /// <summary>The path inside its <c>Assets/</c> folder: "Icons/Talk.png" for "Dialogue:Icons/Talk.png".</summary>
    public static string WithinRoot(string assetPath) =>
        assetPath.IndexOf(PluginSeparator) is var at and > 0 ? assetPath[(at + 1)..] : assetPath;

    /// <summary>The root of a path: "Dialogue:" for a plugin's, "" for the game's.</summary>
    public static string RootOf(string assetPath) => PluginOf(assetPath) is { } plugin ? plugin + PluginSeparator : "";

    /// <summary>A name in a folder: "Models/Walker.gltf", "Dialogue:Icons" (in a plugin's root).</summary>
    public static string Join(string folder, string name) =>
        folder.Length == 0 || folder[^1] == PluginSeparator ? folder + name : folder + "/" + name;

    /// <summary>A path's folder ("" for one at the game's root, "Dialogue:" at a plugin's).</summary>
    public static string FolderOf(string assetPath)
    {
        int slash = assetPath.LastIndexOf('/');
        return slash >= 0 ? assetPath[..slash] : RootOf(assetPath);
    }
}
