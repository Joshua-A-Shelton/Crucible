using System;
using System.Runtime.InteropServices;
using Crucible.Core.Attributes;

namespace Crucible.Core;
[Component]
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Transform
{
    private Vector3 _position;
    private Quaternion _rotation;
    private Vector3 _scale;

    public Transform()
    {
        _position = new Vector3(0,0,0);
        _rotation = new Quaternion(1,0,0,0);
        _scale = new Vector3(1,1,1);
    }

    public Transform(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _position = position;
        _rotation = rotation;
        _scale = scale;
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformTranslate_ptr;
    private static delegate* unmanaged<ref Transform, ref Quaternion, void> _transformRotate_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformSetRotationEuler_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformRotateEuler_ptr;
    private static delegate* unmanaged<ref Transform, float, ref Vector3, void> _transformRotateAxisAngle_ptr;
    private static delegate* unmanaged<ref Transform, ref Vector3, void> _transformScale_ptr;
    private static delegate* unmanaged<ref Transform, ref Transform, ref Transform, void> _transformConcatTransforms_ptr;
    private static delegate* unmanaged<ref Transform, ref Transform, ref Transform, void> _transformDecatTransforms_ptr;
    private static delegate* unmanaged<ref Transform, Node, ref Transform, void> _transformToGlobal_ptr;
    private static delegate* unmanaged<ref Transform, ref Transform, void> _transformInverse_ptr;
#pragma warning restore 0649

    [Expose("Position")]
    public Vector3 Position
    {
        get { return _position; }
        set { _position = value; }
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
    
    [Expose("Rotation")]
    public Quaternion Rotation
    {
        get { return _rotation; }
        set { _rotation = value; }
    }

    public void Rotate(float angle, Vector3 axis)
    {
        _transformRotateAxisAngle_ptr(ref this, angle, ref axis);
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
    
    [Expose("Scale")]
    public Vector3 Scale
    {
        get { return _scale; }
        set {_scale = value; }
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

    public Transform ToGlobal(Node relativeTo)
    {
        Transform t = new Transform();
        _transformToGlobal_ptr(ref this, relativeTo, ref t);
        return t;
    }

    public Transform Inverse()
    {
        Transform t = new Transform();
        _transformInverse_ptr(ref this, ref t);
        return t;
    }

    public static bool Approximate(Transform a, Transform b)
    {
        return Vector3.Approximately(a.Position, b.Position) &&
               Quaternion.Approximately(a.Rotation, b.Rotation) &&
               Vector3.Approximately(a.Scale, b.Scale);
    }

    public static Transform Cumulative(Node from)
    {
        return from.CumulativeTransform();
    }

    /// <summary>
    /// Concatenate a transform to another. Transforms *are not* communicative (eg: t1*t2*t3 != t1*(t2*t3))
    /// </summary>
    /// <param name="t1">Local Transform</param>
    /// <param name="t2">Parent Transform</param>
    /// <returns></returns>
    public static Transform operator+(Transform t1, Transform t2)
    {
        Transform outTransform = new Transform();
        _transformConcatTransforms_ptr(ref t1, ref t2, ref outTransform);
        return outTransform;
    }

    /// <summary>
    /// Remove a transform from another. (eg: final = t1+t2 ==> final-t1 == t2)
    /// </summary>
    /// <param name="t1">Parent Transform</param>
    /// <param name="t2">Local Transform</param>
    /// <returns></returns>
    public static Transform operator-(Transform t1, Transform t2)
    {
        Transform outTransform = new Transform();
        _transformDecatTransforms_ptr(ref t1, ref t2, ref outTransform);
        return outTransform;
    }

    public override string ToString()
    {
        return "position: "+_position.ToString() + "\nrotation: " + _rotation.ToString() + "\nscale: " + _scale.ToString();
    }
}