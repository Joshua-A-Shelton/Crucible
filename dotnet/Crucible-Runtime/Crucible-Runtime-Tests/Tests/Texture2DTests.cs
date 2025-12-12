using Crucible.Core;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class Texture2DTests
{
    public static TestResult CreateDefault()
    {
        Texture2D texture = new Texture2D(Texture.PixelFormat.R8G8B8A8_UNorm,32,32,3);
        if (texture.Width != 32 || texture.Height != 32 || texture.MipCount != 3)
        {
            return TestResult.Fail("Texture created different than given parameters");
        }
        return TestResult.Pass();
    }

    public static TestResult LoadExchangeFormat()
    {
        return TestResult.Fail("Not implemented");
    }

    public static TestResult SaveLoadEngineFormat()
    {
        return TestResult.Fail("Not implemented");
    }

    public static TestResult LoadEngineFormatDeferred()
    {
        return TestResult.Fail("Not Implemented");
    }

    public static TestResult Formats()
    {
        
        foreach (Texture.PixelFormat format in Enum.GetValues(typeof(Texture.PixelFormat)))
        {
            Texture2D texture = new Texture2D(format,32,32,1);
            if (texture.Format != format)
            {
                return TestResult.Fail($"Texture not not created with the format: {texture.Format}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult MultiSampleCount()
    {
        foreach (Texture.MultiSampleCount sampleCount in Enum.GetValues(typeof(Texture.MultiSampleCount)))
        {
            Texture2D texture = new Texture2D(Texture.PixelFormat.R8G8B8A8_UNorm,32,32,1, sampleCount);
            if (texture.MultiSample != sampleCount)
            {
                return TestResult.Fail($"Texture not created with the sample count: {sampleCount}");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult GetSetPixels()
    {
        Texture2D texture = new Texture2D(Texture.PixelFormat.R8G8B8A8_UNorm,32,32,3);
        
        Texture2D.Region[] regions = new Texture2D.Region[]
        {
            new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(32,32),0),
            new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(16,16),1),
            new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(8,8),2)
        };

        byte[] bytes = texture.GetPixels(regions,Texture.PixelAspects.Color);
        var pixelSize = Texture.PixelSize(Texture.PixelFormat.R8G8B8A8_UNorm, Texture.PixelAspects.Color);
        if (bytes.Length != (pixelSize * 32 * 32) + (pixelSize * 16 * 16) + (pixelSize * 8 * 8))
        {
            return TestResult.Fail("Texture Pixels not retrieved");
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 0)
            {
                return TestResult.Fail("Texture Pixels are not expected color");
            }
        }

        byte[] firstLevel = new byte[pixelSize * 32 * 32];
        byte[] secondLevel = new byte[pixelSize * 16 * 16];
        byte[] thirdLevel = new byte[pixelSize * 8 * 8];

        for (var i = 0; i < firstLevel.Length; i++)
        {
            firstLevel[i] = 255;
        }

        for (var i = 0; i < secondLevel.Length; i += 4)
        {
            secondLevel[i] = 255;
            secondLevel[i + 1] = 0;
            secondLevel[i + 2] = 0;
            secondLevel[i + 3] = 255;
        }

        for (var i = 0; i < thirdLevel.Length; i += 4)
        {
            thirdLevel[i] = 15;
            thirdLevel[i + 1] = 255;
            thirdLevel[i + 2] = 0;
            thirdLevel[i + 3] = 255;
        }
        
        GPUBatchInitQueue batchInitQueue = new GPUBatchInitQueue();
        
        Texture2D.UpdateRegion[] updates = new Texture2D.UpdateRegion[]
        {
            new Texture2D.UpdateRegion(firstLevel,new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(32,32),0)),
            new Texture2D.UpdateRegion(secondLevel,new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(16,16),1)),
            new Texture2D.UpdateRegion(thirdLevel,new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(8,8),2))
        };
        texture.SetPixels(updates,Texture.PixelAspects.Color,batchInitQueue);
        batchInitQueue.Process();
        
        bytes = texture.GetPixels(regions,Texture.PixelAspects.Color);

        
        if (bytes.Length != firstLevel.Length + secondLevel.Length + thirdLevel.Length)
        {
            return TestResult.Fail("Texture Pixels not retrieved");
        }
        int retrievedIndex = 0;
        for (int i = 0; i < firstLevel.Length; i++, retrievedIndex++)
        {
            if (firstLevel[i] != bytes[retrievedIndex])
            {
                return TestResult.Fail("Texture Pixels are not expected color");
            }
        }

        for (int i = 0; i < secondLevel.Length; i++, retrievedIndex++)
        {
            if (secondLevel[i] != bytes[retrievedIndex])
            {
                return TestResult.Fail("Texture Pixels are not expected color");
            }
        }

        for (int i = 0; i < thirdLevel.Length; i++, retrievedIndex++)
        {
            if (thirdLevel[i] != bytes[retrievedIndex])
            {
                return TestResult.Fail("Texture Pixels are not expected color");
            }
        }
        return TestResult.Pass();
        
    }
    
}