using System.Text;

namespace Crucible.Loop;

/// <summary>
/// Builds a <see cref="Schedule"/> (ADR 0013, 0036): the phase list, edited
/// at startup, and the systems in each phase with their before/after
/// constraints. C++ systems join from the native registry when it builds.
/// Names are placeholders (docs/naming.md).
/// </summary>
/// <remarks>
/// Editing a phase list that doesn't make sense (an unknown or duplicate
/// phase) throws at once. Problems only visible with every system known
/// (unknown phases or systems, constraints across phases, cycles) are all
/// reported together by <see cref="Build"/>, at startup, never per frame.
/// </remarks>
public sealed class ScheduleBuilder
{
    /// <summary>The fixed-tick group's place in the phase list (<see cref="Phases.FixedTick"/>).</summary>
    public const string FixedTick = Phases.FixedTick;

    private readonly List<string> _phases = [];
    private readonly List<string> _fixedPhases = [];
    private readonly List<Entry> _systems = [];

    internal sealed record Entry(string Phase, GameSystem System, string[] Before, string[] After);

    /// <summary>Fixed ticks per second; 60 by default (ADR 0036).</summary>
    public double FixedTickRate { get; set; } = 60;

    /// <summary>The most fixed steps one frame runs; 4 by default (ADR 0036).</summary>
    public int MaxFixedSteps { get; set; } = 4;

    /// <summary>Whether <see cref="Build"/> adds the C++ systems registered in the native registry.</summary>
    public bool IncludeNativeSystems { get; set; } = true;

    /// <summary>
    /// ADR 0036's default frame: BeginFrame, Input, FixedTick (PrePhysics,
    /// Physics, PostPhysics), Update, Animation, LateUpdate, RenderPrepare,
    /// RenderSubmit, EndFrame, with the engine's pacing, input, and present
    /// systems.
    /// </summary>
    public static ScheduleBuilder CreateDefault()
    {
        ScheduleBuilder builder = new();
        foreach (string phase in (string[])[Phases.BeginFrame, Phases.Input, FixedTick, Phases.Update,
                     Phases.Animation, Phases.LateUpdate, Phases.RenderPrepare, Phases.RenderSubmit, Phases.EndFrame])
        {
            builder.AddPhase(phase);
        }
        foreach (string phase in (string[])[Phases.PrePhysics, Phases.Physics, Phases.PostPhysics])
        {
            builder.AddFixedPhase(phase);
        }
        builder.AddSystem(Phases.BeginFrame, new PacingSystem());
        builder.AddSystem(Phases.Input, new InputSystem());
        builder.AddSystem(Phases.RenderSubmit, new RenderSystem());
        return builder;
    }

    /// <summary>The phase names in order, the fixed-tick group's sub-phases after its name.</summary>
    public IEnumerable<string> PhaseNames =>
        _phases.SelectMany(phase => phase == FixedTick ? [phase, .. _fixedPhases] : (IEnumerable<string>)[phase]);

    /// <summary>Appends a phase to the frame; <see cref="FixedTick"/> places the fixed-tick group.</summary>
    public ScheduleBuilder AddPhase(string name)
    {
        CheckNew(name);
        _phases.Add(name);
        return this;
    }

    /// <summary>Appends a sub-phase to the fixed-tick group.</summary>
    public ScheduleBuilder AddFixedPhase(string name)
    {
        CheckNew(name);
        _fixedPhases.Add(name);
        return this;
    }

    /// <summary>Inserts a phase before another, in the frame or in the fixed-tick group.</summary>
    public ScheduleBuilder InsertPhaseBefore(string existing, string name) => Insert(existing, name, 0);

    /// <summary>Inserts a phase after another, in the frame or in the fixed-tick group.</summary>
    public ScheduleBuilder InsertPhaseAfter(string existing, string name) => Insert(existing, name, 1);

    /// <summary>Removes a phase (its systems must go too, or Build reports them).</summary>
    public ScheduleBuilder RemovePhase(string name)
    {
        if (!_phases.Remove(name) && !_fixedPhases.Remove(name))
        {
            throw new ArgumentException($"There is no phase {name}.", nameof(name));
        }
        return this;
    }

    /// <summary>
    /// Adds a system to a phase, ordered before and after other systems of
    /// the same phase, by name.
    /// </summary>
    public ScheduleBuilder AddSystem(string phase, GameSystem system, string[]? before = null, string[]? after = null)
    {
        _systems.Add(new Entry(phase, system, before ?? [], after ?? []));
        return this;
    }

    /// <summary>Replaces a system, keeping its phase and constraints: how a game swaps an engine system.</summary>
    public ScheduleBuilder ReplaceSystem(string name, GameSystem replacement)
    {
        int index = IndexOfSystem(name);
        _systems[index] = _systems[index] with { System = replacement };
        return this;
    }

    public ScheduleBuilder RemoveSystem(string name)
    {
        _systems.RemoveAt(IndexOfSystem(name));
        return this;
    }

    /// <summary>
    /// Sorts each phase's systems and returns the schedule. Throws, listing
    /// every problem, if the systems don't fit together.
    /// </summary>
    public Schedule Build()
    {
        List<string> errors = [];
        if (!(FixedTickRate > 0) || MaxFixedSteps < 1)
        {
            errors.Add($"the fixed tick needs a positive rate and at least one step (rate {FixedTickRate}, " +
                       $"steps {MaxFixedSteps})");
        }
        if (_fixedPhases.Count > 0 && !_phases.Contains(FixedTick))
        {
            errors.Add($"fixed-tick sub-phases ({string.Join(", ", _fixedPhases)}) without a {FixedTick} phase");
        }

        List<Entry> systems = [.. _systems];
        if (IncludeNativeSystems)
        {
            systems.AddRange(NativeSystem.Registered());
        }

        // Which phase each system is in; names must be unique.
        Dictionary<string, Entry> byName = new(StringComparer.Ordinal);
        foreach (Entry entry in systems)
        {
            string name = entry.System.Name;
            if (!byName.TryAdd(name, entry))
            {
                errors.Add($"two systems are named {name}");
            }
            if (entry.Phase == FixedTick)
            {
                errors.Add($"{name}: systems go in the fixed-tick group's sub-phases, not in {FixedTick} itself");
            }
            else if (!_phases.Contains(entry.Phase) && !_fixedPhases.Contains(entry.Phase))
            {
                errors.Add($"{name}: there is no phase {entry.Phase}");
            }
        }

        Dictionary<string, GameSystem[]> sorted = new(StringComparer.Ordinal);
        foreach (string phase in PhaseNames.Where(phase => phase != FixedTick))
        {
            sorted[phase] = Sort(phase, [.. byName.Values.Where(entry => entry.Phase == phase)], byName, errors);
        }
        if (errors.Count > 0)
        {
            StringBuilder message = new("The schedule can't be built:");
            foreach (string error in errors)
            {
                message.Append("\n  ").Append(error);
            }
            throw new InvalidOperationException(message.ToString());
        }

        FramePhase[] fixedPhases = [.. _fixedPhases.Select(phase => new FramePhase(phase, sorted[phase], [], false))];
        FramePhase[] phases =
        [
            .. _phases.Select(phase => phase == FixedTick
                ? new FramePhase(phase, [], fixedPhases, true)
                : new FramePhase(phase, sorted[phase], [], false)),
        ];
        return new Schedule(phases, (ulong)Math.Round(1e9 / FixedTickRate), MaxFixedSteps);
    }

    // A topological sort of one phase's before/after edges. Of the systems
    // free to go next, the ordinally smallest name goes first, so the order
    // never depends on the order systems were added in (ADR 0013).
    private static GameSystem[] Sort(string phase, List<Entry> entries, Dictionary<string, Entry> byName,
        List<string> errors)
    {
        Dictionary<string, List<string>> next = new(StringComparer.Ordinal);
        Dictionary<string, int> waitingOn = new(StringComparer.Ordinal);
        foreach (Entry entry in entries)
        {
            next[entry.System.Name] = [];
            waitingOn[entry.System.Name] = 0;
        }
        foreach (Entry entry in entries)
        {
            string name = entry.System.Name;
            foreach ((string other, bool isBefore) in entry.Before.Select(other => (other, true))
                         .Concat(entry.After.Select(other => (other, false))))
            {
                if (!byName.TryGetValue(other, out Entry? target))
                {
                    errors.Add($"{name}: {(isBefore ? "before" : "after")} {other}, which isn't a system");
                    continue;
                }
                if (target.Phase != phase)
                {
                    errors.Add($"{name} (in {phase}): {(isBefore ? "before" : "after")} {other}, which is in " +
                               $"{target.Phase}; constraints only order systems within a phase");
                    continue;
                }
                (string first, string second) = isBefore ? (name, other) : (other, name);
                next[first].Add(second);
                waitingOn[second]++;
            }
        }

        PriorityQueue<string, string> ready = new(StringComparer.Ordinal);
        foreach ((string name, int count) in waitingOn.Where(pair => pair.Value == 0))
        {
            ready.Enqueue(name, name);
        }
        List<GameSystem> order = [];
        while (ready.TryDequeue(out string? name, out _))
        {
            order.Add(byName[name].System);
            foreach (string after in next[name])
            {
                if (--waitingOn[after] == 0)
                {
                    ready.Enqueue(after, after);
                }
            }
        }
        if (order.Count < entries.Count)
        {
            IEnumerable<string> stuck = waitingOn.Where(pair => pair.Value > 0).Select(pair => pair.Key)
                .Order(StringComparer.Ordinal);
            errors.Add($"{phase}: the constraints between {string.Join(", ", stuck)} form a cycle");
        }
        return [.. order];
    }

    private ScheduleBuilder Insert(string existing, string name, int offset)
    {
        CheckNew(name);
        List<string> list = _phases.Contains(existing) ? _phases
            : _fixedPhases.Contains(existing) ? _fixedPhases
            : throw new ArgumentException($"There is no phase {existing}.", nameof(existing));
        list.Insert(list.IndexOf(existing) + offset, name);
        return this;
    }

    private void CheckNew(string name)
    {
        if (_phases.Contains(name) || _fixedPhases.Contains(name))
        {
            throw new ArgumentException($"There is already a phase {name}.", nameof(name));
        }
    }

    private int IndexOfSystem(string name)
    {
        int index = _systems.FindIndex(entry => entry.System.Name == name);
        return index >= 0 ? index : throw new ArgumentException($"There is no system {name}.", nameof(name));
    }
}
