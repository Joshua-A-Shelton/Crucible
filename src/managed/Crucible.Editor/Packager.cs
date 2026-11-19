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
/// C++ module, and Data/Game.crpak (and D3D12Core on Windows).
/// </summary>
/// <remarks>
/// The package is made in a folder beside the target and moved into place
/// once whole, so a failed build leaves the last package as it was. A
/// target that isn't empty and has no Data folder isn't a package, and is
/// never replaced.
/// </remarks>
internal sealed class Packager(Project project, GameBuilder builder, NativeBuilder? native, string editorExecutable,
    Func<string, CookResult?> cook, Action<string> log)
{
    public PackageResult Package(string folder)
    {
        List<string> errors = [];
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any() &&
            !Directory.Exists(Path.Combine(folder, "Data")))
        {
            return Failed($"{folder} isn't empty and isn't a package, so it's not replaced");
        }
        bool aot = project.Build.Runtime == PackageRuntime.NativeAot;
        string launcher = StandaloneRun.LauncherPath(editorExecutable);
        string? core = CoreLibrary(launcher);
        if (!aot && (!File.Exists(launcher) || core is null))
        {
            return Failed($"the game launcher or the engine's core isn't beside the editor ({launcher})");
        }

        string staging = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".building";
        DeleteFolder(staging);
        string data = Path.Combine(staging, "Data");
        Directory.CreateDirectory(data);
        try
        {
            // The C++ module first: the C# compiles its bindings (ADR 0074).
            if (native is { HasCode: true })
            {
                log($"Building {native.LibraryName}…");
                GameBuild built = native.BuildNow();
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

            string executable = builder.AssemblyName + (OperatingSystem.IsWindows() ? ".exe" : "");
            if (aot)
            {
                if (LinkAot(Path.Combine(staging, executable)) is { } problems)
                {
                    return Failed(problems);
                }
            }
            else
            {
                log($"Publishing {builder.AssemblyName} with .NET{(project.Build.ReadyToRun ? ", ReadyToRun" : "")}…");
                GameBuild published = builder.Publish(data, project.Build.ReadyToRun);
                if (!published.Succeeded)
                {
                    return Failed([.. published.Errors.Select(error => error.ToString())]);
                }
            }

            if (!aot && native is { HasCode: true })
            {
                File.Copy(native.OutputPath, Path.Combine(data, native.LibraryName + NativeBuilder.LibraryExtension));
            }
            if (!aot)
            {
                File.Copy(launcher, Path.Combine(staging, executable));
                File.Copy(core!, Path.Combine(staging, Path.GetFileName(core!)));
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
        if (native is null || native.ReadToolkit() is not { } toolkit)
        {
            return ["the module toolkit isn't beside the editor (CrucibleModule.json)"];
        }
        log($"Compiling {builder.AssemblyName} with NativeAOT…");
        GameBuild compiled = builder.PublishAot(native.HasCode ? native.LibraryName : null, toolkit.AotInstructionSet);
        if (!compiled.Succeeded)
        {
            return [.. compiled.Errors.Select(error => error.ToString())];
        }
        log($"Linking {Path.GetFileName(executable)}…");
        GameBuild linked = native.LinkAot(builder.AssemblyName, compiled.Assembly!, builder.AotLinkInputs);
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

    // Beside the launcher (where Windows keeps it), else where the engine's
    // build put it, which the module toolkit records (Linux).
    private string? CoreLibrary(string launcher)
    {
        string name = "Crucible.Core" + NativeBuilder.LibraryExtension;
        string beside = Path.Combine(Path.GetDirectoryName(launcher) ?? "", name);
        if (File.Exists(beside))
        {
            return beside;
        }
        string? built = native?.ReadToolkit()?.CoreLibrary;
        return built is not null && Path.GetFileName(built) == name && File.Exists(built) ? built : null;
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
