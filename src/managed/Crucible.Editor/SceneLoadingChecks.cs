using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Scenes;

namespace Crucible.Editor;

/// <summary>
/// Loading a scene while frames go on checking itself (ADR 0099), for
/// CTest's launcher.jit.scene-loading: run with
/// <c>--game-arg source-project=&lt;Walker.crproject&gt;</c> and
/// <c>--game-arg check-scene-loading=&lt;folder&gt;</c>, on a copy of the
/// project in the folder. The Level scene is
/// loaded into the editor's world with <see cref="Scene.LoadAsync"/>: frames
/// run while it loads, none of its entities are there until all of them are,
/// in one frame, and then nothing they need still waits. Nothing is saved.
/// </summary>
[System(Phases.RenderPrepare)]
internal sealed class SceneLoadingChecks : GameSystem
{
    private static readonly AssetId _level = AssetId.Parse("28fe2d80-33ef-4ee4-a409-cc43b935bcc6");

    private IEnumerator<int>? _script;
    private int _wait;
    private int _checks;
    private int _failures;
    private string _folder = "";

    public override void Start(FrameContext context)
    {
        if (!context.GameArguments.TryGetValue("check-scene-loading", out string? folder))
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
            ? $"SceneLoadingChecks: all {_checks} checks passed"
            : $"SceneLoadingChecks: {_failures} of {_checks} checks failed");
        context.QuitRequested = true;
    }

    private IEnumerable<int> Script()
    {
        Shell shell = Shell.Current ?? throw new InvalidOperationException("No editor shell");
        AssetChecks.OpenCopy(shell, Path.Combine(Path.GetFullPath(_folder), "scene-loading-project"), null);
        yield return 3;
        World world = shell.World;
        int before = world.EntityCount;

        SceneLoadRequest request = Scene.LoadAsync(world, _level);
        Check(!request.IsDone && world.EntityCount == before, "nothing is added when it's asked for");

        // Frames go on while it loads; the scene's entities stay out until
        // all of them come in.
        int frames = 0;
        float progress = 0;
        bool steady = true;
        bool rising = true;
        while (!request.IsDone && frames < 600)
        {
            steady = steady && world.EntityCount == before;
            rising = rising && request.Progress >= progress && request.Progress <= 1;
            progress = request.Progress;
            ++frames;
            yield return 0;
        }
        Console.WriteLine($"SceneLoadingChecks: the Level loaded over {frames} frames");
        Check(request.IsDone, "the Level finished loading", $"(after {frames} frames)");
        Check(frames >= 1, "frames ran while it loaded");
        Check(steady, "none of its entities were there before it finished");
        Check(rising, "progress only went up, to at most 1");
        Check(request.Progress == 1, "progress is 1 once done");
        if (!request.IsDone)
        {
            yield break;
        }

        // All of it in that frame (12 entities at the top, as the launcher
        // reports), and nothing it needs still loading: its models draw, its
        // colliders have their shapes, its Animator its skeleton and clips.
        Check(request.Result.Id == _level, "the result is the Level's");
        Check(request.Result.Roots.Count == 12, "its 12 entities at the top came in",
            $"({request.Result.Roots.Count})");
        Check(request.Result.Roots.All(world.IsAlive), "they're in the world");
        Check(world.EntityCount > before + 12, "their children came with them",
            $"({world.EntityCount - before} entities)");
        Check(Crucible.Assets.Api.StartLoads(world.Live) == 0, "nothing in the world waits for a load");
    }

    private void Check(bool passed, string what, string detail = "")
    {
        ++_checks;
        if (!passed)
        {
            ++_failures;
            Console.WriteLine($"SceneLoadingChecks: FAILED: {what} {detail}");
        }
    }
}
