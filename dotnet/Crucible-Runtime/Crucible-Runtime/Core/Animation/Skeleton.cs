namespace Crucible.Core.Animation;

public unsafe class Skeleton
{
    internal IntPtr _pointer = IntPtr.Zero;

    public Skeleton(Span<byte> skeletonData)
    {
        fixed (byte* skeletonDataPtr = skeletonData)
        {
            _skeletonInitialize_ptr(ref _pointer,skeletonDataPtr, (ulong)skeletonData.Length);
        }

        if (_pointer == IntPtr.Zero)
        {
            throw new ArgumentException("Provided skeleton data was invalid");
        }
    }

    ~Skeleton()
    {
        _skeletonCleanup_ptr(_pointer);
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref IntPtr, byte*, UInt64, void> _skeletonInitialize_ptr;
    private static delegate* unmanaged<IntPtr, void> _skeletonCleanup_ptr;
    private static delegate* unmanaged<IntPtr, UInt16, IntPtr> _skeletonGetBone_ptr;
#pragma warning restore 0649

    public Bone GetBone(UInt16 index)
    {
        return new Bone(_skeletonGetBone_ptr(_pointer,index));
    }
}