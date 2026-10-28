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

    private sealed class Tag
    {
        public string Label = "";
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
}
