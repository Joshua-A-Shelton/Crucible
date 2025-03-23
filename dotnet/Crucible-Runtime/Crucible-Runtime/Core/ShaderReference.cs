using System.Runtime.InteropServices;

namespace Crucible.Core;
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ShaderReference
{
    private IntPtr _unitPointer;
}