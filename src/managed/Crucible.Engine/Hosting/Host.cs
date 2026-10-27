using System.Runtime.InteropServices;
using Crucible.Loop;
using Crucible.Platform;
using Crucible.Render;

namespace Crucible.Hosting;

/// <summary>
/// The engine's single managed entry point, called by the native launcher
/// (ADR 0011).
/// </summary>
public static class Host
{
    /// <summary>
    /// Runs the engine until quit and returns the process exit code. The
    /// launcher has started the platform and the renderer, and stops them
    /// after this returns (ADR 0011).
    /// </summary>
    // JIT hosting finds this method by name; in a NativeAOT build it is
    // exported as crucible_engine_run for the AOT launcher to call.
    [UnmanagedCallersOnly(EntryPoint = "crucible_engine_run")]
    public static int Run(LaunchOptions options)
    {
        // An exception escaping into native code crashes the process with no
        // useful message, so nothing may leave this method (ADR 0030).
        try
        {
            CoreLibraryResolver.Register();
#if DEBUG
            // ADR 0017's startup check: every C# struct mirror must match the
            // C++ layout, or shared native memory would be misread.
            if (Interop.LayoutChecks.Verify() is { } mismatch)
            {
                Console.Error.WriteLine($"Crucible.Hosting: C#/C++ layout mismatch: {mismatch}");
                return 1;
            }
#endif
            Console.WriteLine($"Crucible.Hosting: {EngineInfo.Name} running on .NET {Environment.Version}" +
                              (options.Headless ? ", headless" : ""));
            return options.Headless ? RunHeadless(options) : RunWindowed(options);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Crucible.Hosting: unhandled exception: {exception}");
            return 1;
        }
    }

    private static int RunHeadless(LaunchOptions options) => RunLoop(new FrameContext(), options);

    private static int RunWindowed(LaunchOptions options)
    {
        // Window and surface creation failing is unexpected, so they throw
        // (ADR 0030); Run reports it.
        WindowHandle window = Platform.Api.CreateWindow(1280, 720, EngineInfo.Name);
        try
        {
            SurfaceHandle surface = Render.Api.CreateSurface(window);
            try
            {
                return RunLoop(new FrameContext { Window = window, Surface = surface }, options);
            }
            finally
            {
                Render.Api.DestroySurface(surface);
            }
        }
        finally
        {
            Platform.Api.DestroyWindow(window);
        }
    }

    private static int RunLoop(FrameContext context, LaunchOptions options)
    {
        ulong frames = FrameLoop.CreateDefault().Run(context, options.FrameLimit);
        if (context.Failure != Status.Ok)
        {
            Console.Error.WriteLine(
                $"Crucible.Hosting: {context.FailedPhase} failed with {context.Failure} on frame {context.FrameNumber}");
            return 1;
        }
        Console.WriteLine($"Crucible.Hosting: ran {frames} frames");
        return 0;
    }
}
