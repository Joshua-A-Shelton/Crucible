using System.Reflection;
using System.Runtime.CompilerServices;
using Crucible.Ecs;
using Crucible.Hosting;
using Crucible.Loop;

namespace Crucible.Tests.HotReload;

// The helpers that touch a loaded game are NoInlining: a debug build keeps a
// method's locals alive until it returns, so any reference to the game held
// in the test method itself would keep the old version from unloading.
// Not in parallel: migration changes every live world, and the other tests'
// games make worlds too.
[NotInParallel("reload")]
public class HotReloadTests
{
    // The test game runs a job on the workers; its job type must not keep it
    // from unloading.
    [Before(Class)]
    public static void StartWorkers() => Crucible.Jobs.Api.Initialize(4);

    [After(Class)]
    public static void StopWorkers() => Crucible.Jobs.Api.Shutdown();

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

    [Test]
    public async Task MigratesComponentValuesAcrossAReload()
    {
        WeakReference v1;
        WeakReference v2;
        string problems;
        IReadOnlyList<MigrationNote> notes;
        using (World world = new())
        {
            (problems, notes, v1, v2) = PopulateReloadAndCheck(world);
        }

        await Assert.That(problems).IsEqualTo("");
        const string game = "Crucible.TestGame.TestGameEntry";
        string[] expected =
        [
            $"{game}+Body.Inner.Added: FieldAdded",
            $"{game}+Body.Inner.Removed: FieldRemoved",
            $"{game}+Body.Health: FieldTypeChanged",
            $"{game}+Body.Armor: FieldAdded",
            $"{game}+Status.Flags: FieldAdded",
            $"{game}+Doomed: ComponentRemoved",
            $"{game}+Tag.Owner: FieldReset",
            $"{game}+Tag.Payload: FieldReset", // held a game object in some values
            $"{game}+Tag.Added: FieldAdded",
        ];
        await Assert.That(notes.Select(note => note.ToString()).Order()).IsEquivalentTo(expected.Order());
        // Migration kept nothing of the old version.
        await Assert.That(GameLoader.WaitForUnload(v1)).IsTrue();
        await Assert.That(GameLoader.WaitForUnload(v2)).IsTrue();
    }

    [Test]
    public async Task RegistersEachVersionsSystems()
    {
        (string first, WeakReference v1) = SystemsOf(TestGame("v1"));
        await Assert.That(first).IsEqualTo("Crucible.TestGame.SpinSystem");
        await Assert.That(GameLoader.WaitForUnload(v1)).IsTrue();

        (string second, WeakReference v2) = SystemsOf(TestGame("v2"));
        await Assert.That(second).IsEqualTo("Crucible.TestGame.AimSystem Crucible.TestGame.SpinSystem");
        // The built schedule held the game's systems; once it's gone, nothing does.
        await Assert.That(GameLoader.WaitForUnload(v2)).IsTrue();
    }

    // The game's systems in schedule order, through its generated registration.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Systems, WeakReference Context) SystemsOf(string path)
    {
        LoadedGame game = GameLoader.Load(path);
        ScheduleBuilder builder = new ScheduleBuilder { IncludeNativeSystems = false }.AddPhase(Phases.Update);
        game.RegisterSystems(builder);
        string systems = string.Join(" ", builder.Build().Systems.Select(system => system.Name));
        return (systems, game.Unload());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (string Problems, IReadOnlyList<MigrationNote> Notes, WeakReference V1, WeakReference V2)
        PopulateReloadAndCheck(World world)
    {
        LoadedGame v1 = GameLoader.Load(TestGame("v1"));
        Entity[] entities = (Entity[])Call(v1.Entry, "Populate", world);
        LoadedGame v2 = GameLoader.Reload(v1, TestGame("v2"), out IReadOnlyList<MigrationNote> notes);
        WeakReference oldContext = v1.Unload();
        string problems = (string)Call(v2.Entry, "Check", world, entities);
        return (problems, notes, oldContext, v2.Unload());
    }

    // The test game's own methods, beyond IGameEntry.
    private static object Call(IGameEntry entry, string method, params object[] arguments) =>
        entry.GetType().GetMethod(method)!.Invoke(entry, arguments)!;

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
