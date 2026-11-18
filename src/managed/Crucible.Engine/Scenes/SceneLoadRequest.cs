using Crucible.Ecs;
using Crucible.Loop;

namespace Crucible.Scenes;

/// <summary>
/// A scene loading while frames go on (ADR 0099), from
/// <see cref="Scene.LoadAsync"/>. Its entities are added to the world all in
/// one frame, once everything they need is loaded: their models drawable,
/// their collision shapes, their Animators' skeletons and clips, the sounds
/// their AudioSources play on start. Until then the world has none of them.
/// Assets only a game's own components name load when the game first uses
/// them. Names are placeholders (docs/naming.md).
/// </summary>
/// <remarks>
/// The scene is read into a practice world that never runs, and each
/// system's load starter goes over it each frame, starting loads and
/// counting what still waits. Once nothing does, the practice world is
/// dropped and the scene is read again, into the real world. Replacing the
/// scene that was there is the game's to do.
/// </remarks>
public sealed class SceneLoadRequest
{
    private readonly World _target;
    private readonly byte[] _scene;
    private readonly PrefabLibrary? _prefabs;
    private World? _practice;
    private uint _most; // the most that ever waited, for Progress
    private SceneLoad? _result;

    internal SceneLoadRequest(World target, byte[] scene, PrefabLibrary? prefabs)
    {
        _target = target;
        _scene = scene;
        _prefabs = prefabs;
        _practice = new World();
        Scene.Load(_practice, scene, prefabs: prefabs);
    }

    /// <summary>From 0 to 1: how much of what the scene's components wait for has loaded. Never goes back.</summary>
    public float Progress { get; private set; }

    /// <summary>Whether the scene's entities are in the world.</summary>
    public bool IsDone => _result is not null;

    /// <summary>What loading the scene made, once <see cref="IsDone"/>.</summary>
    /// <exception cref="InvalidOperationException">The scene is still loading.</exception>
    public SceneLoad Result => _result ?? throw new InvalidOperationException("the scene is still loading");

    internal World Target => _target;

    // Counts what still waits (starting loads for anything new, such as a
    // model's textures once it's read); with nothing left, adds the scene to
    // the world. True once done, or dropped.
    internal bool Advance()
    {
        if (_result is not null || _practice is null)
        {
            return true;
        }
        uint waiting = Crucible.Assets.Api.StartLoads(_practice!.Live);
        _most = Math.Max(_most, waiting);
        if (waiting != 0)
        {
            Progress = Math.Max(Progress, 1 - (float)waiting / _most);
            return false;
        }
        Drop();
        _result = Scene.Load(_target, _scene, prefabs: _prefabs);
        Progress = 1;
        return true;
    }

    // The practice world, freed; the request stays unfinished.
    internal void Drop()
    {
        _practice?.Dispose();
        _practice = null;
    }
}

/// <summary>
/// Moves on every <see cref="SceneLoadRequest"/> for its schedule's world
/// (ADR 0099), in BeginFrame after "Assets.Publish": a scene whose loads were
/// published this frame is added this frame. Those still loading when the
/// loop stops are dropped. Names are placeholders (docs/naming.md).
/// </summary>
public sealed class SceneLoadSystem : GameSystem
{
    // Every world's; a schedule (the editor's, its play mode's) moves on its
    // own world's. Locked only for tests, whose loops run side by side.
    private static readonly List<SceneLoadRequest> _pending = [];

    internal static void Add(SceneLoadRequest request)
    {
        lock (_pending)
        {
            _pending.Add(request);
        }
    }

    // The world's pending requests (every world's, given none) counted
    // again; finished ones forgotten. Allocates nothing: it runs each frame.
    internal static void Advance(World? world = null)
    {
        lock (_pending)
        {
            // In the order they were asked for, so scenes finishing in one
            // frame are added in that order.
            int kept = 0;
            for (int i = 0; i < _pending.Count; ++i)
            {
                SceneLoadRequest request = _pending[i];
                if ((world is null || request.Target == world) && request.Advance())
                {
                    continue;
                }
                _pending[kept++] = request;
            }
            _pending.RemoveRange(kept, _pending.Count - kept);
        }
    }

    public override void Run(FrameContext context)
    {
        if (context.HasWorld)
        {
            Advance(context.World);
        }
    }

    public override void Stop(FrameContext context)
    {
        if (!context.HasWorld)
        {
            return;
        }
        lock (_pending)
        {
            for (int i = _pending.Count - 1; i >= 0; --i)
            {
                if (_pending[i].Target == context.World)
                {
                    _pending[i].Drop();
                    _pending.RemoveAt(i);
                }
            }
        }
    }
}
