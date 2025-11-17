using System.Runtime.CompilerServices;

namespace Crucible.Core.ECS;

internal struct AlignmentHelper<T> where T: unmanaged
{
    public byte Padding;
    public T Target;
}

internal class Alignment
{
    [MethodImpl(MethodImplOptions.AggressiveInlining), SkipLocalsInit]
    public static int AlignmentOf<T>() where T: unmanaged
    {
        Unsafe.SkipInit(out AlignmentHelper<T> helper);
        var alignment = Unsafe.ByteOffset(in helper.Padding, in Unsafe.As<T, byte>(ref helper.Target));
        return Unsafe.As<nint, int>(ref alignment);
    }
}