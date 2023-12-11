using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex3d
{
    private Vector3 _position;
    private Vector3 _normal;
    private Vector2 _uv;
}