using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Serialization;

namespace Crucible.TestGame;

// The test game's systems, registered through [System] and the source
// generator (ADR 0036): version 2 adds one, so the hot reload tests can see
// the loader register each version's own systems.

[System(Phases.Update)]
public sealed class SpinSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
    }
}

#if TEST_GAME_V2
[System(Phases.Update, Before = [typeof(SpinSystem)])]
public sealed class AimSystem : GameSystem
{
    public override void Run(FrameContext context)
    {
    }
}
#endif

// A saved component and a saved service of the game's own (ADR 0041): the
// game must still unload after registering them.
[Component, Serialized]
public partial struct Fuel
{
    public float Litres;
}

[Service, Serialized]
public sealed partial class Garage
{
    public int Cars;
}
