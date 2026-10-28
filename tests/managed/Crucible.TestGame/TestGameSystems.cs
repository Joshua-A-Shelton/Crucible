using Crucible.Loop;

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
