using System.Runtime.InteropServices;

namespace Crucible.Core;

public abstract partial class Texture
{
    /// <summary>
    /// Underlying texel size and layout
    /// </summary>
    public enum PixelFormat
    {
        R32G32B32A32_Float = 1,
        R32G32B32A32_Uint = 2,
        R32G32B32A32_SInt = 3,
        R16G16B16A16_Float = 7,
        R16G16B16A16_UNorm = 8,
        R16G16B16A16_UInt = 9,
        R16G16B16A16_SNORM = 10,
        R16G16B16A16_SInt = 11,
        R32G32_Float = 12,
        R32G32_UInt = 13,
        R32G32_SInt = 14,
        D32_Float_S8X24_UInt = 15,
        R10G10B10A2_UNorm = 16,
        R10G10B10A2_UInt = 17,
        R11G11B10_Float = 18,
        R8G8B8A8_UNorm = 19,
        R8G8B8A8_UNorm_sRGB = 20,
        R8G8B8A8_UInt = 21,
        R8G8B8A8_SNORM = 22,
        R8G8B8A8_SInt = 23,
        R16G16_Float = 24,
        R16G16_UNorm = 25,
        R16G16_UInt = 26,
        R16G16_SNORM = 27,
        R16G16_SInt = 28,
        D32_Float = 29,
        R32_Float = 30,
        R32_UInt = 31,
        R32_SInt = 32,
        R8G8_UNorm = 34,
        R8G8_UInt = 35,
        R8G8_SNORM = 36,
        R8G8_SInt = 37,
        R16_Float = 38,
        D16_UNorm = 39,
        R16_UNorm = 40,
        R16_UInt = 41,
        R16_SNORM = 42,
        R16_SInt = 43,
        R8_UNorm = 44,
        R8_UInt = 45,
        R8_SNORM = 46,
        R8_SInt = 47,
        A8_UNorm = 48,
        R9G9B9E5_SharedExp = 49,
        BC1_UNorm = 52,
        BC1_UNorm_sRGB = 53,
        BC2_UNorm = 54,
        BC2_UNorm_sRGB = 55,
        BC3_UNorm = 56,
        BC3_UNorm_sRGB = 57,
        BC4_UNorm = 58,
        BC4_SNORM = 59,
        BC5_UNorm = 60,
        BC5_SNORM = 61,
        B5G6R5_UNorm = 62,
        B5G5R5A1_UNorm = 63,
        B8G8R8A8_UNorm = 64,
        B8G8R8X8_UNorm = 65,
        B8G8R8A8_UNorm_sRGB = 66,
        B8G8R8X8_UNorm_sRGB = 67,
        BC6H_UF16 = 68,
        BC6H_SF16 = 69,
        BC7_UNorm = 70,
        BC7_UNorm_sRGB = 71,
        AYUV = 72,
        NV12 = 73,
        Opaque_420 = 74,
        B4G4R4A4_UNorm = 76
    }
    
    public enum MultiSampleCount
    {
        One = 1,
        Two = 2,
        Four = 4,
        Eight = 8,
    }
    protected IntPtr _handle =  IntPtr.Zero;
    
    [LibraryImport("Crucible")]
    protected static partial void CRUCIBLE_NATIVE_TextureDestroy(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial uint CRUCIBLE_NATIVE_TextureGetWidth(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial uint CRUCIBLE_NATIVE_TextureGetHeight(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial uint CRUCIBLE_NATIVE_TextureGetDepth(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial uint CRUCIBLE_NATIVE_TextureGetArraySize(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial uint CRUCIBLE_NATIVE_TextureGetMipCount(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial PixelFormat CRUCIBLE_NATIVE_TextureGetFormat(IntPtr textureHandle);
    [LibraryImport("Crucible")]
    protected static partial MultiSampleCount CRUCIBLE_NATIVE_TextureGetSampleCount(IntPtr textureHandle);


    ~Texture()
    {
        CRUCIBLE_NATIVE_TextureDestroy(_handle);
    }
    
    public PixelFormat Format
    {
        get { return CRUCIBLE_NATIVE_TextureGetFormat(_handle); }
    }
    
    public MultiSampleCount MultiSample
    {
        get { return CRUCIBLE_NATIVE_TextureGetSampleCount(_handle); }
    }

    public uint MipCount
    {
        get { return CRUCIBLE_NATIVE_TextureGetMipCount(_handle); }
    }
    
    public abstract byte[] Serialize();
    
}