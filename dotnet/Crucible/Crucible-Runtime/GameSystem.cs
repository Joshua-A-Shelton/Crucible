using Arch.Core;
using Arch.System;

namespace Crucible;

public abstract class GameSystem:BaseSystem<World,float>
{
    public int Priority { get; protected set; } = 0;
    protected GameSystem() : base(GameWorld._world)
    {
    }
}