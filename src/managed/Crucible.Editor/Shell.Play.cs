using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Hosting;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Scenes;
using Crucible.UI;

namespace Crucible.Editor;

// The project's own code and play mode (ADR 0072). Opening a project builds
// its Code folder and loads the game, so its components are known before a
// scene loads; saving a .cs file rebuilds it and hot-reloads it. Play saves
// the scene to memory and runs the game's systems in the same world (through
// one editor system per phase, PlayPhaseSystems.cs); Stop puts the scene back.
// Pause (ADR 0088) holds the gameplay phases and the game's sound while the
// game still draws; Step runs one fixed tick.
internal sealed partial class Shell
{
    private const string JitOnly = "The editor loads games at runtime, which only JIT builds do (ADR 0001).";

    private GameBuilder? _builder;
    private AssetWatcher? _codeWatcher;
    private LoadedGame? _game;
    private Schedule? _playSchedule;
    private Dictionary<string, GameSystem[]> _playPhases = [];
    private byte[]? _playSnapshot;
    private HashSet<ulong> _keptAtPlay = []; // entities without scene IDs that were there before Play
    private UndoHistory.Saved? _historyAtPlay;
    private InputEvent[] _hiddenEvents = [];
    // Pause and Step (ADR 0088): a step asked for, the frame running it, and
    // the fixed ticks it has run (one, whatever the editor's loop gives).
    private bool _stepPending;
    private bool _stepping;
    private int _stepTicks;
    private bool _skippingTick;

    /// <summary>Whether the game is playing.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// Whether Pause is on (ADR 0088): while playing, the game's gameplay
    /// phases and sound hold; on before Play, Play runs the first tick and
    /// holds. It stays on across Stop.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>Play started or stopped.</summary>
    public event Action? PlayStateChanged;

    /// <summary>
    /// A new build of the game is about to replace the old one, which
    /// unloads after: whatever holds the old version's types (the
    /// inspector's sections, an edit in progress) finishes and lets go now,
    /// or it can't unload (ADR 0011).
    /// </summary>
    public event Action? GameReloading;

    /// <summary>The project's game, once built and loaded.</summary>
    public LoadedGame? Game => _game;

    public GameBuilder? Builder => _builder;

    /// <summary>The Game panel's view while it has the game's input; null when the editor keeps it.</summary>
    internal GameView? GameInput { get; set; }

    private void RegisterPlayCommands()
    {
        Commands.Add(new Command("Play.Toggle", "Play", TogglePlay, () => _context.HasWorld)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl, Keycode.P),
            IsChecked = () => IsPlaying,
        });
        Commands.Add(new Command("Play.Pause", "Pause", () => Pause(!IsPaused), () => _context.HasWorld)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Shift, Keycode.P),
            IsChecked = () => IsPaused,
        });
        Commands.Add(new Command("Play.Step", "Step", Step, () => IsPlaying && IsPaused)
        {
            Shortcut = new Shortcut(KeyModifiers.Ctrl | KeyModifiers.Alt, Keycode.P),
        });
    }

    /// <summary>Turns Pause on or off (ADR 0088); while playing, the game holds or goes on from where it was.</summary>
    public void Pause(bool paused)
    {
        if (paused == IsPaused)
        {
            return;
        }
        IsPaused = paused;
        if (IsPlaying)
        {
            Audio.Api.PauseAll(paused);
        }
        if (!paused)
        {
            _stepPending = false;
        }
        UpdateTitle();
        PlayStateChanged?.Invoke();
        Status(paused ? "Paused" : IsPlaying ? $"Playing {_builder?.AssemblyName}" : "Pause off");
    }

    /// <summary>While paused, runs the game for one fixed tick on the next frame, sound included.</summary>
    public void Step()
    {
        if (!IsPlaying || !IsPaused || _stepPending)
        {
            return;
        }
        _stepPending = true;
    }

    private void TogglePlay()
    {
        if (IsPlaying)
        {
            Stop();
        }
        else
        {
            Play();
        }
    }

    // -----------------------------------------------------------------------
    // Building and loading
    // -----------------------------------------------------------------------

    // Opening a project: its code built now (its components must be known
    // before a scene loads), and watched from then on.
    private void OpenProjectCode(Project project)
    {
        // The native module first: the game's C# uses its bindings.
        OpenProjectNative(project);
        _codeWatcher?.Dispose();
        _codeWatcher = null;
        _builder = new GameBuilder(project);
        if (Directory.Exists(project.CodeFolder))
        {
            try
            {
                _codeWatcher = new AssetWatcher(project.CodeFolder);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                Log($"Can't watch {project.CodeFolder} for changes: {exception.Message}");
            }
        }
        if (!_builder.HasCode)
        {
            return;
        }
        Log($"Building {_builder.AssemblyName}…");
        Loaded(_builder.BuildNow());
    }

    // Each frame: code changes that settled start a build; a finished build is loaded.
    internal void PollCode()
    {
        if (_builder is null)
        {
            return;
        }
        if (_codeWatcher is not null && _codeWatcher.TakeSettled(out _, out _) && _builder.HasCode && _builder.Start())
        {
            Status($"Building {_builder.AssemblyName}…");
        }
        if (_builder.TakeFinished() is { } build)
        {
            Loaded(build);
        }
    }

    /// <summary>Builds the project's code now, and loads it if it built.</summary>
    public bool BuildCode()
    {
        if (_builder is null || !_builder.HasCode)
        {
            return false;
        }
        return Loaded(_builder.BuildNow());
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = JitOnly)]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = JitOnly)]
    private bool Loaded(GameBuild build)
    {
        if (!build.Succeeded || build.Assembly is null)
        {
            foreach (BuildError error in build.Errors)
            {
                Log($"Build error: {error}");
            }
            Status(_game is null ? "The game didn't build" : "The game didn't build: still running the last build");
            return false;
        }
        bool wasPlaying = IsPlaying;
        if (wasPlaying)
        {
            StopSystems();
        }
        try
        {
            if (_game is null)
            {
                _game = GameLoader.Load(build.Assembly);
                _game.RegisterComponents();
                Log($"Loaded {Path.GetFileName(build.Assembly)}");
            }
            else
            {
                LoadedGame old = _game;
                GameReloading?.Invoke();
                _game = GameLoader.Reload(old, build.Assembly, out IReadOnlyList<MigrationNote> notes);
                _game.RegisterComponents();
                foreach (MigrationNote note in notes)
                {
                    Log($"Reload: {note}");
                }
                WeakReference unloaded = old.Unload();
                // ADR 0011: an old version that won't unload is a leak worth knowing about.
                if (!GameLoader.WaitForUnload(unloaded))
                {
                    Log("The previous build didn't unload: something still holds on to it");
                }
                Log($"Reloaded {Path.GetFileName(build.Assembly)}");
            }
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or InvalidOperationException)
        {
            Log($"Can't load {build.Assembly}: {exception.Message}");
            return false;
        }
        if (wasPlaying)
        {
            StartSystems();
        }
        Status(wasPlaying ? "Reloaded while playing" : "Game built");
        return true;
    }

    // -----------------------------------------------------------------------
    // Play and Stop
    // -----------------------------------------------------------------------

    /// <summary>Saves the scene to memory and runs the game's systems on it.</summary>
    public void Play()
    {
        if (IsPlaying || !_context.HasWorld)
        {
            return;
        }
        World world = World;
        _playSnapshot = Scene.Save(world, SceneId, Prefabs);
        _keptAtPlay = [.. world.Contents().Select(item => item.Entity)
            .Where(entity => SceneIds.IdOf(world, entity) == 0).Select(entity => entity.Value)];
        _historyAtPlay = History.Save();
        IsPlaying = true;
        if (IsPaused)
        {
            // Started paused: the first tick runs, then it holds.
            Audio.Api.PauseAll(true);
            _stepPending = true;
        }
        StartSystems();
        // The Game panel shown, with the input.
        if (_open.GetValueOrDefault("Game") is GamePanel game)
        {
            game.Group?.Activate(game);
            game.View.TakeInput(true);
        }
        UpdateTitle();
        PlayStateChanged?.Invoke();
        Log(_game is null ? "Playing (no game code: nothing runs)" : $"Playing {_builder?.AssemblyName}");
    }

    /// <summary>Stops the game's systems and puts the scene back as it was at Play.</summary>
    public void Stop()
    {
        if (!IsPlaying)
        {
            return;
        }
        StopSystems();
        // The game's sounds stop with it, music and sources too (ADR 0082);
        // let go first if paused, so their fades run.
        if (IsPaused)
        {
            Audio.Api.PauseAll(false);
        }
        _stepPending = _stepping = false;
        Audio.Api.StopAll(0.1f);
        IsPlaying = false;
        GameInput = null;
        World world = World;
        ulong selected = SceneIds.IdOf(world, Selection.Selected);
        // Everything made since Play goes, then the scene comes back with its IDs.
        foreach (Entity entity in world.Contents().Select(item => item.Entity).ToArray())
        {
            if (!_keptAtPlay.Contains(entity.Value) && world.IsAlive(entity) && world.ParentOf(entity).IsNull)
            {
                world.Destroy(entity);
            }
        }
        if (_playSnapshot is { } snapshot)
        {
            Scene.Load(world, snapshot, prefabs: Prefabs);
        }
        _playSnapshot = null;
        if (_historyAtPlay is { } saved)
        {
            History.Restore(saved);
        }
        Selection.Selected = SceneIds.Find(world, selected);
        SceneChanged?.Invoke();
        UpdateTitle();
        PlayStateChanged?.Invoke();
        Log("Stopped: the scene is as it was");
    }

    // The game's systems in a schedule of their own, phase by phase; started.
    private void StartSystems()
    {
        // The engine's native systems are in the editor's schedule already;
        // gameplay ones (physics, audio, a native module's) are the game's
        // (ADR 0074, 0075).
        ScheduleBuilder builder = new() { IncludeNativeSystems = false, IncludeGameplaySystems = true };
        foreach (string phase in (string[])[Phases.BeginFrame, Phases.Input, ScheduleBuilder.FixedTick, Phases.Update,
                     Phases.Animation, Phases.LateUpdate, Phases.RenderPrepare, Phases.RenderSubmit, Phases.EndFrame])
        {
            builder.AddPhase(phase);
        }
        foreach (string phase in (string[])[Phases.PrePhysics, Phases.Physics, Phases.PostPhysics])
        {
            builder.AddFixedPhase(phase);
        }
        _game?.RegisterSystems(builder);
        _playSchedule = builder.Build();
        _playPhases = [];
        foreach (FramePhase phase in _playSchedule.Phases)
        {
            _playPhases[phase.Name] = [.. phase.Systems];
            foreach (FramePhase sub in phase.SubPhases)
            {
                _playPhases[sub.Name] = [.. sub.Systems];
            }
        }
        foreach (GameSystem system in _playSchedule.Systems)
        {
            Guard(system, () => system.Start(_context));
        }
    }

    private void StopSystems()
    {
        if (_playSchedule is null)
        {
            return;
        }
        GameSystem[] systems = [.. _playSchedule.Systems];
        for (int i = systems.Length - 1; i >= 0; --i)
        {
            GameSystem system = systems[i];
            Guard(system, () => system.Stop(_context));
        }
        _playSchedule = null;
        _playPhases = [];
    }

    /// <summary>
    /// Runs the game's systems of a phase, while playing (the phase's editor
    /// system calls it). The game sees the frame's input only while the Game
    /// panel has it, with pointer positions in the panel's pixels.
    /// </summary>
    internal void RunPlayPhase(string phase, FrameContext context)
    {
        if (!IsPlaying)
        {
            return;
        }
        if (phase == Phases.BeginFrame)
        {
            _stepping = IsPaused && _stepPending;
            if (_stepping)
            {
                _stepPending = false;
                _stepTicks = 0;
                Audio.Api.AdvancePaused((float)context.FixedStepSeconds);
            }
        }
        bool drawing = phase is Phases.RenderPrepare or Phases.RenderSubmit;
        if (IsPaused && !_stepping && !drawing)
        {
            return; // paused: gameplay holds, drawing goes on
        }
        if (!_stepping || drawing)
        {
            RunPlaySystems(phase, context);
            return;
        }
        // A step: exactly one fixed tick, whatever the editor's loop runs
        // this frame, and the rest of the frame as one tick long.
        bool fixedPhase = phase is Phases.PrePhysics or Phases.Physics or Phases.PostPhysics;
        if (phase == Phases.PrePhysics)
        {
            _skippingTick = _stepTicks > 0;
            _stepTicks = 1;
        }
        if (fixedPhase && _skippingTick)
        {
            return;
        }
        double delta = context.DeltaSeconds, alpha = context.FixedAlpha;
        int steps = context.FixedSteps;
        context.DeltaSeconds = context.FixedStepSeconds;
        context.FixedSteps = 1;
        context.FixedAlpha = fixedPhase ? alpha : 1;
        try
        {
            if (phase == Phases.Update && _stepTicks == 0)
            {
                // The editor's loop ran no tick this frame: the step's runs now.
                _stepTicks = 1;
                RunPlaySystems(Phases.PrePhysics, context);
                RunPlaySystems(Phases.Physics, context);
                RunPlaySystems(Phases.PostPhysics, context);
            }
            RunPlaySystems(phase, context);
        }
        finally
        {
            context.DeltaSeconds = delta;
            context.FixedSteps = steps;
            context.FixedAlpha = alpha;
            if (phase == Phases.EndFrame)
            {
                _stepping = false;
                Status("Stepped one tick");
            }
        }
    }

    private void RunPlaySystems(string phase, FrameContext context)
    {
        if (!_playPhases.TryGetValue(phase, out GameSystem[]? systems) || systems.Length == 0)
        {
            return;
        }
        int count = context.Events.Length;
        if (_hiddenEvents.Length < count)
        {
            _hiddenEvents = new InputEvent[Math.Max(count, 64)];
        }
        context.Events.CopyTo(_hiddenEvents);
        ShowGameInput(context, count);
        try
        {
            foreach (GameSystem system in systems)
            {
                if (system.Enabled)
                {
                    Guard(system, () => system.Run(context));
                }
            }
        }
        finally
        {
            _hiddenEvents.AsSpan(0, count).CopyTo(context.EventBuffer);
            context.SetEventCount(count);
        }
    }

    // What the game sees of the frame's events: none, unless the Game panel
    // has its input; then its window's pointer moved into the panel's pixels.
    private void ShowGameInput(FrameContext context, int count)
    {
        if (GameInput is not { Root: { } root } view || WindowOf(view) is not { } window)
        {
            context.SetEventCount(0);
            return;
        }
        Vector2 origin = new(view.Bounds.X * root.Scale, view.Bounds.Y * root.Scale);
        Span<InputEvent> buffer = context.EventBuffer;
        int kept = 0;
        for (int i = 0; i < count; ++i)
        {
            InputEvent input = _hiddenEvents[i];
            bool pointer = input.Type is InputEventType.MouseMotion or InputEventType.MouseButtonDown or
                InputEventType.MouseButtonUp or InputEventType.MouseWheel;
            if (pointer)
            {
                if (input.Window.Value != window.Window.Value)
                {
                    continue;
                }
                input = input with { X = input.X - origin.X, Y = input.Y - origin.Y };
            }
            buffer[kept++] = input;
        }
        context.SetEventCount(kept);
    }

    // A game system's exception is reported and play goes on, as the frame loop does (ADR 0036).
    private void Guard(GameSystem system, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ++system.ExceptionCount;
            Log($"{system.Name} threw: {exception.GetType().Name}: {exception.Message}");
            Console.Error.WriteLine($"Crucible.Editor: {system.Name} threw: {exception}");
        }
    }
}
