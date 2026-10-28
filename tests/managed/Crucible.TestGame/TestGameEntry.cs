using System.Numerics;
using Crucible.Ecs;
using Crucible.Hosting;
using Crucible.Jobs;

[assembly: GameEntry(typeof(Crucible.TestGame.TestGameEntry))]

namespace Crucible.TestGame;

/// <summary>
/// A tiny game built twice (Crucible.TestGame.V1 and V2, same assembly name)
/// so the hot reload tests can load one version, unload it, and load the
/// other.
/// </summary>
public sealed class TestGameEntry : IGameEntry
{
#if TEST_GAME_V2
    private const int Version = 2;
#else
    private const int Version = 1;
#endif

    // A job type of the game's own, run on the engine's workers: the hot
    // reload tests check the game still unloads after its jobs ran.
    private struct SquareSum(long[] partials) : IParallelForJob
    {
        public readonly void Execute(int begin, int end)
        {
            long sum = 0;
            for (long i = begin; i < end; ++i)
            {
                sum += i * i;
            }
            Interlocked.Add(ref partials[0], sum);
        }
    }

    // A component type and a managed one of the game's own, in an ECS world:
    // the game must still unload after using them.
    private struct Score
    {
        public long Points;
    }

    // Components that change between the versions, for the migration test
    // (ADR 0035): fields reordered, added, removed, and retyped, at the top
    // level and inside a nested struct of the game's own.
#pragma warning disable CS0649 // written through native memory and reflection
#if TEST_GAME_V2
    private struct Inner
    {
        public double Added;
        public float Kept;
    }

    private struct Body
    {
        public long Id;
        public Inner Inner;
        public double Health; // was an int
        public Vector3 Velocity;
        public int Armor;
    }

    private struct Status // sparse
    {
        public byte Flags;
        public int Stacks;
    }
#else
    private struct Inner
    {
        public int Removed;
        public float Kept;
    }

    private struct Body
    {
        public Vector3 Velocity;
        public Inner Inner;
        public int Health;
        public long Id;
    }

    private struct Status
    {
        public int Stacks;
    }

    private struct Doomed // gone in V2
    {
        public int Value;
    }
#endif
#pragma warning restore CS0649

    private sealed class Marker;

    private sealed class Tag
    {
        public string Label = "";
        public Marker? Owner;          // the game's own type: reset by migration
        public object? Payload;        // reset only when it holds a game object
        public List<string> Items = [];
#if TEST_GAME_V2
        public int Added = 7;
#endif
    }

    private struct Tally(long[] total) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk)
        {
            long sum = 0;
            foreach (Score score in chunk.Read<Score>())
            {
                sum += score.Points;
            }
            Interlocked.Add(ref total[0], sum);
        }
    }

    // The version, if the parallel sum of i² for i < 1000 is right, both as
    // a job and through ECS chunks.
    public int Run()
    {
        long[] partials = new long[1];
        SquareSum job = new(partials);
        JobSystem.ParallelFor(ref job, 1000, 10);

        ComponentId score = Components.Register<Score>();
        Components.RegisterManaged<Tag>();
        long[] total = new long[1];
        using (World world = new())
        {
            for (int i = 0; i < 1000; ++i)
            {
                Entity e = world.CreateEntity(score);
                world.Get<Score>(e).Points = (long)i * i;
                world.SetManaged(e, new Tag { Label = "scored" });
            }
            using Query query = world.CreateQuery([score]);
            Tally tally = new(total);
            query.ParallelForEachChunk(ref tally);
        }
        return partials[0] == 332_833_500 && total[0] == 332_833_500 ? Version : -1;
    }

    // Called by the hot reload tests through reflection, in V1: fills a world
    // the engine owns, which outlives this version.
    public Entity[] Populate(World world)
    {
        ComponentId body = Components.Register<Body>();
        ComponentId score = Components.Register<Score>();
        ComponentId status = Components.RegisterSparse<Status>();
        Components.RegisterManaged<Tag>();
#if !TEST_GAME_V2
        ComponentId doomed = Components.Register<Doomed>();
#endif
        Entity[] entities = new Entity[300];
        for (int i = 0; i < entities.Length; ++i)
        {
            Entity e = entities[i] = world.CreateEntity(body, score);
            world.Get<Body>(e) = new Body { Id = i, Velocity = new Vector3(i, 0, 0), Inner = { Kept = i }, Health = i };
            world.Get<Score>(e).Points = i;
            if (i % 3 == 0)
            {
                world.Set(e, new Status { Stacks = i });
            }
#if !TEST_GAME_V2
            if (i % 5 == 0)
            {
                world.Add(e, doomed);
            }
#endif
            if (i % 2 == 0)
            {
                world.SetManaged(e, new Tag
                {
                    Label = $"t{i}",
                    Owner = new Marker(),
                    Payload = i % 4 == 0 ? new Marker() : $"p{i}",
                    Items = [$"i{i}"],
                });
            }
        }
        return entities;
    }

    // Called in V2 after migration: the problems found, or "".
    public string Check(World world, Entity[] entities)
    {
        ComponentId body = Components.Register<Body>();
        Components.Register<Score>();
        ComponentId status = Components.RegisterSparse<Status>();
        Components.RegisterManaged<Tag>();
        List<string> problems = [];
        void Expect(bool condition, string what, int i)
        {
            if (!condition && problems.Count < 10)
            {
                problems.Add($"entity {i}: {what}");
            }
        }

        for (int i = 0; i < entities.Length; ++i)
        {
            Entity e = entities[i];
            Body b = world.Read<Body>(e);
            Expect(b.Id == i && b.Velocity == new Vector3(i, 0, 0) && b.Inner.Kept == i, "kept Body fields", i);
#if TEST_GAME_V2
            Expect(b.Health == 0 && b.Armor == 0 && b.Inner.Added == 0, "new and retyped Body fields zeroed", i);
            if (i % 3 == 0)
            {
                Expect(world.Read<Status>(e).Stacks == i && world.Read<Status>(e).Flags == 0, "Status", i);
            }
#endif
            Expect(world.Read<Score>(e).Points == i, "Score", i);
            Expect(world.Has(e, status) == (i % 3 == 0), "sparse membership", i);
            if (i % 2 == 0)
            {
                Tag tag = world.GetManaged<Tag>(e);
                Expect(tag.Label == $"t{i}" && tag.Items is [var item] && item == $"i{i}", "kept Tag fields", i);
                Expect(tag.Owner is null, "game-typed Tag field reset", i);
                Expect(i % 4 == 0 ? tag.Payload is null : tag.Payload as string == $"p{i}", "Tag payload", i);
#if TEST_GAME_V2
                Expect(tag.Added == 7, "added Tag field initialized", i);
#endif
            }
        }

        using Query query = world.CreateQuery([body]);
        int[] count = new int[1];
        Count counter = new(count);
        query.ForEachChunk(ref counter);
        if (count[0] != entities.Length)
        {
            problems.Add($"a query found {count[0]} bodies");
        }
        return string.Join("; ", problems);
    }

    private struct Count(int[] count) : IChunkJob
    {
        public readonly void Execute(in ChunkView chunk) => count[0] += chunk.Count;
    }
}
