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

    public static bool LoadExchangeFormat()
    {
        var tex1 = Texture2D.LoadExchange("Crucible.png");
        if (tex1.Width != 600 || tex1.Height != 600 || tex1.MipCount != 1 || tex1.Format != Texture.PixelFormat.R8G8B8A8_UNorm || tex1.MultiSample!= Texture.MultiSampleCount.One)
        {
            return false;
        }
        var tex2 = Texture2D.LoadExchange("Crucible.png",3);
        if (tex2.Width != 600 || tex2.Height != 600 || tex2.MipCount != 3 || tex2.Format != Texture.PixelFormat.R8G8B8A8_UNorm || tex2.MultiSample!= Texture.MultiSampleCount.One)
        {
            return false;
        }
            
        return true;
    }

    public static bool SaveLoadEngineFormat()
    {
        var save = Texture2D.LoadExchange("Crucible.png");
        string path = "Crucible.ctxr";
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        save.Save(path);
        var loaded = Texture2D.Load(path);
        if (loaded.Width == save.Width && loaded.Height == save.Height && loaded.MipCount == save.MipCount &&
            loaded.Format == save.Format)
        {
            File.Delete(path);
            return true;
        }
        File.Delete(path);
        return false;
    }

    /*public static bool Formats()
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
            Texture2D texture = new Texture2D(Texture.PixelFormat.R8G8B8A8_UNorm,32,32,1, sampleCount);
            if (texture.MultiSample != sampleCount)
            {
                return false;
            }
        }
        return true;
    }*/
}