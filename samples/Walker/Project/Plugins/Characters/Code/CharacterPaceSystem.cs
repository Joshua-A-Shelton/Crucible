using Crucible.Animation;
using Crucible.Ecs;
using Crucible.Loop;

namespace Characters;

/// <summary>While the game runs, each character's Animator plays at its Pace (ADR 0128).</summary>
[System(Phases.Update)]
public sealed class CharacterPaceSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
        World world = context.World;
        using Query query = world.CreateQuery([Components.Of<Character>(), Components.Of<Animator>()]);
        Paced paced = new() { World = world, Found = [] };
        query.ForEachChunk(ref paced);
        foreach (Entity entity in paced.Found)
        {
            Animator animator = world.Read<Animator>(entity);
            float pace = world.Read<Character>(entity).Pace;
            if (animator.Speed != pace)
            {
                world.Set(entity, animator with { Speed = pace });
            }
        }
    }

    private struct Paced : IChunkJob
    {
        public World World;
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }
}
