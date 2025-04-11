using System.Runtime.InteropServices;

namespace Crucible.Core;
/// <summary>
/// 4X4 Matrix used for manipulating 3d constructs
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Matrix
{
    private fixed float _data[16];
}