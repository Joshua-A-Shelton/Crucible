using Crucible.Ecs;
using Crucible.Loop;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;

namespace Crucible.Editor;

/// <summary>
/// Run Standalone checking itself headless (ADR 0073), for CTest's
/// launcher.jit.standalone: run with the Diorama's project,
/// <c>--game-arg check-standalone=&lt;folder&gt;</c>, and a settings file of
/// its own. On a copy of the project (in the folder): the village, with an
/// unsaved entity added, runs in the game launcher for a few headless
/// frames, loading the project's game and the scene as it is; the command
/// reads Stop while it runs; the game's output reaches the Log, and its
/// end; a run without a frame limit is stopped.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class StandaloneChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-standalone", out string? folder))
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
            ? $"StandaloneChecks: all {_checks} checks passed"
            : $"StandaloneChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "standalone-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Village.crscene"));
        yield return 3;
        World world = shell.World;
        int top = world.Contents().Count(item => world.ParentOf(item.Entity).IsNull && SceneIds.IdOf(world, item.Entity) != 0);
        // An edit not saved: the run has it.
        Check(shell.Commands.Find("Hierarchy.CreateEmpty")!.Execute(), "an entity is added");
        yield return 2;

        Command run = shell.Commands.Find("Play.RunStandalone")!;
        Check(run.CurrentTitle?.Invoke() == "Run Standalone", "the command reads Run");
        Check(shell.RunStandalone("--headless", "--frames", "30"), "the run starts");
        Check(shell.Standalone.IsRunning, "and is running");
        Check(run.CurrentTitle?.Invoke() == "Stop Standalone", "the command reads Stop while it runs");
        long started = Environment.TickCount64;
        while (!Log(shell).Contains("The standalone run ended", StringComparison.Ordinal) &&
               Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        string log = Log(shell);
        Check(File.Exists(Path.Combine(copy, "Cache", "Run", "Village.crscene")), "the scene is written to Cache/Run");
        Check(log.Contains("[game] Crucible.Hosting: loaded Diorama.Game.dll", StringComparison.Ordinal),
            "the game loads the project's code", Tail(log));
        Check(log.Contains($"[game] Crucible.Hosting: opened Village.crscene: {top + 1} entities at the top",
            StringComparison.Ordinal), "the scene opens as it is, the unsaved entity too", Tail(log));
        Check(!log.Contains("which nothing registered", StringComparison.Ordinal), "with every component known",
            Tail(log));
        Check(log.Contains("[game] Crucible.Hosting: ran 30 frames", StringComparison.Ordinal), "it runs its frames",
            Tail(log));
        Check(log.Contains("The standalone run ended (exit code 0)", StringComparison.Ordinal), "and ends cleanly",
            Tail(log));
        Check(!shell.Standalone.IsRunning && run.CurrentTitle?.Invoke() == "Run Standalone", "the command reads Run again");

        // A run without a limit, stopped.
        int opened = Opened(Log(shell));
        Check(shell.RunStandalone("--headless"), "a second run starts");
        started = Environment.TickCount64;
        while (Opened(Log(shell)) == opened && Environment.TickCount64 - started < 120000)
        {
            yield return 2;
        }
        Check(run.Execute() && !shell.Standalone.IsRunning, "the command stops it");
    }

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join("\n", H.Descendants(log).OfType<Label>().Select(label => label.Text))
            : "";

    private static int Opened(string log) =>
        log.Split('\n').Count(line => line.StartsWith("[game] Crucible.Hosting: opened", StringComparison.Ordinal));

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"StandaloneChecks: FAILED: {what} {detail}");
        }
    }
}
