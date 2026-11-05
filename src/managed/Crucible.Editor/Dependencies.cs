using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
            Assembly loaded = resolving.LoadFromStream(stream);
            if (name.Name == "Crucible.Assets")
            {
                NativeLibrary.SetDllImportResolver(loaded, FindImporters);
            }
            return loaded;
        };
    }

    // The asset pipeline's native library, beside the editor's executable
    // (the build copies it there, ADR 0067); else .NET's own search.
    private static nint FindImporters(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != "crucible_importers" || Environment.ProcessPath is not { } processPath)
        {
            return 0;
        }
        string fileName = OperatingSystem.IsWindows() ? $"{libraryName}.dll" : $"lib{libraryName}.so";
        return NativeLibrary.TryLoad(Path.Combine(Path.GetDirectoryName(processPath)!, fileName), out nint handle)
            ? handle
            : 0;
    }
}
