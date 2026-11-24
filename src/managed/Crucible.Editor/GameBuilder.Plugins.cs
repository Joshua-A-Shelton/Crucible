using System.Text;
using Crucible.Assets;

namespace Crucible.Editor;

/// <summary>
/// Plugins in a game's build (ADR 0118). Each source plugin with code gets a
/// project of its own in every variant of the build (the editor's, the
/// symbol check's, a package's), and an editor project beside it for its
/// Editor folders in the editor's; compiled plugins are referenced as
/// assemblies. The game's project references them all and carries their
/// list, in load order, for the game loader (<c>Plugins.g.cs</c>).
/// </summary>
public sealed partial class GameBuilder
{
    /// <summary>The generated file naming the game's plugins (ADR 0118), beside its project.</summary>
    public const string PluginListFile = "Plugins.g.cs";

    // The folder a variant's projects are in: the editor's (Cache/Build), the
    // symbol check's, or a package's.
    private string VariantFolder(bool package, bool? editorSymbol) =>
        Path.GetDirectoryName(package ? PackageProjectFile : editorSymbol == false ? SymbolCheckProjectFile : ProjectFile)!;

    // Where the editor's build writes every assembly: the game's, its editor
    // assembly, and its source plugins'.
    private string BinFolder => Path.GetDirectoryName(OutputPath)!;

    /// <summary>A source plugin's project in a variant's folder: Plugins/&lt;Name&gt;/&lt;Name&gt;.csproj.</summary>
    public static string PluginProjectFile(string variant, Plugin plugin) =>
        Path.Combine(variant, "Plugins", plugin.Name, plugin.Name + ".csproj");

    /// <summary>Its editor project: Plugins/&lt;Name&gt;/Editor/&lt;Name&gt;.Editor.csproj.</summary>
    public static string PluginEditorProjectFile(string variant, Plugin plugin) =>
        Path.Combine(variant, "Plugins", plugin.Name, EditorFolder, plugin.Name + ".Editor.csproj");

    /// <summary>Where the editor's build writes a source plugin's assembly.</summary>
    public string PluginOutputPath(Plugin plugin) => Path.Combine(BinFolder, plugin.Name + ".dll");

    /// <summary>And its editor assembly.</summary>
    public string PluginEditorOutputPath(Plugin plugin) => Path.Combine(BinFolder, plugin.Name + ".Editor.dll");

    /// <summary>Whether a source plugin has editor code: .cs files under a folder named Editor in its Code.</summary>
    public static bool HasPluginEditorCode(Plugin plugin) => !plugin.IsCompiled && EditorSourcesIn(plugin.CodeFolder).Any();

    // The editor project is built when the game or a source plugin has
    // editor code: it references them all, so its one build builds everything.
    private bool NeedsEditorProject => HasEditorCode || SourcePlugins.Any(HasPluginEditorCode);

    private IEnumerable<Plugin> Used(Plugin plugin) =>
        plugin.Manifest.Uses.Select(_project.PluginNamed).OfType<Plugin>().Where(used => used.HasCode);

    // References to plugins' runtime assemblies: a source one's project in
    // the variant, a compiled one's assembly. Copied where a package or a
    // standalone run needs them beside the game.
    private static string PluginReferences(IEnumerable<Plugin> plugins, string variant, bool copySource, bool copyCompiled)
    {
        StringBuilder text = new();
        foreach (Plugin plugin in plugins)
        {
            text.Append(plugin.IsCompiled
                ? $"    <Reference Include=\"{plugin.Name}\" HintPath=\"{Escape(plugin.Assembly)}\" Private=\"{Bool(copyCompiled)}\" />\n"
                : $"    <ProjectReference Include=\"{Escape(PluginProjectFile(variant, plugin))}\" Private=\"{Bool(copySource)}\" />\n");
        }
        return text.ToString();
    }

    // References to plugins' editor assemblies, for editor code using them.
    private static string PluginEditorReferences(IEnumerable<Plugin> plugins, string variant)
    {
        StringBuilder text = new();
        foreach (Plugin plugin in plugins)
        {
            if (plugin.IsCompiled && File.Exists(plugin.EditorAssembly))
            {
                text.Append($"    <Reference Include=\"{plugin.Name}.Editor\" HintPath=\"{Escape(plugin.EditorAssembly)}\" Private=\"false\" />\n");
            }
            else if (HasPluginEditorCode(plugin))
            {
                text.Append($"    <ProjectReference Include=\"{Escape(PluginEditorProjectFile(variant, plugin))}\" Private=\"false\" />\n");
            }
        }
        return text.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// The plugin list compiled into the game (ADR 0118): the plugins with
    /// code, in the order they load, for the game loader to register.
    /// </summary>
    public string PluginListText()
    {
        StringBuilder text = new("// Written by the Crucible editor (ADR 0118): the plugins this game uses, in the order they load.\n");
        string[] names = [.. CodePlugins.Select(plugin => $"\"{plugin.Name}\"")];
        if (names.Length > 0)
        {
            text.Append($"[assembly: global::Crucible.Hosting.GamePlugins({string.Join(", ", names)})]\n");
        }
        return text.ToString();
    }

    /// <summary>
    /// A source plugin's project (ADR 0118): its Code folder's sources, its
    /// Editor folders left out, with the game's settings and symbols; the
    /// engine and the plugins it uses referenced. The editor's builds it
    /// beside the game's assembly.
    /// </summary>
    public string PluginProjectText(Plugin plugin, bool package = false, bool? editorSymbol = null)
    {
        string variant = VariantFolder(package, editorSymbol);
        bool editors = !package && editorSymbol != false;
        StringBuilder text = new(Header(plugin.Name, plugin.Name, editors ? BinFolder + "/" : "bin/", editorSymbol ?? !package));
        if (package)
        {
            text.Append("    <Optimize>true</Optimize>\n");
        }
        text.Append("  </PropertyGroup>\n  <ItemGroup>\n");
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(plugin.CodeFolder, "**", "*.cs"))}\" " +
                    $"Exclude=\"{Escape(Path.Combine(plugin.CodeFolder, "**", EditorFolder, "**", "*.cs"))}\" />\n");
        text.Append($"    <Reference Include=\"Crucible.Engine\" HintPath=\"{Escape(_engine ?? EngineAssembly())}\" Private=\"false\" />\n");
        if (Generators() is { } generators)
        {
            text.Append($"    <Analyzer Include=\"{Escape(generators)}\" />\n");
        }
        text.Append(PluginReferences(Used(plugin), variant, copySource: false, copyCompiled: false));
        text.Append("  </ItemGroup>\n</Project>\n");
        return text.ToString();
    }

    /// <summary>
    /// A source plugin's editor project (ADR 0118): its Editor folders' code,
    /// with <c>CRUCIBLE_EDITOR</c>, referencing its runtime project, the
    /// editor's assemblies, and the plugins it uses (theirs too).
    /// </summary>
    public string PluginEditorProjectText(Plugin plugin)
    {
        string variant = VariantFolder(false, null);
        StringBuilder text = new(Header(plugin.Name + ".Editor", plugin.Name, BinFolder + "/", editorSymbol: true, aotCompatible: false));
        text.Append("  </PropertyGroup>\n  <ItemGroup>\n");
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(plugin.CodeFolder, "**", EditorFolder, "**", "*.cs"))}\" />\n");
        text.Append($"    <ProjectReference Include=\"{Escape(PluginProjectFile(variant, plugin))}\" Private=\"false\" />\n");
        foreach (string assembly in (string[])[_engine ?? EngineAssembly(), EditorAssembly(), AssetsAssembly()])
        {
            text.Append($"    <Reference Include=\"{Path.GetFileNameWithoutExtension(assembly)}\" HintPath=\"{Escape(assembly)}\" Private=\"false\" />\n");
        }
        text.Append(PluginReferences(Used(plugin), variant, copySource: false, copyCompiled: false));
        text.Append(PluginEditorReferences(Used(plugin), variant));
        text.Append("  </ItemGroup>\n</Project>\n");
        return text.ToString();
    }

    // A variant's plugin list and source plugins' projects (and in the
    // editor's, their editor projects).
    private void WritePlugins(bool package, bool? editorSymbol)
    {
        string variant = VariantFolder(package, editorSymbol);
        Write(Path.Combine(variant, PluginListFile), PluginListText());
        foreach (Plugin plugin in SourcePlugins)
        {
            Write(PluginProjectFile(variant, plugin), PluginProjectText(plugin, package, editorSymbol));
            if (!package && editorSymbol != false && HasPluginEditorCode(plugin))
            {
                Write(PluginEditorProjectFile(variant, plugin), PluginEditorProjectText(plugin));
            }
        }
    }

    // A source plugin's .cs files and the editor's build of them, and a
    // compiled one's assemblies: what the build stamp covers (ADR 0110).
    private void HashPlugins(Action<string> add)
    {
        foreach (Plugin plugin in _project.Plugins)
        {
            add($"plugin {plugin.Name}");
            add(ContentHash.OfFile(plugin.ManifestPath));
            if (plugin.IsCompiled)
            {
                add(File.Exists(plugin.Assembly) ? ContentHash.OfFile(plugin.Assembly) : "no assembly");
                add(File.Exists(plugin.EditorAssembly) ? ContentHash.OfFile(plugin.EditorAssembly) : "no editor assembly");
                continue;
            }
            if (!plugin.HasCode)
            {
                continue;
            }
            add(Relocatable(PluginProjectText(plugin)));
            add(HasPluginEditorCode(plugin) ? Relocatable(PluginEditorProjectText(plugin)) : "no editor code");
            foreach (string file in Directory.EnumerateFiles(plugin.CodeFolder, "*.cs", SearchOption.AllDirectories)
                         .Select(file => Path.GetRelativePath(plugin.CodeFolder, file).Replace('\\', '/'))
                         .Order(StringComparer.Ordinal))
            {
                add(file);
                add(ContentHash.OfFile(Path.Combine(plugin.CodeFolder, file)));
            }
        }
        add(PluginListText());
    }

    // The source plugins' assemblies the editor's build made, in order; null if one is missing.
    private string? PluginOutputHash()
    {
        StringBuilder hashes = new();
        foreach (Plugin plugin in SourcePlugins)
        {
            string assembly = PluginOutputPath(plugin);
            if (!File.Exists(assembly))
            {
                return null;
            }
            hashes.Append('+').Append(ContentHash.OfFile(assembly));
            if (HasPluginEditorCode(plugin))
            {
                string editor = PluginEditorOutputPath(plugin);
                if (!File.Exists(editor))
                {
                    return null;
                }
                hashes.Append('+').Append(ContentHash.OfFile(editor));
            }
        }
        return hashes.ToString();
    }

    // The project's folder left out of a project's text, so a copy hashes the same.
    private string Relocatable(string text) => text.Replace(Escape(_project.Root), "$(Project)", StringComparison.Ordinal);

    // -----------------------------------------------------------------------
    // Build Plugin
    // -----------------------------------------------------------------------

    /// <summary>Where Build Plugin builds a plugin: Cache/Build/PluginBuild/&lt;Name&gt;.</summary>
    public string PluginBuildFolder(Plugin plugin) => Path.Combine(_project.CacheFolder, "Build", "PluginBuild", plugin.Name);

    /// <summary>
    /// Builds a source plugin to share (ADR 0118): its runtime assembly in
    /// Release without <c>CRUCIBLE_EDITOR</c> (it ships in packages as it is),
    /// its editor assembly with it, into <c>&lt;folder&gt;/&lt;Name&gt;</c> with its
    /// assets and its manifest stamped with this Crucible's version. A
    /// previous build there is replaced; any other folder of that name isn't
    /// touched. Waits for the build.
    /// </summary>
    public PluginBuild BuildPlugin(Plugin plugin, string folder)
    {
        static PluginBuild Failed(string message) => new(false, null, [new BuildError("", 0, 0, message)]);
        if (plugin.IsCompiled)
        {
            return Failed($"{plugin.Name} is already compiled");
        }
        if (Used(plugin).FirstOrDefault(used => !used.IsCompiled) is { } source)
        {
            return Failed($"{plugin.Name} uses {source.Name}, which is source: build {source.Name} first, and use it compiled");
        }
        string destination = Path.GetFullPath(Path.Combine(folder, plugin.Name));
        string own = Path.GetFullPath(plugin.Folder);
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (destination.Equals(own, comparison) || destination.StartsWith(own + Path.DirectorySeparatorChar, comparison) ||
            own.StartsWith(destination + Path.DirectorySeparatorChar, comparison))
        {
            return Failed($"{plugin.Name} can't be built into its own folder");
        }
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any() &&
            !File.Exists(Path.Combine(destination, plugin.Name + Plugin.Extension)))
        {
            return Failed($"{destination} isn't empty, and isn't a build of {plugin.Name}");
        }

        string build = PluginBuildFolder(plugin);
        string runtimeProject = Path.Combine(build, plugin.Name + ".csproj");
        string editorProject = Path.Combine(build, EditorFolder, plugin.Name + ".Editor.csproj");
        string runtimeOutput = Path.Combine(build, "bin", plugin.Name + ".dll");
        string editorOutput = Path.Combine(build, EditorFolder, "bin", plugin.Name + ".Editor.dll");
        bool editor = HasPluginEditorCode(plugin);
        Write(runtimeProject, SharedPluginProjectText(plugin));
        if (editor)
        {
            Write(editorProject, SharedPluginEditorProjectText(plugin, runtimeProject));
        }
        GameBuild built = plugin.HasCode
            ? RunDotnet(["build", editor ? editorProject : runtimeProject, "-c", "Release"], editor ? editorOutput : runtimeOutput)
            : new GameBuild(true, null, [], "");
        if (!built.Succeeded)
        {
            return new PluginBuild(false, null, built.Errors);
        }

        try
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
            Directory.CreateDirectory(destination);
            if (plugin.HasCode)
            {
                CopyWithSymbols(runtimeOutput, destination);
                if (editor)
                {
                    CopyWithSymbols(editorOutput, destination);
                }
            }
            if (Directory.Exists(plugin.AssetsFolder))
            {
                CopyAssets(plugin.AssetsFolder, Path.Combine(destination, "Assets"));
            }
            PluginManifest manifest = new()
            {
                Name = plugin.Name,
                Version = plugin.Manifest.Version,
                Uses = [.. plugin.Manifest.Uses],
                Crucible = CrucibleVersion.Text,
            };
            File.WriteAllBytes(Path.Combine(destination, plugin.Name + Plugin.Extension), Crucible.Serialization.Json.Serialize(manifest));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed($"{plugin.Name} can't be written to {destination}: {exception.Message}");
        }
        return new PluginBuild(true, destination, []);
    }

    // The shared build's runtime project: as a package's (no editor symbol,
    // optimized), the plugins it uses compiled ones.
    private string SharedPluginProjectText(Plugin plugin)
    {
        StringBuilder text = new(Header(plugin.Name, plugin.Name, "bin/", editorSymbol: false));
        text.Append("  </PropertyGroup>\n  <ItemGroup>\n");
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(plugin.CodeFolder, "**", "*.cs"))}\" " +
                    $"Exclude=\"{Escape(Path.Combine(plugin.CodeFolder, "**", EditorFolder, "**", "*.cs"))}\" />\n");
        text.Append($"    <Reference Include=\"Crucible.Engine\" HintPath=\"{Escape(_engine ?? EngineAssembly())}\" Private=\"false\" />\n");
        if (Generators() is { } generators)
        {
            text.Append($"    <Analyzer Include=\"{Escape(generators)}\" />\n");
        }
        text.Append(PluginReferences(Used(plugin), "", copySource: false, copyCompiled: false));
        text.Append("  </ItemGroup>\n</Project>\n");
        return text.ToString();
    }

    private string SharedPluginEditorProjectText(Plugin plugin, string runtimeProject)
    {
        StringBuilder text = new(Header(plugin.Name + ".Editor", plugin.Name, "bin/", editorSymbol: true, aotCompatible: false));
        text.Append("  </PropertyGroup>\n  <ItemGroup>\n");
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(plugin.CodeFolder, "**", EditorFolder, "**", "*.cs"))}\" />\n");
        text.Append($"    <ProjectReference Include=\"{Escape(runtimeProject)}\" Private=\"false\" />\n");
        foreach (string assembly in (string[])[_engine ?? EngineAssembly(), EditorAssembly(), AssetsAssembly()])
        {
            text.Append($"    <Reference Include=\"{Path.GetFileNameWithoutExtension(assembly)}\" HintPath=\"{Escape(assembly)}\" Private=\"false\" />\n");
        }
        text.Append(PluginReferences(Used(plugin), "", copySource: false, copyCompiled: false));
        text.Append(PluginEditorReferences(Used(plugin), ""));
        text.Append("  </ItemGroup>\n</Project>\n");
        return text.ToString();
    }

    private static void CopyWithSymbols(string assembly, string folder)
    {
        File.Copy(assembly, Path.Combine(folder, Path.GetFileName(assembly)), overwrite: true);
        string symbols = Path.ChangeExtension(assembly, ".pdb");
        if (File.Exists(symbols))
        {
            File.Copy(symbols, Path.Combine(folder, Path.GetFileName(symbols)), overwrite: true);
        }
    }

    // Its assets with their sidecars, so their IDs travel with them; not
    // hidden files or writes in progress.
    private static void CopyAssets(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(from, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part.StartsWith('.')) ||
                relative.Contains(".tmp-", StringComparison.Ordinal))
            {
                continue;
            }
            string target = Path.Combine(to, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    // What the editor's build gave the game loader (ADR 0118).
    private GameBuild WithPlugins(GameBuild build) => build with
    {
        PluginAssemblies = [.. SourcePlugins.Select(PluginOutputPath)],
        PluginEditorAssemblies = [.. SourcePlugins.Where(HasPluginEditorCode).Select(PluginEditorOutputPath)],
    };
}

/// <summary>A finished Build Plugin (ADR 0118): whether it built, the plugin's folder, and its errors.</summary>
public sealed record PluginBuild(bool Succeeded, string? Folder, IReadOnlyList<BuildError> Errors);
