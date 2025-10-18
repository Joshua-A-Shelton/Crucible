using System.Reflection;
using System.Runtime.InteropServices;

namespace Crucible.Initialization;

public static class Native
{
    public static IntPtr GetExportedSymbol(string library,string symbolName)
    {
        var libraryHandle =  NativeLibrary.Load(library, Assembly.GetExecutingAssembly(), DllImportSearchPath.AssemblyDirectory);
        return NativeLibrary.GetExport(libraryHandle, symbolName);
    }
}