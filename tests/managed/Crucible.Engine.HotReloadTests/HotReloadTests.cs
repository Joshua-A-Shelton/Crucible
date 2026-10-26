using System.Reflection;
using System.Runtime.CompilerServices;
using Crucible.Hosting;

namespace Crucible.Tests.HotReload;

// The helpers that touch a loaded game are NoInlining: a debug build keeps a
// method's locals alive until it returns, so any reference to the game held
// in the test method itself would keep the old version from unloading.
public class HotReloadTests
{
    // Where the test games are built; see TestGame.targets.
    private static readonly string _testGames = Path.Combine(
        typeof(HotReloadTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "CrucibleArtifactsPath").Value!,
        "testgames");

    private static string TestGame(string version) => Path.Combine(_testGames, version, "Crucible.TestGame.dll");

    // A reference that outlives the game, as a careless engine system might keep.
    private static IGameEntry? _leakedEntry;

    [Test]
    public async Task ReloadsANewVersionAfterUnloading()
    {
        (int result, WeakReference context) first = RunAndUnload(TestGame("v1"));
        await Assert.That(first.result).IsEqualTo(1);
        await Assert.That(GameLoader.WaitForUnload(first.context)).IsTrue();

        (int result, WeakReference context) second = RunAndUnload(TestGame("v2"));
        await Assert.That(second.result).IsEqualTo(2);
        await Assert.That(GameLoader.WaitForUnload(second.context)).IsTrue();
    }

    [Test]
    public async Task UnloadCheckCatchesALeakedReference()
    {
        WeakReference context = RunAndLeak(TestGame("v1"));
        await Assert.That(GameLoader.WaitForUnload(context)).IsFalse();

        // Dropping the leaked reference lets the context go.
        _leakedEntry = null;
        await Assert.That(GameLoader.WaitForUnload(context)).IsTrue();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Result, WeakReference Context) RunAndUnload(string path)
    {
        LoadedGame game = GameLoader.Load(path);
        int result = game.Entry.Run();
        return (result, game.Unload());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RunAndLeak(string path)
    {
        LoadedGame game = GameLoader.Load(path);
        _leakedEntry = game.Entry;
        return game.Unload();
    }
}
