using Crucible.Demo;
using Crucible.Ecs;
using Crucible.Loop;
using DemoApi = Crucible.Demo.Api;

namespace Crucible.Samples.Particles;

// The sample's C# systems. Each fixed step runs, in order:
//   PrePhysics   crucible.demo.Respawn (C++)  records creations up to the target
//   (sync point: the creations are applied)
//   Physics      crucible.demo.Steer (C++)    velocity, one job per chunk
//                IntegrateSystem (C#)         position and lifetime, one job per chunk
//   (sync point)
//   PostPhysics  ExpireSystem (C#)            records destroys, one job per chunk
//   (sync point: the destroys are applied)

/// <summary>
/// Registers the C++ systems and components, and fills the world with
/// particles before the first frame.
/// </summary>
[System(Phases.BeginFrame)]
public sealed class ParticleSetup : GameSystem
{
    // Steer's constants (crucible/Demo.h), public so tests can check the motion.
    public const float Pull = 1.0f;
    public const float Spin = 0.6f;
    public const float Drag = 0.3f;

    /// <summary>The settings in use, for the other systems.</summary>
    public static ParticleSettings Settings { get; private set; } = ParticleSettings.Default;

    public ParticleSetup()
    {
        // C++ systems join the schedule when it's built, and the generated
        // registration creates this system just before that, so this is where
        // a game registers them (ADR 0036). Registering again replaces them.
        DemoApi.RegisterDemoSystems();
        Components.Register<Position>();
        Components.Register<Velocity>();
        Components.Register<Lifetime>();
    }

    public override void Start(FrameContext context)
    {
        Settings = ParticleSettings.Parse(context.GameArguments);
        DemoApi.ConfigureDemo(new DemoSettings
        {
            Population = (uint)Settings.Particles,
            Seed = Settings.Seed,
            Pull = Pull,
            Spin = Spin,
            Drag = Drag,
            Radius = 10,
            MinLifetime = 2,
            MaxLifetime = 6,
        });

        // The first particles, with lifetimes spread out so they don't all
        // expire at once. Respawn's later ones start on the ring.
        Random random = new((int)Settings.Seed);
        World world = context.World;
        ComponentId[] components = [Components.Of<Position>(), Components.Of<Velocity>(), Components.Of<Lifetime>()];
        for (int i = 0; i < Settings.Particles; ++i)
        {
            float angle = random.NextSingle() * MathF.Tau;
            float distance = 5 + random.NextSingle() * 5;
            System.Numerics.Vector3 direction = new(MathF.Cos(angle), 0, MathF.Sin(angle));
            Entity entity = world.CreateEntity(components);
            world.Set(entity, new Position { Value = direction * distance });
            world.Set(entity, new Velocity { Value = new System.Numerics.Vector3(-direction.Z, 0, direction.X) * 4 });
            world.Set(entity, new Lifetime { Remaining = random.NextSingle() * 6 });
        }
    }

    public override void Run(FrameContext context)
    {
    }

    public override void Stop(FrameContext context) => DemoApi.UnregisterDemoSystems();
}

/// <summary>Moves each particle by its velocity and ages it, after the C++ Steer system.</summary>
[System(Phases.Physics, AfterNames = ["crucible.demo.Steer"])]
public sealed class IntegrateSystem : GameSystem
{
    private Query? _particles;

    private struct IntegrateJob : IChunkJob
    {
        public float Step;

        public readonly void Execute(in ChunkView chunk)
        {
            Span<Position> positions = chunk.Write<Position>();
            ReadOnlySpan<Velocity> velocities = chunk.Read<Velocity>();
            Span<Lifetime> lifetimes = chunk.Write<Lifetime>();
            for (int i = 0; i < positions.Length; ++i)
            {
                positions[i].Value += velocities[i].Value * Step;
                lifetimes[i].Remaining -= Step;
            }
        }
    }

    public override void Start(FrameContext context) =>
        _particles = context.World.CreateQuery([Components.Of<Position>(), Components.Of<Velocity>(), Components.Of<Lifetime>()]);

    public override void Run(FrameContext context)
    {
        IntegrateJob job = new() { Step = (float)context.FixedStepSeconds };
        _particles!.ParallelForEachChunk(ref job);
    }

    public override void Stop(FrameContext context) => _particles?.Dispose();
}

/// <summary>Records a destroy for every particle whose lifetime ran out; the phase's sync point applies them.</summary>
[System(Phases.PostPhysics)]
public sealed class ExpireSystem : GameSystem
{
    private Query? _particles;

    private struct ExpireJob : IChunkJob
    {
        // Recording takes calls from every worker at once (ADR 0023).
        public CommandBuffer Commands;

        public readonly void Execute(in ChunkView chunk)
        {
            ReadOnlySpan<Lifetime> lifetimes = chunk.Read<Lifetime>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            for (int i = 0; i < lifetimes.Length; ++i)
            {
                if (lifetimes[i].Remaining <= 0)
                {
                    Commands.RecordDestroy(entities[i]);
                }
            }
        }
    }

    public override void Start(FrameContext context) =>
        _particles = context.World.CreateQuery([Components.Of<Lifetime>()]);

    public override void Run(FrameContext context)
    {
        ExpireJob job = new() { Commands = context.Commands };
        _particles!.ParallelForEachChunk(ref job);
    }

    public override void Stop(FrameContext context) => _particles?.Dispose();
}
