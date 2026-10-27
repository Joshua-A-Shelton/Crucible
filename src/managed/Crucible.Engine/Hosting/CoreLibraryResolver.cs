using System.Reflection;
using System.Runtime.InteropServices;
using Crucible.Interop;

namespace Crucible.Hosting;

/// <summary>
/// Tells .NET where the C++ core library is when the launcher hosts the engine
/// (ADR 0011): next to the executable, where shipped games keep it.
/// </summary>
/// <remarks>
/// .NET's own search starts next to the engine assembly, which in development
/// builds isn't where the launcher and core are. On Linux the launcher has
/// already loaded the core, so the search finds it anyway; on Windows the
/// linker drops a DLL import nothing calls yet, so it hasn't. Saying where it
/// is beats relying on that. Under NativeAOT the core is linked in and this
/// resolver is never asked.
/// </remarks>
internal static class CoreLibraryResolver
{
    private static int _registered;

    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(CoreLibraryResolver).Assembly, Resolve);
        }
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Native.CoreLibrary || Environment.ProcessPath is not { } processPath)
        {
            return 0; // .NET's normal search
        }

        string fileName = OperatingSystem.IsWindows() ? $"{libraryName}.dll" : $"lib{libraryName}.so";
        string candidate = Path.Combine(Path.GetDirectoryName(processPath)!, fileName);
        return NativeLibrary.TryLoad(candidate, out nint handle) ? handle : 0;
    }
}
