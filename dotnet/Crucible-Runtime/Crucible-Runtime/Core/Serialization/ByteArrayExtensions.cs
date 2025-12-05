using System.Runtime.InteropServices;

namespace Crucible.Core.Serialization;

public static class ByteArrayExtensions
{
    /// <summary>
    /// Insert data objects into a byte array
    /// </summary>
    /// <param name="byteArray">array to insert into</param>
    /// <param name="data">data to insert</param>
    /// <param name="index">index to insert at</param>
    /// <typeparam name="T"></typeparam>
    /// <returns>number of bytes inserted</returns>
    public static int Insert<T>(this byte[] byteArray, T data, int index) where T : unmanaged
    {
        int size = Marshal.SizeOf<T>(data);
        byte[] objectArray = new byte[size];
        GCHandle handle = GCHandle.Alloc(objectArray, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(data, handle.AddrOfPinnedObject(), false);
        }
        finally
        {
            handle.Free();
        }

        for (var i = 0; i < size; i++)
        {
            byteArray[index + i] = byteArray[i];
        }

        return size;
    }
}