using System.Runtime.InteropServices;

namespace Crucible.BindGen;

/// <summary>
/// Parses the headers with libclang and collects every declaration annotated
/// with CRUCIBLE_EXPOSE, enforcing ADR 0017's exposure rules: only C-shaped
/// signatures and plain-data structs cross the boundary. Anything else is an
/// error with its source location, never silently skipped.
/// </summary>
internal sealed class Parser
{
    private const string ExposeAnnotation = "crucible_expose";

    private readonly HashSet<string> _headerFiles;
    private readonly Dictionary<string, ExposedStruct> _structsByName = [];
    private readonly List<string> _errors = [];

    private Parser(IEnumerable<string> headers)
    {
        _headerFiles = headers.Select(Path.GetFullPath).ToHashSet();
    }

    public static BindingModel? Parse(string clang, IReadOnlyList<string> headers,
        IReadOnlyList<string> includeDirectories)
    {
        Parser parser = new(headers);
        BindingModel model = new();
        foreach (string header in headers)
        {
            model.Headers.Add(IncludeName(header, includeDirectories));
        }

        nint index = Libclang.CreateIndex(0, 0);
        nint translationUnit = ParseHeaders(index, clang, headers, includeDirectories);
        try
        {
            if (translationUnit == 0 || !ReportDiagnostics(translationUnit))
            {
                return null;
            }

            CXCursor root = Libclang.GetTranslationUnitCursor(translationUnit);
            // Structs first, so functions can refer to them in any order.
            parser.Walk(root, "", model, structs: true);
            parser.Walk(root, "", model, structs: false);

            model.Dependencies.AddRange(Libclang.Inclusions(translationUnit)
                .Where(file => file != SyntheticSource)
                .Select(RealPath.Resolve)
                .Distinct()
                .Order(StringComparer.Ordinal));
        }
        finally
        {
            if (translationUnit != 0)
            {
                Libclang.DisposeTranslationUnit(translationUnit);
            }
            Libclang.DisposeIndex(index);
        }

        foreach (string error in parser._errors)
        {
            Console.Error.WriteLine(error);
        }
        return parser._errors.Count == 0 ? model : null;
    }

    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    // The headers are parsed together, through one unsaved file that includes them.
    private const string SyntheticSource = "crucible_bindgen_input.cpp";

    private static unsafe nint ParseHeaders(nint index, string clang, IReadOnlyList<string> headers,
        IReadOnlyList<string> includeDirectories)
    {
        // A full command line, as if running the system clang on the synthetic
        // source: argv[0] lets libclang's driver find Clang's built-in headers
        // and the platform's C++ headers the way the compiler does.
        List<string> arguments = [clang, "-x", "c++", "-std=c++20", "-DCRUCIBLE_BINDGEN=1"];
        arguments.AddRange(includeDirectories.Select(directory => $"-I{directory}"));
        arguments.Add(SyntheticSource);
        string source = string.Concat(headers.Select(header => $"#include \"{Path.GetFullPath(header)}\"\n"));

        List<nint> allocations = [];
        nint Utf8(string text)
        {
            nint pointer = Marshal.StringToCoTaskMemUTF8(text);
            allocations.Add(pointer);
            return pointer;
        }

        try
        {
            nint* argumentPointers = stackalloc nint[arguments.Count];
            for (int i = 0; i < arguments.Count; ++i)
            {
                argumentPointers[i] = Utf8(arguments[i]);
            }
            CXUnsavedFile unsaved = new()
            {
                Filename = Utf8(SyntheticSource),
                Contents = Utf8(source),
                Length = new CULong((nuint)System.Text.Encoding.UTF8.GetByteCount(source)),
            };

            nint translationUnit = 0;
            // The source file is named in the command line, so no separate name.
            int error = Libclang.ParseTranslationUnit2FullArgv(index, null, (byte**)argumentPointers,
                arguments.Count, &unsaved, 1, Libclang.SkipFunctionBodies, &translationUnit);
            if (error != 0)
            {
                Console.Error.WriteLine($"crucible-bindgen: libclang could not parse the headers (error {error})");
                return 0;
            }
            return translationUnit;
        }
        finally
        {
            allocations.ForEach(Marshal.FreeCoTaskMem);
        }
    }

    /// <summary>Prints compiler diagnostics; returns false if any is an error.</summary>
    private static bool ReportDiagnostics(nint translationUnit)
    {
        bool ok = true;
        uint options = Libclang.DefaultDiagnosticDisplayOptions();
        for (uint i = 0; i < Libclang.GetNumDiagnostics(translationUnit); ++i)
        {
            nint diagnostic = Libclang.GetDiagnostic(translationUnit, i);
            if (Libclang.GetDiagnosticSeverity(diagnostic) >= Libclang.DiagnosticError)
            {
                Console.Error.WriteLine(Libclang.Take(Libclang.FormatDiagnostic(diagnostic, options)));
                ok = false;
            }
            Libclang.DisposeDiagnostic(diagnostic);
        }
        return ok;
    }

    private static string IncludeName(string header, IReadOnlyList<string> includeDirectories)
    {
        string full = Path.GetFullPath(header);
        foreach (string directory in includeDirectories)
        {
            string relative = Path.GetRelativePath(Path.GetFullPath(directory), full);
            if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            {
                return relative.Replace('\\', '/');
            }
        }
        throw new ArgumentException($"{header} is not under any include directory");
    }

    // -------------------------------------------------------------------------
    // Declarations
    // -------------------------------------------------------------------------

    /// <param name="scope">The enclosing namespaces, "crucible::" style.</param>
    private void Walk(CXCursor parent, string scope, BindingModel model, bool structs)
    {
        foreach (CXCursor cursor in Libclang.Children(parent))
        {
            string? file = Libclang.File(cursor);
            if (file is null || !_headerFiles.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            switch (cursor.Kind)
            {
                case CursorKind.Namespace:
                    Walk(cursor, $"{scope}{Libclang.Spelling(cursor)}::", model, structs);
                    break;
                case CursorKind.StructDecl or CursorKind.ClassDecl when structs && IsExposed(cursor) &&
                                                                        Libclang.IsCursorDefinition(cursor) != 0:
                    AddStruct(cursor, model);
                    break;
                case CursorKind.FunctionDecl when !structs && IsExposed(cursor):
                    AddFunction(cursor, scope, model);
                    break;
            }
        }
    }

    private static bool IsExposed(CXCursor cursor) =>
        Libclang.Children(cursor).Any(child =>
            child.Kind == CursorKind.AnnotateAttr && Libclang.Spelling(child) == ExposeAnnotation);

    private void AddStruct(CXCursor cursor, BindingModel model)
    {
        CXType type = Libclang.GetCursorType(cursor);
        string qualifiedName = Libclang.Spelling(Libclang.GetCanonicalType(type));
        if (Libclang.IsPodType(type) == 0)
        {
            Error(cursor, $"{qualifiedName} must be plain data (no virtual functions, base classes, " +
                          "constructors, or non-trivial members) to cross the boundary");
            return;
        }

        ExposedStruct exposed = new(qualifiedName, Libclang.Spelling(cursor), Libclang.TypeGetSizeOf(type));
        foreach (CXCursor field in Libclang.Children(cursor).Where(child => child.Kind == CursorKind.FieldDecl))
        {
            string name = Libclang.Spelling(field);
            if (Libclang.CursorIsBitField(field) != 0)
            {
                Error(field, $"{qualifiedName}::{name}: bit fields can't cross the boundary");
                continue;
            }
            TypeRef? fieldType = Resolve(field, Libclang.GetCursorType(field), $"{qualifiedName}::{name}");
            if (fieldType is not null)
            {
                // libclang reports offsets in bits.
                exposed.Fields.Add(new Field(name, fieldType, Libclang.CursorGetOffsetOfField(field) / 8));
            }
        }

        _structsByName[qualifiedName] = exposed;
        model.Structs.Add(exposed);
    }

    private void AddFunction(CXCursor cursor, string scope, BindingModel model)
    {
        string name = Libclang.Spelling(cursor);
        if (model.Functions.Any(function => function.Name == name))
        {
            Error(cursor, $"{name}: an exposed function with this name already exists; exported C names " +
                          "(crucible_<name>) must be unique");
            return;
        }

        TypeRef? returnType = Resolve(cursor, Libclang.GetCursorResultType(cursor), $"{name}: return type",
            allowVoid: true);
        List<Parameter> parameters = [];
        int count = Libclang.CursorGetNumArguments(cursor);
        for (uint i = 0; i < count; ++i)
        {
            CXCursor argument = Libclang.CursorGetArgument(cursor, i);
            string parameterName = Libclang.Spelling(argument);
            if (parameterName.Length == 0)
            {
                Error(argument, $"{name}: parameter {i + 1} needs a name for the C# binding");
                continue;
            }
            TypeRef? type = Resolve(argument, Libclang.GetCursorType(argument), $"{name}: parameter {parameterName}");
            if (type is not null)
            {
                parameters.Add(new Parameter(parameterName, type));
            }
        }

        if (returnType is not null && parameters.Count == count)
        {
            model.Functions.Add(new ExposedFunction($"{scope}{name}", name, returnType, parameters));
        }
    }

    // -------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------

    private TypeRef? Resolve(CXCursor where, CXType type, string what, bool allowVoid = false)
    {
        CXType canonical = Libclang.GetCanonicalType(type);
        long size = Libclang.TypeGetSizeOf(canonical);
        Primitive? primitive = canonical.Kind switch
        {
            TypeKind.Void when allowVoid => Primitive.Void,
            TypeKind.Bool => Primitive.Bool,
            TypeKind.SChar => Primitive.Int8,
            TypeKind.UChar => Primitive.UInt8,
            TypeKind.Short => Primitive.Int16,
            TypeKind.UShort => Primitive.UInt16,
            TypeKind.Int or TypeKind.Long or TypeKind.LongLong => size == 8 ? Primitive.Int64 : Primitive.Int32,
            TypeKind.UInt or TypeKind.ULong or TypeKind.ULongLong => size == 8 ? Primitive.UInt64 : Primitive.UInt32,
            TypeKind.Float => Primitive.Float,
            TypeKind.Double => Primitive.Double,
            _ => null,
        };
        if (primitive is not null)
        {
            return new TypeRef(primitive.Value, null);
        }

        string spelling = Libclang.Spelling(type);
        if (canonical.Kind == TypeKind.Record)
        {
            if (_structsByName.TryGetValue(Libclang.Spelling(canonical), out ExposedStruct? exposed))
            {
                return new TypeRef(Primitive.Void, exposed);
            }
            Error(where, $"{what}: {spelling} isn't marked CRUCIBLE_EXPOSE, so it can't cross the boundary");
            return null;
        }

        string hint = canonical.Kind is TypeKind.CharS or TypeKind.CharU
            ? " (plain char has no fixed signedness; use int8_t or uint8_t)"
            : " (only bool, fixed-size integers, float, double, and exposed structs, by value)";
        Error(where, $"{what}: {spelling} can't cross the boundary{hint}");
        return null;
    }

    private void Error(CXCursor cursor, string message) =>
        _errors.Add($"{Libclang.Location(cursor)}: error: {message}");
}
