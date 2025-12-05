using System.Runtime.InteropServices;

namespace Crucible.Core.ECS;


public unsafe struct ReferenceTypeId : IEquatable<ReferenceTypeId>
{
    private UInt64 _id=0;

    [DllImport("Crucible")]
    private static extern UInt64 CRUCIBLE_NATIVE_EcsGetReferenceTypeID(string name);

    private ReferenceTypeId(UInt64 id)
    {
        _id = id;
    }

    public static ReferenceTypeId For<T>() where T : class
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            return new ReferenceTypeId(CRUCIBLE_NATIVE_EcsGetReferenceTypeID(name));
        }
        return new ReferenceTypeId(0);
    }

    public static bool operator ==(ReferenceTypeId a, ReferenceTypeId b)
    {
        return a._id == b._id;
    }

    public static bool operator !=(ReferenceTypeId a, ReferenceTypeId b)
    {
        return a._id != b._id;
    }

    public override bool Equals(object obj)
    {
        if (obj is ReferenceTypeId id)
        {
            return _id == id._id;
        }
        return false;
    }

    public bool Equals(ReferenceTypeId other)
    {
        return _id == other._id;
    }

    public override int GetHashCode()
    {
        return _id.GetHashCode();
    }
}