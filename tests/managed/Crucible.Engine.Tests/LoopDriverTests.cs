using System.Runtime.Loader;
using Crucible.Hosting;
using Crucible.Loop;

namespace Crucible.Tests;

// The loop driver contract (ADR 0076), under JIT and NativeAOT: the
// engine's FrameLoop is a driver, and a game naming its own gets it for its
// schedule.
public class LoopDriverTests
{
    // A game's driver: counts its runs, and runs the frames with FrameLoop.
    private sealed class CountingLoop(Schedule schedule) : ILoopDriver
    {
        public static int Runs;

        public Schedule Schedule { get; } = schedule;

        public ulong Run(FrameContext context, uint frameLimit = 0)
        {
            ++Runs;
            return new FrameLoop(Schedule).Run(context, frameLimit);
        }
    }

    [Test]
    public async Task TheEngineLoopIsADriver()
    {
        Schedule schedule = new ScheduleBuilder().AddPhase("Update").Build();
        ILoopDriver driver = new FrameLoop(schedule);
        await Assert.That(driver.Schedule).IsSameReferenceAs(schedule);
    }

    [Test]
    public async Task AGameNamingADriverGetsIt()
    {
        Schedule schedule = new ScheduleBuilder().AddPhase("Update").Build();
        AssemblyLoadContext context = new("LoopDriverTests", isCollectible: true);
        LoadedGame withDriver = new(context, null, new Registrations(null, null, null), typeof(CountingLoop));
        ILoopDriver? driver = withDriver.CreateLoopDriver(schedule);
        await Assert.That(driver).IsTypeOf<CountingLoop>();
        await Assert.That(driver!.Schedule).IsSameReferenceAs(schedule);

        using Crucible.Ecs.World world = new();
        int before = CountingLoop.Runs;
        ulong frames = driver.Run(new FrameContext { World = world, Services = new Services(), Pacing = false }, 3);
        await Assert.That(frames).IsEqualTo(3ul);
        await Assert.That(CountingLoop.Runs - before).IsEqualTo(1);

        LoadedGame without = new(context, null, new Registrations(null, null, null));
        await Assert.That(without.CreateLoopDriver(schedule)).IsNull();
        context.Unload();
    }
}
