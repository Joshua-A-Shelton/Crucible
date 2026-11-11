using System.Globalization;
using System.Text.RegularExpressions;
using Crucible.Loop;
using Crucible.UI;
using H = Crucible.Editor.HierarchyChecks;

namespace Crucible.Editor;

/// <summary>
/// The M8 demonstration checking itself headless (ADR 0084), for CTest's
/// samples.walker: run with the Walker's project and
/// <c>--game-arg check-walker=&lt;folder&gt;</c>. On a copy of the project (in
/// the folder): Walk.crscene opened, its game code built, and run standalone
/// headless, mixing offline, at 30 and then 144 frames a second. Each run
/// checks every footstep's onset against its event's time; then the two
/// runs' steps must fall at the same times.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed partial class WalkerChecks : GameSystem
{
    private const int Seconds = 7;

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-walker", out string? folder))
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
            ? $"WalkerChecks: all {_checks} checks passed"
            : $"WalkerChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "walker-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Walk.crscene"));
        yield return 3;

        List<double>[] runs = new List<double>[2];
        int[] rates = [30, 144];
        for (int r = 0; r < rates.Length; ++r)
        {
            int ended = Count(Log(shell), "The standalone run ended");
            Check(shell.RunStandalone("--headless", "--frame-rate", rates[r].ToString(CultureInfo.InvariantCulture),
                "--game-arg", "check=1", "--game-arg", $"seconds={Seconds}"), $"the run at {rates[r]} fps starts");
            long started = Environment.TickCount64;
            while (Count(Log(shell), "The standalone run ended") == ended && Environment.TickCount64 - started < 120000)
            {
                yield return 2;
            }
            string log = Log(shell);
            Match report = Report().Matches(log).LastOrDefault() ?? Match.Empty;
            Check(report.Success, $"at {rates[r]} fps the game reports its footsteps", Tail(log));
            if (!report.Success)
            {
                yield break;
            }
            int heard = int.Parse(report.Groups["heard"].Value, CultureInfo.InvariantCulture);
            int steps = int.Parse(report.Groups["steps"].Value, CultureInfo.InvariantCulture);
            int worst = int.Parse(report.Groups["worst"].Value, CultureInfo.InvariantCulture);
            int late = int.Parse(report.Groups["late"].Value, CultureInfo.InvariantCulture);
            Console.WriteLine($"WalkerChecks: at {rates[r]} fps, {heard} of {steps} footsteps, at most {worst} " +
                              $"frames from their times, {late} late");
            // Over seven seconds the Walker idles (2 s) and walks (1 s, two
            // steps) by turns: at least four steps.
            Check(steps >= 4, $"at {rates[r]} fps it takes its steps", report.Value);
            Check(heard == steps, $"at {rates[r]} fps every step is heard", report.Value);
            Check(worst <= 1, $"at {rates[r]} fps every step starts within a frame of its time", report.Value);
            Check(late == 0, $"at {rates[r]} fps no step starts late", report.Value);
            Check(log.Contains("The standalone run ended (exit code 0)", StringComparison.Ordinal),
                $"the run at {rates[r]} fps ends cleanly", Tail(log));
            runs[r] = [.. report.Groups["times"].Value.Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture))];
        }

        // The same steps at the same times at either rate (from the first).
        int both = Math.Min(runs[0].Count, runs[1].Count);
        double apart = 0;
        for (int i = 0; i < both; ++i)
        {
            apart = Math.Max(apart, Math.Abs(runs[0][i] - runs[1][i]));
        }
        Console.WriteLine($"WalkerChecks: 30 and 144 fps steps at most {apart:0.000} ms apart");
        Check(both >= 4, "both runs take their steps");
        Check(apart < 1.0, "the steps fall at the same times at 30 and 144 fps, within a millisecond",
            $"{apart:0.000} ms");
    }

    [GeneratedRegex(@"Crucible\.Walker: (?<heard>\d+) of (?<steps>\d+) footsteps heard, at most (?<worst>\d+) frames from their times, (?<late>\d+) late; steps at ms (?<times>[0-9.,]+)")]
    private static partial Regex Report();

    private static string Log(Shell shell) =>
        shell.LogPanel is { } log
            ? string.Join("\n", H.Descendants(log).OfType<Label>().Select(label => label.Text))
            : "";

    private static int Count(string log, string text) =>
        log.Split('\n').Count(line => line.Contains(text, StringComparison.Ordinal));

    private static string Tail(string log) => string.Join(" | ", log.Split('\n').TakeLast(8));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"WalkerChecks: FAILED: {what} {detail}");
        }
    }
}
