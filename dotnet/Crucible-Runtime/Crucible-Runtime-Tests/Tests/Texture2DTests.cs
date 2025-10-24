using Crucible.Core;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;
[Test]
public static class Texture2DTests
{
    public static bool CreateDefault()
    {
        Texture2D texture = new Texture2D(Texture.PixelFormat.R8G8B8A8_UNorm,32,32,3);
        if (texture.Width != 32 || texture.Height != 32 || texture.MipCount != 3)
        {
            return false;
        }
        return true;
    }

    public static bool LoadEngineFormat()
    {
        return false;
    }

    public static bool LoadExchangeFormat()
    {
        return false;
    }

    public static bool Formats()
    {
        
        foreach (Texture.PixelFormat format in Enum.GetValues(typeof(Texture.PixelFormat)))
        {
            Texture2D texture = new Texture2D(format,32,32,1);
            if (texture.Format != format)
            {
                return false;
            }
        }
        return true;
    }

    public static bool MultiSampleCount()
    {
        foreach (Texture.MultiSampleCount sampleCount in Enum.GetValues(typeof(Texture.MultiSampleCount)))
        {
            Texture2D texture = new Texture2D(Texture.PixelFormat.R8G8B8A8_UNorm,32,32,1);
            if (texture.MultiSample != sampleCount)
            {
                return false;
            }
        }
        return true;
    }
}