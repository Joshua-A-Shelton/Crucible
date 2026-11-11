using System.Text.Json.Nodes;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Physics;
using Crucible.Scenes;
using Crucible.UI;
using V = Crucible.Editor.ViewportChecks;

namespace Crucible.Editor;

/// <summary>
/// The play toolbar checking itself (ADR 0088), for CTest's
/// launcher.jit.play-toolbar: run with the Walker's project and
/// <c>--game-arg check-play-toolbar=&lt;folder&gt;</c>. On a copy of the
/// project (in the folder), a ball falling freely is timed by its speed: with
/// Pause on, Play runs one tick and holds; Step runs exactly one more; turning
/// Pause off lets it fall on; Stop puts it back. Every press is a click on the
/// toolbar's buttons; the window is saved as play-toolbar.png while paused.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class PlayToolbarChecks : GameSystem
{
    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-play-toolbar", out string? folder))
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
            ? $"PlayToolbarChecks: all {_checks} checks passed"
            : $"PlayToolbarChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        string source = Path.GetDirectoryName(shell.Project?.ProjectFilePath ?? "") ?? "";
        string copy = Path.Combine(Path.GetFullPath(_folder), "play-toolbar-project");
        if (Directory.Exists(copy))
        {
            Directory.Delete(copy, recursive: true);
        }
        AssetChecks.CopyProject(source, copy);
        shell.OpenProject(Path.Combine(copy, Path.GetFileName(shell.Project!.ProjectFilePath)));
        shell.OpenScene(Path.Combine(copy, "Assets", "Scenes", "Walk.crscene"));
        yield return 3;
        PlayToolbar toolbar = shell.Toolbar;
        double tick = 1.0 / 60;
        double g = 9.81;

        // A ball high above everything, falling freely when played.
        Entity ball = shell.CreateEmpty(Entity.Null);
        shell.Rename(ball, "Ball");
        string rigidBody = typeof(RigidBody).FullName!, collider = typeof(Collider).FullName!;
        shell.SetComponent(ball, typeof(LocalTransform).FullName!,
            new JsonObject { ["Position"] = new JsonArray(0, 50, 0) }, "Move");
        shell.SetComponent(ball, rigidBody, new JsonObject { ["Kind"] = "Dynamic", ["Mass"] = 1, ["GravityScale"] = 1 },
            "Add Component");
        shell.SetComponent(ball, collider, new JsonObject { ["Shape"] = "Sphere", ["Size"] = new JsonArray(0.5, 0, 0) },
            "Add Component");
        yield return 2;
        Check(!toolbar.StepButton.IsEnabled, "Step is off before Play");

        // Pause before Play: Play runs one tick, then holds.
        Click(toolbar.PauseButton);
        yield return 2;
        Check(shell.IsPaused && toolbar.PauseButton.Classes.Contains("on"), "Pause is on before Play");
        Check(shell.Main.TitleBar.Title.Contains("(paused)", StringComparison.Ordinal), "the title says paused",
            shell.Main.TitleBar.Title);
        Click(toolbar.PlayButton);
        yield return 8;
        Check(shell.IsPlaying && toolbar.PlayButton.Glyph == PlayGlyph.Stop, "Play plays, and its button shows Stop");
        double first = Speed(shell, ball);
        Check(Math.Abs(first - g * tick) < 0.01, "started paused, it ran one tick", $"{first:0.0000} m/s");
        yield return 10;
        Check(Speed(shell, ball) == first, "paused, it holds", $"{Speed(shell, ball):0.0000} m/s");
        Check(toolbar.StepButton.IsEnabled, "Step is on while paused");
        V.Save(shell.Main.Surface, Path.Combine(_folder, "play-toolbar.png"));

        // Step: exactly one more tick each press.
        Click(toolbar.StepButton);
        yield return 4;
        double second = Speed(shell, ball);
        Check(Math.Abs(second - 2 * g * tick) < 0.01, "Step runs one tick", $"{second:0.0000} m/s");
        Click(toolbar.StepButton);
        yield return 4;
        Check(Math.Abs(Speed(shell, ball) - 3 * g * tick) < 0.01, "and another", $"{Speed(shell, ball):0.0000} m/s");
        Check(shell.Main.StatusBar.Text.Contains("Stepped one tick", StringComparison.Ordinal),
            "the status bar says it stepped", shell.Main.StatusBar.Text);

        // Pause off: it falls on (for a moment of real time: headless frames
        // can come far faster than ticks).
        Click(toolbar.PauseButton);
        long resumed = Environment.TickCount64;
        while (Speed(shell, ball) <= 5 * g * tick && Environment.TickCount64 - resumed < 10000)
        {
            yield return 1;
        }
        Check(!shell.IsPaused && !toolbar.PauseButton.Classes.Contains("on"), "Pause is off");
        Check(Speed(shell, ball) > 5 * g * tick, "unpaused, it falls on", $"{Speed(shell, ball):0.0000} m/s");
        Check(!toolbar.StepButton.IsEnabled, "Step is off while running");

        // Paused again, then Stop: the scene is back, and Pause stays on.
        Click(toolbar.PauseButton);
        yield return 2;
        Click(toolbar.PlayButton);
        yield return 3;
        Check(!shell.IsPlaying && toolbar.PlayButton.Glyph == PlayGlyph.Play, "Stop stops, and the button shows Play");
        Check(shell.IsPaused, "Pause stays on across Stop");
        Entity back = SceneIds.Of(shell.World).Entities.FirstOrDefault(entity => shell.LabelOf(entity) == "Ball");
        Check(!back.IsNull && Speed(shell, back) == 0, "the ball is back as it was");
        Click(toolbar.PauseButton);
        yield return 2;
        Check(!shell.IsPaused, "Pause turns off when stopped");
    }

    // How fast the ball falls, from its RigidBody's velocity.
    private static double Speed(Shell shell, Entity ball) =>
        SceneIds.Read(shell.World, ball, typeof(RigidBody).FullName!)?["LinearVelocity"] is JsonArray velocity
            ? -velocity[1]!.GetValue<double>()
            : double.NaN;

    private static void Click(UIElement button) => V.Click(button.Root!, V.Center(button));

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"PlayToolbarChecks: FAILED: {what} {detail}");
        }
    }
}
