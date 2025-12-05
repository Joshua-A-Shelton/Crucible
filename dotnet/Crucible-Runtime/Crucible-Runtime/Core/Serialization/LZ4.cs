using System.Runtime.InteropServices;

namespace Crucible.Core.Serialization;

public static unsafe partial class LZ4
{
    [LibraryImport("Crucible",EntryPoint = "CRUCIBLE_NATIVE_LZ4MaxCompressedSize")]
    public static partial Int32 MaxCompressedSize(Int32 uncompressedSize);
    [LibraryImport("Crucible")]
    private static partial Int32 CRUCIBLE_NATIVE_LZ4Compress(void* uncompressedData, Int32 uncompressedLength, void* compressedBuffer, Int32 compressedLength);

    public static Int32 Compress(Span<byte> uncompressedData, Span<byte> compressedBuffer)
    {
        fixed (byte* uncompressedPtr = uncompressedData,  compressedPtr = compressedBuffer)
        {
            return CRUCIBLE_NATIVE_LZ4Compress(uncompressedPtr,uncompressedData.Length,compressedPtr,compressedBuffer.Length);
        }
    }

    public static byte[] Compress<T>(Span<T> uncompressedData)where T: unmanaged
    {
        var uncompressedBufferSize = uncompressedData.Length * sizeof(T);
        var compressedBufferSize = MaxCompressedSize(uncompressedData.Length*sizeof(T));
        byte[] compressedBuffer = new byte[compressedBufferSize];
        int actualSize = 0;
        fixed (T* uncompressedPtr = uncompressedData)
        {
            fixed (byte* compressedBufferPtr = compressedBuffer)
            {
                actualSize = CRUCIBLE_NATIVE_LZ4Compress(uncompressedPtr,uncompressedBufferSize,compressedBufferPtr,uncompressedBufferSize);
            }
        }
        byte[] compressedData = new byte[actualSize];
        System.Buffer.BlockCopy(compressedBuffer, 0, compressedData, 0, actualSize);
        return compressedData;
    }
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_LZ4Decompress(void* compressedData, Int32 compressedLength, void* decompressedBuffer, Int32 decompressedBufferSize);

    public static byte[] Decompress(Span<byte> compressedBuffer, Int32 uncompressedLength)
    {
        byte[] uncompressedData = new byte[uncompressedLength];
        fixed (byte* compressedPtr = compressedBuffer, decompressedPtr = uncompressedData)
        {
            CRUCIBLE_NATIVE_LZ4Decompress(compressedPtr,compressedBuffer.Length,decompressedPtr,uncompressedLength);
        }
        return uncompressedData;
    }

    public static T[] DecompressAs<T>(Span<byte> compressedBuffer, Int32 uncompressedElements)where T: unmanaged
    {
        T[] uncompressedData = new T[uncompressedElements];
        var uncompressedDataLength = uncompressedData.Length* sizeof(T);
        fixed (byte* compressedBufferPtr = compressedBuffer)
        {
            fixed (T* uncompressedDataPtr = uncompressedData)
            {
                CRUCIBLE_NATIVE_LZ4Decompress(compressedBufferPtr,compressedBuffer.Length,uncompressedDataPtr,uncompressedDataLength);

            }    
        }

        return uncompressedData;

    }
}

public class LZ4CompressionData
{
    public byte[] CompressedData { get; private set; }
    public Int32 CompressedDataSize { get{return CompressedData.Length;} }
    public Int32 UncompressedDataSize { get; private set; }

    public LZ4CompressionData(Span<byte> uncompressedData)
    {
        CompressedData = LZ4.Compress(uncompressedData);
        UncompressedDataSize = uncompressedData.Length;
    }
    
}