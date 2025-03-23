using System.Runtime.InteropServices;

namespace Crucible.Core;
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MeshRenderer
{
    private byte _priority;
    private bool _firtHalf;
    private MeshInstance _meshInstance;
    private ShaderReference _shaderReference;
    private IntPtr _rawUniformData;
    private UInt32 _rawDataLength;
}