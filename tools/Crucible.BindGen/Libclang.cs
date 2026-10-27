using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crucible.BindGen;

// Bindings to the libclang C API (clang-c/Index.h), only what the generator
// uses (ADR 0017's amendment). The C API is ABI-stable across Clang versions,
// so these hold for any system libclang. Struct layouts and enum values are
// copied from the header.

[StructLayout(LayoutKind.Sequential)]
internal struct CXCursor
{
    public int Kind;
    public int XData;
    public nint Data0;
    public nint Data1;
    public nint Data2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CXType
{
    public int Kind;
    public nint Data0;
    public nint Data1;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CXString
{
    public nint Data;
    public uint PrivateFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CXSourceLocation
{
    public nint Pointer0;
    public nint Pointer1;
    public uint IntData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CXUnsavedFile
{
    public nint Filename;
    public nint Contents;
    public CULong Length; // unsigned long: 32 bits on Windows, 64 on Linux
}

internal static class CursorKind
{
    public const int StructDecl = 2;
    public const int ClassDecl = 4;
    public const int EnumDecl = 5;
    public const int FieldDecl = 6;
    public const int EnumConstantDecl = 7;
    public const int FunctionDecl = 8;
    public const int Namespace = 22;
    public const int TranslationUnit = 350;
    public const int AnnotateAttr = 406;
}

internal static class TypeKind
{
    public const int Invalid = 0;
    public const int Void = 2;
    public const int Bool = 3;
    public const int CharU = 4;
    public const int UChar = 5;
    public const int UShort = 8;
    public const int UInt = 9;
    public const int ULong = 10;
    public const int ULongLong = 11;
    public const int CharS = 13;
    public const int SChar = 14;
    public const int Short = 16;
    public const int Int = 17;
    public const int Long = 18;
    public const int LongLong = 19;
    public const int Float = 21;
    public const int Double = 22;
    public const int Record = 105;
    public const int Enum = 106;
}

internal static unsafe partial class Libclang
{
    private const string Library = "libclang";

    public const int ChildVisitContinue = 1;
    public const int DiagnosticError = 3;
    public const uint SkipFunctionBodies = 0x40;

    /// <summary>Loads libclang from <paramref name="path"/> for every call below.</summary>
    public static void Load(string path)
    {
        nint handle = NativeLibrary.Load(path);
        NativeLibrary.SetDllImportResolver(typeof(Libclang).Assembly,
            (name, _, _) => name == Library ? handle : 0);
    }

    [LibraryImport(Library, EntryPoint = "clang_createIndex")]
    public static partial nint CreateIndex(int excludeDeclarationsFromPch, int displayDiagnostics);

    [LibraryImport(Library, EntryPoint = "clang_disposeIndex")]
    public static partial void DisposeIndex(nint index);

    // The full-command-line form: argv[0] is the compiler's path, which tells
    // libclang's driver where Clang is installed. Without it the driver looks
    // for Clang's built-in headers (stddef.h, ...) in a relative path and
    // finds nothing.
    [LibraryImport(Library, EntryPoint = "clang_parseTranslationUnit2FullArgv")]
    public static partial int ParseTranslationUnit2FullArgv(nint index, byte* sourceFilename, byte** commandLineArgs,
        int numCommandLineArgs, CXUnsavedFile* unsavedFiles, uint numUnsavedFiles, uint options,
        nint* translationUnit);

    [LibraryImport(Library, EntryPoint = "clang_disposeTranslationUnit")]
    public static partial void DisposeTranslationUnit(nint translationUnit);

    [LibraryImport(Library, EntryPoint = "clang_getNumDiagnostics")]
    public static partial uint GetNumDiagnostics(nint translationUnit);

    [LibraryImport(Library, EntryPoint = "clang_getDiagnostic")]
    public static partial nint GetDiagnostic(nint translationUnit, uint index);

    [LibraryImport(Library, EntryPoint = "clang_getDiagnosticSeverity")]
    public static partial int GetDiagnosticSeverity(nint diagnostic);

    [LibraryImport(Library, EntryPoint = "clang_formatDiagnostic")]
    public static partial CXString FormatDiagnostic(nint diagnostic, uint options);

    [LibraryImport(Library, EntryPoint = "clang_defaultDiagnosticDisplayOptions")]
    public static partial uint DefaultDiagnosticDisplayOptions();

    [LibraryImport(Library, EntryPoint = "clang_disposeDiagnostic")]
    public static partial void DisposeDiagnostic(nint diagnostic);

    [LibraryImport(Library, EntryPoint = "clang_getTranslationUnitCursor")]
    public static partial CXCursor GetTranslationUnitCursor(nint translationUnit);

    [LibraryImport(Library, EntryPoint = "clang_visitChildren")]
    public static partial uint VisitChildren(CXCursor parent, delegate* unmanaged[Cdecl]<CXCursor, CXCursor, nint, int> visitor,
        nint clientData);

    [LibraryImport(Library, EntryPoint = "clang_getInclusions")]
    public static partial void GetInclusions(nint translationUnit,
        delegate* unmanaged[Cdecl]<nint, CXSourceLocation*, uint, nint, void> visitor, nint clientData);

    [LibraryImport(Library, EntryPoint = "clang_getCursorSpelling")]
    public static partial CXString GetCursorSpelling(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_isCursorDefinition")]
    public static partial uint IsCursorDefinition(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getCursorType")]
    public static partial CXType GetCursorType(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getCursorResultType")]
    public static partial CXType GetCursorResultType(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_Cursor_getNumArguments")]
    public static partial int CursorGetNumArguments(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_Cursor_getArgument")]
    public static partial CXCursor CursorGetArgument(CXCursor cursor, uint index);

    [LibraryImport(Library, EntryPoint = "clang_Cursor_getOffsetOfField")]
    public static partial long CursorGetOffsetOfField(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_Cursor_isBitField")]
    public static partial uint CursorIsBitField(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getCanonicalType")]
    public static partial CXType GetCanonicalType(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_getTypeSpelling")]
    public static partial CXString GetTypeSpelling(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_Type_getSizeOf")]
    public static partial long TypeGetSizeOf(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_isPODType")]
    public static partial uint IsPodType(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_getUnqualifiedType")]
    public static partial CXType GetUnqualifiedType(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_isConstQualifiedType")]
    public static partial uint IsConstQualifiedType(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_getTypeDeclaration")]
    public static partial CXCursor GetTypeDeclaration(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_getCursorSemanticParent")]
    public static partial CXCursor GetCursorSemanticParent(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_Type_getNumTemplateArguments")]
    public static partial int TypeGetNumTemplateArguments(CXType type);

    [LibraryImport(Library, EntryPoint = "clang_Type_getTemplateArgumentAsType")]
    public static partial CXType TypeGetTemplateArgumentAsType(CXType type, uint index);

    [LibraryImport(Library, EntryPoint = "clang_getEnumDeclIntegerType")]
    public static partial CXType GetEnumDeclIntegerType(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_EnumDecl_isScoped")]
    public static partial uint EnumDeclIsScoped(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getEnumConstantDeclValue")]
    public static partial long GetEnumConstantDeclValue(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getEnumConstantDeclUnsignedValue")]
    public static partial ulong GetEnumConstantDeclUnsignedValue(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getCursorLocation")]
    public static partial CXSourceLocation GetCursorLocation(CXCursor cursor);

    [LibraryImport(Library, EntryPoint = "clang_getSpellingLocation")]
    public static partial void GetSpellingLocation(CXSourceLocation location, nint* file, uint* line, uint* column,
        uint* offset);

    [LibraryImport(Library, EntryPoint = "clang_getFileName")]
    public static partial CXString GetFileName(nint file);

    [LibraryImport(Library, EntryPoint = "clang_getCString")]
    public static partial byte* GetCString(CXString text);

    [LibraryImport(Library, EntryPoint = "clang_disposeString")]
    public static partial void DisposeString(CXString text);

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>Converts and frees a libclang string.</summary>
    public static string Take(CXString text)
    {
        string result = Marshal.PtrToStringUTF8((nint)GetCString(text)) ?? "";
        DisposeString(text);
        return result;
    }

    public static string Spelling(CXCursor cursor) => Take(GetCursorSpelling(cursor));

    public static string Spelling(CXType type) => Take(GetTypeSpelling(type));

    /// <summary>"file:line:column" of a cursor, for error messages.</summary>
    public static string Location(CXCursor cursor)
    {
        nint file;
        uint line, column, offset;
        GetSpellingLocation(GetCursorLocation(cursor), &file, &line, &column, &offset);
        return file == 0 ? "<unknown>" : $"{Take(GetFileName(file))}:{line}:{column}";
    }

    /// <summary>The file a cursor is spelled in, or null.</summary>
    public static string? File(CXCursor cursor)
    {
        nint file;
        uint line, column, offset;
        GetSpellingLocation(GetCursorLocation(cursor), &file, &line, &column, &offset);
        return file == 0 ? null : Take(GetFileName(file));
    }

    /// <summary>A cursor's direct children.</summary>
    public static List<CXCursor> Children(CXCursor cursor)
    {
        List<CXCursor> children = [];
        GCHandle handle = GCHandle.Alloc(children);
        try
        {
            VisitChildren(cursor, &CollectChild, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return children;
    }

    /// <summary>Every file the translation unit includes, directly or not.</summary>
    public static List<string> Inclusions(nint translationUnit)
    {
        List<string> files = [];
        GCHandle handle = GCHandle.Alloc(files);
        try
        {
            GetInclusions(translationUnit, &CollectInclusion, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return files;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CollectChild(CXCursor cursor, CXCursor parent, nint clientData)
    {
        ((List<CXCursor>)GCHandle.FromIntPtr(clientData).Target!).Add(cursor);
        return ChildVisitContinue;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void CollectInclusion(nint includedFile, CXSourceLocation* inclusionStack, uint includeLength,
        nint clientData)
    {
        ((List<string>)GCHandle.FromIntPtr(clientData).Target!).Add(Take(GetFileName(includedFile)));
    }
}
