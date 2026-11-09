using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;

namespace Crucible.Editor;

/// <summary>
/// Native modules checking themselves headless (ADR 0074), for CTest's
/// launcher.jit.native: run with the Diorama's project,
/// <c>--game-arg check-native=&lt;folder&gt;</c>, and a settings file of its
/// own. On a copy of the project (in the folder): opening it builds and
/// loads its module before its C#; Bob is known and on the lamp lights;
/// playing bobs them, C# reads the module's count through the bindings, and
/// Stop puts them back; a standalone run loads the module too; a broken C++
/// file is reported with its file and line, and the fixed build says the
/// editor needs a restart.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class NativeChecks : GameSystem
{
    private const string Light = "232be8fc53b98f30";

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-native", out string? folder))
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
            ? $"NativeChecks: all {_checks} checks passed"
            : $"NativeChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "native-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);

        // Opening the project: the module built and loaded, then the C# with its bindings.
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        string log = Log(shell);
        Check(Shell.ModuleLoaded && log.Contains("Loaded Diorama.Native", StringComparison.Ordinal),
            "opening the project builds and loads its module", Tail(log));
        Check(log.IndexOf("Loaded Diorama.Native", StringComparison.Ordinal) <
              log.IndexOf("Loaded Diorama.Game.dll", StringComparison.Ordinal), "before its C#");
        SavedComponent? bob = SavedComponents.Find("Diorama.Bob");
        Check(bob is not null, "Bob is known to C#");
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Village.crscene"));
        yield return 3;
        World world = shell.World;
        Entity light = SceneIds.Find(world, Convert.ToUInt64(Light, 16));
        Check(!light.IsNull && bob is not null && world.Has(light, bob.Id), "the lamp light has its Bob");
        if (light.IsNull)
        {
            yield break;
        }
        float placed = world.Read<LocalTransform>(light).Position.Y;

        // Playing: the module's system moves it, and C# calls into the module.
        shell.Play();
        yield return 20;
        float playing = world.Read<LocalTransform>(light).Position.Y;
        Check(MathF.Abs(playing - placed) > 1e-4f, "playing bobs the light", $"{placed} then {playing}");
        Check(BobbedLastFrame(shell) == 4, "C# reads the module's count through the bindings",
            $"{BobbedLastFrame(shell)}");
        shell.Stop();
        yield return 2;
        light = SceneIds.Find(world, Convert.ToUInt64(Light, 16));
        Check(!light.IsNull && world.Read<LocalTransform>(light).Position.Y == placed, "Stop puts it back");

        // Standalone: the game loads the module itself.
        Check(shell.RunStandalone("--headless", "--frames", "30"), "a standalone run starts");
        long started = Environment.TickCount64;
        while (!Log(shell).Contains("The standalone run ended", StringComparison.Ordinal) &&
               Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        log = Log(shell);
        Check(log.Contains("[game] Crucible.Hosting: ran 30 frames", StringComparison.Ordinal) &&
              log.Contains("The standalone run ended (exit code 0)", StringComparison.Ordinal),
            "and runs with the module", Tail(log));
        Check(!log.Contains("which nothing registered", StringComparison.Ordinal), "with Bob known", Tail(log));

        // A broken C++ file: its file and line in the Log; fixed, a restart is asked for.
        string broken = Path.Combine(copy, "Native", "Broken.cpp");
        File.WriteAllText(broken, "int broken() { return }\n");
        started = Environment.TickCount64;
        while (!Log(shell).Contains("Broken.cpp(1", StringComparison.Ordinal) && Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        Check(Log(shell).Contains("Build error: ", StringComparison.Ordinal) &&
              Log(shell).Contains("Broken.cpp(1", StringComparison.Ordinal), "a C++ error names its file and line",
            Tail(Log(shell)));
        File.Delete(broken);
        started = Environment.TickCount64;
        while (!Log(shell).Contains("Built Diorama.Native: restart the editor to load it", StringComparison.Ordinal) &&
               Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        Check(Log(shell).Contains("Built Diorama.Native: restart the editor to load it", StringComparison.Ordinal),
            "the fixed build asks for a restart", Tail(Log(shell)));
    }

    // Diorama.Api.BobbedLastFrame, from the game's generated bindings.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The editor runs JIT only (ADR 0016)")]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The editor runs JIT only (ADR 0016)")]
    private static long BobbedLastFrame(Shell shell)
    {
        Assembly? game = shell.Game?.Context.Assemblies.FirstOrDefault();
        MethodInfo? method = game?.GetType("Diorama.Api")?.GetMethod("BobbedLastFrame");
        return method?.Invoke(null, null) is uint count ? count : -1;
    }

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join("\n", H.Descendants(log).OfType<Label>().Select(label => label.Text))
            : "";

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"NativeChecks: FAILED: {what} {detail}");
        }
    }
}
