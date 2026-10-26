using Crucible.Hosting;

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
    public int Run() => 2;
#else
    public int Run() => 1;
#endif
}
