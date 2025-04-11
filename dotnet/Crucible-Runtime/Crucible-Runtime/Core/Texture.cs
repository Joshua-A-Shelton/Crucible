namespace Crucible.Core;
/// <summary>
/// Collection of texels
/// </summary>
public unsafe class Texture
{
    /// <summary>
    /// Underlying texel size and layout
    /// </summary>
    public enum Format
    {
        UNDEFINED,
        R32G32B32A32_FLOAT,
        R32G32B32A32_UINT,
        R32G32B32A32_SINT,
        R32G32B32_FLOAT,
        R32G32B32_UINT,
        R32G32B32_SINT,
        R16G16B16A16_FLOAT,
        R16G16B16A16_UNORM,
        R16G16B16A16_UINT,
        R16G16B16A16_SNORM,
        R16G16B16A16_SINT,
        R32G32_FLOAT,
        R32G32_UINT,
        R32G32_SINT,
        D32_FLOAT_S8X24_UINT,
        R10G10B10A2_UNORM,
        R10G10B10A2_UINT,
        R11G11B10_FLOAT,
        R8G8B8A8_UNORM,
        R8G8B8A8_UNORM_SRGB,
        R8G8B8A8_UINT,
        R8G8B8A8_SNORM,
        R8G8B8A8_SINT,
        R16G16_FLOAT,
        R16G16_UNORM,
        R16G16_UINT,
        R16G16_SNORM,
        R16G16_SINT,
        D32_FLOAT,
        R32_FLOAT,
        R32_UINT,
        R32_SINT,
        D24_UNORM_S8_UINT,
        R8G8_UNORM,
        R8G8_UINT,
        R8G8_SNORM,
        R8G8_SINT,
        R16_FLOAT,
        D16_UNORM,
        R16_UNORM,
        R16_UINT,
        R16_SNORM,
        R16_SINT,
        R8_UNORM,
        R8_UINT,
        R8_SNORM,
        R8_SINT,
        A8_UNORM,
        R9G9B9E5_SHAREDEXP,
        R8G8_B8G8_UNORM,
        G8R8_G8B8_UNORM,
        BC1_UNORM,
        BC1_UNORM_SRGB,
        BC2_UNORM,
        BC2_UNORM_SRGB,
        BC3_UNORM,
        BC3_UNORM_SRGB,
        BC4_UNORM,
        BC4_SNORM,
        BC5_UNORM,
        BC5_SNORM,
        B5G6R5_UNORM,
        B5G5R5A1_UNORM,
        B8G8R8A8_UNORM,
        B8G8R8X8_UNORM,
        B8G8R8A8_UNORM_SRGB,
        B8G8R8X8_UNORM_SRGB,
        BC6H_UF16,
        BC6H_SF16,
        BC7_UNORM,
        BC7_UNORM_SRGB,
        AYUV,
        NV12,
        OPAQUE_420,
        YUY2,
        B4G4R4A4_UNORM
    }
    /// <summary>
    /// Dimensionality of the texture
    /// </summary>
    public enum TextureType
    {
        TEXTURE_1D,
        TEXTURE_2D,
        TEXTURE_3D,
        CUBE_MAP
    }
    /// <summary>
    /// How many pixels to use when multisampling
    /// </summary>
    public enum MultiSamplePixels
    {
        ONE = 1,
        TWO = 2,
        FOUR = 4,
        EIGHT = 8,
    }
    private IntPtr _texturePointer = IntPtr.Zero;
    private bool _ownsMemory = false;
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref IntPtr, TextureType, Format, UInt32, UInt32, UInt32, UInt32, byte, bool,bool,bool, void> _textureInitBlank_ptr;
    private static delegate* unmanaged<ref IntPtr, string, Format, UInt32, void> _textureInitFromPath_ptr;
    private static delegate* unmanaged<ref IntPtr, byte*, Format, UInt32, UInt32, UInt32, void> _textureInitFromRaw_ptr;
    private static delegate* unmanaged<IntPtr, void> _textureCleanResources_ptr;
    private static delegate* unmanaged<IntPtr, Format> _texturePixelFormat_ptr;
    private static delegate* unmanaged<IntPtr, TextureType> _textureType_ptr;
    private static delegate* unmanaged<IntPtr, UInt32> _textureWidth_ptr;
    private static delegate* unmanaged<IntPtr, UInt32> _textureHeight_ptr;
    private static delegate* unmanaged<IntPtr, UInt32> _textureDepth_ptr;
    private static delegate* unmanaged<IntPtr, UInt32> _textureMipMapLevels_ptr;
#pragma warning restore 0649
    /// <summary>
    /// Handle to the texture at the low level
    /// </summary>
    public IntPtr LowLevelHandle{get{return _texturePointer;}}
    /// <summary>
    /// The format the texels in this texture are layed out as
    /// </summary>
    public Format PixelFormat{get{return _texturePixelFormat_ptr(_texturePointer);}}
    /// <summary>
    /// The dimensionality of this texture
    /// </summary>
    public TextureType Type
    {
        get { return _textureType_ptr(_texturePointer); }
    }
    /// <summary>
    /// Number of texels wide this texture is 
    /// </summary>
    public UInt32 Width{get{return _textureWidth_ptr(_texturePointer);}}
    /// <summary>
    /// Number of texels tall this texture is, (1 for 1D textures)
    /// </summary>
    public UInt32 Height{get{return _textureHeight_ptr(_texturePointer);}}
    /// <summary>
    /// Number of texels deep this texture is (in the case of 3D textures, or number of array slices in 1D or 2D textures, or just 6 in case of cubemap, 1 otherwise)
    /// </summary>
    public UInt32 Depth{get{return _textureDepth_ptr(_texturePointer);}}
    /// <summary>
    /// Number of downsized copies this texture has to use in sampling
    /// </summary>
    public UInt32 MipMapLevels{get{return _textureMipMapLevels_ptr(_texturePointer);}}
    
    internal Texture(TextureType textureType, Format format, UInt32 width, UInt32 height, UInt32 mipLevels, UInt32 layers, MultiSamplePixels multiSamplePixels, bool shaderResource, bool renderTarget, bool depthTarget)
    {
        if (!(shaderResource || renderTarget || depthTarget))
        {
            throw new ArgumentException("Must have at least one usage");
        }
        _textureInitBlank_ptr(ref _texturePointer,textureType, format, width, height, mipLevels,layers, (byte)multiSamplePixels,shaderResource,renderTarget,depthTarget);
        _ownsMemory = true;
    }
    /// <summary>
    /// Load a texture from a file
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <param name="format">R8G8B8A8_UNORM or R8G8B8A8_UINT representation of the pixels</param>
    /// <param name="mipLevels">Number of downsized copies of the image to have for sampling purposes</param>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="InvalidDataException"></exception>
    public Texture(string path, Format format, UInt32 mipLevels)
    {
        if (!Path.Exists(path))
        {
            throw new FileNotFoundException(string.Format("Path '{0}' does not exist", path));
        }

        if (!isValidInterchangeFormat(format))
        {
            throw new ArgumentException(string.Format("Format '{0}' is not supported", format));
        }
        
        _textureInitFromPath_ptr(ref _texturePointer, path, format, mipLevels);
        if (_texturePointer == IntPtr.Zero)
        {
            throw new InvalidDataException(string.Format("Unable to load texture from file'{0}'",path));
        }
        _ownsMemory = true;
    }
    /// <summary>
    /// Create a texure from raw texel data
    /// </summary>
    /// <param name="texelData">Raw texel data</param>
    /// <param name="format">Format of the texels in <paramref name="texelData"/></param>
    /// <param name="width">Width of the texture</param>
    /// <param name="height">Height of the texture</param>
    /// <param name="mipLevels">Number of downsized copies of the image to have for sampling purposes</param>
    public Texture(Span<byte> texelData, Format format, UInt32 width, UInt32 height, UInt32 mipLevels)
    {
        fixed (byte* texelDataPtr = texelData)
        {
            _textureInitFromRaw_ptr(ref _texturePointer, texelDataPtr, format, width, height, mipLevels);
            _ownsMemory = true;
        }
    }

    internal Texture(IntPtr texturePointer)
    {
        _texturePointer = texturePointer;
        _ownsMemory = false;
    }

    ~Texture()
    {
        if (_ownsMemory)
        {
            _textureCleanResources_ptr(_texturePointer);
        }
    }

    private bool isValidInterchangeFormat(Format format)
    {
        switch (format)
        {
            case Format.R8G8B8A8_UINT:
                return true;
            case Format.R8G8B8A8_UNORM:
                return true;
        }
        return false;
    }
}