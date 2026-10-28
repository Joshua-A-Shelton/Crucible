// Records and init accessors need this type, which netstandard2.0 lacks; the
// compiler only looks for it by name.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
