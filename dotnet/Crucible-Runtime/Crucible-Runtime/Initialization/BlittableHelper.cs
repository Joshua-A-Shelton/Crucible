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
    
    internal struct AlignmentHelper<T> where T : unmanaged
    {
        public byte Padding;
        public T Target;
    }

    public static int AlignmentOf<T>() where T : unmanaged
    {
        return (int)Marshal.OffsetOf<AlignmentHelper<T>>(nameof(AlignmentHelper<T>.Target));
    }
}