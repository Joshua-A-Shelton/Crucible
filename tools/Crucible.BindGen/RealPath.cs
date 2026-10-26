using System.Runtime.InteropServices;

namespace Crucible.BindGen;

/// <summary>
/// Resolves paths through the filesystem. libclang reports system headers as
/// paths like "/../lib64/gcc/.../../../../include/c++/16/cstdint", which only
/// mean /usr/include/... once symlinks (/lib64 -> /usr/lib) are followed.
/// Path.GetFullPath resolves ".." as text and would get them wrong.
/// </summary>
internal static unsafe partial class RealPath
{
    public static string Resolve(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.GetFullPath(path);
        }
        byte* resolved = realpath(path, null);
        if (resolved == null)
        {
            return Path.GetFullPath(path);
        }
        string result = Marshal.PtrToStringUTF8((nint)resolved)!;
        free(resolved);
        return result;
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial byte* realpath(string path, byte* resolved);

    [LibraryImport("libc")]
    private static partial void free(byte* pointer);
}
