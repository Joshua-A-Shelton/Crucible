using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Crucible.Editor;

// The editor launcher loads the editor as the launcher loads a game, alone
// into its own load context (ADR 0011, 0066), and nothing there finds the
// assemblies it brings with it (the asset pipeline, Crucible.Assets). Until
// the engine does (docs/open-items.md), the editor finds them beside itself,
// as the samples do: loaded from a stream, it has no location, so beside
// the launcher's --game path.
internal static class Dependencies
{
    // The assembly's module initializer is its earliest code, before any
    // system touches the asset pipeline; loading from streams is JIT-only, as
    // the editor is.
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
