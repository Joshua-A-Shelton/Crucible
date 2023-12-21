using System.Runtime.InteropServices;

namespace Crucible;

public unsafe class Texture
{
    public IntPtr lowLevelHandle { get; protected set; }
    [Flags]
    private enum TextureFlags: uint
    {
        None = 0x00000000,
        ManagedMemory = 0x00000001
    }

    private TextureFlags _flags = TextureFlags.None;
    
    private static delegate* unmanaged<IntPtr,void> _destructor_ptr;

    ~Texture()
    {
        if (_flags.HasFlag(TextureFlags.ManagedMemory))
        {
            _destructor_ptr(lowLevelHandle);
        }
    }
}