using Crucible.Ecs;
using Crucible.Loop;
using LoopApi = Crucible.Loop.Api;

namespace Crucible.Tests;

// The loop driver and the schedule (ADR 0013, 0036), with test systems. The
// platform and renderer aren't started in the test host, so only headless
// systems touch native code. Not in parallel with other schedule tests: the
// C++ system registry is global.
[NotInParallel("schedule")]
public class FrameLoopTests
{
    private sealed class RecordingSystem(string name, List<string> log) : GameSystem
    {
        public override string Name => name;

        public override void Start(FrameContext context) => log.Add($"start:{name}");

        public override void Run(FrameContext context) => log.Add($"{context.FrameNumber}:{name}");

        public override void Stop(FrameContext context) => log.Add($"stop:{name}");
    }

    private sealed class ActionSystem(string name, Action<FrameContext> action) : GameSystem
    {
        public override string Name => name;

        public override void Run(FrameContext context) => action(context);
    }

    // A builder with no engine or C++ systems: phases A and B, or the ones given.
    private static ScheduleBuilder Builder(params string[] phases)
    {
        ScheduleBuilder builder = new() { IncludeNativeSystems = false };
        foreach (string phase in phases.Length > 0 ? phases : ["A", "B"])
        {
            builder.AddPhase(phase);
        }
        return builder;
    }

    private static string Runs(List<string> log) => string.Join(" ", log.Where(line => !line.Contains(':') ||
        char.IsDigit(line[0])));

    [Test]
    public async Task SystemsRunInPhaseOrderEveryFrameUntilTheLimit()
    {
        List<string> log = [];
        Schedule schedule = Builder()
            .AddSystem("B", new RecordingSystem("b", log))
            .AddSystem("A", new RecordingSystem("a", log))
            .Build();

        ulong frames = new FrameLoop(schedule).Run(new FrameContext(), frameLimit: 2);

        await Assert.That(frames).IsEqualTo(2ul);
        await Assert.That(Runs(log)).IsEqualTo("0:a 0:b 1:a 1:b");
    }

    [Test]
    public async Task StartAndStopRunInScheduleOrderAndReverse()
    {
        List<string> log = [];
        Schedule schedule = Builder()
            .AddSystem("A", new RecordingSystem("a", log))
            .AddSystem("B", new RecordingSystem("b", log))
            .Build();

        new FrameLoop(schedule).Run(new FrameContext(), frameLimit: 1);

        await Assert.That(string.Join(" ", log)).IsEqualTo("start:a start:b 0:a 0:b stop:b stop:a");
    }

    [Test]
    public async Task QuitEndsTheLoopAfterTheCurrentFrame()
    {
        List<string> log = [];
        Schedule schedule = Builder()
            .AddSystem("A", new ActionSystem("quit", context => context.QuitRequested = context.FrameNumber == 1))
            .AddSystem("B", new RecordingSystem("after", log))
            .Build();

        ulong frames = new FrameLoop(schedule).Run(new FrameContext());

        // The systems after the request still finish frame 1.
        await Assert.That(frames).IsEqualTo(2ul);
        await Assert.That(Runs(log)).IsEqualTo("0:after 1:after");
    }

    [Test]
    public async Task FailureEndsTheFrameAtTheFailingSystemAndStillStops()
    {
        List<string> log = [];
        FrameContext context = new();
        Schedule schedule = Builder()
            .AddSystem("A", new ActionSystem("render", c => c.Fail(Status.DeviceLost, "render")))
            .AddSystem("B", new RecordingSystem("skipped", log))
            .Build();

        new FrameLoop(schedule).Run(context);

        await Assert.That(context.Failure).IsEqualTo(Status.DeviceLost);
        await Assert.That(context.FailedSystem).IsEqualTo("render");
        await Assert.That(string.Join(" ", log)).IsEqualTo("start:skipped stop:skipped");
    }

    [Test]
    public async Task OnlyTheFirstFailureIsKept()
    {
        FrameContext context = new();
        context.Fail(Status.OutOfDeviceMemory, "First");
        context.Fail(Status.DeviceLost, "Second");

        await Assert.That(context.Failure).IsEqualTo(Status.OutOfDeviceMemory);
        await Assert.That(context.FailedSystem).IsEqualTo("First");
    }

    [Test]
    public async Task ConstraintsOrderSystemsAndTiesBreakByName()
    {
        // a after c, d before b; otherwise by name: c d a b? No: of the ready
        // ones the smallest name goes first, each time.
        string Order(params string[] added)
        {
            List<string> log = [];
            ScheduleBuilder builder = Builder("A");
            foreach (string name in added)
            {
                builder.AddSystem("A", new RecordingSystem(name, log),
                    before: name == "d" ? ["b"] : null,
                    after: name == "a" ? ["c"] : null);
            }
            return string.Join(" ", builder.Build().Phases[0].Systems.Select(system => system.Name));
        }

        // Ready: c, d (a waits on c, b on d) -> c; then a, d -> a; then d; then b.
        await Assert.That(Order("a", "b", "c", "d")).IsEqualTo("c a d b");
        await Assert.That(Order("d", "c", "b", "a")).IsEqualTo("c a d b");
    }

    [Test]
    public async Task BuildReportsEveryProblemAtOnce()
    {
        List<string> log = [];
        ScheduleBuilder builder = Builder("A", "B", ScheduleBuilder.FixedTick)
            .AddSystem("Nowhere", new RecordingSystem("lost", log))
            .AddSystem("A", new RecordingSystem("typo", log), after: ["nobody"])
            .AddSystem("A", new RecordingSystem("across", log), after: ["inB"])
            .AddSystem("B", new RecordingSystem("inB", log))
            .AddSystem("A", new RecordingSystem("x", log), before: ["y"])
            .AddSystem("A", new RecordingSystem("y", log), before: ["x"])
            .AddSystem("B", new RecordingSystem("inB", log))
            .AddSystem(ScheduleBuilder.FixedTick, new RecordingSystem("group", log));

        string message = Assert.Throws<InvalidOperationException>(() => builder.Build())!.Message;

        await Assert.That(message).Contains("lost: there is no phase Nowhere");
        await Assert.That(message).Contains("typo: after nobody, which isn't a system");
        await Assert.That(message).Contains("constraints only order systems within a phase");
        await Assert.That(message).Contains("A: the constraints between x, y form a cycle");
        await Assert.That(message).Contains("two systems are named inB");
        await Assert.That(message).Contains("group: systems go in the fixed-tick group's sub-phases");
    }

    [Test]
    public async Task PhasesCanBeInsertedAndRemovedInTheFrameAndTheFixedTick()
    {
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeNativeSystems = false;
        builder.InsertPhaseAfter("Update", "AI")
            .InsertPhaseBefore("Physics", "Wind")
            .RemovePhase("Animation");

        await Assert.That(string.Join(" ", builder.PhaseNames)).IsEqualTo(
            "BeginFrame Input FixedTick PrePhysics Wind Physics PostPhysics Update AI LateUpdate RenderPrepare " +
            "RenderSubmit EndFrame");
        await Assert.That(Assert.Throws<ArgumentException>(() => builder.AddPhase("Update"))).IsNotNull();
        await Assert.That(Assert.Throws<ArgumentException>(() => builder.InsertPhaseAfter("Gone", "X"))).IsNotNull();
    }

    [Test]
    public async Task DefaultScheduleFollowsAdr0036()
    {
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeNativeSystems = false;
        Schedule schedule = builder.Build();

        string phases = string.Join(" ", schedule.Phases.Select(phase =>
            phase.IsFixedGroup ? $"{phase.Name}({string.Join(" ", phase.SubPhases.Select(sub => sub.Name))})" : phase.Name));
        await Assert.That(phases).IsEqualTo("BeginFrame Input FixedTick(PrePhysics Physics PostPhysics) Update " +
                                            "Animation LateUpdate RenderPrepare RenderSubmit EndFrame");
        await Assert.That(string.Join(" ", schedule.Systems.Select(system => system.Name))).IsEqualTo(
            "Crucible.Loop.PacingSystem Crucible.Loop.InputSystem crucible.TransformPropagation Crucible.Loop.RenderSystem");
        await Assert.That(schedule.FixedStepNs).IsEqualTo(16_666_667ul);
        await Assert.That(schedule.MaxFixedSteps).IsEqualTo(4);
    }

    [Test]
    public async Task ReplacingAnEngineSystemKeepsItsPhase()
    {
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        builder.IncludeNativeSystems = false;
        builder.ReplaceSystem("Crucible.Loop.RenderSystem", new ActionSystem("MyPresent", _ => { }));

        Schedule schedule = builder.Build();

        await Assert.That(schedule.Phases.Single(phase => phase.Name == "RenderSubmit").Systems.Single().Name)
            .IsEqualTo("MyPresent");
    }

    [Test]
    public async Task TheFixedTickStepsFromTheClockUpToItsCap()
    {
        // 100 Hz: a 10 ms step, at most 4 per frame.
        ulong now = 1_000_000_000;
        ulong[] advances = [0, 25_000_000, 5_000_000, 1_000_000_000, 0];
        int frame = 0;
        int physicsRuns = 0;
        List<string> seen = [];
        Schedule schedule = Builder("Input", ScheduleBuilder.FixedTick, "Update")
            .AddFixedPhase("Physics")
            .AddSystem("Physics", new ActionSystem("physics", _ => physicsRuns++))
            .AddSystem("Update", new ActionSystem("update", c =>
                seen.Add($"{c.FixedSteps}/{c.FixedAlpha:0.##}/{c.DeltaSeconds:0.###}")))
            .Let(builder => builder.FixedTickRate = 100)
            .Build();
        FrameLoop loop = new(schedule) { Clock = () => now += advances[frame++] };

        loop.Run(new FrameContext(), frameLimit: 5);

        // Frame 0 has no delta. 25 ms: two steps, half a step left. 5 ms more:
        // one step, nothing left. A 1 s hitch: clamped to 40 ms, four steps.
        // Then no time: no step.
        await Assert.That(string.Join(" ", seen)).IsEqualTo("0/0/0 2/0.5/0.025 1/0/0.005 4/0/0.04 0/0/0");
        await Assert.That(physicsRuns).IsEqualTo(7);
    }

    [Test]
    public async Task EveryPhaseAndFixedStepEndsInASyncPoint()
    {
        using World world = new();
        ulong now = 0;
        List<string> seen = [];
        Schedule schedule = Builder(ScheduleBuilder.FixedTick, "Update", "Late")
            .AddFixedPhase("Physics")
            .AddSystem("Physics", new ActionSystem("spawn", c =>
            {
                seen.Add($"step sees {c.World.EntityCount}");
                c.Commands.RecordCreate();
            }))
            .AddSystem("Update", new ActionSystem("spawnAgain", c => c.Commands.RecordCreate()))
            .AddSystem("Late", new ActionSystem("count", c => seen.Add($"late sees {c.World.EntityCount}")))
            .Let(builder => builder.FixedTickRate = 100)
            .Build();
        FrameLoop loop = new(schedule) { Clock = () => now += 20_000_000 };

        loop.Run(new FrameContext { World = world }, frameLimit: 2);

        // Frame 1 runs two steps: each sees the one before it applied.
        await Assert.That(string.Join(", ", seen)).IsEqualTo(
            "late sees 1, step sees 1, step sees 2, late sees 4");
    }

    [Test]
    public async Task AThrowingSystemIsReportedAndKeepsRunning()
    {
        int after = 0;
        ActionSystem thrower = new("thrower", _ => throw new InvalidOperationException("broken"));
        Schedule schedule = Builder("A")
            .AddSystem("A", thrower)
            .AddSystem("A", new ActionSystem("z", _ => after++))
            .Build();
        FrameContext context = new();

        new FrameLoop(schedule).Run(context, frameLimit: 3);

        await Assert.That(thrower.ExceptionCount).IsEqualTo(3); // ran, and threw, every frame
        await Assert.That(after).IsEqualTo(3);
        await Assert.That(context.Failure).IsEqualTo(Status.Ok);
    }

    [Test]
    public async Task DisabledSystemsAreSkipped()
    {
        List<string> log = [];
        RecordingSystem off = new("off", log) { Enabled = false };
        Schedule schedule = Builder("A").AddSystem("A", off).AddSystem("A", new RecordingSystem("on", log)).Build();

        new FrameLoop(schedule).Run(new FrameContext(), frameLimit: 1);

        await Assert.That(Runs(log)).IsEqualTo("0:on");
    }

    [Test]
    public async Task EachSystemSeesTheVersionOfItsLastRun()
    {
        using World world = new();
        Entity entity = world.CreateEntity(Components.Of<Crucible.Scenes.LocalTransform>());
        List<(uint Last, bool Changed)> seen = [];
        Schedule schedule = Builder("A")
            .AddSystem("A", new ActionSystem("reader", c =>
                seen.Add((c.LastRunVersion, world.ChangedSince(entity, Components.Of<Crucible.Scenes.LocalTransform>(),
                    c.LastRunVersion)))))
            .AddSystem("A", new ActionSystem("writer", c =>
            {
                if (c.FrameNumber == 1)
                {
                    world.Get<Crucible.Scenes.LocalTransform>(entity).Position.X = 1;
                }
            }))
            .Build();

        new FrameLoop(schedule).Run(new FrameContext { World = world }, frameLimit: 4);

        // First run: 0 (everything is new). The write in frame 1, after the
        // reader ran, is seen in frame 2 and not again.
        await Assert.That(seen[0].Last).IsEqualTo(0u);
        await Assert.That(string.Join(" ", seen.Select(s => s.Changed))).IsEqualTo("True False True False");
        await Assert.That(seen[1].Last).IsGreaterThan(0u);
        await Assert.That(seen[3].Last).IsGreaterThan(seen[2].Last);
    }

    [Test]
    public async Task CppSystemsSortAndRunWithCSharpSystems()
    {
        using World world = new();
        LoopApi.RegisterProbeSystems("Update");
        try
        {
            List<string> seen = [];
            Schedule schedule = Builder("Update", "Late")
                .Let(builder => builder.IncludeNativeSystems = true)
                .AddSystem("Update", new ActionSystem("csharp.Before", _ => seen.Add($"before {LoopApi.ProbeSystemCalls()}")),
                    before: ["crucible.probe.Count"])
                .AddSystem("Update", new ActionSystem("csharp.After", _ => seen.Add($"after {LoopApi.ProbeSystemCalls()}")),
                    after: ["crucible.probe.Spawn"])
                .AddSystem("Late", new ActionSystem("csharp.Late", c => seen.Add($"late {c.World.EntityCount}")))
                .Build();

            await Assert.That(string.Join(" ", schedule.Phases[0].Systems.Select(system => system.Name))).IsEqualTo(
                "csharp.Before crucible.probe.Count crucible.probe.Spawn csharp.After");

            new FrameLoop(schedule).Run(new FrameContext { World = world }, frameLimit: 2);

            // C++ ran between the two C# systems, and its command buffer
            // entity was applied at Update's sync point.
            await Assert.That(string.Join(", ", seen)).IsEqualTo(
                "before 0, after 1, late 1, before 1, after 2, late 2");
            SystemContext last = LoopApi.ProbeLastContext();
            await Assert.That(last.FrameNumber).IsEqualTo(1ul);
            await Assert.That(last.World.Address).IsEqualTo(world.Handle.Address);
            await Assert.That(last.FixedStepSeconds).IsEqualTo(16_666_667 / 1e9);
        }
        finally
        {
            LoopApi.UnregisterProbeSystems();
        }
    }

    [Test]
    public async Task HeadlessFramesAllocateNothing()
    {
        // The default schedule, headless: pacing and present skip, and input
        // polls (SDL isn't started here, so it reads nothing). A warm-up run
        // first, so one-time costs (JIT, statics) aren't counted.
        FrameLoop loop = FrameLoop.CreateDefault();
        loop.Run(new FrameContext(), frameLimit: 10);

        FrameContext context = new();
        long before = GC.GetAllocatedBytesForCurrentThread();
        loop.Run(context, frameLimit: 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }

    [Test]
    public async Task FramesWithAWorldAndCommandsAllocateNothingOnceRunning()
    {
        // Measured inside the loop: starting it creates the command buffers.
        using World world = new();
        long[] allocated = new long[2];
        ulong now = 0;
        Schedule schedule = Builder(ScheduleBuilder.FixedTick, "Update")
            .AddFixedPhase("Physics")
            .AddSystem("Physics", new ActionSystem("spawn", c => c.Commands.RecordCreate()))
            .AddSystem("Update", new ActionSystem("measure", c =>
            {
                if (c.FrameNumber is 10 or 1010)
                {
                    allocated[c.FrameNumber == 10 ? 0 : 1] = GC.GetAllocatedBytesForCurrentThread();
                }
            }))
            .Build();
        FrameLoop loop = new(schedule) { Clock = () => now += 16_666_667 };

        loop.Run(new FrameContext { World = world }, frameLimit: 1011);

        await Assert.That(allocated[1] - allocated[0]).IsEqualTo(0L);
    }
}

internal static class BuilderExtensions
{
    // Sets a property in a builder chain.
    public static ScheduleBuilder Let(this ScheduleBuilder builder, Action<ScheduleBuilder> change)
    {
        change(builder);
        return builder;
    }
}
