using System.Text;

namespace Crucible.Core;

public unsafe class Material
{
    internal IntPtr _materialPointer = IntPtr.Zero;
    private Dictionary<string,Texture?> _textures = new Dictionary<string, Texture?>();

    public Material(string shaderName)
    {
        _materialInitialize_ptr(ref _materialPointer, shaderName);
        var textureNamesCount = TextureCount;
        for (UInt32 i = 0; i < textureNamesCount; i++)
        {
            _textures[GetTextureName(i)] = null;
        }
    }

    ~Material()
    {
        _materialCleanup_ptr(_materialPointer);
    }
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref IntPtr,string, void> _materialInitialize_ptr;
    private static delegate* unmanaged<IntPtr, void> _materialCleanup_ptr;
    private static delegate* unmanaged<IntPtr, string, bool> _materialHasUniform_ptr;
    private static delegate* unmanaged<IntPtr, string, bool> _materialHasTexture_ptr;
    private static delegate* unmanaged<IntPtr, string, byte*, UInt32, void> _materialSetUniform_ptr;
    private static delegate* unmanaged<IntPtr, string, IntPtr, void> _materialSetTexture_ptr;
    private static delegate* unmanaged<IntPtr, UInt32> _materialGetUniformCount_ptr;
    private static delegate* unmanaged<IntPtr, UInt32> _materialGetTextureCount_ptr;
    private static delegate* unmanaged<IntPtr, UInt32, int> _materialGetUniformNameSize_ptr;
    private static delegate* unmanaged<IntPtr, UInt32, int> _materialGetTextureNameSize_ptr;
    private static delegate* unmanaged<IntPtr, UInt32, byte*, int, void> _materialGetUniformNamePtr_ptr;
    private static delegate* unmanaged<IntPtr,  UInt32, byte*, int, void> _materialGetTextureNamePtr_ptr;
#pragma warning restore 0649
    
    public void SetUniform<T>(string uniformName, T value) where T : unmanaged
    {
        if (!_materialHasUniform_ptr(_materialPointer, uniformName))
        {
            throw new KeyNotFoundException("No uniform named " + uniformName+" was found for this material");
        }

        byte* ptr = (byte*)&value;
        UInt32 size = (UInt32)sizeof(T);
        _materialSetUniform_ptr(_materialPointer, uniformName, ptr, size);

    }

    public void SetTexture(string textureName, Texture texture)
    {
        if (!_materialHasTexture_ptr(_materialPointer, textureName))
        {
            throw new KeyNotFoundException("No texture named " + textureName + " was found for this material");
        }
        _materialSetTexture_ptr(_materialPointer, textureName, texture.LowLevelHandle);
        _textures[textureName] = texture;
    }
    
    public UInt32 UniformCount{get{return _materialGetUniformCount_ptr(_materialPointer);}}
    public UInt32 TextureCount{get{return _materialGetTextureCount_ptr(_materialPointer);}}

    public string GetUniformName(UInt32 uniformIndex)
    {
        if (uniformIndex >= UniformCount)
        {
            throw new IndexOutOfRangeException();
        }
        var nameSize = _materialGetUniformNameSize_ptr(_materialPointer, uniformIndex);
        var chars = new byte[nameSize];
        fixed (byte* ptr = &chars[0])
        {
            _materialGetUniformNamePtr_ptr(_materialPointer, uniformIndex, ptr, nameSize);
        }
        return System.Text.Encoding.UTF8.GetString(chars);
    }

    public string GetTextureName(UInt32 textureIndex)
    {
        if (textureIndex >= TextureCount)
        {
            throw new IndexOutOfRangeException();
        }
        var nameSize = _materialGetTextureNameSize_ptr(_materialPointer, textureIndex);
        var chars = new byte[nameSize];
        fixed (byte* ptr = &chars[0])
        {
            _materialGetTextureNamePtr_ptr(_materialPointer, textureIndex, ptr, nameSize);
        }
        return System.Text.Encoding.UTF8.GetString(chars);
    }
    
}