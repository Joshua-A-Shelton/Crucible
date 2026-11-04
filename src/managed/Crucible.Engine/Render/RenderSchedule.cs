using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Crucible.Render;

/// <summary>
/// A render schedule (ADR 0048): steps edited by name like frame phases,
/// the engine's C++ steps and C# <see cref="IRenderStep"/>s together (ADR
/// 0058). Surfaces render <see cref="Default"/>, which holds the scene's
/// steps, unless given another (ADR 0066): a new schedule holds only Clear,
/// for surfaces that draw just their own steps, such as an editor window's
/// UI. Native code calls C# steps through one engine trampoline with a step
/// ID, so it never holds a pointer into a game assembly. Main thread only,
/// like the renderer.
/// </summary>
public sealed unsafe class RenderSchedule : IDisposable
{
    private sealed record Entry(RenderSchedule Schedule, string Name, IRenderStep Step);

    // Every schedule's C# steps, by the ID native code calls them with.
    private static readonly Dictionary<ulong, Entry> _steps = [];
    private static readonly List<RenderSchedule> _schedules = [];
    private static readonly RenderStepContext _context = new();
    private static ulong _nextId = 1;
    private static bool _trampolineSet;
    private static ExceptionDispatchInfo? _failure;

    private readonly Dictionary<string, ulong> _ids = new(StringComparer.Ordinal);
    private uint _id;
    private bool _disposed;

    private RenderSchedule(uint id)
    {
        _id = id;
        _schedules.Add(this);
    }

    /// <summary>The scene's steps (DepthPrepass … Tonemap), which surfaces render unless given another.</summary>
    public static RenderSchedule Default { get; } = new(0);

    /// <summary>A new schedule holding one step, Clear (the surface cleared to the frame's clear color).</summary>
    public static RenderSchedule Create() => new(Api.CreateRenderSchedule());

    /// <summary>Has a surface render a schedule; <see cref="Default"/> puts it back.</summary>
    public static void SetSurfaceSchedule(SurfaceHandle surface, RenderSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        Api.SetSurfaceSchedule(surface, schedule.Id);
    }

    /// <summary>Adds the step after every other.</summary>
    public void Add(string name, IRenderStep step) => Insert(name, step, StepPlacement.Last, "");

    public void InsertBefore(string existing, string name, IRenderStep step) =>
        Insert(name, step, StepPlacement.Before, existing);

    public void InsertAfter(string existing, string name, IRenderStep step) =>
        Insert(name, step, StepPlacement.After, existing);

    /// <summary>Puts the step where <paramref name="existing"/> was; that one is released.</summary>
    public void Replace(string existing, string name, IRenderStep step)
    {
        Insert(name, step, StepPlacement.Replacing, existing);
        if (existing != name)
        {
            Forget(existing);
        }
        ThrowFailure(); // the old step's Release
    }

    /// <summary>Removes a step, C++ or C#; a C# step is released first. Throws if there is none of that name.</summary>
    public void Remove(string name)
    {
        Api.RemoveStep(Id, name);
        Forget(name);
        ThrowFailure();
    }

    /// <summary>The steps' names, in order.</summary>
    public IReadOnlyList<string> StepNames
    {
        get
        {
            uint id = Id;
            string[] names = new string[Api.StepCount(id)];
            for (int i = 0; i < names.Length; ++i)
            {
                names[i] = Api.StepName(id, (uint)i);
            }
            return names;
        }
    }

    /// <summary>
    /// Releases its steps and destroys it; surfaces rendering it go back to
    /// <see cref="Default"/>. The default can't be disposed.
    /// </summary>
    public void Dispose()
    {
        if (_id == 0 || _disposed)
        {
            return;
        }
        _disposed = true;
        Api.DestroyRenderSchedule(_id);
        foreach (ulong stepId in _ids.Values)
        {
            _steps.Remove(stepId);
        }
        _ids.Clear();
        _schedules.Remove(this);
        ThrowFailure();
    }

    private uint Id => _disposed ? throw new ObjectDisposedException(nameof(RenderSchedule)) : _id;

    /// <summary>
    /// Rethrows what a C# step threw during the last native call that ran
    /// steps (a frame, or a schedule edit that prepared or released one).
    /// </summary>
    internal static void ThrowFailure()
    {
        ExceptionDispatchInfo? failure = _failure;
        _failure = null;
        failure?.Throw();
    }

    /// <summary>Removes every schedule's C# steps, before the renderer stops.</summary>
    internal static void RemoveAll()
    {
        foreach (RenderSchedule schedule in _schedules.ToArray())
        {
            foreach (string name in schedule._ids.Keys.ToArray())
            {
                Api.RemoveStep(schedule._id, name);
                schedule.Forget(name);
            }
        }
        _failure = null;
    }

    /// <summary>
    /// Removes the steps whose types a game's load context holds, from every
    /// schedule, so none keeps an unloading game alive (ADR 0011).
    /// </summary>
    internal static void Forget(AssemblyLoadContext context)
    {
        foreach ((ulong id, Entry entry) in _steps.ToArray())
        {
            if (AssemblyLoadContext.GetLoadContext(entry.Step.GetType().Assembly) == context)
            {
                Api.RemoveStep(entry.Schedule._id, entry.Name);
                entry.Schedule._ids.Remove(entry.Name);
                _steps.Remove(id);
            }
        }
        _failure = null;
    }

    private void Insert(string name, IRenderStep step, StepPlacement placement, string anchor)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!_trampolineSet)
        {
            Api.SetStepTrampoline((ulong)(delegate* unmanaged<ulong, uint, nint, int>)&Call);
            _trampolineSet = true;
        }
        // Known before the native insert, which prepares the step.
        ulong id = _nextId++;
        _steps[id] = new Entry(this, name, step);
        try
        {
            Api.InsertManagedStep(Id, name, id, placement, anchor);
        }
        catch (CrucibleException)
        {
            _steps.Remove(id);
            ThrowFailure(); // Prepare's exception, if it threw
            throw;
        }
        _ids[name] = id;
    }

    private void Forget(string name)
    {
        if (_ids.Remove(name, out ulong id))
        {
            _steps.Remove(id);
        }
    }

    // Native code's way in: (step ID, StepCall, StepFrame*). An exception
    // can't cross into native code, so it's kept for ThrowFailure, and the
    // native side sees a failure.
    [UnmanagedCallersOnly]
    private static int Call(ulong id, uint call, nint frame)
    {
        if (!_steps.TryGetValue(id, out Entry? entry))
        {
            return 1;
        }
        try
        {
            switch ((StepCall)call)
            {
            case StepCall.Prepare:
                entry.Step.Prepare();
                break;
            case StepCall.Release:
                entry.Step.Release();
                break;
            case StepCall.AddPasses:
                _context.Begin(new StepFrameHandle(frame));
                try
                {
                    entry.Step.AddPasses(_context);
                    _context.Commit();
                }
                finally
                {
                    _context.End();
                }
                break;
            }
            return 0;
        }
        catch (Exception exception)
        {
            string during = (StepCall)call switch
            {
                StepCall.Prepare => "preparing",
                StepCall.Release => "releasing",
                _ => "adding passes",
            };
            _failure ??= ExceptionDispatchInfo.Capture(
                new InvalidOperationException($"Render step \"{entry.Name}\" threw while {during}", exception));
            return 1;
        }
    }
}
