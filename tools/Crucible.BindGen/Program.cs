using System.Text;

namespace Crucible.BindGen;

/// <summary>
/// crucible-bindgen: generates the C#↔C++ bindings from annotated headers
/// (ADR 0017). Run by CMake before the C++ core and the C# engine build.
/// </summary>
public static class Program
{
    private const string Usage =
        "usage: Crucible.BindGen --libclang <path> --output-cpp <file> --output-cs <file> --depfile <file>\n" +
        "                        [--include-dir <dir>]... --header <file>...";

    public static int Main(string[] args)
    {
        string? libclang = null, outputCpp = null, outputCs = null, depfile = null;
        List<string> headers = [];
        List<string> includeDirectories = [];
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--libclang": libclang = args[i + 1]; break;
                case "--output-cpp": outputCpp = args[i + 1]; break;
                case "--output-cs": outputCs = args[i + 1]; break;
                case "--depfile": depfile = args[i + 1]; break;
                case "--header": headers.Add(args[i + 1]); break;
                case "--include-dir": includeDirectories.Add(args[i + 1]); break;
                default:
                    Console.Error.WriteLine($"crucible-bindgen: unknown argument {args[i]}\n{Usage}");
                    return 2;
            }
        }
        if (libclang is null || outputCpp is null || outputCs is null || depfile is null || headers.Count == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        Libclang.Load(libclang);
        BindingModel? model = Parser.Parse(headers, includeDirectories);
        if (model is null)
        {
            return 1;
        }

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
