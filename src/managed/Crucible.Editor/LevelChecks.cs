using System.Globalization;
using System.Text.RegularExpressions;
using Crucible.Loop;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;

namespace Crucible.Editor;

/// <summary>
/// The M9 demonstration checking itself headless (ADR 0097), for CTest's
/// samples.walker-level: run with the Walker's project and
/// <c>--game-arg check-level=&lt;folder&gt;</c>. On a copy of the project (in
/// the folder): Level.crscene opened, its game code built, and run
/// standalone headless at 30 and then 240 frames a second. In each run the
/// Walker's swing hits the post once, inside its Strike window, and the
/// Walker walks its route to the end without falling; then the two runs'
/// hits must land at the same moment into the window.
/// With <c>check-archive=&lt;folder&gt;</c> instead, for samples.walker-archive
/// (ADR 0099): one run from the Cache at 30 fps, then the project cooked
/// and run from its archive at 30 fps, which must read its 8 assets from it
/// and play out the same. With <c>check-packaged=&lt;folder&gt;</c>, for
/// samples.walker-packaged (the M10 demonstration, ADR 0102): one run from
/// the Cache at 30 fps, then the Walker's JIT and NativeAOT packages built
/// and each run at 30 fps from its archive, which must play out the same.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed partial class LevelChecks : GameSystem
{
    // The route takes about 35 s.
    private const int Seconds = 45;
    private const double WindowMs = 250;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";
    private bool _archive;
    private bool _packaged;
    // The latest run's log, route, and hit (ms into its window; NaN if none).
    private string _run = "";
    private Match _route = Match.Empty;
    private double _into;
    // The run from the Cache, to compare the others with.
    private double _fromCache = double.NaN;
    private double _fromCacheArrival = double.NaN;
    private string _cacheRoute = "";

    public override void Start(FrameContext context)
    {
        if (context.GameArguments.TryGetValue("check-archive", out string? archive))
        {
            _archive = true;
        }
        else if (context.GameArguments.TryGetValue("check-packaged", out archive))
        {
            _packaged = true;
        }
        else if (!context.GameArguments.TryGetValue("check-level", out archive))
        {
            Enabled = false;
            return;
        }
        _folder = archive;
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
            ? $"LevelChecks: all {_checks} checks passed"
            : $"LevelChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(AssetChecks.SourceProjectFile(shell)) ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder),
            _archive ? "archive-project" : _packaged ? "packaged-project" : "level-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy, AssetChecks.IsWarm(shell));
        string projectFile = Path.Combine(copy, Path.GetFileName(AssetChecks.SourceProjectFile(shell)));
        // Known build settings, whatever the sources' say: the Level scene alone.
        Assets.Project.Open(projectFile).SaveBuild(new Assets.BuildSettings
        {
            Scenes = [Assets.AssetId.Parse("28fe2d80-33ef-4ee4-a409-cc43b935bcc6")],
        });
        shell.OpenProject(projectFile);
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Level.crscene"));
        yield return 3;

        if (_archive || _packaged)
        {
            foreach (int wait in Run(shell, "from the Cache", () => shell.RunStandalone(RunOptions(30))))
            {
                yield return wait;
            }
            _fromCache = _into;
            _cacheRoute = _route.Success ? _route.Groups["arrived"].Value : "";
            _fromCacheArrival = _route.Success ? Arrival(_route) : double.NaN;
        }
        if (_packaged)
        {
            // Each package built and run as a player runs it, with only the
            // check's options, and the Cache's imported files moved aside.
            string imported = Path.Combine(copy, "Cache", "Imported");
            foreach ((Assets.PackageRuntime runtime, string loaded) in ((Assets.PackageRuntime, string)[])[
                         (Assets.PackageRuntime.Jit, "loaded Walker.dll"), (Assets.PackageRuntime.NativeAot, "Walker compiled in")])
            {
                Assets.Project.Open(projectFile).SaveBuild(new Assets.BuildSettings
                {
                    Scenes = [Assets.AssetId.Parse("28fe2d80-33ef-4ee4-a409-cc43b935bcc6")],
                    Runtime = runtime,
                    // Every message kept, as the check reads the game's.
                    Configuration = Assets.PackageConfiguration.Development,
                });
                shell.OpenProject(projectFile);
                shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Level.crscene"));
                yield return 3;
                string what = runtime == Assets.PackageRuntime.Jit ? "from the JIT package" : "from the NativeAOT package";
                PackageResult? built = shell.Build();
                Check(built is { Succeeded: true }, $"the {runtime} package builds", string.Join("; ", built?.Errors ?? []));
                if (built is not { Succeeded: true })
                {
                    continue;
                }
                Directory.Move(imported, imported + "-aside");
                foreach (int wait in Run(shell, what, () => shell.RunPackage(built.Folder, RunOptions(30))))
                {
                    yield return wait;
                }
                PutBack(imported);
                Check(_run.Contains(loaded, StringComparison.Ordinal) &&
                      _run.Contains("reading 8 assets from Game.crpak", StringComparison.Ordinal),
                    $"{what} the game ({loaded}) reads its assets from its archive", Tail(_run));
                Compare(what);
            }
            yield break;
        }
        if (_archive)
        {
            Assets.CookResult? cooked = shell.Cook(Path.Combine(Path.GetFullPath(_folder), "archive-cooked"));
            Check(cooked is { Succeeded: true }, "the project cooks", string.Join("; ", cooked?.Errors ?? []));
            if (cooked is not { Succeeded: true })
            {
                yield break;
            }
            // The imported files moved aside, so nothing can come from the Cache.
            string imported = Path.Combine(copy, "Cache", "Imported");
            Directory.Move(imported, imported + "-aside");
            foreach (int wait in Run(shell, "from the archive", () => shell.RunCooked(cooked.ArchivePath, RunOptions(30))))
            {
                yield return wait;
            }
            PutBack(imported);
            Check(_run.Contains("reading 8 assets from Game.crpak", StringComparison.Ordinal),
                "the run reads its assets from the archive, with the Cache's imported files moved aside", Tail(_run));
            Compare("from the archive");
            yield break;
        }

        double[] into = new double[2];
        int[] rates = [30, 240];
        for (int r = 0; r < rates.Length; ++r)
        {
            int rate = rates[r];
            foreach (int wait in Run(shell, $"at {rate} fps", () => shell.RunStandalone(RunOptions(rate))))
            {
                yield return wait;
            }
            into[r] = _into;
        }

        // The screw between frames isn't the kick's curve (ADR 0096), so a
        // 30 fps frame across the kick's snap moves the hit a little; 0.5 ms
        // is under a sixtieth of that frame (ADR 0097).
        double apart = Math.Abs(into[0] - into[1]);
        Console.WriteLine($"LevelChecks: the 30 and 240 fps hits are {apart:0.000} ms apart");
        Check(apart < 0.5, "the hit lands at the same moment at 30 and 240 fps, within half a millisecond",
            $"{apart:0.000} ms");
    }

    // Each run's options: headless, on the fixed clock (ADR 0105), and
    // without the pacing wait, which would make every frame wait for the GPU
    // to finish the one before; beside this editor's own frames, on Windows,
    // that wait more than doubled a frame's cost (ADR 0111). The clock is
    // fixed, so the run plays out the same either way.
    private static string[] RunOptions(int rate) =>
        ["--headless", "--fixed-clock", "--no-pacing", "--frame-rate", rate.ToString(CultureInfo.InvariantCulture),
            "--game-arg", $"seconds={Seconds}"];

    // The latest run against the one from the Cache: the hit within half a
    // millisecond, and the arrival within a frame, since the route times it
    // on the input clock, real time, which a run's pacing moves by a
    // millisecond or so.
    private void Compare(string what)
    {
        Console.WriteLine($"LevelChecks: from the Cache the hit is {_fromCache:0.000} ms in, {what} {_into:0.000} ms");
        Check(Math.Abs(_fromCache - _into) < 0.5, $"{what} the hit lands as it does from the Cache",
            $"{_fromCache:0.000} and {_into:0.000} ms");
        Check(_route.Success && Math.Abs(Arrival(_route) - _fromCacheArrival) < 1.0 / 30,
            $"{what} the Walker arrives within a frame of the same moment", $"{_cacheRoute} and {_route.Groups["arrived"].Value}");
    }

    // One headless run, started by `start`, checked: the post hit once inside
    // its window, the route walked to the end without falling, a clean end.
    // A frame the loop clamped (longer than four ticks, ADR 0036) drops time
    // by design, moving the swing: such a run, as a loaded machine can make,
    // is run again, up to three times.
    private IEnumerable<int> Run(Shell shell, string what, Func<bool> start)
    {
        _run = "";
        _route = Match.Empty;
        _into = double.NaN;
        for (int attempt = 1; attempt <= 3; ++attempt)
        {
            int ended = Count(Log(shell), "The standalone run ended");
            Check(start(), $"the run {what} starts");
            long started = Environment.TickCount64;
            while (Count(Log(shell), "The standalone run ended") == ended && Environment.TickCount64 - started < 180000)
            {
                yield return 2;
            }
            _run = LastRun(Log(shell));
            _route = Route().Match(_run);
            int clamped = _route.Success ? int.Parse(_route.Groups["clamped"].Value, CultureInfo.InvariantCulture) : 0;
            if (clamped == 0)
            {
                break;
            }
            Console.WriteLine($"LevelChecks: {what}, {clamped} frames clamped (the machine is busy); " +
                              (attempt < 3 ? "running it again" : "comparing it as it is"));
        }

        MatchCollection hits = Hit().Matches(_run);
        Check(hits.Count == 1, $"{what} the swing hits the post once", Tail(_run));
        if (hits.Count > 0)
        {
            _into = double.Parse(hits[0].Groups["into"].Value, CultureInfo.InvariantCulture);
            Console.WriteLine($"LevelChecks: {what} the post is hit {_into:0.000} ms into the Strike window");
            Check(_into >= 0 && _into <= WindowMs, $"{what} the hit is inside the Strike window", hits[0].Value);
        }
        Check(_route.Success, $"{what} the game reports the route", Tail(_run));
        if (_route.Success)
        {
            int reached = int.Parse(_route.Groups["reached"].Value, CultureInfo.InvariantCulture);
            int count = int.Parse(_route.Groups["count"].Value, CultureInfo.InvariantCulture);
            double lowest = double.Parse(_route.Groups["lowest"].Value, CultureInfo.InvariantCulture);
            Console.WriteLine($"LevelChecks: {what}, {_route.Value}");
            Check(count >= 2 && reached == count, $"{what} the Walker reaches every waypoint", _route.Value);
            Check(_route.Groups["arrived"].Success, $"{what} the Walker arrives", _route.Value);
            Check(lowest > -0.1, $"{what} the Walker never falls through the level", _route.Value);
        }
        Check(_run.Contains("The standalone run ended (exit code 0)", StringComparison.Ordinal),
            $"the run {what} ends cleanly", Tail(_run));
    }

    [GeneratedRegex(@"Walker\.Kicks: [^\n]* hit Post at [0-9.]+ s, (?<into>-?[0-9.]+) ms into its window")]
    private static partial Regex Hit();

    [GeneratedRegex(@"Walker\.Route: [^\n]* reached (?<reached>\d+) of (?<count>\d+) waypoints, (?:(?<arrived>arriving at [0-9.]+ s)|not arriving); lowest at (?<lowest>-?[0-9.]+) m; (?<clamped>\d+) frames clamped")]
    private static partial Regex Route();

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join("\n", H.Descendants(log).OfType<Label>().Select(label => label.Text))
            : "";

    // The log from the latest run's start (the log keeps its last 500 lines).
    private static string LastRun(string log)
    {
        string[] lines = log.Split('\n');
        int start = Array.FindLastIndex(lines, line => line.StartsWith("Running ", StringComparison.Ordinal) &&
                                                       line.EndsWith(" standalone", StringComparison.Ordinal));
        return string.Join("\n", lines[Math.Max(start, 0)..]);
    }

    private static double Arrival(Match route) =>
        route.Groups["arrived"].Success
            ? double.Parse(route.Groups["arrived"].Value.Split(' ')[2], CultureInfo.InvariantCulture)
            : double.NaN;

    private static int Count(string log, string text) =>
        log.Split('\n').Count(line => line.Contains(text, StringComparison.Ordinal));

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    // The Cache's imported files moved back. The editor may have imported
    // them again meanwhile (a watcher settling during the run, as on Windows
    // after the project was copied); what it made is dropped for the real
    // ones. The run read its archive either way, which the checks see.
    private static void PutBack(string imported)
    {
        if (Directory.Exists(imported))
        {
            Directory.Delete(imported, recursive: true);
        }
        Directory.Move(imported + "-aside", imported);
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"LevelChecks: FAILED: {what} {detail}");
        }
    }
}
