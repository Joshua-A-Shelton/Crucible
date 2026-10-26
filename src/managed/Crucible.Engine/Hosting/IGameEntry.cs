namespace Crucible.Hosting;

/// <summary>
/// What the engine calls in a game assembly. A placeholder for M1: ADR 0011's
/// source-generated plugin registration replaces it.
/// </summary>
public interface IGameEntry
{
    /// <summary>Runs the game's M1 check and returns a value the test can see.</summary>
    int Run();
}
