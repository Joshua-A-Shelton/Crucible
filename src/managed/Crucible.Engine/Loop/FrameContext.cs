using Crucible.Ecs;
using Crucible.Platform;
using Crucible.Render;

namespace Crucible.Loop;

/// <summary>
/// What the phases of one frame share: allocated once, reused every frame, so
/// the loop creates no garbage.
/// </summary>
public sealed unsafe class FrameContext
{
    // Enough for a frame's events at any realistic rate; more stay queued in
    // SDL for the next frame.
    private readonly InputEvent[] _events = new InputEvent[256];
    private int _eventCount;

    /// <summary>The main window, or default when headless.</summary>
    public WindowHandle Window { get; init; }

    /// <summary>The main window's surface, or default when headless.</summary>
    public SurfaceHandle Surface { get; init; }

    /// <summary>Whether BeginFrame waits for the next frame (ADR 0003); --no-pacing turns it off.</summary>
    public bool Pacing { get; init; } = true;

    /// <summary>The latency harness, when --latency is given; null otherwise.</summary>
    public LatencyRecorder? Latency { get; init; }

    /// <summary>
    /// The game's options, from the launcher's --game-arg key=value (ADR
    /// 0011): the engine passes them on without reading them. Empty without any.
    /// </summary>
    public IReadOnlyDictionary<string, string> GameArguments { get; init; } = new Dictionary<string, string>();

    /// <summary>The game's plain C# services (ADR 0041), found by type.</summary>
    public Services Services { get; init; } = new();

    /// <summary>The schedule being run, for systems that report on others (timing, the editor).</summary>
    public Schedule? Schedule { get; internal set; }

    /// <summary>Counts from 0.</summary>
    public ulong FrameNumber { get; internal set; }

    /// <summary>When the frame started, on the input event clock (Platform.Api.TimestampNow).</summary>
    public ulong FrameStartNs { get; internal set; }

    /// <summary>Set by a phase to end the loop after this frame.</summary>
    public bool QuitRequested { get; set; }

    /// <summary>Ok, or the status of the failure that ends the loop.</summary>
    public Status Failure { get; private set; } = Status.Ok;

    /// <summary>The system that failed, for the message.</summary>
    public string? FailedSystem { get; private set; }

    /// <summary>
    /// The main world (ADR 0036), made by the engine: every system finds it
    /// here, C++ ones too. Throws if the context has none (as in loop tests).
    /// </summary>
    public World World
    {
        get => _world ?? throw new InvalidOperationException("This frame context has no main world.");
        init
        {
            _world = value;
            _worlds.Add(new WorldBuffer(value));
        }
    }

    public bool HasWorld => _world is not null;

    /// <summary>
    /// The main world's command buffer: record structural changes here, and
    /// the phase's sync point applies them (ADR 0013, 0036). Valid while the
    /// loop runs.
    /// </summary>
    public CommandBuffer Commands => CommandsFor(World);

    /// <summary>
    /// Adds a world whose command buffer every sync point applies too, as for
    /// the main world.
    /// </summary>
    public void AddWorld(World world)
    {
        WorldBuffer entry = new(world);
        if (_running)
        {
            entry.Buffer = world.CreateCommandBuffer();
        }
        _worlds.Add(entry);
    }

    /// <summary>Removes an added world, disposing its command buffer (unapplied commands are dropped).</summary>
    public void RemoveWorld(World world)
    {
        int index = _worlds.FindIndex(entry => entry.World == world);
        if (index >= 0)
        {
            _worlds[index].Buffer?.Dispose();
            _worlds.RemoveAt(index);
        }
    }

    /// <summary>A world's command buffer, for the main world or one added with <see cref="AddWorld"/>.</summary>
    public CommandBuffer CommandsFor(World world)
    {
        foreach (WorldBuffer entry in _worlds)
        {
            if (entry.World == world)
            {
                return entry.Buffer ?? throw new InvalidOperationException("Command buffers exist while the loop runs.");
            }
        }
        throw new InvalidOperationException("That world isn't in this frame context.");
    }

    /// <summary>
    /// Time since the last frame, clamped to the fixed tick's cap (ADR 0036):
    /// after a long hitch, gameplay and physics agree on how much time passed.
    /// 0 on the first frame.
    /// </summary>
    public double DeltaSeconds { get; internal set; }

    /// <summary>The fixed tick's step.</summary>
    public double FixedStepSeconds { get; internal set; }

    /// <summary>
    /// How far the frame is between the last fixed tick and the next, 0..1,
    /// for interpolating physics transforms (ADR 0003). Set after the
    /// fixed-tick group runs.
    /// </summary>
    public double FixedAlpha { get; internal set; }

    /// <summary>
    /// The main world's change version during the running system's previous
    /// run (ADR 0034), for <c>ChangedSince</c>; 0 on its first run on this
    /// world. The loop advances the version after every system, so writes
    /// since then, by anyone, are newer.
    /// </summary>
    public uint LastRunVersion { get; internal set; }

    /// <summary>Fixed steps run this frame.</summary>
    public int FixedSteps { get; internal set; }

    private readonly World? _world;
    private readonly List<WorldBuffer> _worlds = [];
    private bool _running;

    private sealed class WorldBuffer(World world)
    {
        public World World { get; } = world;
        public CommandBuffer? Buffer { get; set; }
    }

    // What C++ systems see (ADR 0036): native memory the loop owns while it
    // runs, refreshed at each phase.
    internal SystemContext* NativeContext { get; private set; }

    internal void StartRunning()
    {
        _running = true;
        foreach (WorldBuffer entry in _worlds)
        {
            entry.Buffer = entry.World.CreateCommandBuffer();
        }
        NativeContext = (SystemContext*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)sizeof(SystemContext));
        if (_world is not null)
        {
            NativeContext->World = _world.Live;
            NativeContext->Commands = _worlds[0].Buffer!.Handle;
        }
    }

    internal void StopRunning()
    {
        foreach (WorldBuffer entry in _worlds)
        {
            entry.Buffer?.Dispose();
            entry.Buffer = null;
        }
        System.Runtime.InteropServices.NativeMemory.Free(NativeContext);
        NativeContext = null;
        _running = false;
    }

    // A sync point (ADR 0013): every world's recorded changes, applied.
    internal void ApplyCommands()
    {
        for (int i = 0; i < _worlds.Count; ++i)
        {
            _worlds[i].Buffer?.Apply();
        }
    }

    // Before each system: its version from its previous run on the main world.
    internal void BeginSystem(GameSystem system)
    {
        LastRunVersion = _world is not null && ReferenceEquals(system.LastRunWorld, _world) ? system.LastRunVersion : 0;
        if (NativeContext != null)
        {
            NativeContext->LastRunVersion = LastRunVersion;
        }
    }

    // After each system: remember the version it ran at, then start a newer
    // one, so anything written afterwards (other systems, sync points, edits
    // between frames) counts as a change to it (ADR 0034).
    internal void EndSystem(GameSystem system)
    {
        if (_world is not null)
        {
            system.LastRunWorld = _world;
            system.LastRunVersion = _world.CurrentVersion;
            _world.AdvanceVersion();
        }
    }

    internal void RefreshNativeContext()
    {
        NativeContext->FrameNumber = FrameNumber;
        NativeContext->DeltaSeconds = DeltaSeconds;
        NativeContext->FixedStepSeconds = FixedStepSeconds;
        NativeContext->FixedAlpha = FixedAlpha;
        NativeContext->LastRunVersion = LastRunVersion;
    }

    /// <summary>This frame's input and window events, in arrival order.</summary>
    public ReadOnlySpan<InputEvent> Events => _events.AsSpan(0, _eventCount);

    /// <summary>Ends the frame here, and the loop with it, with an error status.</summary>
    public void Fail(Status status, string system)
    {
        if (Failure == Status.Ok)
        {
            Failure = status;
            FailedSystem = system;
        }
    }

    /// <summary>The buffer the Input phase polls into.</summary>
    internal Span<InputEvent> EventBuffer => _events;

    internal void SetEventCount(int count) => _eventCount = count;
}
