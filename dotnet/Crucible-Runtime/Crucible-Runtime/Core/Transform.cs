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
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformSetPosition_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformTranslate_ptr;
    private static delegate* unmanaged<ref Transform, ref Quaternion, void> _transformSetRotation_ptr;
    private static delegate* unmanaged<ref Transform, ref Quaternion, void> _transformRotate_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformSetRotationEuler_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformRotateEuler_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformSetScale_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformScale_ptr;
    private static delegate* unmanaged<ref Transform, void> _transformUpdateMatrix_ptr;
#pragma warning restore 0649

    public Vector3 Position
    {
        get { return _position; }
        set { _transformSetPosition_ptr(ref this, ref value); }
    }

    public void Translate(float x, float y, float z)
    {
        Vector3 by = new Vector3(x, y, z);
        _transformTranslate_ptr(ref this, ref by);
    }

    public void Translate(Vector3 by)
    {
        _transformTranslate_ptr(ref this, ref by);
    }

    public Quaternion Rotation
    {
        get { return _rotation; }
        set { _transformSetRotation_ptr(ref this, ref value); }
    }

    public void Rotate(Quaternion by)
    {
        _transformRotate_ptr(ref this, ref by);
    }

    public void Rotate(Vector3 euler)
    {
        _transformRotateEuler_ptr(ref this, ref euler);
    }

    public void Rotate(float pitch, float yaw, float roll)
    {
        Vector3 euler = new Vector3(pitch, yaw, roll);
        _transformRotateEuler_ptr(ref this, ref euler);
    }

    public void SetRotationEuler(Vector3 euler)
    {
        _transformSetRotationEuler_ptr(ref this, ref euler);
    }

    public void SetRotationEuler(float pitch, float yaw, float roll)
    {
        Vector3 euler = new Vector3(pitch, yaw, roll);
        _transformSetRotationEuler_ptr(ref this, ref euler);
    }

    public Vector3 Scale
    {
        get { return _scale; }
        set { _transformSetScale_ptr(ref this, ref value); }
    }

    public void ScaleBy(float amount)
    {
        Vector3 scaleVector = new Vector3(amount, amount, amount);
        _transformScale_ptr(ref this, ref scaleVector);
    }

    public void ScaleBy(Vector3 amount)
    {
        _transformScale_ptr(ref this, ref amount);
    }

    public void ScaleBy(float x, float y, float z)
    {
        Vector3 scaleVector = new Vector3(x, y, z);
        _transformScale_ptr(ref this, ref scaleVector);
    }
    
    
    public Matrix4x4 Matrix
    {
        get
        {
            _transformUpdateMatrix_ptr(ref this);
            return _matrix;
        }
    }
    
    public bool IsDirty
    {
        get { return _needsUpdate; }
    }
}