using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Crucible.Samples.UiShowcase;

// The launcher loads a game alone into its own load context (ADR 0011), and
// nothing there finds the assemblies a game brings with it (here the asset
// pipeline, Crucible.Assets). Until the engine does (docs/open-items.md),
// the sample finds them beside itself: loaded from a stream, it has no
// location, so beside the launcher's --game path. They load into the
// game's context, so they unload with it.
internal static class Dependencies
{
    // A game assembly's module initializer is its earliest code, before any
    // system touches the asset pipeline; loading from streams is JIT-only, as
    // loading the game is.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Games load only under the JIT (ADR 0011).")]
    internal static void FindBesideTheGame()
    {
        Assembly game = typeof(Dependencies).Assembly;
        if (AssemblyLoadContext.GetLoadContext(game) is not { } context || context == AssemblyLoadContext.Default)
        {
            return;
        }
        string folder = Path.GetDirectoryName(Path.GetFullPath(Crucible.Api.GamePath()))!;
        context.Resolving += (resolving, name) =>
        {
            string path = Path.Combine(folder, $"{name.Name}.dll");
            if (!File.Exists(path))
            {
                return null;
            }
            using FileStream stream = File.OpenRead(path);
            return resolving.LoadFromStream(stream);
        };
    }
}
