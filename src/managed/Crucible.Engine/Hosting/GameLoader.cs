using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

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
    /// The assembly is read from a stream, not opened by path, so the file
    /// isn't locked: a rebuild can overwrite it while the old version runs.
    /// Crucible.Engine itself isn't loaded again: the new context falls back
    /// to the default one for it, so the game and the engine share one
    /// <see cref="IGameEntry"/> type.
    /// </remarks>
    [RequiresUnreferencedCode(JitOnly)]
    public static LoadedGame Load(string path)
    {
        AssemblyLoadContext context = new($"Game: {Path.GetFileName(path)}", isCollectible: true);
        Assembly assembly;
        using (FileStream stream = File.OpenRead(path))
        {
            assembly = context.LoadFromStream(stream);
        }

        GameEntryAttribute attribute = assembly.GetCustomAttribute<GameEntryAttribute>() ??
                                       throw new InvalidOperationException($"{path} has no [GameEntry] attribute.");
        IGameEntry entry = (IGameEntry)(Activator.CreateInstance(attribute.EntryType) ??
                                        throw new InvalidOperationException($"Could not create {attribute.EntryType}."));
        return new LoadedGame(context, entry);
    }

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

    internal LoadedGame(AssemblyLoadContext context, IGameEntry entry)
    {
        _context = context;
        _entry = entry;
    }

    public IGameEntry Entry => _entry ?? throw new InvalidOperationException("The game has been unloaded.");

    /// <summary>
    /// Starts unloading and drops this object's references. Pass the returned
    /// weak reference to <see cref="GameLoader.WaitForUnload"/> to confirm.
    /// </summary>
    public WeakReference Unload()
    {
        AssemblyLoadContext context = _context ?? throw new InvalidOperationException("Already unloaded.");
        WeakReference weak = new(context);
        _entry = null;
        _context = null;
        context.Unload();
        return weak;
    }
}
