using Arch.Core;

namespace Crucible.Core;

public static class GameWorld
{
    private static World _world = World.Create();

    /// <summary>
    /// Used from c++ to create a new entity
    /// </summary>
    /// <returns></returns>
    private static Entity UnmanagedCreateEntity()
    {
        return _world.Create();
    }
    /// <summary>
    /// Used from c++ to destroy an entity
    /// </summary>
    /// <param name="entity"></param>
    private static void UnmanagedFreeEntity(Entity entity)
    {
        _world.Destroy(entity);
    }
}