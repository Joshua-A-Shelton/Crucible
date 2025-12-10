using Crucible.Core.Math;
using Crucible.Core.Serialization;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;

[Test]
public static class LZ4Tests
{
    public static TestResult MaxCompressedSize()
    {
        var maxSize = LZ4.MaxCompressedSize(100);
        if (maxSize != 116)
        {
            return TestResult.Fail("Unexpected max compressed size");
        }
        maxSize = LZ4.MaxCompressedSize(4256);
        if (maxSize != 4288)
        {
            return TestResult.Fail("Unexpected max compressed size");
        }
        maxSize = LZ4.MaxCompressedSize(65536);
        if (maxSize != 65809)
        {
            return TestResult.Fail("Unexpected max compressed size");
        }
        return TestResult.Pass();
    }

    public static TestResult CompressRaw()
    {
        byte[] rawData = new byte[1287];
        for (int i = 0; i < rawData.Length; i++)
        {
            rawData[i] = (byte)(i%255);
        }
        var compressedBufferSize = LZ4.MaxCompressedSize(rawData.Length);
        byte[] compressedBuffer =  new byte[compressedBufferSize];
        var finalSize = LZ4.Compress(rawData,compressedBuffer);
        if (finalSize > rawData.Length)
        {
            return TestResult.Fail("Compressed Buffer is larger than raw data");
        }
        bool dataIncluded = false;
        for (int i = 0; i < finalSize; i++)
        {
            if (compressedBuffer[i] != 0)
            {
                dataIncluded = true;
            }
        }

        if (!dataIncluded)
        {
            return TestResult.Fail("Compressed buffer is empty");
        }
        return TestResult.Pass();
    }

    public static TestResult CompressGeneric()
    {
        Vector3[] data = new Vector3[500];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new Vector3(100,-50.0f,.00256f);
        }

        var compressed = LZ4.Compress<Vector3>(data);
        if (compressed.Length >= 500 * sizeof(float)*3)
        {
            return TestResult.Fail("Compressed Buffer is larger than original data");
        }
        return TestResult.Pass();
    }

    public static TestResult Decompress()
    {
        byte[] rawData = new byte[1287];
        for (int i = 0; i < rawData.Length; i++)
        {
            rawData[i] = (byte)(i%255);
        }

        var compressed = LZ4.Compress(rawData.AsSpan());
        var decompressed = LZ4.Decompress(compressed, rawData.Length);
        if (rawData.Length != decompressed.Length)
        {
            return TestResult.Fail("Decompressed buffer is different size than original data");
        }

        for (int i = 0; i < decompressed.Length; i++)
        {
            if (rawData[i] != decompressed[i])
            {
                return TestResult.Fail("Decompressed buffer contains different data than original data");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult DecompressAs()
    {
        Vector3[] data = new Vector3[500];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = new Vector3(100,-50.0f,.00256f);
        }

        var compressed = LZ4.Compress<Vector3>(data);
        var decompressed = LZ4.DecompressAs<Vector3>(compressed, 500);
        if (data.Length != decompressed.Length)
        {
            return TestResult.Fail("Decompressed buffer is different size than original data");
        }

        for (int i = 0; i < decompressed.Length; i++)
        {
            if (data[i] != decompressed[i])
            {
                return TestResult.Fail("Decompressed buffer contains different data than original data");
            }
        }
        return TestResult.Pass();
    }
}