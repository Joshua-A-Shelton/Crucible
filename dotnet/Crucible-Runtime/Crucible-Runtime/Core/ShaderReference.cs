using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// Pointer to a shader reference in unmanaged code
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ShaderReference
{
    private IntPtr _unitPointer;
}