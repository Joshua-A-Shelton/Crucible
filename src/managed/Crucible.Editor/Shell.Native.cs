using Crucible.Assets;

namespace Crucible.Editor;

// The project's native module (ADR 0074). Opening a project builds its Native
// folder before its C# (whose bindings come from it) and loads the module
// into the editor; saving a C++ file rebuilds it in the background. Native
// code can't be unloaded safely, so a rebuilt module is used by the next
// standalone run at once and by the editor at its next start.
internal sealed partial class Shell
{
    private NativeBuilder? _native;
    private AssetWatcher? _nativeWatcher;

    public NativeBuilder? NativeBuilder => _native;

    private void OpenProjectNative(Project project)
    {
        _nativeWatcher?.Dispose();
        _nativeWatcher = null;
        _native = new NativeBuilder(project, NativeBuilder.ToolkitBeside(Environment.ProcessPath ?? ""));
        if (Directory.Exists(project.NativeFolder))
        {
            try
            {
                _nativeWatcher = new AssetWatcher(project.NativeFolder);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                Log($"Can't watch {project.NativeFolder} for changes: {exception.Message}");
            }
        }
        if (!_native.HasCode)
        {
            return;
        }
        Log($"Building {_native.LibraryName}…");
        NativeBuilt(_native.BuildNow());
    }

    // Each frame: native changes that settled start a build; a finished one is reported.
    internal void PollNative()
    {
        if (_native is null)
        {
            return;
        }
        if (_nativeWatcher is not null && _nativeWatcher.TakeSettled(out _, out _) && _native.HasCode && _native.Start())
        {
            Status($"Building {_native.LibraryName}…");
        }
        if (_native.TakeFinished() is { } build && NativeBuilt(build))
        {
            // Its bindings may have changed: the game's C# builds again.
            if (_builder is { HasCode: true } builder && builder.Start())
            {
                Status($"Building {builder.AssemblyName}…");
            }
        }
    }

    /// <summary>Builds the project's native module now, and loads it if no module is loaded yet.</summary>
    public bool BuildNative() => _native is { HasCode: true } native && NativeBuilt(native.BuildNow());

    /// <summary>Whether a module was loaded into the editor.</summary>
    public static bool ModuleLoaded => Api.ModuleCount() > 0;

    private bool NativeBuilt(GameBuild build)
    {
        if (!build.Succeeded || build.Assembly is null)
        {
            foreach (BuildError error in build.Errors)
            {
                Log($"Build error: {error}");
            }
            Status($"{_native?.LibraryName} didn't build");
            return false;
        }
        if (ModuleLoaded)
        {
            Log($"Built {_native!.LibraryName}: restart the editor to load it");
            return true;
        }
        // A copy is loaded, so the build can replace the library while the
        // editor holds it (Windows locks a loaded DLL). Its file name stays
        // the module's, which the game's bindings find it by.
        string copy = Path.Combine(_native!.Folder, "Loaded", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Path.GetFileName(build.Assembly));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(build.Assembly, copy, overwrite: true);
            Api.LoadModule(copy);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CrucibleException)
        {
            Log($"Can't load {_native.LibraryName}: {exception.Message}");
            return false;
        }
        Log($"Loaded {_native.LibraryName}");
        return true;
    }
}
