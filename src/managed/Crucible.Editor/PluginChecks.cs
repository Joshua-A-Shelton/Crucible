using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Hosting;
using Crucible.Loop;
using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// Plugins checking themselves (ADR 0118), for CTest's launcher.jit.plugins:
/// run with the Walker's project and <c>--game-arg check-plugins=&lt;folder&gt;</c>.
/// On a copy of the project, a source plugin (a component, a system, an
/// editor extension, an asset) built, loaded, run, and reloaded with the
/// game, packaged without its editor code, and built to share; then a
/// second copy using the compiled build, loaded once and kept across the
/// game's reloads, its assets left as they are, and a plugin built for
/// another Crucible refused. The second copy is left for the package tests.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class PluginChecks : GameSystem
{
    private const string JitOnly = "The editor runs JIT only (ADR 0016); the game's assemblies are loaded, not trimmed.";
    private const string ChainPath = "Waypoints:Paths/Loop.crchain";

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-plugins", out string? folder))
        {
            Enabled = false;
            return;
        }
        _folder = folder;
        _script = Script().GetEnumerator();
        _wait = 3;
    }

    public override void Run(FrameContext context)
    {
        if (_script is null || _wait-- > 0)
        {
            return;
        }
        try
        {
            if (_script.MoveNext())
            {
                _wait = _script.Current;
                return;
            }
        }
        catch (Exception exception)
        {
            Check(false, "the script ran", exception.ToString());
        }
        _script = null;
        Console.WriteLine(_failures == 0
            ? $"PluginChecks: all {_checks} checks passed"
            : $"PluginChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string folder = Path.GetFullPath(_folder);

        // A source plugin in a copy of the Walker, and game code using it.
        string copy = Path.Combine(folder, "plugins-project");
        AssetChecks.OpenCopy(shell, copy, null, withoutEditorCode: true);
        Project first = shell.Project!;
        // Its file leaves the official plugins out (ADR 0123): no map mode.
        Check(first.Official.Count == 0 && !first.OfficialPlugins.Any() && !shell.RunCreateItem("2D/Tilemap", Entity.Null) &&
              !_logged.Any(line => line.StartsWith("Official plugins:", StringComparison.Ordinal)),
            "a project leaving the official plugins out has none of theirs", Logged());
        string plugin = Path.Combine(first.PluginsFolder, "Waypoints");
        WritePlugin(plugin, version: 1);
        File.WriteAllText(Path.Combine(first.CodeFolder, "UsesWaypoints.cs"), UsesWaypoints("1"));
        _logged.Clear();
        shell.OpenProject(first.ProjectFilePath);
        yield return 3;
        first = shell.Project!;
        Check(_logged.Contains("Plugins: Waypoints 1.0.0 (source)"), "the project's plugins listed", Logged());
        // Loaded again: the copy's game was loaded when it first opened.
        Check(_logged.Any(line => line.StartsWith("Reloaded Waypoints.dll, Walker.dll, Waypoints.Editor.dll", StringComparison.Ordinal)),
            "the plugin built and loaded with the game, its editor assembly too", Logged());
        (bool inGame, bool compiled) = Where(shell);
        Check(inGame && !compiled, "a source plugin is in the game's context");
        Check(SavedComponents.Find("Waypoints.Waypoint") is not null, "its component registered, and saved by scenes");
        Check(Run(shell, "Waypoints.Version") && _logged.Contains("Waypoints v1"), "its editor extension started", Logged());
        Check(shell.Main.MenuBar.Menus.FirstOrDefault(menu => menu.Title == "Tools")?.Items.OfType<MenuItem>()
                  .Any(item => item.Title == "Waypoints") == true, "its command under Tools > Waypoints");
        Check(shell.Database?.Find(ChainPath) is { } chain && shell.Database.ImportedFile(chain.Id) is not null,
            "its asset found by its plugin's name, and imported");

        // Its system runs with the game's.
        shell.Commands.Find("Play.Toggle")!.Execute();
        yield return 5;
        Check(StaticOf(shell, "Waypoints.WaypointSystem", "Started") is int started && started > 0, "its system runs in Play");
        shell.Commands.Find("Play.Toggle")!.Execute();
        yield return 2;

        // Changed, it rebuilds and reloads with the game, the old version unloaded.
        WritePlugin(plugin, version: 2);
        _logged.Clear();
        Check(shell.BuildCode(), "a changed plugin builds", Logged());
        Check(_logged.Any(line => line.StartsWith("Reloaded Waypoints.dll", StringComparison.Ordinal)) &&
              !_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)), "and reloads, the old one unloaded", Logged());
        Check(Run(shell, "Waypoints.Version") && _logged.Contains("Waypoints v2"), "its new extension running");
        yield return 2;

        // A package has its runtime assembly, not its editor assembly.
        first.Build.Configuration = PackageConfiguration.Development;
        first.Build.Runtime = PackageRuntime.Jit;
        first.Build.ReadyToRun = false;
        string package = Path.Combine(folder, "plugins-package");
        PackageResult? packaged = shell.Build(package);
        Check(packaged is { Succeeded: true }, "the project packages", string.Join(" | ", packaged?.Errors ?? []));
        string data = Path.Combine(package, "Data");
        Check(File.Exists(Path.Combine(data, "Waypoints.dll")) && !File.Exists(Path.Combine(data, "Waypoints.Editor.dll")),
            "the package has the plugin and none of its editor code");

        // Built to share: its assemblies, assets, and manifest stamped with this Crucible.
        Plugin source = first.PluginNamed("Waypoints")!;
        _logged.Clear();
        Check(!shell.BuildPlugin(source, first.PluginsFolder) &&
              _logged.Contains("Build Plugin: Waypoints can't be built into its own folder"), "not into its own folder", Logged());
        string shared = Path.Combine(folder, "plugins-shared");
        Check(shell.BuildPlugin(source, shared), "Build Plugin builds it", Logged());
        string built = Path.Combine(shared, "Waypoints");
        Check(File.Exists(Path.Combine(built, "Waypoints.dll")) && File.Exists(Path.Combine(built, "Waypoints.Editor.dll")) &&
              File.Exists(Path.Combine(built, "Assets", "Paths", "Loop.crchain")) && !Directory.Exists(Path.Combine(built, "Code")),
            "with its assemblies and assets, and no source");
        JsonObject? manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(built, "Waypoints.crplugin"))) as JsonObject;
        Check(manifest?["Crucible"]?.GetValue<string>() == CrucibleVersion.Text && manifest["Version"]?.GetValue<string>() == "1.0.0",
            "its manifest names this Crucible", manifest?.ToJsonString() ?? "");
        Check(shell.BuildPlugin(source, shared), "built again over its last build");
        yield return 2;

        // Another copy using the compiled build, and one built for another Crucible.
        string second = Path.Combine(folder, "plugins-compiled-project");
        if (Directory.Exists(second))
        {
            Directory.Delete(second, recursive: true);
        }
        AssetChecks.CopyProject(first.Root, second, withCache: true);
        string secondPlugins = Path.Combine(second, "Plugins");
        Directory.Delete(Path.Combine(secondPlugins, "Waypoints"), recursive: true);
        AssetChecks.CopyProject(built, Path.Combine(secondPlugins, "Waypoints"));
        Directory.CreateDirectory(Path.Combine(secondPlugins, "Old"));
        File.WriteAllText(Path.Combine(secondPlugins, "Old", "Old.crplugin"), """{ "Name": "Old", "Crucible": "0.0.1" }""");
        _logged.Clear();
        shell.OpenProject(Path.Combine(second, Path.GetFileName(first.ProjectFilePath)));
        yield return 3;
        Project project = shell.Project!;
        string line = CrucibleVersion.Line;
        Check(_logged.Contains($"Old was built for Crucible 0.0; this is {line}. Rebuild it for {line}"),
            "a plugin built for another Crucible refused, and why", Logged());
        Check(_logged.Contains("Plugins: Waypoints 1.0.0 (compiled)"), "the compiled plugin listed", Logged());
        (inGame, compiled) = Where(shell);
        Check(!inGame && compiled, "a compiled plugin is in its own context, shared with the game's");
        Check(Run(shell, "Waypoints.Version") && _logged.Contains("Waypoints v2"), "its editor extension started");
        WeakReference loaded = PluginAssembly(shell);

        // The game reloads; the compiled plugin stays.
        File.WriteAllText(Path.Combine(project.CodeFolder, "UsesWaypoints.cs"), UsesWaypoints("2"));
        _logged.Clear();
        Check(shell.BuildCode(), "game code using the compiled plugin builds", Logged());
        Check(PluginAssembly(shell).Target is { } after && after == loaded.Target, "and the plugin isn't loaded again");
        Check(Run(shell, "Waypoints.Version") && _logged.Count(entry => entry == "Waypoints v2") == 1, "its extension kept running");
        shell.Commands.Find("Play.Toggle")!.Execute();
        yield return 5;
        Check(StaticOf(shell, "Waypoints.WaypointSystem", "Started") is int runs && runs > 0, "its system runs in Play");
        shell.Commands.Find("Play.Toggle")!.Execute();
        yield return 2;

        // Its assets are as it was built with.
        _logged.Clear();
        Check(shell.Database?.Find(ChainPath) is not null, "its asset found");
        Check(!shell.CanChange(ChainPath) &&
              _logged.Contains("Waypoints is a compiled plugin; its assets can't be changed here"), "and can't be changed", Logged());
        Check(!shell.WriteExtensionFile("Waypoints:Notes.txt", [1], "Note"), "an extension can't write there either");

        // The package tests cook its asset with the level.
        project.SaveBuild(new BuildSettings { Scenes = [.. project.Build.Scenes], Include = ["Waypoints:Paths"] });
        shell.Logged -= _logged.Add;
    }

    private static void WritePlugin(string folder, int version)
    {
        Directory.CreateDirectory(Path.Combine(folder, "Code", "Editor"));
        Directory.CreateDirectory(Path.Combine(folder, "Assets", "Paths"));
        File.WriteAllText(Path.Combine(folder, "Waypoints.crplugin"), """{ "Name": "Waypoints", "Version": "1.0.0", "Uses": [] }""");
        File.WriteAllText(Path.Combine(folder, "Code", "Waypoint.cs"), """
            using Crucible.Diagnostics;
            using Crucible.Ecs;
            using Crucible.Loop;
            using Crucible.Serialization;

            namespace Waypoints;

            [Component, Serialized]
            public partial struct Waypoint
            {
                public float Radius;
            }

            [System(Phases.Update)]
            public sealed class WaypointSystem : GameSystem
            {
                public static int Started;

                public override void Start(FrameContext context)
                {
                    ++Started;
                    Log.Info("Waypoints: the plugin's system started");
                }

                public override void Run(FrameContext context)
                {
                }
            }
            """);
        File.WriteAllText(Path.Combine(folder, "Code", "Editor", "WaypointTools.cs"), $$"""
            using Crucible.Editor;
            using Crucible.UI;

            namespace Waypoints.Tools;

            public sealed class WaypointTools : EditorExtension
            {
                public override void Start(EditorContext editor) =>
                    editor.Commands.Add(new Command("Waypoints.Version", "Version", () => editor.Log("Waypoints v{{version}}")), "Tools/Waypoints");
            }
            """);
        string chain = Path.Combine(folder, "Assets", "Paths", "Loop.crchain");
        if (!File.Exists(chain))
        {
            File.WriteAllText(chain, ChainHandles.Format(
                new JsonObject { ["$guid"] = AssetId.New().ToString(), ["Points"] = null, ["Loop"] = true },
                [new System.Numerics.Vector2(1, 0), new System.Numerics.Vector2(0, 1), new System.Numerics.Vector2(-1, 0)]));
        }
    }

    private static string UsesWaypoints(string version) => $$"""
        namespace Walker;

        // Game code using a plugin's types (ADR 0118).
        public static class UsesWaypoints
        {
            public static System.Type Type => typeof(Waypoints.Waypoint);
            public const string Version = "{{version}}";
        }
        """;

    private static bool Run(Shell shell, string command) => shell.Commands.Find(command)?.Execute() == true;

    // Whether the game's Waypoints plugin is in the game's context, or the compiled plugins'.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool InGame, bool Compiled) Where(Shell shell)
    {
        Assembly? plugin = shell.Game?.Plugins.FirstOrDefault(assembly => assembly.GetName().Name == "Waypoints");
        AssemblyLoadContext? context = plugin is null ? null : AssemblyLoadContext.GetLoadContext(plugin);
        AssemblyLoadContext? game = shell.Game?.Plugins.Count > 0 && shell.Game.EditorAssembly is { } editor
            ? AssemblyLoadContext.GetLoadContext(editor)
            : null;
        bool compiled = plugin is not null && Shell.CompiledPlugins?.Assemblies.Contains(plugin) == true;
        return (context is not null && context == game, compiled);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PluginAssembly(Shell shell) =>
        new(shell.Game?.Plugins.FirstOrDefault(assembly => assembly.GetName().Name == "Waypoints"));

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = JitOnly)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? StaticOf(Shell shell, string type, string field) =>
        shell.Game?.Plugins.Select(assembly => assembly.GetType(type)).OfType<Type>().FirstOrDefault()
            ?.GetField(field, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private string Logged() => string.Join(" | ", _logged);

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"PluginChecks: FAILED: {what}{(detail.Length > 0 ? $" ({detail})" : "")}");
        }
    }
}
