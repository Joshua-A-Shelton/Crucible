using Crucible.Loop;

namespace Crucible.Tests;

// The loop driver (ADR 0013), with test phases. The platform and renderer
// aren't started in the test host, so only headless phases touch native code.
public class FrameLoopTests
{
    private sealed class RecordingPhase(string name, List<string> log) : FramePhase
    {
        public override string Name => name;

        public override void Run(FrameContext context) => log.Add($"{context.FrameNumber}:{name}");
    }

    private sealed class ActionPhase(Action<FrameContext> action) : FramePhase
    {
        public override string Name => "Action";

        public override void Run(FrameContext context) => action(context);
    }

    [Test]
    public async Task PhasesRunInOrderEveryFrameUntilTheLimit()
    {
        List<string> log = [];
        FrameLoop loop = new([new RecordingPhase("A", log), new RecordingPhase("B", log)]);

        ulong frames = loop.Run(new FrameContext(), frameLimit: 2);

        await Assert.That(frames).IsEqualTo(2ul);
        await Assert.That(string.Join(" ", log)).IsEqualTo("0:A 0:B 1:A 1:B");
    }

    [Test]
    public async Task QuitEndsTheLoopAfterTheCurrentFrame()
    {
        List<string> log = [];
        FrameLoop loop = new([
            new ActionPhase(context => context.QuitRequested = context.FrameNumber == 1),
            new RecordingPhase("After", log),
        ]);

        ulong frames = loop.Run(new FrameContext());

        // The phases after the request still finish frame 1.
        await Assert.That(frames).IsEqualTo(2ul);
        await Assert.That(string.Join(" ", log)).IsEqualTo("0:After 1:After");
    }

    [Test]
    public async Task FailureStopsTheFrameAtTheFailingPhase()
    {
        List<string> log = [];
        FrameContext context = new();
        FrameLoop loop = new([
            new ActionPhase(c => c.Fail(Status.DeviceLost, "Render")),
            new RecordingPhase("Skipped", log),
        ]);

        loop.Run(context);

        await Assert.That(context.Failure).IsEqualTo(Status.DeviceLost);
        await Assert.That(context.FailedPhase).IsEqualTo("Render");
        await Assert.That(log.Count).IsEqualTo(0);
    }

    [Test]
    public async Task OnlyTheFirstFailureIsKept()
    {
        FrameContext context = new();
        context.Fail(Status.OutOfDeviceMemory, "First");
        context.Fail(Status.DeviceLost, "Second");

        await Assert.That(context.Failure).IsEqualTo(Status.OutOfDeviceMemory);
        await Assert.That(context.FailedPhase).IsEqualTo("First");
    }

    [Test]
    public async Task DefaultPhasesFollowAdr0013()
    {
        string names = string.Join(" ", FrameLoop.CreateDefault().Phases.Select(phase => phase.Name));

        await Assert.That(names).IsEqualTo("BeginFrame Input RenderSubmit");
    }

    [Test]
    public async Task HeadlessFramesAllocateNothing()
    {
        // The default phases, headless: BeginFrame and RenderSubmit skip, and
        // Input polls (SDL isn't started here, so it reads nothing). A warm-up
        // run first, so one-time costs (JIT, statics) aren't counted.
        FrameLoop loop = FrameLoop.CreateDefault();
        loop.Run(new FrameContext(), frameLimit: 10);

        FrameContext context = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        loop.Run(context, frameLimit: 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }
}
