using Crucible.Assets;

namespace Crucible.Editor;

/// <summary>
/// Makes a project's package (ADR 0100, 0101): a folder a player runs as it
/// is, for the platform the editor runs on. For a JIT game: at its top, the
/// launcher renamed after the game and the engine's core library (on
/// Windows also .NET's nethost and the Agility SDK's D3D12Core); in Data,
/// the game's assembly with its own .NET runtime and the engine's
/// assemblies, ReadyToRun unless the build settings say not, the game's C++
/// module if it has one, and Game.crpak. For a NativeAOT game: one
/// executable holding the launcher, the core, the engine, the game, and its
/// C++ module, and Data/Game.crpak (and D3D12Core on Windows). A Shipping
/// package (ADR 0103) is made from the shipping preset's build, its game
/// compiled with information messages out; a Development one from the
/// editor's own build.
/// </summary>
/// <remarks>
/// The package is made in a folder beside the target and moved into place
/// once whole, so a failed build leaves the last package as it was. A
/// target that isn't empty and has no Data folder isn't a package, and is
/// never replaced.
/// </remarks>
internal sealed class Packager(Project project, GameBuilder editorBuilder, NativeBuilder? editorNative,
    Func<string, CookResult?> cook, Action<string> log)
{
    // The builders and the build they package from: the editor's, or the
    // shipping preset's for a Shipping package.
    private GameBuilder _builder = editorBuilder;
    private NativeBuilder? _native = editorNative;

    public PackageResult Package(string folder)
    {
        List<string> errors = [];
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() &&
            !Directory.Exists(Path.Combine(folder, "Data")))
        {
            return Failed($"{folder} isn't empty and isn't a package, so it's not replaced");
        }
        if (_native?.ReadToolkit() is not { } editorToolkit)
        {
            return Failed("the module toolkit isn't beside the editor (CrucibleModule.json): the engine's build writes it");
        }
        ModuleToolkit toolkit = editorToolkit;
        if (project.Build.Configuration == PackageConfiguration.Shipping)
        {
            string shipping = editorToolkit.ShippingToolkit;
            _native = new NativeBuilder(project, shipping, "Shipping");
            if (!File.Exists(shipping) || _native.ReadToolkit() is not { Shipping: true } shippingToolkit)
            {
                string preset = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(shipping))))!;
                return Failed($"a Shipping package comes from the shipping preset's build, which isn't at {preset}: " +
                              "build it (cmake --preset shipping, then cmake --build --preset shipping), or choose Development");
            }
            toolkit = shippingToolkit;
            _builder = new GameBuilder(project, Path.Combine(toolkit.EngineDirectory, "Crucible.Engine.dll"));
        }
        bool aot = project.Build.Runtime == PackageRuntime.NativeAot;
        string launcher = toolkit.Launcher;
        string core = toolkit.CoreRuntime;
        if (!aot && (!File.Exists(launcher) || !File.Exists(core)))
        {
            return Failed($"the game launcher or the engine's core isn't where its build put it ({launcher}, {core})");
        }

        string staging = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".building";
        DeleteFolder(staging);
        string data = Path.Combine(staging, "Data");
        Directory.CreateDirectory(data);
        try
        {
            // The C++ module first: the C# compiles its bindings (ADR 0074).
            if (_native is { HasCode: true })
            {
                log($"Building {_native.LibraryName}…");
                GameBuild built = _native.BuildNow();
                if (!built.Succeeded)
                {
                    return Failed([.. built.Errors.Select(error => error.ToString())]);
                }
            }

            CookResult? cooked = cook(data);
            if (cooked is not { Succeeded: true })
            {
                return Failed([.. cooked?.Errors ?? ["the cook failed"]]);
            }

            string executable = _builder.AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : "");
            if (aot)
            {
                if (LinkAot(Path.Combine(staging, executable)) is { } problems)
                {
                    return Failed(problems);
                }
            }
            else
            {
                log($"Publishing {_builder.AssemblyName} with .NET{(project.Build.ReadyToRun ? ", ReadyToRun" : "")}…");
                GameBuild published = _builder.Publish(data, project.Build.ReadyToRun);
                if (!published.Succeeded)
                {
                    return Failed([.. published.Errors.Select(error => error.ToString())]);
                }
            }

            if (!aot && _native is { HasCode: true })
            {
                File.Copy(_native.OutputPath, Path.Combine(data, _native.LibraryName + NativeBuilder.LibraryExtension));
            }
            if (!aot)
            {
                File.Copy(launcher, Path.Combine(staging, executable));
                File.Copy(core, Path.Combine(staging, Path.GetFileName(core)));
            }
            if (OperatingSystem.IsWindows())
            {
                UseWindowsSubsystem(Path.Combine(staging, executable));
            }
            if (OperatingSystem.IsWindows())
            {
                // The JIT launcher's nethost, and the Agility SDK's runtime,
                // which D3D12 loads from D3D12 beside the executable; its
                // debug layer stays out of packages.
                string beside = Path.GetDirectoryName(launcher)!;
                if (!aot)
                {
                    File.Copy(Path.Combine(beside, "nethost.dll"), Path.Combine(staging, "nethost.dll"));
                }
                string agility = Path.Combine(beside, "D3D12", "D3D12Core.dll");
                if (File.Exists(agility))
                {
                    Directory.CreateDirectory(Path.Combine(staging, "D3D12"));
                    File.Copy(agility, Path.Combine(staging, "D3D12", "D3D12Core.dll"));
                }
            }

            long size = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
            DeleteFolder(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
            Directory.Move(staging, folder);
            return new PackageResult(true, folder, [], cooked, size);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failed(exception.Message);
        }
        finally
        {
            DeleteFolder(staging);
        }

        PackageResult Failed(params string[] problems)
        {
            errors.AddRange(problems);
            return new PackageResult(false, folder, errors, null, 0);
        }
    }

    // NativeAOT (ADR 0101): the game and engine compiled into a static
    // library, then linked with the launcher, the core, and the game's C++
    // module into the executable, copied to `executable` without its debug
    // information, which stays in the Cache. The problems, if it failed.
    private string[]? LinkAot(string executable)
    {
        if (_native is null || _native.ReadToolkit() is not { } toolkit)
        {
            return ["the module toolkit isn't beside the editor (CrucibleModule.json)"];
        }
        log($"Compiling {_builder.AssemblyName} with NativeAOT…");
        GameBuild compiled = _builder.PublishAot(_native.HasCode ? _native.LibraryName : null, toolkit.AotInstructionSet);
        if (!compiled.Succeeded)
        {
            return [.. compiled.Errors.Select(error => error.ToString())];
        }
        log($"Linking {Path.GetFileName(executable)}…");
        GameBuild linked = _native.LinkAot(_builder.AssemblyName, compiled.Assembly!, _builder.AotLinkInputs);
        if (!linked.Succeeded)
        {
            return [.. linked.Errors.Select(error => error.ToString())];
        }
        File.Copy(linked.Assembly!, executable);
        return null;
    }

    // A game opens no console window (ADR 0100): the launcher is linked as a
    // console program, which suits the editor's runs, so a package's copy is
    // marked a GUI one, the PE header's Subsystem field, as editbin
    // /SUBSYSTEM:WINDOWS would. Its entry point stays the same, and output
    // still reaches whatever a parent redirects it to.
    internal static void UseWindowsSubsystem(string executable)
    {
        const ushort windowsGui = 2;
        using FileStream file = new(executable, FileMode.Open, FileAccess.ReadWrite);
        Span<byte> header = stackalloc byte[4];
        file.Position = 0x3C; // e_lfanew: where the PE header starts
        file.ReadExactly(header);
        // "PE\0\0", the file header (20 bytes), then the optional header,
        // whose Subsystem is at 68 in PE32 and PE32+ alike.
        long start = BitConverter.ToUInt32(header);
        file.Position = start;
        file.ReadExactly(header);
        if (!header.SequenceEqual("PE\0\0"u8))
        {
            throw new IOException($"{executable} isn't a Windows executable");
        }
        file.Position = start + 4 + 20 + 68;
        BitConverter.TryWriteBytes(header, windowsGui);
        file.Write(header[..2]);
    }

    private static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}

/// <summary>A finished build: whether it succeeded, where, its problems, its cook, and the package's size in bytes.</summary>
public sealed record PackageResult(bool Succeeded, string Folder, IReadOnlyList<string> Errors, CookResult? Cook, long Size);
