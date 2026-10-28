using System.Diagnostics;

namespace Crucible.Loop;

/// <summary>
/// The loop driver (ADR 0011, 0013, 0036): runs a <see cref="Schedule"/> once
/// per frame until a system requests quit or fails, or the frame limit is
/// reached. The driver is replaceable (ADR 0013), so it holds no state a
/// system needs.
/// </summary>
public sealed class FrameLoop(Schedule schedule)
{
    /// <summary>The schedule it runs.</summary>
    public Schedule Schedule { get; } = schedule;

    /// <summary>
    /// The frame clock in nanoseconds: the input event clock by default
    /// (Platform.Api.TimestampNow). Tests step it by hand.
    /// </summary>
    public Func<ulong> Clock { get; init; } = static () => Platform.Api.TimestampNow();

    /// <summary>ADR 0036's default schedule, with the C++ systems registered so far.</summary>
    public static FrameLoop CreateDefault() => new(ScheduleBuilder.CreateDefault().Build());

    /// <summary>
    /// Starts the systems, runs frames, and stops them; returns how many
    /// frames ran (counting from the context's frame number).
    /// </summary>
    /// <param name="frameLimit">Stop after this many frames; 0 runs until quit or failure.</param>
    public ulong Run(FrameContext context, uint frameLimit = 0)
    {
        FramePhase[] phases = Schedule.PhaseArray;
        ulong stepNs = Schedule.FixedStepNs;
        int maxSteps = Schedule.MaxFixedSteps;
        // Time owed beyond the step cap is dropped, and the frame's delta is
        // clamped to the same limit (ADR 0036).
        ulong maxDeltaNs = stepNs * (ulong)maxSteps;
        context.FixedStepSeconds = stepNs / 1e9;
        context.Schedule = Schedule;

        context.StartRunning();
        try
        {
            foreach (GameSystem system in Schedule.AllSystemArray)
            {
                Invoke(system, context, starting: true);
            }
            ulong accumulatorNs = 0;
            ulong lastNs = 0;
            bool first = true;
            while (!context.QuitRequested && context.Failure == Status.Ok &&
                   (frameLimit == 0 || context.FrameNumber < frameLimit))
            {
                ulong now = Clock();
                ulong deltaNs = first ? 0 : Math.Min(now - lastNs, maxDeltaNs);
                first = false;
                lastNs = now;
                accumulatorNs += deltaNs;
                context.FrameStartNs = now;
                context.DeltaSeconds = deltaNs / 1e9;
                context.FixedSteps = 0;

                foreach (FramePhase phase in phases)
                {
                    if (phase.IsFixedGroup)
                    {
                        int steps = (int)Math.Min(accumulatorNs / stepNs, (ulong)maxSteps);
                        accumulatorNs -= (ulong)steps * stepNs;
                        accumulatorNs %= stepNs; // anything still owed past the cap
                        context.FixedSteps = steps;
                        for (int step = 0; step < steps && context.Failure == Status.Ok; ++step)
                        {
                            foreach (FramePhase subPhase in phase.SubPhaseArray)
                            {
                                if (!RunPhase(subPhase, context))
                                {
                                    break;
                                }
                            }
                        }
                        context.FixedAlpha = (double)accumulatorNs / stepNs;
                    }
                    else
                    {
                        RunPhase(phase, context);
                    }
                    if (context.Failure != Status.Ok)
                    {
                        break;
                    }
                }
                context.FrameNumber++;
            }
        }
        finally
        {
            GameSystem[] systems = Schedule.AllSystemArray;
            for (int i = systems.Length - 1; i >= 0; --i)
            {
                Invoke(systems[i], context, starting: false);
            }
            context.StopRunning();
        }
        return context.FrameNumber;
    }

    // Runs the phase's systems in order, then its sync point; false if a
    // system failed, which ends the frame there (ADR 0030).
    private static bool RunPhase(FramePhase phase, FrameContext context)
    {
        context.RefreshNativeContext();
        foreach (GameSystem system in phase.SystemArray)
        {
            if (!system.Enabled)
            {
                continue;
            }
            long start = Stopwatch.GetTimestamp();
            try
            {
                system.Run(context);
            }
            catch (Exception exception)
            {
                // Reported, and the frame goes on; the system runs again next
                // frame (ADR 0036). A try costs nothing until something throws.
                system.ExceptionCount++;
                Console.Error.WriteLine($"Crucible.Loop: {system.Name} threw: {exception}");
            }
            system.LastRunTicks = Stopwatch.GetTimestamp() - start;
            if (context.Failure != Status.Ok)
            {
                return false;
            }
        }
        long syncStart = Stopwatch.GetTimestamp();
        context.ApplyCommands();
        phase.LastSyncTicks = Stopwatch.GetTimestamp() - syncStart;
        return true;
    }

    // Start and Stop: reported like Run's exceptions, so one broken system
    // doesn't keep the others from starting or stopping.
    private static void Invoke(GameSystem system, FrameContext context, bool starting)
    {
        try
        {
            if (starting)
            {
                system.Start(context);
            }
            else
            {
                system.Stop(context);
            }
        }
        catch (Exception exception)
        {
            system.ExceptionCount++;
            Console.Error.WriteLine(
                $"Crucible.Loop: {system.Name} threw while {(starting ? "starting" : "stopping")}: {exception}");
        }
    }
}
