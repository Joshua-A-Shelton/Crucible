using System.Runtime.InteropServices;

namespace Crucible;

public unsafe class Texture
{
    public IntPtr lowLevelHandle { get; protected set; } = IntPtr.Zero;
    [Flags]
    private enum TextureFlags: uint
    {
        None = 0x00000000,
        ManagedMemory = 0x00000001
    }

    private TextureFlags _flags = TextureFlags.None;
    
    public enum TextureType
    {
        Color,
        Depth
    }

    private static delegate* unmanaged<int, int, Vector4, int, bool, IntPtr> _createColorTexture_ptr;
    private static delegate* unmanaged<int, int, IntPtr> _createDepthTexture_ptr;

    public Texture(int width, int height, Vector4 defaultColor, TextureType type = TextureType.Color, int mipLevels = 1, bool renderTargetable = false)
    {
        switch (type)
        {
            case TextureType.Color:
                lowLevelHandle = _createColorTexture_ptr(width, height, defaultColor,mipLevels,renderTargetable);
                break;
            case TextureType.Depth:
                lowLevelHandle = _createDepthTexture_ptr(width, height);
                break;
        }

        _flags |= TextureFlags.ManagedMemory;
    }

    private static delegate* unmanaged<IntPtr,void> _deleteTexture_ptr;

    ~Texture()
    {
        if (_flags.HasFlag(TextureFlags.ManagedMemory))
        {
            _deleteTexture_ptr(lowLevelHandle);
        }
    }

    private static delegate* unmanaged<IntPtr, int> _textureWidth_ptr;
    private static delegate* unmanaged<IntPtr, int> _textureHeight_ptr;
    
    public int Width
    {
        get { return _textureWidth_ptr(lowLevelHandle); }
    }

    public int Height
    {
        get { return _textureHeight_ptr(lowLevelHandle); }
    }
    
}