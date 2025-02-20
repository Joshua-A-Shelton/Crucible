using System;
using System.Runtime.InteropServices;

namespace Crucible.Core;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct Transform
{
    private Vector3 _position;
    private Quaternion _rotation;
    private Vector3 _scale;
    private Matrix4x4 _matrix;
    private bool _needsUpdate;
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref Transform, void> _transformUpdateMatrix_ptr;
#pragma warning restore 0649

    public Matrix4x4 Matrix
    {
        get
        {
            _transformUpdateMatrix_ptr(ref this);
            return _matrix;
        }
    }
}