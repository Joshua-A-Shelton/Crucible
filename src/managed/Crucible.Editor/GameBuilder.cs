using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Crucible.Assets;

namespace Crucible.Editor;

/// <summary>
/// Builds a project's own code (ADR 0072): every .cs file under its Code
/// folder, through a project file the editor writes (Cache/Build/&lt;Project&gt;.csproj:
/// the engine referenced, not copied; the engine's source generators; the
/// engine's AOT-safe settings), with <c>dotnet build</c> run in the
/// background. The build's errors come back with their file, line, and column.
/// </summary>
public sealed partial class GameBuilder
{
    private readonly Project _project;
    private Task<GameBuild>? _running;

    public GameBuilder(Project project) => _project = project;

    /// <summary>The project's game assembly's name, the project's own: "Diorama" (ADR 0100).</summary>
    public string AssemblyName => Sanitize(_project.Name);

    public string ProjectFile => Path.Combine(_project.CacheFolder, "Build", AssemblyName + ".csproj");

    /// <summary>Where the built assembly lands.</summary>
    public string OutputPath => Path.Combine(_project.CacheFolder, "Build", "bin", AssemblyName + ".dll");

    /// <summary>Whether the project has code to build (a Code folder with .cs files in it).</summary>
    public bool HasCode =>
        Directory.Exists(_project.CodeFolder) &&
        Directory.EnumerateFiles(_project.CodeFolder, "*.cs", SearchOption.AllDirectories).Any();

    public bool IsBuilding => _running is { IsCompleted: false };

    /// <summary>Starts a build in the background; false if one is already running.</summary>
    public bool Start()
    {
        if (IsBuilding)
        {
            return false;
        }
        WriteProject();
        _running = Task.Run(RunBuild);
        return true;
    }

    /// <summary>The finished build, once; null while it's running or if none was started.</summary>
    public GameBuild? TakeFinished()
    {
        if (_running is not { IsCompleted: true } done)
        {
            return null;
        }
        _running = null;
        return done.IsFaulted
            ? new GameBuild(false, null, [new BuildError("", 0, 0, done.Exception?.GetBaseException().Message ?? "the build failed")], "")
            : done.Result;
    }

    /// <summary>Builds now, waiting for it (opening a project, before its scenes load).</summary>
    public GameBuild BuildNow()
    {
        _running?.Wait();
        _running = null;
        WriteProject();
        return RunBuild();
    }

    /// <summary>The project file a package is published from (ADR 0100): Cache/Build/Package/&lt;Project&gt;.csproj.</summary>
    public string PackageProjectFile => Path.Combine(_project.CacheFolder, "Build", "Package", AssemblyName + ".csproj");

    /// <summary>
    /// The project file's text: the Code folder's sources, the engine, and its
    /// generators. For a package (<paramref name="package"/>), also the
    /// runtime for this platform, copied in with the engine, and ReadyToRun
    /// unless <paramref name="readyToRun"/> is false (ADR 0100).
    /// </summary>
    public string ProjectText(bool package = false, bool readyToRun = true)
    {
        string engine = EngineAssembly();
        string framework = typeof(Crucible.Ecs.World).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName
            is { } name && name.Contains("Version=v", StringComparison.Ordinal)
            ? "net" + name[(name.IndexOf("Version=v", StringComparison.Ordinal) + 9)..]
            : "net10.0";
        StringBuilder text = new();
        text.Append("""
            <Project Sdk="Microsoft.NET.Sdk">
              <!-- Written by the Crucible editor (ADR 0072); rewritten before every build. -->
              <PropertyGroup>

            """);
        text.Append($"    <TargetFramework>{framework}</TargetFramework>\n");
        text.Append($"    <AssemblyName>{AssemblyName}</AssemblyName>\n");
        text.Append($"    <RootNamespace>{Sanitize(_project.Name)}</RootNamespace>\n");
        text.Append("""
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <IsAotCompatible>true</IsAotCompatible>
                <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
                <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <OutputPath>bin/</OutputPath>
                <GenerateDocumentationFile>false</GenerateDocumentationFile>

            """);
        if (package)
        {
            // The game's assembly is the app its runtime starts for: its
            // runtimeconfig.json and deps.json name the runtime, copied in
            // (self-contained), and every assembly (ADR 0100).
            text.Append($"    <RuntimeIdentifier>{RuntimeIdentifier}</RuntimeIdentifier>\n");
            text.Append($"    <PublishReadyToRun>{(readyToRun ? "true" : "false")}</PublishReadyToRun>\n");
            text.Append("""
                    <SelfContained>true</SelfContained>
                    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
                    <UseAppHost>false</UseAppHost>
                    <Optimize>true</Optimize>

                """);
        }
        text.Append("""
              </PropertyGroup>
              <ItemGroup>

            """);
        text.Append($"    <Compile Include=\"{Escape(Path.Combine(_project.CodeFolder, "**", "*.cs"))}\" />\n");
        // The native module's bindings (ADR 0074), when it exposes anything.
        string bindings = Escape(Path.Combine(_project.CacheFolder, "Build", "Native", "Bindings.g.cs"));
        text.Append($"    <Compile Include=\"{bindings}\" Condition=\"Exists('{bindings}')\" />\n");
        // Referenced, not copied, except into a package.
        text.Append($"    <Reference Include=\"Crucible.Engine\" HintPath=\"{Escape(engine)}\" Private=\"{(package ? "true" : "false")}\" />\n");
        if (Generators() is { } generators)
        {
            text.Append($"    <Analyzer Include=\"{Escape(generators)}\" />\n");
        }
        text.Append("""
              </ItemGroup>
            </Project>

            """);
        return text.ToString();
    }

    private void WriteProject()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ProjectFile)!);
        string text = ProjectText();
        // Unchanged, it's left alone, so the build stays incremental.
        if (!File.Exists(ProjectFile) || File.ReadAllText(ProjectFile) != text)
        {
            File.WriteAllText(ProjectFile, text);
        }
    }

    /// <summary>"linux-x64" or "win-x64": the only platform a package is made for is this one.</summary>
    public static string RuntimeIdentifier => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    /// <summary>
    /// Publishes the game into <paramref name="folder"/> with its own .NET
    /// runtime and the engine's assemblies (ADR 0100), waiting for it. The
    /// first publish downloads the runtime and ReadyToRun compiler from NuGet.
    /// </summary>
    public GameBuild Publish(string folder, bool readyToRun)
    {
        _running?.Wait();
        _running = null;
        Directory.CreateDirectory(Path.GetDirectoryName(PackageProjectFile)!);
        File.WriteAllText(PackageProjectFile, ProjectText(package: true, readyToRun));
        string assembly = Path.Combine(folder, AssemblyName + ".dll");
        return RunDotnet(["publish", PackageProjectFile, "-c", "Release", "-o", folder], assembly);
    }

    private GameBuild RunBuild() => RunDotnet(["build", ProjectFile], OutputPath);

    private static GameBuild RunDotnet(string[] command, string result)
    {
        if (DotnetPath() is not { } dotnet)
        {
            return new GameBuild(false, null, [new BuildError("", 0, 0, "no dotnet found to build with (set DOTNET_ROOT)")], "");
        }
        ProcessStartInfo start = new(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Outside any repository the project sits in: a global.json there
            // could ask for an SDK the dotnet the editor runs on doesn't have.
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (string argument in (string[])[
                     .. command, "-nologo", "-v:q", "-clp:NoSummary",
                     // Not the settings of a repository the project happens to sit in.
                     "-p:ImportDirectoryBuildProps=false", "-p:ImportDirectoryBuildTargets=false",
                     "-p:ImportDirectoryPackagesProps=false"])
        {
            start.ArgumentList.Add(argument);
        }
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet didn't start");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        string all = output + error.Result;
        List<BuildError> errors = [.. Errors(all)];
        bool built = process.ExitCode == 0 && File.Exists(result);
        if (!built && errors.Count == 0)
        {
            string first = all.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
            errors.Add(new BuildError("", 0, 0, $"dotnet {command[0]} failed ({process.ExitCode}): {first}"));
        }
        return new GameBuild(built, built ? result : null, errors, all);
    }

    // "path(12,5): error CS1002: ; expected [project]"
    [GeneratedRegex(@"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\): error (?<code>\w+): (?<message>.*?)( \[[^\]]*\])?$", RegexOptions.Multiline)]
    private static partial Regex ErrorLine();

    internal static IEnumerable<BuildError> Errors(string output) =>
        ErrorLine().Matches(output).Select(match => new BuildError(match.Groups["file"].Value.Trim(),
                int.Parse(match.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture),
                $"{match.Groups["code"].Value}: {match.Groups["message"].Value.Trim()}"))
            .Distinct();

    // The dotnet the editor runs on, if its SDK is there; else DOTNET_ROOT, then PATH.
    private static string? DotnetPath()
    {
        string exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        List<string> roots = [];
        // .../dotnet/shared/Microsoft.NETCore.App/<version>/ → .../dotnet
        string runtime = RuntimeEnvironment.GetRuntimeDirectory();
        if (Directory.GetParent(runtime.TrimEnd(Path.DirectorySeparatorChar))?.Parent?.Parent is { } root)
        {
            roots.Add(root.FullName);
        }
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            roots.Add(dotnetRoot);
        }
        roots.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
        return roots.Select(folder => Path.Combine(folder, exe)).FirstOrDefault(File.Exists);
    }

    // The engine's source generators: beside the engine (shipped), or in the build's artifacts.
    private static string? Generators()
    {
        string engineDir = Path.GetDirectoryName(EngineAssembly())!;
        string beside = Path.Combine(engineDir, "Crucible.Generators.dll");
        if (File.Exists(beside))
        {
            return beside;
        }
        // artifacts/bin/Crucible.Engine/<config> → artifacts/bin/Crucible.Generators/<config>
        string config = Path.GetFileName(engineDir);
        string built = Path.Combine(engineDir, "..", "..", "Crucible.Generators", config, "Crucible.Generators.dll");
        return File.Exists(built) ? Path.GetFullPath(built) : null;
    }

    // The editor runs JIT only (ADR 0016), never as a single-file app.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000",
        Justification = "The editor is never published as a single file")]
    internal static string EngineAssembly() => typeof(Crucible.Ecs.World).Assembly.Location;

    // An identifier, and not a name already taken in a package's Data folder
    // (ADR 0100): the engine's core (Crucible.Core) or the runtime's own
    // assemblies (System.dll, mscorlib.dll).
    internal static string Sanitize(string name)
    {
        string kept = new([.. name.Where(c => char.IsLetterOrDigit(c) || c == '_')]);
        bool taken = kept.Equals("Crucible", StringComparison.OrdinalIgnoreCase) ||
                     File.Exists(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), kept + ".dll"));
        return kept.Length == 0 || char.IsDigit(kept[0]) || taken ? "Game" + kept : kept;
    }

    private static string Escape(string path) => System.Security.SecurityElement.Escape(path.Replace('\\', '/'))!;
}

/// <summary>An error from building a project's code: its file, line, and column (0 when it has none), and the message.</summary>
public sealed record BuildError(string File, int Line, int Column, string Message)
{
    // The column too: errors on one line would otherwise read as repeats.
    public override string ToString() => File.Length == 0 ? Message : $"{File}({Line},{Column}): {Message}";
}

/// <summary>A finished build: whether it succeeded, the assembly, its errors, and dotnet's whole output.</summary>
public sealed record GameBuild(bool Succeeded, string? Assembly, IReadOnlyList<BuildError> Errors, string Output);
