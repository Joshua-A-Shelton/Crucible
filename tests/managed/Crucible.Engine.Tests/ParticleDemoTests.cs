using System.Numerics;
using System.Reflection;
using Crucible.Demo;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Samples.Particles;

namespace Crucible.Tests;

// The M3 demonstration's sample game (ADR 0037), run through the engine's
// loop on a stepped clock, under JIT and NativeAOT: its C# and C++ systems
// together, in parallel, with creations and destructions at the sync points.
// It touches the job system and the native system registry, both global.
[NotInParallel(["jobs", "schedule"])]
public class ParticleDemoTests
{
    [Before(Class)]
    public static void StartWorkers() => Crucible.Jobs.Api.Initialize(4);

    [After(Class)]
    public static void StopWorkers() => Crucible.Jobs.Api.Shutdown();

    private sealed class CheckSystem(string name, Action<FrameContext> action) : GameSystem
    {
        public override string Name => name;

        public override void Run(FrameContext context) => action(context);
    }

    private sealed class NoInput : GameSystem
    {
        public override void Run(FrameContext context)
        {
        }
    }

    // The default schedule with the sample's systems, registered the way the
    // game loader does it: through the assembly attribute.
    private static ScheduleBuilder SampleBuilder()
    {
        ScheduleBuilder builder = ScheduleBuilder.CreateDefault();
        // No platform layer in these tests.
        builder.ReplaceSystem(typeof(InputSystem).FullName!, new NoInput());
        SystemRegistrationAttribute attribute =
            typeof(ParticleSetup).Assembly.GetCustomAttribute<SystemRegistrationAttribute>()!;
        ((ISystemRegistration)Activator.CreateInstance(attribute.RegistrationType)!).Register(builder);
        return builder;
    }

    [Test]
    public async Task ParticlesMoveAsSteeredAndIntegratedAndKeepTheirNumber()
    {
        const int particles = 3000;
        const int steps = 120; // two seconds: lifetimes are 0-6 s, so many expire and respawn
        List<int> populations = [];
        List<string> problems = [];
        Entity watched = Entity.Null;
        Vector3 position = default, velocity = default;
        float step = 0;

        ScheduleBuilder builder = SampleBuilder()
            // Before Steer: the population right after Respawn's creations
            // were applied, and one particle's state.
            .AddSystem(Phases.Physics, new CheckSystem("check.Before", context =>
            {
                World world = context.World;
                populations.Add(world.EntityCount);
                if (watched.IsNull || !world.IsAlive(watched))
                {
                    using Query query = world.CreateQuery([Components.Of<Position>()]);
                    FirstEntity first = default;
                    query.ForEachChunk(ref first);
                    watched = first.Entity;
                }
                position = world.Read<Position>(watched).Value;
                velocity = world.Read<Velocity>(watched).Value;
                step = (float)context.FixedStepSeconds;
            }), before: ["crucible.demo.Steer"])
            // After Integrate: the same particle, against the formulas.
            .AddSystem(Phases.Physics, new CheckSystem("check.After", context =>
            {
                Vector3 up = Vector3.UnitY;
                Vector3 acceleration = -ParticleSetup.Pull * position + ParticleSetup.Spin * Vector3.Cross(up, position) -
                                       ParticleSetup.Drag * velocity;
                Vector3 expectedVelocity = velocity + acceleration * step;
                Vector3 expectedPosition = position + expectedVelocity * step;
                // Float rounding (and C++'s fused multiply-adds) stay far
                // below these; a wrong term is visible in the first step.
                Vector3 actualVelocity = context.World.Read<Velocity>(watched).Value;
                Vector3 actual = context.World.Read<Position>(watched).Value;
                if (Vector3.Distance(actualVelocity, expectedVelocity) > 1e-5f * (1 + expectedVelocity.Length()) ||
                    Vector3.Distance(actual, expectedPosition) > 1e-5f * (1 + expectedPosition.Length()))
                {
                    problems.Add($"frame {context.FrameNumber}: {actual} moving {actualVelocity}, " +
                                 $"expected {expectedPosition} moving {expectedVelocity}");
                }
            }), after: [typeof(IntegrateSystem).FullName!])
            // After PostPhysics applied the destroys: nothing expired is left.
            .AddSystem(Phases.Update, new CheckSystem("check.Expired", context =>
            {
                using Query query = context.World.CreateQuery([Components.Of<Lifetime>()]);
                CountExpired expired = default;
                query.ForEachChunk(ref expired);
                if (expired.Count > 0)
                {
                    problems.Add($"frame {context.FrameNumber}: {expired.Count} expired particles survived");
                }
            }));

        ulong respawnedBefore = 0;
        using (World world = new())
        {
            FrameContext context = new()
            {
                World = world,
                GameArguments = new Dictionary<string, string>
                {
                    ["particles"] = $"{particles}",
                    ["steps"] = $"{steps}",
                    ["seed"] = "7",
                },
            };
            long now = 0;
            // One fixed step per frame after the first, whose delta is 0.
            FrameLoop loop = new(builder.Build()) { Clock = () => (ulong)(now += 16_666_667) };
            loop.Run(context, steps + 10);
            respawnedBefore = Crucible.Demo.Api.DemoRespawned();
            await Assert.That(context.QuitRequested).IsTrue(); // the report quit after `steps`
        }

        await Assert.That(problems).IsEmpty();
        await Assert.That(populations.Count).IsEqualTo(steps);
        await Assert.That(populations.All(count => count == particles)).IsTrue();
        await Assert.That(respawnedBefore).IsGreaterThan(500ul);
    }

    [Test]
    public async Task FramesAllocateNothingOnceRunning()
    {
        long before = 0;
        long after = 0;
        ScheduleBuilder builder = SampleBuilder()
            .AddSystem(Phases.EndFrame, new CheckSystem("check.Allocations", context =>
            {
                // Measured inside the loop, as the latency test does.
                if (context.FrameNumber == 10)
                {
                    before = GC.GetAllocatedBytesForCurrentThread();
                }
                else if (context.FrameNumber == 109)
                {
                    after = GC.GetAllocatedBytesForCurrentThread();
                }
            }));
        using (World world = new())
        {
            FrameContext context = new()
            {
                World = world,
                GameArguments = new Dictionary<string, string> { ["particles"] = "5000", ["steps"] = "0" },
            };
            long now = 0;
            FrameLoop loop = new(builder.Build()) { Clock = () => (ulong)(now += 16_666_667) };
            loop.Run(context, 110);
        }
        await Assert.That(after - before).IsEqualTo(0);
    }

    [Test]
    public async Task SettingsComeFromGameArguments()
    {
        ParticleSettings settings = ParticleSettings.Parse(new Dictionary<string, string>
        {
            ["particles"] = "500",
            ["steps"] = "0",
        });
        await Assert.That(settings).IsEqualTo(ParticleSettings.Default with { Particles = 500, Steps = 0 });
        await Assert.That(Assert.Throws<ArgumentException>(() =>
            ParticleSettings.Parse(new Dictionary<string, string> { ["particles"] = "lots" }))).IsNotNull();
        await Assert.That(Assert.Throws<ArgumentException>(() =>
            ParticleSettings.Parse(new Dictionary<string, string> { ["colour"] = "red" }))).IsNotNull();
    }

    [Test]
    public async Task CppAndCSharpShareTheComponentIds()
    {
        // Registered by C++ under the C# mirror's name, and by C#: one ID each.
        Crucible.Demo.Api.RegisterDemoComponents();
        await Assert.That(Components.Register<Position>()).IsEqualTo(Components.Of<Position>());
        await Assert.That(typeof(Position).FullName).IsEqualTo("Crucible.Demo.Position");
        await Assert.That(typeof(Lifetime).FullName).IsEqualTo("Crucible.Demo.Lifetime");
    }

    private struct FirstEntity : IChunkJob
    {
        public Entity Entity;

        public void Execute(in ChunkView chunk)
        {
            if (Entity.IsNull && chunk.Count > 0)
            {
                Entity = chunk.Entities[0];
            }
        }
    }

    private struct CountExpired : IChunkJob
    {
        public int Count;

        public void Execute(in ChunkView chunk)
        {
            foreach (Lifetime lifetime in chunk.Read<Lifetime>())
            {
                Count += lifetime.Remaining <= 0 ? 1 : 0;
            }
        }
    }
}
