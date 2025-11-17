using System.Runtime.InteropServices;
using Crucible.Core.Math;
namespace Crucible.Core;

/// <summary>
/// Represents a 3d Position, Rotation, and Scale of an object
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe partial struct Transform
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
        _rotation = new Quaternion(0,0,0,1);
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
    
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_TransformToGlobal(ref Transform transform, IntPtr node, out Transform result);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_TransformInverse(ref Transform transform, out Transform result);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_TransformConcat(ref Transform a, ref Transform b, out Transform result);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_TransformDecat(ref Transform a, ref Transform b, out Transform result);

    /// <summary>
    /// An object's position in 3D space
    /// </summary>
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
        Position += by;
    }
    /// <summary>
    /// Move by an amount
    /// </summary>
    /// <param name="by"></param>
    public void Translate(Vector3 by)
    {
        Position += by;
    }
    /// <summary>
    /// An objects rotation in 3D space
    /// </summary>
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
        Rotation *= new Quaternion(angle, axis);
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="by"></param>
    public void Rotate(Quaternion by)
    {
        Rotation *= by;
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="euler">Euler angles to rotate by, (Pitch, Yaw, and Roll)</param>
    public void Rotate(Vector3 euler)
    {
        Rotation *= new Quaternion(euler);
    }
    /// <summary>
    /// Rotate by an amount
    /// </summary>
    /// <param name="pitch"></param>
    /// <param name="yaw"></param>
    /// <param name="roll"></param>
    public void Rotate(float pitch, float yaw, float roll)
    {
        Rotation *= new Quaternion(pitch, yaw, roll);
    }
    /// <summary>
    /// Set rotation to an equivelent euler rotation
    /// </summary>
    /// <param name="euler"></param>
    public void SetRotationEuler(Vector3 euler)
    {
        Rotation = new Quaternion(euler);
    }
    /// <summary>
    /// Set rotation to an equivelent euler rotation
    /// </summary>
    /// <param name="pitch"></param>
    /// <param name="yaw"></param>
    /// <param name="roll"></param>
    public void SetRotationEuler(float pitch, float yaw, float roll)
    {
       Rotation = new Quaternion(pitch, yaw, roll);
    }
    /// <summary>
    /// An objects scale in 3D space
    /// </summary>
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
        Scale *= amount;
    }
    /// <summary>
    /// Scale by non-uniform amounts
    /// </summary>
    /// <param name="amount"></param>
    public void ScaleBy(Vector3 amount)
    {
        Scale = new Vector3(Scale.X * amount.X, Scale.Y * amount.Y, Scale.Z * amount.Z);
    }
    /// <summary>
    /// Scale by non-uniform amounts
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void ScaleBy(float x, float y, float z)
    {
        Scale = new Vector3(Scale.X * x, Scale.Y * y, Scale.Z * z);
    }
    /// <summary>
    /// Get the world space transform of this transform relative to a node in the heirarchy
    /// </summary>
    /// <param name="relativeTo">Object to transform the local space transformation into global space by</param>
    /// <returns></returns>
    public Transform ToGlobal(Node relativeTo)
    {
        CRUCIBLE_NATIVE_TransformToGlobal(ref this,relativeTo.Pointer, out Transform result);
        return result;
    }
    /// <summary>
    /// Get the inverse of this transform (Concatenating with this transform should give identity transform)
    /// </summary>
    /// <returns></returns>
    public Transform Inverse()
    {
        CRUCIBLE_NATIVE_TransformInverse(ref this,out Transform result);
        return result;
    }
    /// <summary>
    /// Are two transforms approximately the same
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public static bool Approximately(Transform a, Transform b)
    {
        return Vector3.Approximately(a.Position, b.Position) &&
               Quaternion.Approximately(a.Rotation, b.Rotation) &&
               Vector3.Approximately(a.Scale, b.Scale);
    }
    /// <summary>
    /// Get the world space transform of a given node (equivalent to calling Node.CumulativeTransform)
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
        CRUCIBLE_NATIVE_TransformConcat(ref t1, ref t2, out Transform result);
        return result;
    }

    /// <summary>
    /// Remove a transform from another. (eg: final = t1+t2 ==> final-t1 == t2)
    /// </summary>
    /// <param name="t1">Parent Transform</param>
    /// <param name="t2">Local Transform</param>
    /// <returns></returns>
    public static Transform operator-(Transform t1, Transform t2)
    {
        CRUCIBLE_NATIVE_TransformDecat(ref t1, ref t2, out Transform result);
        return result;
    }

    public override string ToString()
    {
        return "position: "+_position.ToString() + "\nrotation: " + _rotation.ToString() + "\nscale: " + _scale.ToString();
    }
}