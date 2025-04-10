using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace Crucible.Initialization;

public class BlittableHelper
{
    public static bool IsBlittable<T>()
    {
        return IsBlittableCache<T>.Value;
    }

    public static bool IsBlittable(Type type)
    {
        if(type.IsArray)
        {
            var elem = type.GetElementType();
            if (elem == null)
            {
                return false;
            }
            return elem.IsValueType && IsBlittable(elem);
        }
        try{
            var instance = Activator.CreateInstance(type);
            if (instance == null)
            {
                return false;
            }
            GCHandle.Alloc(instance, GCHandleType.Pinned).Free();
            return true;
        }catch{
            return false;
        }
    }

    private static class IsBlittableCache<T>
    {
        public static readonly bool Value = IsBlittable(typeof(T));
    }
    //disable warning, alignment helper isn't used for anything other than calculating sizes, so it doesn't matter if it's never assigned
#pragma warning disable 0649
    internal struct AlignmentHelper<T> where T : unmanaged
    {
        public byte Padding;
        public T Target;
    }
#pragma warning restore 0649
    public static int AlignmentOf<T>() where T : unmanaged
    {
        return (int)Marshal.OffsetOf<AlignmentHelper<T>>(nameof(AlignmentHelper<T>.Target));
    }
}