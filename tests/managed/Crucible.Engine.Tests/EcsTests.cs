using Crucible.Ecs;

namespace Crucible.Tests;

// The ECS from C# (ADR 0035), under JIT and NativeAOT. Parallel queries use
// the global job system, so these run apart from the other job tests.
[NotInParallel("jobs")]
public class EcsTests
{
    // Component fields are written in native memory, which the compiler
    // can't see, so it would call them never assigned.
#pragma warning disable CS0649
    private struct Position
    {
        public float X, Y, Z;
    }

    private struct Velocity
    {
        public float X, Y, Z;
    }

    private struct Health
    {
        public int Value;
    }

    private struct Enemy; // a tag
#pragma warning restore CS0649

    private struct Damaged // toggled often: sparse
    {
        public int Amount;
    }

    private sealed class Inventory // managed: a class
    {
        public List<string> Items { get; } = [];
    }

    private struct Name // managed: a struct holding a reference
    {
        public string Value;
    }

    private static readonly ComponentId _position = Components.Register<Position>();
    private static readonly ComponentId _velocity = Components.Register<Velocity>();
    private static readonly ComponentId _health = Components.Register<Health>();
    private static readonly ComponentId _enemy = Components.RegisterTag<Enemy>();
    private static readonly ComponentId _damaged = Components.RegisterSparse<Damaged>();
    private static readonly ComponentId _inventory = Components.RegisterManaged<Inventory>();
    private static readonly ComponentId _name = Components.RegisterManaged<Name>();

    [Before(Class)]
    public static void StartWorkers() => Crucible.Jobs.Api.Initialize(4);

    [After(Class)]
    public static void StopWorkers() => Crucible.Jobs.Api.Shutdown();

    [Test]
    public async Task ComponentsLiveInNativeMemoryAndAreWrittenInPlace()
    {
        using World world = new();
        Entity e = world.CreateEntity(_position, _health);

        world.Get<Position>(e).X = 3;
        ref Health health = ref world.Get<Health>(e);
        health.Value = 7;
        world.Set(e, new Velocity { Z = 2 }); // adds it

        await Assert.That(world.Read<Position>(e).X).IsEqualTo(3f);
        await Assert.That(world.Read<Health>(e).Value).IsEqualTo(7);
        await Assert.That(world.Has(e, _velocity)).IsTrue();
        await Assert.That(world.Read<Velocity>(e).Z).IsEqualTo(2f);
        await Assert.That(Components.Register<Position>()).IsEqualTo(_position); // registered once

        world.Destroy(e);
        await Assert.That(world.IsAlive(e)).IsFalse();
        await Assert.That(Assert.Throws<InvalidOperationException>(() => world.Get<Position>(e))).IsNotNull();
    }

    [Test]
    public async Task TagsAndSparseComponentsWork()
    {
        using World world = new();
        Entity e = world.CreateEntity(_position, _enemy);
        world.Add(e, _damaged);
        world.Get<Damaged>(e).Amount = 5;

        await Assert.That(world.Has(e, _enemy)).IsTrue();
        await Assert.That(world.Read<Damaged>(e).Amount).IsEqualTo(5);
        world.Remove(e, _damaged);
        await Assert.That(world.Has(e, _damaged)).IsFalse();
    }

    private struct Integrate : IChunkJob
    {
        public float DeltaTime;

        public readonly void Execute(in ChunkView chunk)
        {
            Span<Position> positions = chunk.Write<Position>();
            ReadOnlySpan<Velocity> velocities = chunk.Read<Velocity>();
            for (int i = 0; i < chunk.Count; ++i)
            {
                positions[i].X += velocities[i].X * DeltaTime;
            }
        }
    }

    private struct CountEntities(int[] counts) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk) => Interlocked.Add(ref counts[0], chunk.Count);
    }

    [Test]
    public async Task ChunkJobsRunSequentiallyAndInParallel()
    {
        using World world = new();
        for (int i = 0; i < 5000; ++i)
        {
            Entity e = world.CreateEntity(_position, _velocity);
            world.Get<Velocity>(e).X = 2;
        }
        for (int i = 0; i < 100; ++i)
        {
            world.CreateEntity(_position, _velocity, _enemy); // excluded below
        }
        using Query moving = world.CreateQuery([_position, _velocity], [_enemy]);

        int[] counts = new int[1];
        CountEntities count = new(counts);
        moving.ForEachChunk(ref count);
        await Assert.That(counts[0]).IsEqualTo(5000);

        Integrate integrate = new() { DeltaTime = 0.5f };
        moving.ParallelForEachChunk(ref integrate);
        moving.ForEachChunk(ref integrate);

        float[] seen = new float[1];
        CheckX check = new(seen);
        moving.ForEachChunk(ref check);
        await Assert.That(seen[0]).IsEqualTo(2f); // 0 + 1 + 1, in every entity
    }

    private struct CheckX(float[] seen) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk)
        {
            foreach (Position p in chunk.Read<Position>())
            {
                seen[0] = p.X;
                if (p.X != 2f)
                {
                    throw new InvalidOperationException($"X was {p.X}");
                }
            }
        }
    }

    private struct SeeChanges(uint since, bool[] seen) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk)
        {
            seen[0] |= chunk.ChangedSince(_position, since);
            seen[1] |= chunk.ChangedSince(_velocity, since);
        }
    }

    [Test]
    public async Task WritesStampVersionsAndReadsDont()
    {
        using World world = new();
        Entity e = world.CreateEntity(_position, _velocity);
        using Query query = world.CreateQuery([_position, _velocity]);
        uint lastRun = world.AdvanceVersion();
        world.AdvanceVersion();

        _ = world.Read<Position>(e);
        world.Get<Velocity>(e).X = 1;
        bool[] seen = new bool[2];
        SeeChanges job = new(lastRun, seen);
        query.ForEachChunk(ref job);

        await Assert.That(seen[0]).IsFalse();
        await Assert.That(seen[1]).IsTrue();
    }

    [Test]
    public async Task ManagedComponentsAreReleasedByEveryNativePath()
    {
        int before = ManagedPool<Inventory>.Instance.Count;
        int namesBefore = ManagedPool<Name>.Instance.Count;
        using (World world = new())
        {
            Entity a = world.CreateEntity(_position);
            Entity b = world.CreateEntity(_position);
            Entity c = world.CreateEntity(_position);
            Entity child = world.CreateEntity(_position);
            Entity kept = world.CreateEntity(_position);
            foreach (Entity e in new[] { a, b, c, child, kept })
            {
                world.SetManaged(e, new Inventory());
            }
            world.SetManaged(a, new Name { Value = "a" });
            world.GetManaged<Inventory>(a).Items.Add("sword");
            world.SetParent(child, c);

            await Assert.That(world.GetManaged<Inventory>(a).Items[0]).IsEqualTo("sword");
            await Assert.That(world.GetManaged<Name>(a).Value).IsEqualTo("a");
            await Assert.That(ManagedPool<Inventory>.Instance.Count).IsEqualTo(before + 5);

            world.Remove(a, _inventory); // removed
            world.Destroy(b);            // entity destroyed
            world.Destroy(c);            // with its child, through the hierarchy
            await Assert.That(ManagedPool<Inventory>.Instance.Count).IsEqualTo(before + 1);
            await Assert.That(world.GetManaged<Name>(a).Value).IsEqualTo("a"); // moved with a
        }
        // The world's destruction releases the rest.
        await Assert.That(ManagedPool<Inventory>.Instance.Count).IsEqualTo(before);
        await Assert.That(ManagedPool<Name>.Instance.Count).IsEqualTo(namesBefore);
    }

    private struct CullDamaged(CommandBuffer buffer) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk)
        {
            ReadOnlySpan<Health> healths = chunk.Read<Health>();
            ReadOnlySpan<Entity> entities = chunk.Entities;
            for (int i = 0; i < chunk.Count; ++i)
            {
                if (healths[i].Value % 2 == 0)
                {
                    buffer.RecordDestroy(entities[i]);
                    Entity replacement = buffer.RecordCreate(_health);
                    buffer.RecordSet(replacement, new Health { Value = 1 });
                }
            }
        }
    }

    [Test]
    public async Task ParallelChunkJobsRecordIntoACommandBuffer()
    {
        using World world = new();
        for (int i = 0; i < 4000; ++i)
        {
            Entity e = world.CreateEntity(_health);
            world.Get<Health>(e).Value = i;
        }
        using Query query = world.CreateQuery([_health]);
        using CommandBuffer buffer = world.CreateCommandBuffer();
        CullDamaged job = new(buffer);
        query.ParallelForEachChunk(ref job);
        await Assert.That(world.EntityCount).IsEqualTo(4000); // nothing applied yet

        buffer.Apply();

        int[] odd = new int[1];
        CountOdd countOdd = new(odd);
        query.ForEachChunk(ref countOdd);
        await Assert.That(world.EntityCount).IsEqualTo(4000);
        await Assert.That(odd[0]).IsEqualTo(4000); // every even one replaced by a 1
    }

    private struct CountOdd(int[] odd) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk)
        {
            foreach (Health h in chunk.Read<Health>())
            {
                odd[0] += h.Value % 2;
            }
        }
    }

    [Test]
    public async Task TheHierarchyKeepsOrderAndRefusesCycles()
    {
        using World world = new();
        Entity root = world.CreateEntity();
        Entity a = world.CreateEntity();
        Entity b = world.CreateEntity();
        world.SetParent(a, root);
        world.SetParent(b, root, 0);
        Entity[] children = new Entity[4];

        await Assert.That(world.ChildrenOf(root, children)).IsEqualTo(2);
        await Assert.That(children[0]).IsEqualTo(b);
        await Assert.That(world.ParentOf(a)).IsEqualTo(root);
        CrucibleException? cycle = Assert.Throws<CrucibleException>(() => world.SetParent(root, a));
        await Assert.That(cycle!.Status).IsEqualTo(Status.InvalidData);
    }

    [Test]
    public async Task IteratingMakesNoGarbageOnceWarm()
    {
        using World world = new();
        for (int i = 0; i < 2000; ++i)
        {
            world.CreateEntity(_position, _velocity);
        }
        using Query query = world.CreateQuery([_position, _velocity]);
        Integrate job = new() { DeltaTime = 0.016f };
        query.ForEachChunk(ref job);
        query.ParallelForEachChunk(ref job);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; ++i)
        {
            query.ForEachChunk(ref job);
            query.ParallelForEachChunk(ref job);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }
}
