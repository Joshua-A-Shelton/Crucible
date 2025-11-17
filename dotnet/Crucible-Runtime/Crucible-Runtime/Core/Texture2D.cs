using System.Runtime.InteropServices;

namespace Crucible.Core;

public unsafe partial class Texture2D: Texture
{
    public uint Width
    {
        get { return CRUCIBLE_NATIVE_TextureGetWidth(_handle); }
    }
    public uint Height
    {
        get { return CRUCIBLE_NATIVE_TextureGetHeight(_handle); }
    }
    public Texture2D(PixelFormat format,uint width, uint height, uint mips, MultiSampleCount multiSampleCount = MultiSampleCount.One)
    {
        CRUCIBLE_NATIVE_TextureCreate2D(format, width, height, mips, multiSampleCount, out _handle);
    }

    private Texture2D(IntPtr handle)
    {
        _handle = handle;
    }

    public static Texture2D LoadExchange(string path, UInt32 mipLevels = 1)
    {
        if (!Path.Exists(path))
        {
            throw new FileNotFoundException(path);
        }
        IntPtr texturePtr = IntPtr.Zero;
        try
        {
            CRUCIBLE_NATIVE_Texture2DFromExchange(path, mipLevels, out texturePtr);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        if (texturePtr == IntPtr.Zero)
        {
            throw new Exception("Could not load texture");
        }
        return new Texture2D(texturePtr);
    }

    public static Texture2D Load(string path)
    {
        if (!Path.Exists(path))
        {
            throw new FileNotFoundException(path);
        }
        IntPtr texturePtr = IntPtr.Zero;
        try
        {
            var bytes = File.ReadAllBytes(path);
            fixed (byte* ptr = bytes)
            {
                CRUCIBLE_NATIVE_Texture2DFromEngineFormat(ptr, out texturePtr);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        if (texturePtr == IntPtr.Zero)
        {
            throw new Exception("Could not load texture");
        }
        return new Texture2D(texturePtr);
    }

    public override void Save(string path)
    {
        CRUCIBLE_NATIVE_Texture2DToEngineFormat(path, _handle);
    }
    
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_TextureCreate2D(PixelFormat format, UInt32 width, UInt32 height, UInt32 mips, MultiSampleCount multiSampleCount, out IntPtr texture);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_Texture2DFromExchange(string filePath, UInt32 mipLevels, out IntPtr texture);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_Texture2DFromEngineFormat(byte* data, out IntPtr texture);
    [LibraryImport("Crucible", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void CRUCIBLE_NATIVE_Texture2DToEngineFormat(string filePath, IntPtr texture);
    
}