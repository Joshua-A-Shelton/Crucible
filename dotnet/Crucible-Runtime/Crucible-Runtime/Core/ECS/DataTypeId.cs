using System.Runtime.InteropServices;

namespace Crucible.Core.ECS;

public unsafe struct DataTypeId
{
    private UInt64 _id=0;

    [DllImport("Crucible")]
    private static extern UInt64 CRUCIBLE_NATIVE_EcsGetDataTypeID(string name, UInt64 size, UInt64 alignment);

    private DataTypeId(UInt64 id)
    {
        _id = id;
    }

    public static DataTypeId For<T>() where T : unmanaged
    {
        string? name = typeof(T).FullName;
        if (name != null)
        {
            return new DataTypeId(CRUCIBLE_NATIVE_EcsGetDataTypeID(name, (UInt64)sizeof(T),
                (UInt64)Alignment.AlignmentOf<T>()));
        }
        return new DataTypeId(0);
    }
    
    public static bool operator ==(DataTypeId a, DataTypeId b)
    {
        return a._id == b._id;
    }

    public static bool operator !=(DataTypeId a, DataTypeId b)
    {
        return a._id != b._id;
    }
}