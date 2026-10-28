namespace Crucible.Loop;

/// <summary>
/// A system (ADR 0036): one step of the frame, run in a phase in the
/// schedule's order. Everything in a frame is a system, the engine's pacing
/// wait, input, and present included. Names are placeholders (docs/naming.md).
/// </summary>
/// <remarks>
/// An exception from <see cref="Run"/> is reported with the system's name and
/// the frame goes on; the system runs again next frame. Engine systems never
/// throw: they report failure through <see cref="FrameContext.Fail"/>, which
/// ends the loop (ADR 0030).
/// </remarks>
public abstract class GameSystem
{
    /// <summary>
    /// Unique in a schedule, and how constraints name the system. Defaults to
    /// the full type name.
    /// </summary>
    public virtual string Name => GetType().FullName ?? GetType().Name;

    /// <summary>A disabled system is skipped, for the editor and for debugging.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long the last <see cref="Run"/> took, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long LastRunTicks { get; internal set; }

    /// <summary>Exceptions <see cref="Run"/> has thrown since the loop started.</summary>
    public int ExceptionCount { get; internal set; }

    /// <summary>Called once before the first frame, in schedule order: create queries here.</summary>
    public virtual void Start(FrameContext context)
    {
    }

    /// <summary>Called every frame (every step, in the fixed tick) unless disabled.</summary>
    public abstract void Run(FrameContext context);

    /// <summary>Called once after the last frame, in reverse schedule order, even after a failure.</summary>
    public virtual void Stop(FrameContext context)
    {
    }
}
