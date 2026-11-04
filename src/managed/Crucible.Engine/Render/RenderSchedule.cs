using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Crucible.Render;

/// <summary>
/// The renderer's ordered steps (ADR 0048), edited by name like frame
/// phases: the engine's C++ steps and C# <see cref="IRenderStep"/>s together
/// (ADR 0058). Native code calls C# steps through one engine trampoline with
/// a step ID, so it never holds a pointer into a game assembly. Main thread
/// only, like the renderer.
/// </summary>
public static unsafe class RenderSchedule
{
    private sealed record Entry(string Name, IRenderStep Step);

    private static readonly Dictionary<ulong, Entry> _steps = [];
    private static readonly Dictionary<string, ulong> _ids = new(StringComparer.Ordinal);
    private static readonly RenderStepContext _context = new();
    private static ulong _nextId = 1;
    private static bool _trampolineSet;
    private static ExceptionDispatchInfo? _failure;

    /// <summary>Adds the step after every other.</summary>
    public static void Add(string name, IRenderStep step) => Insert(name, step, StepPlacement.Last, "");

    public static void InsertBefore(string existing, string name, IRenderStep step) =>
        Insert(name, step, StepPlacement.Before, existing);

    public static void InsertAfter(string existing, string name, IRenderStep step) =>
        Insert(name, step, StepPlacement.After, existing);

    /// <summary>Puts the step where <paramref name="existing"/> was; that one is released.</summary>
    public static void Replace(string existing, string name, IRenderStep step)
    {
        Insert(name, step, StepPlacement.Replacing, existing);
        if (existing != name)
        {
            Forget(existing);
        }
        ThrowFailure(); // the old step's Release
    }

    /// <summary>Removes a step, C++ or C#; a C# step is released first. Throws if there is none of that name.</summary>
    public static void Remove(string name)
    {
        Api.RemoveStep(name);
        Forget(name);
        ThrowFailure();
    }

    /// <summary>The steps' names, in order.</summary>
    public static IReadOnlyList<string> StepNames
    {
        get
        {
            string[] names = new string[Api.StepCount()];
            for (int i = 0; i < names.Length; ++i)
            {
                names[i] = Api.StepName((uint)i);
            }
            return names;
        }
    }

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

    /// <summary>Removes every C# step, before the renderer stops.</summary>
    internal static void RemoveAll()
    {
        foreach (string name in _ids.Keys.ToArray())
        {
            Api.RemoveStep(name);
            Forget(name);
        }
        _failure = null;
    }

    /// <summary>
    /// Removes the steps whose types a game's load context holds, so the
    /// schedule doesn't keep an unloading game alive (ADR 0011).
    /// </summary>
    internal static void Forget(AssemblyLoadContext context)
    {
        foreach ((ulong id, Entry entry) in _steps.ToArray())
        {
            if (AssemblyLoadContext.GetLoadContext(entry.Step.GetType().Assembly) == context)
            {
                Api.RemoveStep(entry.Name);
                _ids.Remove(entry.Name);
                _steps.Remove(id);
            }
        }
        _failure = null;
    }

    private static void Insert(string name, IRenderStep step, StepPlacement placement, string anchor)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (!_trampolineSet)
        {
            Api.SetStepTrampoline((ulong)(delegate* unmanaged<ulong, uint, nint, int>)&Call);
            _trampolineSet = true;
        }
        // Known before the native insert, which prepares the step.
        ulong id = _nextId++;
        _steps[id] = new Entry(name, step);
        try
        {
            Api.InsertManagedStep(name, id, placement, anchor);
        }
        catch (CrucibleException)
        {
            _steps.Remove(id);
            ThrowFailure(); // Prepare's exception, if it threw
            throw;
        }
        _ids[name] = id;
    }

    private static void Forget(string name)
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
