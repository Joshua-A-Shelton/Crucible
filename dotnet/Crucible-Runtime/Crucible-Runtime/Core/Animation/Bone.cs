using System.Runtime.InteropServices;

namespace Crucible.Core.Animation;

[StructLayout(LayoutKind.Sequential)]
public unsafe ref struct Bone
{
    private IntPtr _pointer = IntPtr.Zero;

    internal Bone(IntPtr pointer)
    {
        _pointer = pointer;
    }
    
    public bool IsNull{get{return _pointer == IntPtr.Zero;}}
    
    private void ErrorIfNull()
    {
        if (_pointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<IntPtr, UInt16> _boneSkeletalIndex_ptr;
    private static delegate* unmanaged<IntPtr, Transform> _boneGetTransform_ptr;
    private static delegate* unmanaged<IntPtr, ref Transform, void> _boneSetTransform_ptr;
    private static delegate* unmanaged<IntPtr, UInt16> _boneGetChildCount_ptr;
    private static delegate* unmanaged<IntPtr, IntPtr, UInt16, IntPtr> _boneGetChild_ptr;
#pragma warning restore 0649

    public UInt16 SkeletalIndex
    {
        get
        {
            ErrorIfNull();
            return _boneSkeletalIndex_ptr(_pointer);
        }
    }

    public Transform Transform
    {
        get
        {
            ErrorIfNull();
            return _boneGetTransform_ptr(_pointer);
        }
        set
        {
            ErrorIfNull();
            _boneSetTransform_ptr(_pointer, ref value);
        }
    }

    public Bone Parent(Skeleton skeleton)
    {
        ErrorIfNull();
        return skeleton.GetBone(SkeletalIndex);
    }

    public UInt16 ChildCount
    {
        get
        {
            ErrorIfNull();
            return _boneGetChildCount_ptr(_pointer);
        }
    }

    public Bone GetChild(UInt16 index, Skeleton skeleton)
    {
        ErrorIfNull();
        if (skeleton._pointer == IntPtr.Zero)
        {
            throw new NullReferenceException();
        }
        return new Bone(_boneGetChild_ptr(_pointer, skeleton._pointer, index));
    }
    
    public static bool operator ==(Bone b1, Bone b2)
    {
        return b1._pointer == b2._pointer;
    }

    public static bool operator !=(Bone b1, Bone b2)
    {
        return b1._pointer != b2._pointer;
    }

    public override bool Equals(object? obj)
    {
        throw new NotSupportedException("Bone does not support Equals");
    }

    public override int GetHashCode()
    {
        throw new NotSupportedException("Bone does not support GetHashCode");
    }
    
}