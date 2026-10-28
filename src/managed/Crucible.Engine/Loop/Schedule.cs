namespace Crucible.Loop;

/// <summary>
/// One phase of a built schedule (ADR 0013, 0036): its systems in run order,
/// then a sync point. The fixed-tick group is a phase holding sub-phases
/// instead of systems. Names are placeholders (docs/naming.md).
/// </summary>
public sealed class FramePhase
{
    internal FramePhase(string name, GameSystem[] systems, FramePhase[] subPhases, bool isFixedGroup)
    {
        Name = name;
        IsFixedGroup = isFixedGroup;
        SystemArray = systems;
        SubPhaseArray = subPhases;
    }

    public string Name { get; }

    /// <summary>The systems, in the order they run.</summary>
    public IReadOnlyList<GameSystem> Systems => SystemArray;

    /// <summary>The fixed-tick group's sub-phases, run 0..N times per frame; empty for other phases.</summary>
    public IReadOnlyList<FramePhase> SubPhases => SubPhaseArray;

    public bool IsFixedGroup { get; }

    /// <summary>How long the phase's last sync point took, in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long LastSyncTicks { get; internal set; }

    // Arrays, so the loop indexes them without allocating enumerators.
    internal GameSystem[] SystemArray { get; }

    internal FramePhase[] SubPhaseArray { get; }
}

/// <summary>
/// The frame, built once by <see cref="ScheduleBuilder"/> and then fixed
/// (ADR 0036): the phases in order, each with its systems sorted. Running it
/// sorts and looks up nothing.
/// </summary>
public sealed class Schedule
{
    internal Schedule(FramePhase[] phases, ulong fixedStepNs, int maxFixedSteps)
    {
        PhaseArray = phases;
        AllSystemArray =
        [
            .. phases.SelectMany(phase => phase.SubPhaseArray.SelectMany(sub => sub.SystemArray).Concat(phase.SystemArray)),
        ];
        FixedStepNs = fixedStepNs;
        MaxFixedSteps = maxFixedSteps;
    }

    /// <summary>The top-level phases in order; the fixed-tick group is one of them.</summary>
    public IReadOnlyList<FramePhase> Phases => PhaseArray;

    /// <summary>The fixed tick's step, in nanoseconds.</summary>
    public ulong FixedStepNs { get; }

    /// <summary>The most fixed steps a frame runs; owed time beyond them is dropped.</summary>
    public int MaxFixedSteps { get; }

    /// <summary>Every system, in the order a frame with one fixed step runs them.</summary>
    public IReadOnlyList<GameSystem> Systems => AllSystemArray;

    internal FramePhase[] PhaseArray { get; }

    // Every system once, for starting and stopping without allocating.
    internal GameSystem[] AllSystemArray { get; }
}
