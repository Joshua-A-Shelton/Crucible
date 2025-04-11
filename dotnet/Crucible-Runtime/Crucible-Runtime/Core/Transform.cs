using System;
using System.Runtime.InteropServices;
using Crucible.Core.Attributes;

namespace Crucible.Core;
/// <summary>
/// Represents a 3d Position, Rotation, and Scale of an object
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Transform
{
    private Vector3 _position;
    private Quaternion _rotation;
    private Vector3 _scale;
    /// <summary>
    /// Create a Transform
    /// </summary>
    public Transform()
    {
        _position = new Vector3(0,0,0);
        _rotation = new Quaternion(1,0,0,0);
        _scale = new Vector3(1,1,1);
    }
    /// <summary>
    /// Create a Transform
    /// </summary>
    /// <param name="position"></param>
    /// <param name="rotation"></param>
    /// <param name="scale"></param>
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

    /// <summary>
    /// An object's position in 3D space
    /// </summary>
    [Expose("Position")]
    public Vector3 Position
    {
        get { return _position; }
        set { _position = value; }
    }
    /// <summary>
    /// Move by an amount
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void Translate(float x, float y, float z)
    {
        Vector3 by = new Vector3(x, y, z);
        _transformTranslate_ptr(ref this, ref by);
    }
    /// <summary>
    /// Move by an amount
    /// </summary>
    /// <param name="by"></param>
    public void Translate(Vector3 by)
    {
        _transformTranslate_ptr(ref this, ref by);
    }
    /// <summary>
    /// An objects rotation in 3D space
    /// </summary>
    [Expose("Rotation")]
    public Quaternion Rotation
    {
        get { return _rotation; }
        set { _rotation = value; }
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="angle">Radians around the axis to rotate by</param>
    /// <param name="axis">Axis to rotate around</param>
    public void Rotate(float angle, Vector3 axis)
    {
        _transformRotateAxisAngle_ptr(ref this, angle, ref axis);
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="by"></param>
    public void Rotate(Quaternion by)
    {
        _transformRotate_ptr(ref this, ref by);
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="euler">Euler angles to rotate by, (Pitch, Yaw, and Roll)</param>
    public void Rotate(Vector3 euler)
    {
        _transformRotateEuler_ptr(ref this, ref euler);
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="pitch"></param>
    /// <param name="yaw"></param>
    /// <param name="roll"></param>
    public void Rotate(float pitch, float yaw, float roll)
    {
        Vector3 euler = new Vector3(pitch, yaw, roll);
        _transformRotateEuler_ptr(ref this, ref euler);
    }
    /// <summary>
    /// Set rotation to an equivelent euler rotation
    /// </summary>
    /// <param name="euler"></param>
    public void SetRotationEuler(Vector3 euler)
    {
        _transformSetRotationEuler_ptr(ref this, ref euler);
    }
    /// <summary>
    /// Set rotation to an equivelent euler rotation
    /// </summary>
    /// <param name="pitch"></param>
    /// <param name="yaw"></param>
    /// <param name="roll"></param>
    public void SetRotationEuler(float pitch, float yaw, float roll)
    {
        Vector3 euler = new Vector3(pitch, yaw, roll);
        _transformSetRotationEuler_ptr(ref this, ref euler);
    }
    /// <summary>
    /// An objects scale in 3D space
    /// </summary>
    [Expose("Scale")]
    public Vector3 Scale
    {
        get { return _scale; }
        set {_scale = value; }
    }
    /// <summary>
    /// Scale uniformly by an amount
    /// </summary>
    /// <param name="amount"></param>
    public void ScaleBy(float amount)
    {
        Vector3 scaleVector = new Vector3(amount, amount, amount);
        _transformScale_ptr(ref this, ref scaleVector);
    }
    /// <summary>
    /// Scale by non-uniform amounts
    /// </summary>
    /// <param name="amount"></param>
    public void ScaleBy(Vector3 amount)
    {
        _transformScale_ptr(ref this, ref amount);
    }
    /// <summary>
    /// Scale by non-uniform amounts
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void ScaleBy(float x, float y, float z)
    {
        Vector3 scaleVector = new Vector3(x, y, z);
        _transformScale_ptr(ref this, ref scaleVector);
    }
    /// <summary>
    /// Get the world space transform of this transform relative to a node in the heirarchy
    /// </summary>
    /// <param name="relativeTo">Object to transform the local space transformation into global space by</param>
    /// <returns></returns>
    public Transform ToGlobal(Node relativeTo)
    {
        Transform t = new Transform();
        _transformToGlobal_ptr(ref this, relativeTo, ref t);
        return t;
    }
    /// <summary>
    /// Get the inverse of this transform (Concatenating with this transform should give identity transform)
    /// </summary>
    /// <returns></returns>
    public Transform Inverse()
    {
        Transform t = new Transform();
        _transformInverse_ptr(ref this, ref t);
        return t;
    }
    /// <summary>
    /// Are two transforms approximately the same
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool Approximate(Transform a, Transform b)
    {
        return Vector3.Approximately(a.Position, b.Position) &&
               Quaternion.Approximately(a.Rotation, b.Rotation) &&
               Vector3.Approximately(a.Scale, b.Scale);
    }
    /// <summary>
    /// Get the world space tranform of a given node
    /// </summary>
    /// <param name="from"></param>
    /// <returns></returns>
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