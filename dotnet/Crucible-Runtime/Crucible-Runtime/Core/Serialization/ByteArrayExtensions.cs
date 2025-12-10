using System.Runtime.InteropServices;

namespace Crucible.Core.Serialization;

public static unsafe class ByteArrayExtensions
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
        int size = sizeof(T);
        
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        
        try
        {
            Marshal.Copy(handle.AddrOfPinnedObject(),byteArray,index,size);
        }
        finally
        {
            handle.Free();
        }

        return size;
    }
}