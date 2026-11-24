using System.Reflection;
using System.Runtime.Loader;
using Crucible.Assets;
using Crucible.Loop;

namespace Crucible.Editor;

/// <summary>
/// Editor assemblies checking themselves (ADR 0116), for CTest's
/// launcher.jit.editor-code: run with the Walker's project and
/// <c>--game-arg check-editor-code=&lt;folder&gt;</c>. On a copy of the
/// project: code under Code/Editor builds into Walker.Editor.dll, loaded in
/// the game's context and using the game's own types; changed, the two
/// reload together and the old pair unloads; game code that builds only
/// with CRUCIBLE_EDITOR is warned about; a package has the game without its
/// editor code.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class EditorCodeChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private readonly List<string> _logged = [];

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-editor-code", out string? folder))
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
            ? $"EditorCodeChecks: all {_checks} checks passed"
            : $"EditorCodeChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        shell.Logged += _logged.Add;
        string copy = Path.Combine(Path.GetFullPath(_folder), "editor-code-project");
        AssetChecks.OpenCopy(shell, copy, null, withoutEditorCode: true);
        yield return 3;
        Project project = shell.Project!;
        string editorFolder = Path.Combine(project.CodeFolder, "Editor");
        Directory.CreateDirectory(editorFolder);
        string tools = Path.Combine(editorFolder, "WalkerTools.cs");
        File.WriteAllText(tools, Tools("1"));

        // Built and loaded beside the game, in its context.
        _logged.Clear();
        Check(shell.BuildCode(), "the game builds with an Editor folder", string.Join(" | ", _logged));
        Check(_logged.Contains("Reloaded Walker.dll and Walker.Editor.dll"), "the pair loads together", string.Join(" | ", _logged));
        // Read in a method of its own: nothing of the first pair stays in
        // this iterator's fields to keep it from unloading.
        (bool holds, bool sameGame, bool editorTypes, WeakReference first) = Inspect(shell);
        Check(holds, "Walker.Editor.dll holds the Editor folder's code");
        Check(sameGame, "it uses the game's own types, loaded in the same context; the game has none of its code");
        Check(editorTypes, "it uses the editor's types, the editor's own assembly");
        yield return 2;

        // Changed: both reload, and the old pair goes.
        File.WriteAllText(tools, Tools("2"));
        _logged.Clear();
        Check(shell.BuildCode(), "a change to editor code builds");
        Check(_logged.Contains("Reloaded Walker.dll and Walker.Editor.dll") &&
              !_logged.Any(line => line.Contains("didn't unload", StringComparison.Ordinal)),
            "the pair reloads together and the old one unloads", string.Join(" | ", _logged));
        Check(ToolsVersion(shell) == "2", "the new editor code runs");
        Check(GameLoader_Collected(first), "the first editor assembly was collected");
        yield return 2;

        // Game code built only with CRUCIBLE_EDITOR: warned about.
        string uses = Path.Combine(project.CodeFolder, "UsesEditor.cs");
        File.WriteAllText(uses, """
            namespace Walker;
            #if CRUCIBLE_EDITOR
            public static class EditorNote { public const string Text = "editor"; }
            #endif
            public static class UsesEditor { public static string Text => EditorNote.Text; }
            """);
        _logged.Clear();
        Check(shell.BuildCode(), "game code using CRUCIBLE_EDITOR builds for the editor");
        GameBuild? check = shell.Builder?.WaitForSymbolCheck();
        Check(check is { Succeeded: false }, "but not without it");
        if (check is not null)
        {
            shell.SymbolChecked(check);
        }
        Check(_logged.Any(line => line.StartsWith("Walker doesn't build without CRUCIBLE_EDITOR: Code/UsesEditor.cs(5,", StringComparison.Ordinal)),
            "the Log says where", string.Join(" | ", _logged));
        File.Delete(uses);
        Check(shell.BuildCode(), "fixed, it builds again");
        yield return 2;

        // A package: the game, none of its editor code.
        project.Build.Configuration = PackageConfiguration.Development;
        project.Build.Runtime = PackageRuntime.Jit;
        project.Build.ReadyToRun = false;
        string package = Path.Combine(Path.GetFullPath(_folder), "editor-code-package");
        PackageResult? result = shell.Build(package);
        Check(result is { Succeeded: true }, "the project packages", string.Join(" | ", result?.Errors ?? []));
        string data = Path.Combine(package, "Data");
        Check(File.Exists(Path.Combine(data, "Walker.dll")) && !File.Exists(Path.Combine(data, "Walker.Editor.dll")) &&
              !TypeNames(Path.Combine(data, "Walker.dll")).Contains("WalkerTools"),
            "the package has the game and none of its editor code");
        shell.Logged -= _logged.Add;
    }

    private static string Tools(string version) => $$"""
        namespace Walker.Tools;

        // Editor code (ADR 0116): the game's types and the editor's.
        public static class WalkerTools
        {
            public static System.Type Game => typeof(Walker.Patroller);
            public static string Editor => typeof(Crucible.Editor.GameBuilder).Name;
            public static string Version => "{{version}}";
        }
        """;

    private const string JitOnly = "The editor runs JIT only (ADR 0016); the game's assemblies are loaded, not trimmed.";

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = JitOnly)]
    private static string? ToolsVersion(Shell shell) =>
        Read(shell.Game?.EditorAssembly?.GetType("Walker.Tools.WalkerTools"), "Version") as string;

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = JitOnly)]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (bool Holds, bool SameGame, bool EditorTypes, WeakReference Editor) Inspect(Shell shell)
    {
        Assembly? editor = shell.Game?.EditorAssembly;
        Type? type = editor?.GetType("Walker.Tools.WalkerTools");
        Assembly? game = editor is null ? null
            : AssemblyLoadContext.GetLoadContext(editor)?.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == "Walker");
        bool sameGame = game is not null && Read(type, "Game") is Type patroller && patroller.Assembly == game &&
                        game.GetType("Walker.Tools.WalkerTools") is null;
        // Resolved at run time to the editor's own assembly, not a copy.
        bool editorTypes = Read(type, "Editor") as string == nameof(GameBuilder) &&
                           AssemblyLoadContext.GetLoadContext(editor!)?.Assemblies.All(assembly => assembly.GetName().Name != "Crucible.Editor") == true;
        return (type is not null, sameGame, editorTypes, new WeakReference(editor));
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070", Justification = JitOnly)]
    private static object? Read(Type? type, string property) =>
        type?.GetProperty(property, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    private static bool GameLoader_Collected(WeakReference reference)
    {
        for (int i = 0; i < 10 && reference.IsAlive; ++i)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !reference.IsAlive;
    }

    private static string[] TypeNames(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }
        using FileStream stream = File.OpenRead(path);
        using System.Reflection.PortableExecutable.PEReader pe = new(stream);
        System.Reflection.Metadata.MetadataReader reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
        return [.. reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name))];
    }

    private void Check(bool condition, string what, string? detail = null)
    {
        ++_checks;
        if (!condition)
        {
            ++_failures;
        }
        Console.WriteLine($"EditorCodeChecks: {(condition ? "ok" : "FAILED")}: {what}{(condition || detail is null ? "" : $" ({detail})")}");
    }
}
