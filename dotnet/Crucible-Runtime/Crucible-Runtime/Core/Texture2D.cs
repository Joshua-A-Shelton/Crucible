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
    
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_TextureCreate2D(PixelFormat format, UInt32 width, UInt32 height, UInt32 mips, MultiSampleCount multiSampleCount, out IntPtr texture);

    public override byte[] Serialize()
    {
        throw new NotImplementedException();
    }
}