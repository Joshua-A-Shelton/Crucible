using System.Text;

namespace Crucible.BindGen;

/// <summary>
/// crucible-bindgen: generates the C#↔C++ bindings from annotated headers
/// (ADR 0017). Run by CMake before the C++ core and the C# engine build.
/// </summary>
public static class Program
{
    private const string Usage =
        "usage: Crucible.BindGen --libclang <path> --clang <path> --output-cpp <file> --output-cs <file>\n" +
        "                        --depfile <file>\n" +
        "                        [--include-dir <dir>]... --header <file>... [--extern-header <file>]...\n" +
        "                        [--library <name>] [--interop-namespace <namespace>]\n" +
        "                        [--export-macro <macro>] [--export-header <include path>]";

    public static int Main(string[] args)
    {
        string? libclang = null, clang = null, outputCpp = null, outputCs = null, depfile = null;
        List<string> headers = [];
        List<string> includeDirectories = [];
        List<string> externHeaders = [];
        string? library = null, interopNamespace = null, exportMacro = null, exportHeader = null;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--libclang": libclang = args[i + 1]; break;
                case "--clang": clang = args[i + 1]; break;
                case "--output-cpp": outputCpp = args[i + 1]; break;
                case "--output-cs": outputCs = args[i + 1]; break;
                case "--depfile": depfile = args[i + 1]; break;
                case "--header": headers.Add(args[i + 1]); break;
                case "--include-dir": includeDirectories.Add(args[i + 1]); break;
                case "--extern-header": externHeaders.Add(args[i + 1]); break;
                case "--library": library = args[i + 1]; break;
                case "--interop-namespace": interopNamespace = args[i + 1]; break;
                case "--export-macro": exportMacro = args[i + 1]; break;
                case "--export-header": exportHeader = args[i + 1]; break;
                default:
                    Console.Error.WriteLine($"crucible-bindgen: unknown argument {args[i]}\n{Usage}");
                    return 2;
            }
        }
        if (libclang is null || clang is null || outputCpp is null || outputCs is null || depfile is null ||
            headers.Count == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        Libclang.Load(libclang);
        BindingModel? model = Parser.Parse(clang, headers, includeDirectories, externHeaders);
        if (model is null)
        {
            return 1;
        }
        model.Library = library ?? model.Library;
        model.InteropNamespace = interopNamespace ?? model.InteropNamespace;
        model.ExportMacro = exportMacro ?? model.ExportMacro;
        model.ExportHeader = exportHeader ?? model.ExportHeader;

        WriteFile(outputCpp, CppWriter.Write(model));
        WriteFile(outputCs, CSharpWriter.Write(model));
        WriteFile(depfile, Depfile(outputCpp, model.Dependencies));
        return 0;
    }

    private static void WriteFile(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, contents.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    // A Make-style depfile listing every file clang read, so the build reruns
    // the generator when any of them changes, including headers that exposed
    // headers include.
    private static string Depfile(string output, IEnumerable<string> dependencies)
    {
        static string Escape(string path) => path.Replace('\\', '/').Replace(" ", "\\ ");
        return $"{Escape(Path.GetFullPath(output))}:" +
               string.Concat(dependencies.Select(dependency => $" \\\n  {Escape(dependency)}")) + "\n";
    }
}
