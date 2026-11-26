using Crucible.Ecs;

namespace Crucible.Scenes;

/// <summary>
/// A top-level entity's place among the world's top-level entities (ADR
/// 0130), as children have theirs under a parent: loading a scene puts its
/// roots after the world's in file order, saving writes them in this order,
/// and the editor's Hierarchy lists and reorders them by it. Not saved (the
/// file's order is the order). Entities without one (made by game code)
/// come after the rest, by scene ID.
/// </summary>
public struct RootOrder
{
    public long Value;

    /// <summary>The top-level entities with scene IDs, in order.</summary>
    public static IReadOnlyList<Entity> RootsOf(World world)
    {
        EngineComponents.Register();
        using Query query = world.CreateQuery([Components.Of<SceneEntityId>()]);
        Collector collector = new() { Found = [] };
        query.ForEachChunk(ref collector);
        return Order(world, collector.Found.Where(entity => world.ParentOf(entity).IsNull));
    }

    /// <summary>Its place among the top-level entities; -1 if it isn't one.</summary>
    public static int IndexOf(World world, Entity entity) =>
        world.IsAlive(entity) && world.ParentOf(entity).IsNull ? IndexIn(RootsOf(world), entity) : -1;

    /// <summary>
    /// Puts a top-level entity at <paramref name="index"/> among the others
    /// (-1, or past the end: last), numbering them all again.
    /// </summary>
    public static void Insert(World world, Entity entity, int index)
    {
        List<Entity> roots = [.. RootsOf(world).Where(root => root.Value != entity.Value)];
        roots.Insert(index < 0 || index > roots.Count ? roots.Count : index, entity);
        for (int i = 0; i < roots.Count; ++i)
        {
            world.Set(roots[i], new RootOrder { Value = i });
        }
    }

    // Top-level entities in order: by place, then scene ID.
    internal static IReadOnlyList<Entity> Order(World world, IEnumerable<Entity> roots)
    {
        EngineComponents.Register();
        ComponentId order = Components.Of<RootOrder>(), id = Components.Of<SceneEntityId>();
        return
        [
            .. roots.Select(entity => (Entity: entity,
                    Place: world.Has(entity, order) ? world.Read<RootOrder>(entity).Value : long.MaxValue,
                    Id: world.Has(entity, id) ? world.Read<SceneEntityId>(entity).Value : ulong.MaxValue))
                .OrderBy(item => item.Place).ThenBy(item => item.Id).ThenBy(item => item.Entity.Value)
                .Select(item => item.Entity),
        ];
    }

    // After every top-level entity there is now: for a scene's or an instance's new roots.
    internal static long Next(World world, IReadOnlyCollection<Entity> except)
    {
        long next = 0;
        ComponentId order = Components.Of<RootOrder>();
        foreach (Entity root in RootsOf(world))
        {
            if (!except.Contains(root) && world.Has(root, order))
            {
                next = Math.Max(next, world.Read<RootOrder>(root).Value + 1);
            }
        }
        return next;
    }

    private static int IndexIn(IReadOnlyList<Entity> roots, Entity entity)
    {
        for (int i = 0; i < roots.Count; ++i)
        {
            if (roots[i].Value == entity.Value)
            {
                return i;
            }
        }
        return -1;
    }

    private struct Collector : IChunkJob
    {
        public List<Entity> Found;

        public readonly void Execute(in ChunkView chunk) => Found.AddRange(chunk.Entities);
    }
}
