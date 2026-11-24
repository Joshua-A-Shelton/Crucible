using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Crucible.Assets;
using Crucible.Hosting;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Plugins (ADR 0118): what's wrong with the project's, its compiled ones
/// loaded once (their extensions started) and kept across the game's
/// reloads, compiled plugins' assets kept as they are, and Build Plugin.
/// Source plugins build and reload with the game (GameBuilder, Shell.Play).
/// </summary>
internal sealed partial class Shell
{
    private LoadedPlugins? _compiledPlugins;
    // The compiled plugins as loaded: names and their assemblies' hashes, to
    // tell when Plugins/ changed under them.
    private string _compiledLoaded = "";
    private AssetWatcher? _pluginsWatcher;
    // The plugin File > Build Plugin… is choosing a folder for.
    private Plugin? _buildingPlugin;

    /// <summary>The compiled plugins loaded now, for the game's context and theirs to share (Dependencies).</summary>
    internal static LoadedPlugins? CompiledPlugins { get; private set; }

    /// <summary>
    /// Whether the editor may change an asset path (ADR 0118): not in a
    /// compiled plugin, whose assets are as it was built with; says so if not.
    /// </summary>
    public bool CanChange(string assetPath)
    {
        if (Project is not { } project || !project.IsReadOnly(assetPath))
        {
            return true;
        }
        LogWarning($"{AssetPaths.PluginOf(assetPath)} is a compiled plugin; its assets can't be changed here");
        return false;
    }

    /// <summary>As <see cref="CanChange"/>, for a file by its full path.</summary>
    public bool CanChangeFile(string file) => Project?.AssetPathOf(file) is not { } path || CanChange(path);

    // Opening a project: what's wrong with its plugins, and its compiled ones loaded.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    private void OpenProjectPlugins(Project project)
    {
        ClosePlugins();
        foreach (string problem in project.PluginProblems)
        {
            LogWarning(problem);
        }
        if (project.Plugins.Count > 0)
        {
            Log("Plugins: " + string.Join(", ", project.Plugins.Select(plugin =>
                $"{plugin.Name}{(plugin.Manifest.Version.Length > 0 ? " " + plugin.Manifest.Version : "")} ({(plugin.IsCompiled ? "compiled" : "source")})")));
        }
        Plugin[] compiled = [.. project.Plugins.Where(plugin => plugin.IsCompiled && plugin.HasCode)];
        _compiledLoaded = CompiledSignature(project);
        if (compiled.Length > 0)
        {
            try
            {
                _compiledPlugins = GameLoader.LoadPlugins([.. compiled.Select(plugin =>
                    (plugin.Assembly, File.Exists(plugin.EditorAssembly) ? plugin.EditorAssembly : null))]);
                CompiledPlugins = _compiledPlugins;
            }
            catch (Exception exception) when (exception is IOException or BadImageFormatException or FileLoadException)
            {
                LogError($"The compiled plugins can't be loaded: {exception.Message}");
            }
            foreach (Assembly assembly in _compiledPlugins?.EditorAssemblies ?? [])
            {
                StartExtensions(assembly, kept: true);
            }
        }
        _pluginsWatcher?.Dispose();
        _pluginsWatcher = null;
        if (Directory.Exists(project.PluginsFolder))
        {
            try
            {
                _pluginsWatcher = new AssetWatcher(project.PluginsFolder);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or PlatformNotSupportedException)
            {
                Log($"Can't watch {project.PluginsFolder} for changes: {exception.Message}");
            }
        }
    }

    // Closing a project (or opening another): its compiled plugins'
    // extensions stopped and the plugins unloaded.
    private void ClosePlugins()
    {
        StopExtensions(keepPanels: false, all: true);
        _pluginsWatcher?.Dispose();
        _pluginsWatcher = null;
        if (_compiledPlugins is not { } loaded)
        {
            return;
        }
        if (loaded.Assemblies.FirstOrDefault() is { } any && AssemblyLoadContext.GetLoadContext(any) is { } context)
        {
            Forget(context);
        }
        _compiledPlugins = null;
        CompiledPlugins = null;
        WeakReference unloaded = loaded.Unload();
        // A game still loaded (another project's, until this one's replaces
        // it) holds them until then.
        if (_game is null && !GameLoader.WaitForUnload(unloaded))
        {
            Log("The compiled plugins didn't unload: something still holds on to them");
        }
    }

    // Each frame: a change in Plugins/ reads them again. A source plugin's
    // code and assets follow (a build, a rescan); compiled plugins changed
    // need the project opened again, as they load once.
    private void PollPlugins()
    {
        if (_pluginsWatcher is null || !_pluginsWatcher.TakeSettled(out _, out _) || Project is not { } project)
        {
            return;
        }
        string[] before = [.. project.PluginProblems];
        project.RefreshPlugins();
        foreach (string problem in project.PluginProblems.Except(before))
        {
            LogWarning(problem);
        }
        if (CompiledSignature(project) != _compiledLoaded)
        {
            LogWarning("The compiled plugins changed: open the project again to load them");
        }
        RefreshAssets();
        if (_builder is { HasCode: true } builder && builder.Start())
        {
            Status($"Building {builder.AssemblyName}…");
        }
    }

    private static string CompiledSignature(Project project) =>
        string.Join("\n", project.Plugins.Where(plugin => plugin.IsCompiled).Select(plugin =>
            $"{plugin.Name} {(File.Exists(plugin.Assembly) ? ContentHash.OfFile(plugin.Assembly) : "")} " +
            (File.Exists(plugin.EditorAssembly) ? ContentHash.OfFile(plugin.EditorAssembly) : "")));

    // -----------------------------------------------------------------------
    // Build Plugin
    // -----------------------------------------------------------------------

    private void RegisterPluginCommands()
    {
        Commands.Add(new Command("File.BuildPlugin", "Build Plugin…", ChooseBuiltPlugin,
            () => Project?.Plugins.Any(plugin => !plugin.IsCompiled) == true && !(_builder?.IsBuilding ?? false)));
    }

    // Which source plugin, if there's more than one; then where.
    private void ChooseBuiltPlugin()
    {
        Plugin[] sources = [.. Project?.Plugins.Where(plugin => !plugin.IsCompiled) ?? []];
        if (sources.Length == 1)
        {
            ChoosePluginFolder(sources[0]);
            return;
        }
        if (sources.Length > 1)
        {
            AskDrop("Which plugin?", [.. sources.Select(plugin => plugin.Name)], 0, offerRest: false,
                (choice, _) =>
                {
                    if (choice >= 0 && choice < sources.Length)
                    {
                        ChoosePluginFolder(sources[choice]);
                    }
                });
        }
    }

    private void ChoosePluginFolder(Plugin plugin)
    {
        _buildingPlugin = plugin;
        Platform.Api.ShowOpenFolderDialog(Main.Window, Project?.Root ?? "");
    }

    // The folder dialog's answer: the plugin built into it, or nothing if cancelled.
    private void PluginFolderChosen(string? folder)
    {
        Plugin? plugin = _buildingPlugin;
        _buildingPlugin = null;
        if (plugin is not null && folder is not null)
        {
            BuildPlugin(plugin, folder);
        }
    }

    /// <summary>
    /// Builds a source plugin into <c>&lt;folder&gt;/&lt;Name&gt;</c> (ADR 0118), ready for
    /// another game's Plugins/: its assemblies, its assets, and its manifest
    /// stamped with this Crucible's version. Waits for the build.
    /// </summary>
    public bool BuildPlugin(Plugin plugin, string folder)
    {
        if (_builder is not { } builder)
        {
            return false;
        }
        Log($"Building the plugin {plugin.Name}…");
        PluginBuild built = builder.BuildPlugin(plugin, folder);
        if (!built.Succeeded)
        {
            foreach (BuildError error in built.Errors)
            {
                LogError($"Build Plugin: {Relative(error)}");
            }
            Status($"{plugin.Name} didn't build");
            return false;
        }
        Log($"Built {plugin.Name} into {built.Folder}");
        Status($"Built {plugin.Name}");
        return true;
    }
}
