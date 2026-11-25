using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Render;

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
    /// <remarks>
    /// The project's editor assembly (ADR 0116), <paramref name="editorPath"/>,
    /// is loaded into the same context: it references the game's types, so
    /// the two load, reload, and unload together.
    /// </remarks>
    [RequiresUnreferencedCode(JitOnly)]
    public static LoadedGame Load(string path, string? editorPath = null) => Load(new GameFiles(path) { Editor = editorPath });

    /// <summary>
    /// Loads a game and its source plugins' assemblies (ADR 0118) into a new
    /// collectible load context: the plugins first, in order, then the game,
    /// then the plugins' editor assemblies and the game's.
    /// </summary>
    /// <remarks>
    /// The plugins the game names (<see cref="GamePluginsAttribute"/>) that
    /// aren't loaded here (compiled ones) are found through
    /// <see cref="SharedAssemblies"/>, else beside the game's assembly.
    /// </remarks>
    [RequiresUnreferencedCode(JitOnly)]
    public static LoadedGame Load(GameFiles files)
    {
        (AssemblyLoadContext context, Assembly assembly, Assembly? editor, Assembly[] pluginEditors) = LoadAssembly(files);
        return Create(context, assembly, name => context.LoadFromAssemblyName(name), editor, pluginEditors);
    }

    // The game's entry and driver, and its plugins' registrations before its own.
    private static LoadedGame Create(AssemblyLoadContext context, Assembly game, Func<AssemblyName, Assembly> plugin,
        Assembly? editor = null, Assembly[]? pluginEditors = null)
    {
        Assembly[] plugins = [.. PluginNamesOf(game).Select(name => plugin(new AssemblyName(name)))];
        Registrations[] registrations = [.. plugins.Select(CreateRegistrations), CreateRegistrations(game)];
        return new LoadedGame(context, CreateEntry(game), registrations, DriverOf(game))
        {
            EditorAssembly = editor,
            Plugins = plugins,
            PluginEditorAssemblies = pluginEditors ?? [],
        };
    }

    /// <summary>The plugins a game assembly names (ADR 0118), in the order they load; none for a game without.</summary>
    public static IReadOnlyList<string> PluginNamesOf(Assembly game) =>
        game.GetCustomAttribute<GamePluginsAttribute>()?.Names ?? [];

    /// <summary>
    /// Loads a package's game (ADR 0100), which its runtime was started for,
    /// into the default load context: it is never unloaded, and .NET runs
    /// ReadyToRun code only for assemblies loaded this way.
    /// </summary>
    [RequiresUnreferencedCode(JitOnly)]
    public static LoadedGame LoadPackaged(string path)
    {
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveModule;
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        // Its plugins were published with it, so the runtime knows them.
        return Create(AssemblyLoadContext.Default, assembly, AssemblyLoadContext.Default.LoadFromAssemblyName);
    }

    /// <summary>
    /// A NativeAOT package's game, compiled into the executable (ADR 0101):
    /// set before the engine starts by the bootstrap generated into it.
    /// </summary>
    public static Assembly? CompiledIn { get; set; }

    /// <summary>
    /// The compiled-in game's entry and registrations, read from its
    /// assembly's attributes as a loaded game's are (ADR 0011's generated
    /// bootstrap): safe under NativeAOT, which keeps what they name.
    /// </summary>
    /// <remarks>Its plugins are compiled in too, rooted whole by its build, and found by name (ADR 0118).</remarks>
    public static LoadedGame LoadCompiledIn(Assembly assembly) => Create(AssemblyLoadContext.Default, assembly, Assembly.Load);

    /// <summary>
    /// Whether <paramref name="path"/> is one of the assemblies the runtime
    /// was started with: a package's game, not one to load for hot reload.
    /// </summary>
    public static bool IsPackaged(string path) =>
        AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string assemblies &&
        assemblies.Split(Path.PathSeparator).Contains(Path.GetFullPath(path), StringComparer.Ordinal);

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
    public static LoadedGame Reload(LoadedGame current, string path, out IReadOnlyList<MigrationNote> notes, string? editorPath = null) =>
        Reload(current, new GameFiles(path) { Editor = editorPath }, out notes);

    /// <summary>As the other overload, with the game's source plugins reloaded with it (ADR 0118).</summary>
    [RequiresUnreferencedCode(JitOnly)]
    [RequiresDynamicCode(JitOnly)]
    public static LoadedGame Reload(LoadedGame current, GameFiles files, out IReadOnlyList<MigrationNote> notes)
    {
        AssemblyLoadContext oldContext = current.Context;
        (AssemblyLoadContext context, Assembly assembly, Assembly? editor, Assembly[] pluginEditors) = LoadAssembly(files);
        notes = ComponentMigration.Migrate(oldContext, context);
        return Create(context, assembly, name => context.LoadFromAssemblyName(name), editor, pluginEditors);
    }

    // Assemblies are read from streams, not opened by path, so the files
    // aren't locked: a rebuild can overwrite them while the old version runs.
    [RequiresUnreferencedCode(JitOnly)]
    private static (AssemblyLoadContext Context, Assembly Game, Assembly? Editor, Assembly[] PluginEditors) LoadAssembly(GameFiles files)
    {
        AssemblyLoadContext context = new($"Game: {Path.GetFileName(files.Game)}", isCollectible: true);
        // The game's bindings into its native module (ADR 0074) name the
        // module's library, which the launcher or editor already loaded.
        context.ResolvingUnmanagedDll += ResolveModule;
        foreach (string plugin in files.Plugins)
        {
            LoadFrom(context, plugin);
        }
        Assembly game = LoadFrom(context, files.Game);
        // References between the assemblies loaded here (ADR 0116, 0118), and
        // to the editor's own and compiled plugins', shared rather than
        // loaded again; then the game's plugins beside it (a standalone run),
        // and only those: whoever loaded the game resolves the rest. Looked
        // up by name, not captured: a context holding its own assembly in a
        // handler would never be collected.
        string beside = Path.GetDirectoryName(Path.GetFullPath(files.Game))!;
        string[] plugins = [.. PluginNamesOf(game)];
        context.Resolving += (resolving, name) =>
            ResolveShared(resolving, name) ?? (plugins.Contains(name.Name) ? LoadBeside(resolving, name, beside) : null);
        Assembly[] pluginEditors = [.. files.PluginEditors.Select(path => LoadFrom(context, path))];
        Assembly? editor = files.Editor is { } editorPath ? LoadFrom(context, editorPath) : null;
        return (context, game, editor, pluginEditors);
    }

    [RequiresUnreferencedCode(JitOnly)]
    private static Assembly LoadFrom(AssemblyLoadContext context, string path)
    {
        using FileStream stream = File.OpenRead(path);
        return context.LoadFromStream(stream);
    }

    // A plugin's assembly copied beside the game (a standalone run's).
    [RequiresUnreferencedCode(JitOnly)]
    private static Assembly? LoadBeside(AssemblyLoadContext context, AssemblyName name, string folder)
    {
        string path = Path.Combine(folder, $"{name.Name}.dll");
        return File.Exists(path) ? LoadFrom(context, path) : null;
    }

    /// <summary>
    /// Loads compiled plugins (ADR 0118), each runtime assembly then its editor
    /// assembly, into one collectible context of their own: they don't change
    /// while the editor runs, so a game's reload leaves them be. Their
    /// references to the editor's assemblies go through
    /// <see cref="SharedAssemblies"/>; games find them the same way.
    /// </summary>
    [RequiresUnreferencedCode(JitOnly)]
    public static LoadedPlugins LoadPlugins(IReadOnlyList<(string? Assembly, string? Editor)> plugins)
    {
        AssemblyLoadContext context = new("Plugins", isCollectible: true);
        context.ResolvingUnmanagedDll += ResolveModule;
        context.Resolving += ResolveShared;
        // An official plugin may be editor-only (ADR 0123): no runtime assembly.
        Assembly[] runtime = [.. plugins.Where(plugin => plugin.Assembly is not null).Select(plugin => LoadFrom(context, plugin.Assembly!))];
        Assembly[] editors = [.. plugins.Where(plugin => plugin.Editor is not null).Select(plugin => LoadFrom(context, plugin.Editor!))];
        return new LoadedPlugins(context, runtime, editors);
    }

    /// <summary>
    /// Assemblies a game's context takes from elsewhere instead of loading
    /// them (ADR 0116): the editor's, which its editor assembly references.
    /// Set by the editor; null finds none.
    /// </summary>
    public static Func<AssemblyName, Assembly?>? SharedAssemblies { get; set; }

    private static Assembly? ResolveShared(AssemblyLoadContext context, AssemblyName name) =>
        context.Assemblies.FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(name, assembly.GetName())) ??
        SharedAssemblies?.Invoke(name);

    // A loaded module whose file is named <library>.so or <library>.dll.
    private static nint ResolveModule(Assembly assembly, string library)
    {
        uint count = Api.ModuleCount();
        for (uint i = 0; i < count; ++i)
        {
            string path = Api.ModulePath(i);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), library, StringComparison.Ordinal))
            {
                return NativeLibrary.Load(path);
            }
        }
        return 0;
    }

    // Each attribute's type is annotated, so these keep working under
    // NativeAOT (LoadCompiledIn).
    private static IGameEntry? CreateEntry(Assembly assembly) =>
        assembly.GetCustomAttribute<GameEntryAttribute>() is { } attribute
            ? (IGameEntry)(Activator.CreateInstance(attribute.EntryType) ??
                           throw new InvalidOperationException($"Could not create {attribute.EntryType}."))
            : null;

    // The generated registrations (ADR 0036, 0041) the game has.
    private static Registrations CreateRegistrations(Assembly assembly) => new(
        Create<ISystemRegistration>(assembly.GetCustomAttribute<SystemRegistrationAttribute>()?.RegistrationType),
        Create<IComponentRegistration>(assembly.GetCustomAttribute<ComponentRegistrationAttribute>()?.RegistrationType),
        Create<IServiceRegistration>(assembly.GetCustomAttribute<ServiceRegistrationAttribute>()?.RegistrationType));

    // The game's own loop driver (ADR 0076), if it names one.
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private static Type? DriverOf(Assembly assembly) => assembly.GetCustomAttribute<LoopDriverAttribute>()?.DriverType;

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
    private Registrations[]? _registrations;
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    private Type? _driver;

    internal LoadedGame(AssemblyLoadContext context, IGameEntry? entry, Registrations[] registrations,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type? driver = null)
    {
        _context = context;
        _entry = entry;
        _registrations = registrations;
        _driver = driver;
    }

    /// <summary>
    /// The game's own loop driver for <paramref name="schedule"/>, if it names
    /// one (<see cref="LoopDriverAttribute"/>, ADR 0076); null to use
    /// <see cref="FrameLoop"/>.
    /// </summary>
    public ILoopDriver? CreateLoopDriver(Schedule schedule)
    {
        if (_driver is null)
        {
            return null;
        }
        return Activator.CreateInstance(_driver, schedule) as ILoopDriver ??
               throw new InvalidOperationException($"{_driver} isn't an ILoopDriver with a constructor taking the Schedule.");
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
    /// <remarks>Its plugins' systems first, in the order they load (ADR 0118).</remarks>
    public void RegisterSystems(ScheduleBuilder builder)
    {
        foreach (Registrations registrations in Live)
        {
            registrations.Systems?.Register(builder);
        }
    }

    /// <summary>
    /// Registers the game's <see cref="ComponentAttribute"/> types, and which
    /// of them scenes save (ADR 0041). Call it before the game's systems
    /// start or a scene loads.
    /// </summary>
    public void RegisterComponents()
    {
        foreach (Registrations registrations in Live)
        {
            registrations.Components?.Register();
        }
    }

    /// <summary>Creates the game's <see cref="ServiceAttribute"/> services into <paramref name="services"/> (ADR 0041).</summary>
    public void RegisterServices(Services services)
    {
        foreach (Registrations registrations in Live)
        {
            registrations.Services?.Register(services);
        }
    }

    private Registrations[] Live =>
        _registrations ?? throw new InvalidOperationException("The game has been unloaded.");

    /// <summary>The project's editor assembly loaded beside the game (ADR 0116), or null.</summary>
    public Assembly? EditorAssembly { get; internal set; }

    /// <summary>The plugins' runtime assemblies the game uses (ADR 0118), in the order they load.</summary>
    public IReadOnlyList<Assembly> Plugins { get; internal set; } = [];

    /// <summary>Its source plugins' editor assemblies, loaded with it (ADR 0118), in the plugins' order.</summary>
    public IReadOnlyList<Assembly> PluginEditorAssemblies { get; internal set; } = [];

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
        _driver = null;
        EditorAssembly = null;
        Plugins = [];
        PluginEditorAssemblies = [];
        _context = null;
        // The saved-component table would otherwise keep the game's types.
        SavedComponents.Forget(context);
        // So would the render schedule's C# steps.
        RenderSchedule.Forget(context);
        context.Unload();
        return weak;
    }
}

// A game assembly's generated registrations; each is null if it has none.
internal sealed record Registrations(
    ISystemRegistration? Systems,
    IComponentRegistration? Components,
    IServiceRegistration? Services);

/// <summary>
/// What <see cref="GameLoader.Load(GameFiles)"/> loads (ADR 0118): the game's
/// assembly, its editor assembly, and its source plugins' assemblies, built
/// with it.
/// </summary>
public sealed record GameFiles(string Game)
{
    public string? Editor { get; init; }

    /// <summary>Source plugins' runtime assemblies, in the order they load.</summary>
    public IReadOnlyList<string> Plugins { get; init; } = [];

    /// <summary>Source plugins' editor assemblies, in the same order.</summary>
    public IReadOnlyList<string> PluginEditors { get; init; } = [];
}

/// <summary>Compiled plugins loaded into a context of their own (ADR 0118).</summary>
public sealed class LoadedPlugins
{
    private AssemblyLoadContext? _context;

    internal LoadedPlugins(AssemblyLoadContext context, IReadOnlyList<Assembly> assemblies, IReadOnlyList<Assembly> editorAssemblies)
    {
        _context = context;
        Assemblies = assemblies;
        EditorAssemblies = editorAssemblies;
    }

    /// <summary>Their runtime assemblies, in the order given.</summary>
    public IReadOnlyList<Assembly> Assemblies { get; private set; }

    /// <summary>Their editor assemblies, for those that have one.</summary>
    public IReadOnlyList<Assembly> EditorAssemblies { get; private set; }

    /// <summary>One of them by name, as a load context resolving a reference asks.</summary>
    public Assembly? Find(AssemblyName name) =>
        _context?.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == name.Name);

    /// <summary>Starts unloading them, as <see cref="LoadedGame.Unload"/> does a game.</summary>
    public WeakReference Unload()
    {
        AssemblyLoadContext context = _context ?? throw new InvalidOperationException("Already unloaded.");
        WeakReference weak = new(context);
        Assemblies = [];
        EditorAssemblies = [];
        _context = null;
        SavedComponents.Forget(context);
        RenderSchedule.Forget(context);
        context.Unload();
        return weak;
    }
}
