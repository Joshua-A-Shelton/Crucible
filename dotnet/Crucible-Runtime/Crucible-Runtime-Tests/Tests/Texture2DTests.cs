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

    public static TestResult SerializeTexture()
    {
        return TestResult.Fail("Not implemented");
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
}