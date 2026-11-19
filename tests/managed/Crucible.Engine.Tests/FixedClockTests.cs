using System.Diagnostics;
using Crucible.Loop;

namespace Crucible.Tests;

// The fixed clock (ADR 0105): a loop on it moves the platform clock on one
// frame period a frame and never waits for real time. Alone, since the
// platform clock is the whole process's.
[NotInParallel]
public class FixedClockTests
{
    private sealed class Recording(List<ulong> starts, List<double> deltas) : GameSystem
    {
        public override string Name => "record";

        public override void Run(FrameContext context)
        {
            starts.Add(context.FrameStartNs);
            deltas.Add(context.DeltaSeconds);
        }
    }

    [Test]
    public async Task ALoopOnAFixedClockStepsExactlyOneFrameAFrameWithoutWaiting()
    {
        List<ulong> starts = [];
        List<double> deltas = [];
        ScheduleBuilder builder = new() { IncludeNativeSystems = false };
        builder.AddPhase("Update");
        Schedule schedule = builder.AddSystem("Update", new Recording(starts, deltas)).Build();
        // Twenty frames a second (a frame's delta is clamped at four fixed
        // steps, about 0.067 s): 41 frames would take two seconds in real time.
        FrameLoop loop = new(schedule) { FrameRate = 20, FixedClock = true };
        Platform.Api.UseFixedClock(50_000_000);
        long started = Stopwatch.GetTimestamp();
        try
        {
            loop.Run(new FrameContext(), frameLimit: 41);
        }
        finally
        {
            Platform.Api.UseFixedClock(0);
        }
        await Assert.That(Stopwatch.GetElapsedTime(started).TotalSeconds).IsLessThan(1.0);
        await Assert.That(starts.Count).IsEqualTo(41);
        for (int i = 1; i < starts.Count; ++i)
        {
            await Assert.That(starts[i] - starts[i - 1]).IsEqualTo(50_000_000ul);
            await Assert.That(deltas[i]).IsEqualTo(0.05);
        }
    }
}
