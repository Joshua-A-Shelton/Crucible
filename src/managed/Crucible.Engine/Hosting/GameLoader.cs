using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Crucible.Ecs;
using Crucible.Loop;

namespace Crucible.Hosting;

/// <summary>
/// Loads game assemblies into collectible load contexts so they can be
/// unloaded and reloaded (ADR 0001). JIT only: AOT builds compile games in.
/// </summary>
public static class GameLoader
{
    private const string JitOnly =
        "Hot reload loads game assemblies at runtime, which only JIT builds can do (ADR 0001).";

    /// <summary>
    /// Loads the game assembly at <paramref name="path"/> into a new
    /// collectible load context and creates its entry.
    /// </summary>
    /// <remarks>
    /// Crucible.Engine itself isn't loaded again: the new context falls back
    /// to the default one for it, so the game and the engine share one
    /// <see cref="IGameEntry"/> type.
    /// </remarks>
    [RequiresUnreferencedCode(JitOnly)]
    public static LoadedGame Load(string path)
    {
        (AssemblyLoadContext context, Assembly assembly) = LoadAssembly(path);
        return new LoadedGame(context, CreateEntry(assembly), CreateRegistrations(assembly));
    }

    /// <summary>
    /// Loads a new version of <paramref name="current"/>'s game beside it and
    /// moves every world's component values to the new version's types
    /// (ADR 0035); then unload <paramref name="current"/>. Returns the new
    /// version, and in <paramref name="notes"/> what migration had to drop or
    /// reset, for the editor to show.
    /// </summary>
    /// <remarks>
    /// Call it between frames: no world may be iterating, and nothing else
    /// may use the ECS meanwhile. The new version's entry is created after
    /// migration, so its first Register calls already see the migrated IDs.
    /// Queries and command buffers made by the old version are stale
    /// afterwards: the new version makes its own.
    /// </remarks>
    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    public static LoadedGame Reload(LoadedGame current, string path, out IReadOnlyList<MigrationNote> notes)
    {
        AssemblyLoadContext oldContext = current.Context;
        (AssemblyLoadContext context, Assembly assembly) = LoadAssembly(path);
        notes = ComponentMigration.Migrate(oldContext, context);
        return new LoadedGame(context, CreateEntry(assembly), CreateRegistrations(assembly));
    }

    // The assembly is read from a stream, not opened by path, so the file
    // isn't locked: a rebuild can overwrite it while the old version runs.
    [RequiresUnreferencedCode(JitOnly)]
    private static (AssemblyLoadContext Context, Assembly Assembly) LoadAssembly(string path)
    {
        AssemblyLoadContext context = new($"Game: {Path.GetFileName(path)}", isCollectible: true);
        using FileStream stream = File.OpenRead(path);
        return (context, context.LoadFromStream(stream));
    }

    [RequiresUnreferencedCode(JitOnly)]
    private static IGameEntry? CreateEntry(Assembly assembly) =>
        assembly.GetCustomAttribute<GameEntryAttribute>() is { } attribute
            ? (IGameEntry)(Activator.CreateInstance(attribute.EntryType) ??
                           throw new InvalidOperationException($"Could not create {attribute.EntryType}."))
            : null;

    // The generated registrations (ADR 0036, 0041) the game has.
    [RequiresUnreferencedCode(JitOnly)]
    private static Registrations CreateRegistrations(Assembly assembly) => new(
        Create<ISystemRegistration>(assembly.GetCustomAttribute<SystemRegistrationAttribute>()?.RegistrationType),
        Create<IComponentRegistration>(assembly.GetCustomAttribute<ComponentRegistrationAttribute>()?.RegistrationType),
        Create<IServiceRegistration>(assembly.GetCustomAttribute<ServiceRegistrationAttribute>()?.RegistrationType));

    private static T? Create<T>(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type? type)
        where T : class =>
        type is null
            ? null
            : (T)(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"Could not create {type}."));

    /// <summary>
    /// Waits for an unloaded context to be collected (ADR 0011's unload check).
    /// Returns false if something still references the game assembly.
    /// </summary>
    /// <remarks>
    /// Unloading only starts the process: the context is collected once nothing
    /// references it, which takes garbage collections. A context still alive
    /// after several is being held by something, a leak that would stop every
    /// later reload from freeing the old code.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool WaitForUnload(WeakReference unloadedContext, int attempts = 10)
    {
        for (int i = 0; i < attempts && unloadedContext.IsAlive; ++i)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !unloadedContext.IsAlive;
    }
}

/// <summary>A game assembly loaded into its own collectible context.</summary>
public sealed class LoadedGame
{
    private AssemblyLoadContext? _context;
    private IGameEntry? _entry;
    private Registrations? _registrations;

    internal LoadedGame(AssemblyLoadContext context, IGameEntry? entry, Registrations registrations)
    {
        _context = context;
        _entry = entry;
        _registrations = registrations;
    }

    /// <summary>The game's <see cref="GameEntryAttribute"/> entry (an M1 placeholder); throws if it has none.</summary>
    public IGameEntry Entry =>
        _entry ?? throw new InvalidOperationException(_context is null
            ? "The game has been unloaded."
            : "The game has no [GameEntry] attribute.");

    /// <summary>
    /// Adds the game's <see cref="SystemAttribute"/> systems to
    /// <paramref name="builder"/>, through its generated registration (ADR
    /// 0036). After a reload, build a new schedule with the new version's
    /// systems: they are new objects, so values in the old ones' fields are
    /// gone.
    /// </summary>
    public void RegisterSystems(ScheduleBuilder builder) => Live.Systems?.Register(builder);

    /// <summary>
    /// Registers the game's <see cref="ComponentAttribute"/> types, and which
    /// of them scenes save (ADR 0041). Call it before the game's systems
    /// start or a scene loads.
    /// </summary>
    public void RegisterComponents() => Live.Components?.Register();

    /// <summary>Creates the game's <see cref="ServiceAttribute"/> services into <paramref name="services"/> (ADR 0041).</summary>
    public void RegisterServices(Services services) => Live.Services?.Register(services);

    private Registrations Live =>
        _registrations ?? throw new InvalidOperationException("The game has been unloaded.");

    internal AssemblyLoadContext Context =>
        _context ?? throw new InvalidOperationException("The game has been unloaded.");

    /// <summary>
    /// Starts unloading and drops this object's references. Pass the returned
    /// weak reference to <see cref="GameLoader.WaitForUnload"/> to confirm.
    /// </summary>
    public WeakReference Unload()
    {
        AssemblyLoadContext context = _context ?? throw new InvalidOperationException("Already unloaded.");
        WeakReference weak = new(context);
        _entry = null;
        _registrations = null;
        _context = null;
        // The saved-component table would otherwise keep the game's types.
        SavedComponents.Forget(context);
        context.Unload();
        return weak;
    }
}

// A game assembly's generated registrations; each is null if it has none.
internal sealed record Registrations(
    ISystemRegistration? Systems,
    IComponentRegistration? Components,
    IServiceRegistration? Services);
