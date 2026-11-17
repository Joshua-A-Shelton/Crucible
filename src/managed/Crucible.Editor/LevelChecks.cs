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

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-level", out string? folder))
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
            ? $"LevelChecks: all {_checks} checks passed"
            : $"LevelChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "level-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Level.crscene"));
        yield return 3;

        double[] into = new double[2];
        int[] rates = [30, 240];
        for (int r = 0; r < rates.Length; ++r)
        {
            // A frame the loop clamped (longer than four ticks, ADR 0036)
            // drops time by design, moving the swing: such a run, as a
            // loaded machine can make, is run again, up to three times.
            string run = "";
            Match route = Match.Empty;
            for (int attempt = 1; attempt <= 3; ++attempt)
            {
                int ended = Count(Log(shell), "The standalone run ended");
                Check(shell.RunStandalone("--headless", "--frame-rate",
                    rates[r].ToString(CultureInfo.InvariantCulture), "--game-arg",
                    $"seconds={Seconds}"), $"the run at {rates[r]} fps starts");
                long started = Environment.TickCount64;
                while (Count(Log(shell), "The standalone run ended") == ended &&
                       Environment.TickCount64 - started < 180000)
                {
                    yield return 2;
                }
                run = LastRun(Log(shell));
                route = Route().Match(run);
                int clamped = route.Success ? int.Parse(route.Groups["clamped"].Value, CultureInfo.InvariantCulture) : 0;
                if (clamped == 0)
                {
                    break;
                }
                Console.WriteLine($"LevelChecks: at {rates[r]} fps, {clamped} frames clamped (the machine is busy); " +
                                  (attempt < 3 ? "running it again" : "comparing it as it is"));
            }

            MatchCollection hits = Hit().Matches(run);
            Check(hits.Count == 1, $"at {rates[r]} fps the swing hits the post once", Tail(run));
            if (hits.Count > 0)
            {
                into[r] = double.Parse(hits[0].Groups["into"].Value, CultureInfo.InvariantCulture);
                Console.WriteLine($"LevelChecks: at {rates[r]} fps the post is hit {into[r]:0.000} ms into the Strike window");
                Check(into[r] >= 0 && into[r] <= WindowMs, $"at {rates[r]} fps the hit is inside the Strike window",
                    hits[0].Value);
            }
            Check(route.Success, $"at {rates[r]} fps the game reports the route", Tail(run));
            if (route.Success)
            {
                int reached = int.Parse(route.Groups["reached"].Value, CultureInfo.InvariantCulture);
                int count = int.Parse(route.Groups["count"].Value, CultureInfo.InvariantCulture);
                double lowest = double.Parse(route.Groups["lowest"].Value, CultureInfo.InvariantCulture);
                Console.WriteLine($"LevelChecks: at {rates[r]} fps, {route.Value}");
                Check(count >= 2 && reached == count, $"at {rates[r]} fps the Walker reaches every waypoint", route.Value);
                Check(route.Groups["arrived"].Success, $"at {rates[r]} fps the Walker arrives", route.Value);
                Check(lowest > -0.1, $"at {rates[r]} fps the Walker never falls through the level", route.Value);
            }
            Check(run.Contains("The standalone run ended (exit code 0)", StringComparison.Ordinal),
                $"the run at {rates[r]} fps ends cleanly", Tail(run));
        }

        // The screw between frames isn't the kick's curve (ADR 0096), so a
        // 30 fps frame across the kick's snap moves the hit a little; 0.5 ms
        // is under a sixtieth of that frame (ADR 0097).
        double apart = Math.Abs(into[0] - into[1]);
        Console.WriteLine($"LevelChecks: the 30 and 240 fps hits are {apart:0.000} ms apart");
        Check(apart < 0.5, "the hit lands at the same moment at 30 and 240 fps, within half a millisecond",
            $"{apart:0.000} ms");
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

    private static int Count(string log, string text) =>
        log.Split('\n').Count(line => line.Contains(text, StringComparison.Ordinal));

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

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
