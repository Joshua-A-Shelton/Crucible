using System.Runtime.InteropServices;

namespace Crucible.Hosting;

/// <summary>
/// The engine's single managed entry point, called by the native launcher
/// (ADR 0011).
/// </summary>
public static class Host
{
    /// <summary>
    /// Runs the engine and returns the process exit code. In M1 it only runs
    /// a self-check; later it drives the frame loop until quit.
    /// </summary>
    // JIT hosting finds this method by name; in a NativeAOT build it is
    // exported as crucible_engine_run for the AOT launcher to call.
    [UnmanagedCallersOnly(EntryPoint = "crucible_engine_run")]
    public static int Run()
    {
        // An exception escaping into native code crashes the process with no
        // useful message, so nothing may leave this method (ADR 0030).
        try
        {
#if DEBUG
            // ADR 0017's startup check: every C# struct mirror must match the
            // C++ layout, or shared native memory would be misread.
            if (Interop.LayoutChecks.Verify() is { } mismatch)
            {
                Console.Error.WriteLine($"Crucible.Hosting: C#/C++ layout mismatch: {mismatch}");
                return 1;
            }
#endif
            // Calls back into the C++ core, so a launcher run exercises both
            // directions of the boundary.
            bool hasRenderBackend = EngineInfo.HasRenderBackend;
            Console.WriteLine($"Crucible.Hosting: {EngineInfo.Name} running on .NET {Environment.Version}, " +
                              $"render backend: {(hasRenderBackend ? "yes" : "no")}");
            return hasRenderBackend ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Crucible.Hosting: unhandled exception: {exception}");
            return 1;
        }
    }
}
