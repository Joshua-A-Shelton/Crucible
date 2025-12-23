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
        var texture = Texture2D.LoadExchange("Crucible.png");
        if (texture.Width != 600 || texture.Height != 600 || texture.MipCount != 1 || texture.Format != Texture.PixelFormat.R8G8B8A8_UNorm)
        {
            return TestResult.Fail("Texture loaded different than given parameters");
        }
        var texture2 = Texture2D.LoadExchange("Crucible.png",3);
        if (texture2.Width != 600 || texture2.Height != 600 || texture2.MipCount != 3 || texture2.Format != Texture.PixelFormat.R8G8B8A8_UNorm)
        {
            return TestResult.Fail("Texture loaded different than given parameters");
        }
        return TestResult.Pass();
    }

    public static TestResult SaveLoadEngineFormat()
    {
        var groundTexture =  Texture2D.LoadExchange("Crucible.png",4);
        var serialized = groundTexture.Serialize();
        var texture = Texture2D.ReadFromStream(new BinaryReader(new MemoryStream(serialized)));
        if (texture.Width != 600 || texture.Height != 600 || texture.MipCount != 4 || texture.Format != Texture.PixelFormat.R8G8B8A8_UNorm)
        {
            return TestResult.Fail("Texture loaded different than given parameters");
        }
        List<Texture2D.Region>  regions = new List<Texture2D.Region>(4);
        regions.Add(new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(groundTexture.MipWidth(0),groundTexture.MipHeight(0)),0));
        regions.Add(new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(groundTexture.MipWidth(1),groundTexture.MipHeight(1)),1));
        regions.Add(new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(groundTexture.MipWidth(2),groundTexture.MipHeight(2)),2));
        regions.Add(new Texture2D.Region(new Texture2D.Offset(0,0),new Texture2D.Extent(groundTexture.MipWidth(3),groundTexture.MipHeight(3)),3));
        var rawGroundBytes = groundTexture.GetPixels(regions, Texture.PixelAspects.Color);
        var rawLoadedBytes = texture.GetPixels(regions,Texture.PixelAspects.Color);
        if (rawLoadedBytes.Length != rawGroundBytes.Length)
        {
            return TestResult.Fail("loaded texture pixels do not match the original texture");
        }

        for (var i = 0; i < rawGroundBytes.Length; i++)
        {
            if (rawGroundBytes[i] != rawLoadedBytes[i])
            {
                return TestResult.Fail("loaded texture pixels do not match the original texture");
            }
        }
        var serialized2 = texture.Serialize();
        if (serialized2.Length != serialized.Length)
        {
            return TestResult.Fail("Texture serialized data differently between two textures that should be identical");
        }

        for (int i = 0; i < serialized.Length; i++)
        {
            if (serialized[i] != serialized2[i])
            {
                return TestResult.Fail("Texture serialized data differently between two textures that should be identical");
            }
        }
        return TestResult.Pass();
    }

    public static TestResult LoadEngineFormatDeferred()
    {
        var groundTexture =  Texture2D.LoadExchange("Crucible.png",4);
        var serialized = groundTexture.Serialize();
        GPUBatchInitQueue queue = new GPUBatchInitQueue();
        Texture2D? mytexture = null;
        queue.QueueTexture2D(new BinaryReader(new MemoryStream(serialized)),(texture)=>{mytexture = texture;});
        if (mytexture != null)
        {
            return TestResult.Fail("Texture assigned before queue execution");
        }
        queue.Process();
        if (mytexture == null)
        {
            return TestResult.Fail("Texture not assigned before queue execution");
        }
        return TestResult.Pass();
    }

    public static TestResult Formats()
    {
        foreach (Texture.PixelFormat format in Enum.GetValues(typeof(Texture.PixelFormat)))
        {
            if (format == Texture.PixelFormat.None)
            {
                continue;
            }
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
        batchInitQueue.QueueUpdateTexture2D(texture,updates,Texture.PixelAspects.Color);
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