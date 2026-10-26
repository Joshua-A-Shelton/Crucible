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
    [UnmanagedCallersOnly]
    public static int Run()
    {
        // An exception escaping into native code crashes the process with no
        // useful message, so nothing may leave this method (ADR 0030).
        try
        {
            Console.WriteLine($"Crucible.Hosting: {EngineInfo.Name} running on .NET {Environment.Version}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Crucible.Hosting: unhandled exception: {exception}");
            return 1;
        }
    }
}
