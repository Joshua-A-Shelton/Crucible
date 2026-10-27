using System.Runtime.InteropServices;
using System.Text;
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

    // No window: the frames render into an offscreen surface of the default
    // window's size, so a headless run still exercises RenderSubmit on the GPU.
    private static int RunHeadless(LaunchOptions options)
    {
        SurfaceHandle surface = Render.Api.CreateOffscreenSurface(1280, 720);
        try
        {
            return RunLoop(options, default, surface);
        }
        finally
        {
            Render.Api.DestroySurface(surface);
        }
    }

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
                return RunLoop(options, window, surface);
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

    private static int RunLoop(LaunchOptions options, WindowHandle window, SurfaceHandle surface)
    {
        string? latencyLog = LatencyLogPath();
        FrameContext context = new()
        {
            Window = window,
            Surface = surface,
            Pacing = !options.NoPacing,
            Latency = latencyLog is null
                ? null
                : new LatencyRecorder
                {
                    // Offscreen frames are never presented.
                    RecordsPresents = window.Value != 0 && !options.NoPacing && Render.Api.PacingWaitsForPresent(),
                },
        };

        if (context.Latency is not null)
        {
            StartLatencyHarness(surface);
        }
        ulong frames;
        try
        {
            frames = FrameLoop.CreateDefault().Run(context, options.FrameLimit);
        }
        finally
        {
            // GPU timing stays on until Finish has collected the last frames'
            // times; DestroySurface stops it.
            if (context.Latency is not null)
            {
                Platform.Api.StopLatencyMarkers();
            }
        }

        if (context.Latency is not null && latencyLog is not null)
        {
            Console.Write(context.Latency.Finish(surface, latencyLog));
        }
        if (context.Failure != Status.Ok)
        {
            Console.Error.WriteLine(
                $"Crucible.Hosting: {context.FailedPhase} failed with {context.Failure} on frame {context.FrameNumber}");
            return 1;
        }
        Console.WriteLine($"Crucible.Hosting: ran {frames} frames");
        return 0;
    }

    private static void StartLatencyHarness(SurfaceHandle surface)
    {
        Status status = Platform.Api.StartLatencyMarkers(LatencyRecorder.MarkerIntervalUs);
        if (status < 0)
        {
            throw new CrucibleException(status, "crucible::platform::startLatencyMarkers");
        }
        if (surface.Value != 0)
        {
            status = Render.Api.SetGpuTiming(surface, true);
            if (status < 0)
            {
                throw new CrucibleException(status, "crucible::render::setGpuTiming");
            }
        }
    }

    // --latency <file>, stored by the launcher (crucible/Launch.h); null when off.
    private static string? LatencyLogPath()
    {
        Span<byte> buffer = stackalloc byte[512];
        int length = (int)Api.LatencyLogPath(buffer);
        if (length > buffer.Length)
        {
            buffer = new byte[length];
            Api.LatencyLogPath(buffer);
        }
        return length == 0 ? null : Encoding.UTF8.GetString(buffer[..length]);
    }
}
