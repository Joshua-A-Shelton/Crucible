using System.Globalization;
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
    private const string StatusAnnotation = "crucible_status";
    private const string InternalAnnotation = "crucible_internal";
    private const string ExposeAsPrefix = "crucible_expose_as:";

    private readonly HashSet<string> _headerFiles;
    private readonly Dictionary<string, ExposedStruct> _structsByName = [];
    private readonly Dictionary<string, ExposedEnum> _enumsByName = [];
    private readonly Dictionary<string, ExposedOpaque> _opaquesByName = [];
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
            // Enums, then structs, then functions, so each can refer to the
            // ones before it in any order.
            foreach (Pass pass in Enum.GetValues<Pass>())
            {
                parser.Walk(root, [], model, pass);
            }
            model.Status = parser._enumsByName.GetValueOrDefault("crucible::Status");
            if (model.Status is null && model.Functions.Any(function => function.ReturnsResult))
            {
                parser._errors.Add("error: functions return crucible::Result, but crucible::Status isn't marked " +
                                   "CRUCIBLE_EXPOSE");
            }

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

    private enum Pass
    {
        Enums,
        Structs,
        Functions,
    }

    /// <param name="namespaces">The enclosing namespaces, outermost first.</param>
    private void Walk(CXCursor parent, List<string> namespaces, BindingModel model, Pass pass)
    {
        foreach (CXCursor cursor in Libclang.Children(parent))
        {
            string? file = Libclang.File(cursor);
            if (file is null || !_headerFiles.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            if (cursor.Kind == CursorKind.Namespace)
            {
                Walk(cursor, [.. namespaces, Libclang.Spelling(cursor)], model, pass);
                continue;
            }

            bool isDeclaration = pass switch
            {
                Pass.Enums => cursor.Kind == CursorKind.EnumDecl && Libclang.IsCursorDefinition(cursor) != 0,
                Pass.Structs => cursor.Kind is CursorKind.StructDecl or CursorKind.ClassDecl,
                _ => cursor.Kind == CursorKind.FunctionDecl,
            };
            if (!isDeclaration || !HasAnnotation(cursor, ExposeAnnotation))
            {
                continue;
            }

            // Every exposed name is under crucible::, which becomes the C#
            // root namespace Crucible.
            if (namespaces.Count == 0 || namespaces[0] != "crucible" || namespaces.Any(name => name.Length == 0))
            {
                Error(cursor, $"{Libclang.Spelling(cursor)}: exposed declarations must be in namespace crucible " +
                              "or a named namespace inside it");
                continue;
            }

            Scope scope = new(namespaces);
            switch (pass)
            {
                case Pass.Enums:
                    AddEnum(cursor, scope, model);
                    break;
                case Pass.Structs when Libclang.IsCursorDefinition(cursor) != 0:
                    AddStruct(cursor, scope, model);
                    break;
                case Pass.Structs:
                    AddOpaque(cursor, scope, model);
                    break;
                case Pass.Functions:
                    AddFunction(cursor, scope, model);
                    break;
            }
        }
    }

    private static bool HasAnnotation(CXCursor cursor, string annotation) =>
        Libclang.Children(cursor).Any(child =>
            child.Kind == CursorKind.AnnotateAttr && Libclang.Spelling(child) == annotation);

    private void AddEnum(CXCursor cursor, Scope scope, BindingModel model)
    {
        string qualifiedName = Libclang.Spelling(Libclang.GetCanonicalType(Libclang.GetCursorType(cursor)));
        // A scoped enum always has a fixed underlying type; a plain enum's can
        // differ between compilers.
        if (Libclang.EnumDeclIsScoped(cursor) == 0)
        {
            Error(cursor, $"{qualifiedName}: only enum class can cross the boundary");
            return;
        }

        CXType integerType = Libclang.GetEnumDeclIntegerType(cursor);
        TypeRef? underlying = Resolve(cursor, integerType, $"{qualifiedName}: underlying type");
        if (underlying is null)
        {
            return;
        }
        if (underlying.Is(Primitive.Bool))
        {
            Error(cursor, $"{qualifiedName}: underlying type must be an integer");
            return;
        }

        bool unsigned = underlying.Primitive is Primitive.UInt8 or Primitive.UInt16 or Primitive.UInt32 or
            Primitive.UInt64;
        ExposedEnum exposed = new(qualifiedName, Libclang.Spelling(cursor), scope, underlying.Primitive,
            Libclang.TypeGetSizeOf(integerType));
        foreach (CXCursor constant in Libclang.Children(cursor)
                     .Where(child => child.Kind == CursorKind.EnumConstantDecl))
        {
            string value = unsigned
                ? Libclang.GetEnumConstantDeclUnsignedValue(constant).ToString(CultureInfo.InvariantCulture)
                : Libclang.GetEnumConstantDeclValue(constant).ToString(CultureInfo.InvariantCulture);
            exposed.Enumerators.Add(new Enumerator(Libclang.Spelling(constant), value));
        }

        _enumsByName[qualifiedName] = exposed;
        model.Enums.Add(exposed);
    }

    // A declaration without a definition in the headers: native code owns
    // the type, and C# gets a handle. Declared again later, it's still one.
    private void AddOpaque(CXCursor cursor, Scope scope, BindingModel model)
    {
        string qualifiedName = Libclang.Spelling(Libclang.GetCanonicalType(Libclang.GetCursorType(cursor)));
        if (_opaquesByName.ContainsKey(qualifiedName) || _structsByName.ContainsKey(qualifiedName))
        {
            return;
        }
        ExposedOpaque opaque = new(qualifiedName, Libclang.Spelling(cursor), scope);
        _opaquesByName[qualifiedName] = opaque;
        model.Opaques.Add(opaque);
    }

    private void AddStruct(CXCursor cursor, Scope scope, BindingModel model)
    {
        CXType type = Libclang.GetCursorType(cursor);
        string qualifiedName = Libclang.Spelling(Libclang.GetCanonicalType(type));
        // Plain data also rules out default member initializers: C#'s default
        // would silently ignore them.
        if (Libclang.IsPodType(type) == 0)
        {
            Error(cursor, $"{qualifiedName} must be plain data (no virtual functions, base classes, " +
                          "constructors, default member initializers, or non-trivial members) to cross the boundary");
            return;
        }

        string? mapped = Libclang.Children(cursor)
            .Where(child => child.Kind == CursorKind.AnnotateAttr)
            .Select(Libclang.Spelling)
            .FirstOrDefault(annotation => annotation.StartsWith(ExposeAsPrefix, StringComparison.Ordinal))?
            [ExposeAsPrefix.Length..];
        ExposedStruct exposed = new(qualifiedName, Libclang.Spelling(cursor), scope, Libclang.TypeGetSizeOf(type),
            mapped);
        foreach (CXCursor field in Libclang.Children(cursor).Where(child => child.Kind == CursorKind.FieldDecl))
        {
            string name = Libclang.Spelling(field);
            if (Libclang.CursorIsBitField(field) != 0)
            {
                Error(field, $"{qualifiedName}::{name}: bit fields can't cross the boundary");
                continue;
            }
            TypeRef? fieldType = Resolve(field, Libclang.GetCursorType(field), $"{qualifiedName}::{name}",
                allowArray: true, isField: true);
            if (fieldType is not null)
            {
                // libclang reports offsets in bits.
                exposed.Fields.Add(new Field(name, fieldType, Libclang.CursorGetOffsetOfField(field) / 8));
            }
        }

        _structsByName[qualifiedName] = exposed;
        model.Structs.Add(exposed);
    }

    private void AddFunction(CXCursor cursor, Scope scope, BindingModel model)
    {
        string name = Libclang.Spelling(cursor);
        string qualifiedName = $"{scope.Cpp}{name}";

        bool returnsResult = false;
        TypeRef? returnType;
        CXType resultValue = default;
        CXType declaredReturn = Libclang.GetCursorResultType(cursor);
        if (IsResult(declaredReturn, ref resultValue))
        {
            returnsResult = true;
            returnType = Resolve(cursor, resultValue, $"{qualifiedName}: Result value type", allowVoid: true);
        }
        else
        {
            returnType = Resolve(cursor, declaredReturn, $"{qualifiedName}: return type", allowVoid: true,
                allowStringReturn: true);
        }

        bool statusForm = HasAnnotation(cursor, StatusAnnotation);
        if (statusForm && !returnsResult)
        {
            Error(cursor, $"{qualifiedName}: CRUCIBLE_EXPOSE_STATUS only applies to functions returning Result");
        }

        List<Parameter> parameters = [];
        int count = Libclang.CursorGetNumArguments(cursor);
        for (uint i = 0; i < count; ++i)
        {
            CXCursor argument = Libclang.CursorGetArgument(cursor, i);
            string parameterName = Libclang.Spelling(argument);
            if (parameterName.Length == 0)
            {
                Error(argument, $"{qualifiedName}: parameter {i + 1} needs a name for the C# binding");
                continue;
            }
            if (returnType?.Shape == Shape.String && parameterName == "resultLength")
            {
                // The shim adds its own "resultLength" parameter for the string.
                Error(argument, $"{qualifiedName}: \"resultLength\" is reserved for the returned string's length; " +
                                "rename the parameter");
                continue;
            }
            if (returnsResult && parameterName == "result")
            {
                // The shim adds its own "result" parameter for the value.
                Error(argument, $"{qualifiedName}: \"result\" is reserved for the Result value; rename the parameter");
                continue;
            }
            TypeRef? type = Resolve(argument, Libclang.GetCursorType(argument),
                $"{qualifiedName}: parameter {parameterName}", allowParameterShapes: true);
            if (type is not null)
            {
                parameters.Add(new Parameter(parameterName, type));
            }
        }

        if (returnType is null || parameters.Count != count)
        {
            return;
        }
        ExposedFunction function = new(name, scope, returnType, parameters, returnsResult, statusForm,
            HasAnnotation(cursor, InternalAnnotation));
        if (model.Functions.Any(other => other.CName == function.CName || other.RawName == function.RawName))
        {
            Error(cursor, $"{qualifiedName}: another exposed function already maps to {function.CName}; " +
                          "exposed names must be unique within their namespace");
            return;
        }
        model.Functions.Add(function);
    }

    // -------------------------------------------------------------------------
    // Types
    // -------------------------------------------------------------------------

    /// <summary>
    /// The namespaces around a type's declaration, outermost first, so standard
    /// types are recognized by name and namespace instead of by spelling (which
    /// differs between libc++, libstdc++, and MSVC's library).
    /// </summary>
    private static (string Name, List<string> Namespaces) Declaration(CXType canonical)
    {
        CXCursor declaration = Libclang.GetTypeDeclaration(canonical);
        List<string> namespaces = [];
        for (CXCursor parent = Libclang.GetCursorSemanticParent(declaration);
             parent.Kind == CursorKind.Namespace;
             parent = Libclang.GetCursorSemanticParent(parent))
        {
            namespaces.Insert(0, Libclang.Spelling(parent));
        }
        return (Libclang.Spelling(declaration), namespaces);
    }

    private static bool IsStd(List<string> namespaces) => namespaces.Count > 0 && namespaces[0] == "std";

    /// <summary>Whether a type is crucible::Result&lt;T&gt;; if so, sets <paramref name="value"/> to T.</summary>
    private static bool IsResult(CXType type, ref CXType value)
    {
        CXType canonical = Libclang.GetCanonicalType(type);
        if (canonical.Kind != TypeKind.Record)
        {
            return false;
        }
        (string name, List<string> namespaces) = Declaration(canonical);
        if (name != "Result" || namespaces is not ["crucible"] ||
            Libclang.TypeGetNumTemplateArguments(canonical) != 1)
        {
            return false;
        }
        value = Libclang.TypeGetTemplateArgumentAsType(canonical, 0);
        return true;
    }

    /// <param name="allowParameterShapes">Spans and string views, which only parameters may be.</param>
    /// <param name="allowArray">Fixed-size arrays, which only struct fields may be.</param>
    /// <param name="isField">A struct field: pointers to opaque types only.</param>
    /// <param name="allowStringReturn">std::string_view, as a plain function's return value.</param>
    private TypeRef? Resolve(CXCursor where, CXType type, string what, bool allowVoid = false,
        bool allowParameterShapes = false, bool allowArray = false, bool isField = false,
        bool allowStringReturn = false)
    {
        CXType outer = Libclang.GetCanonicalType(type);
        if (outer.Kind is TypeKind.Pointer or TypeKind.LValueReference)
        {
            return ResolveIndirect(where, type, outer, what, allowHandles: allowParameterShapes || allowVoid || isField,
                isField);
        }
        if (Libclang.GetCanonicalType(type).Kind == TypeKind.ConstantArray)
        {
            return allowArray ? ResolveArray(where, Libclang.GetCanonicalType(type), what) : Reject(where,
                $"{what}: {Libclang.Spelling(type)}: fixed-size arrays can only be struct fields");
        }

        // Qualifiers don't change the C ABI of a value; const matters only for
        // span elements, which ask for it below.
        CXType canonical = Libclang.GetUnqualifiedType(Libclang.GetCanonicalType(type));
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
            return TypeRef.Of(primitive.Value);
        }

        string spelling = Libclang.Spelling(type);
        string canonicalSpelling = Libclang.Spelling(canonical);
        if (canonical.Kind == TypeKind.Enum)
        {
            if (_enumsByName.TryGetValue(canonicalSpelling, out ExposedEnum? exposedEnum))
            {
                return new TypeRef(Shape.Enum, Enum: exposedEnum);
            }
            Error(where, $"{what}: {spelling} isn't marked CRUCIBLE_EXPOSE, so it can't cross the boundary");
            return null;
        }

        if (canonical.Kind == TypeKind.Record)
        {
            (string name, List<string> namespaces) = Declaration(canonical);
            if (IsStd(namespaces) && name is "span" or "basic_string_view")
            {
                return allowParameterShapes || (allowStringReturn && name == "basic_string_view")
                    ? ResolveStandard(where, canonical, name, spelling, what)
                    : Fail($"{what}: {spelling} can only be a function parameter" +
                           (name == "basic_string_view" ? " or a plain return value" : ""));
            }
            if (_structsByName.TryGetValue(canonicalSpelling, out ExposedStruct? exposed))
            {
                return new TypeRef(Shape.Struct, Struct: exposed);
            }
            if (name == "Result" && namespaces is ["crucible"])
            {
                return Fail($"{what}: {spelling}: crucible::Result can only be a return type");
            }
            return Fail($"{what}: {spelling} isn't marked CRUCIBLE_EXPOSE, so it can't cross the boundary");
        }

        string hint = canonical.Kind is TypeKind.CharS or TypeKind.CharU
            ? " (plain char has no fixed signedness; use int8_t or uint8_t)"
            : " (only bool, fixed-size integers, float, double, and exposed enums and structs by value; " +
              "std::span and std::string_view parameters; crucible::Result return values)";
        return Fail($"{what}: {spelling} can't cross the boundary{hint}");

        TypeRef? Fail(string message)
        {
            Error(where, message);
            return null;
        }
    }

    // Pointers and references cross only as opaque handles or raw addresses.
    // A struct field may only be an opaque pointer: a handle is plain data of
    // a pointer's size, while a reference can't be a C# field and a raw
    // address would be public in a struct mirror.
    private TypeRef? ResolveIndirect(CXCursor where, CXType type, CXType canonical, string what, bool allowHandles,
        bool isField = false)
    {
        string spelling = Libclang.Spelling(type);
        if (!allowHandles)
        {
            return Reject(where, $"{what}: {spelling}: pointers and references can't cross here");
        }
        if (isField && canonical.Kind == TypeKind.LValueReference)
        {
            return Reject(where, $"{what}: {spelling}: references can't be struct fields; use a pointer");
        }
        CXType pointee = Libclang.GetPointeeType(canonical);
        CXType pointeeCanonical = Libclang.GetCanonicalType(pointee);
        bool isConst = Libclang.IsConstQualifiedType(pointeeCanonical) != 0;
        if (pointeeCanonical.Kind == TypeKind.Void && canonical.Kind == TypeKind.Pointer)
        {
            return isField
                ? Reject(where, $"{what}: {spelling}: void* can't be a struct field")
                : new TypeRef(Shape.Address, IsConst: isConst);
        }
        string pointeeName = Libclang.Spelling(Libclang.GetUnqualifiedType(pointeeCanonical));
        if (pointeeCanonical.Kind == TypeKind.Record && _opaquesByName.TryGetValue(pointeeName, out ExposedOpaque? opaque))
        {
            return new TypeRef(Shape.Opaque, IsConst: isConst, Opaque: opaque,
                IsReference: canonical.Kind == TypeKind.LValueReference);
        }
        return Reject(where, $"{what}: {spelling}: pointers and references cross only as handles to opaque " +
                             "types (a CRUCIBLE_EXPOSE struct declared but not defined in the headers) or as void*");
    }

    private TypeRef? Reject(CXCursor where, string message)
    {
        Error(where, message);
        return null;
    }

    private TypeRef? ResolveArray(CXCursor where, CXType canonical, string what)
    {
        CXType elementType = Libclang.GetArrayElementType(canonical);
        if (Libclang.GetCanonicalType(elementType).Kind == TypeKind.ConstantArray)
        {
            return Reject(where, $"{what}: arrays of arrays can't cross the boundary; use an array of structs");
        }
        TypeRef? element = Resolve(where, elementType, $"{what}: array element");
        if (element is null)
        {
            return null;
        }
        if (element.Is(Primitive.Bool))
        {
            // A C# bool isn't laid out as a C++ bool; use uint8_t.
            return Reject(where, $"{what}: arrays of bool can't cross the boundary; use uint8_t");
        }
        return new TypeRef(Shape.Array, Element: element, Length: Libclang.GetArraySize(canonical));
    }

    private TypeRef? ResolveStandard(CXCursor where, CXType canonical, string name, string spelling, string what)
    {
        CXType argument = Libclang.TypeGetTemplateArgumentAsType(canonical, 0);
        if (name == "basic_string_view")
        {
            // Strings cross as UTF-8 (ADR 0017), so only char views.
            if (Libclang.GetCanonicalType(argument).Kind is not (TypeKind.CharS or TypeKind.CharU))
            {
                Error(where, $"{what}: {spelling}: only std::string_view (UTF-8) can cross the boundary");
                return null;
            }
            return new TypeRef(Shape.String);
        }

        // A parameter's type is never instantiated, so its size is unknown.
        // Instead: libclang leaves defaulted template arguments out of the
        // spelling, so span<T> has no second argument while span<T, 4> does,
        // and exposed element types never contain commas themselves.
        if (Libclang.Spelling(canonical).Contains(','))
        {
            Error(where, $"{what}: {spelling}: only dynamic-extent spans can cross the boundary");
            return null;
        }
        TypeRef? element = Resolve(where, argument, $"{what}: span element");
        if (element is null)
        {
            return null;
        }
        if (element.Is(Primitive.Bool))
        {
            // A C# bool[] isn't laid out as C++ bools; use uint8_t.
            Error(where, $"{what}: {spelling}: spans of bool can't cross the boundary; use uint8_t");
            return null;
        }
        bool isConst = Libclang.IsConstQualifiedType(Libclang.GetCanonicalType(argument)) != 0;
        return new TypeRef(Shape.Span, Element: element, IsConst: isConst);
    }

    private void Error(CXCursor cursor, string message) =>
        _errors.Add($"{Libclang.Location(cursor)}: error: {message}");
}
