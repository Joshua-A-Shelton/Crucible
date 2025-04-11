namespace Crucible.Core;
/// <summary>
/// Instance of drawable data
/// </summary>
public unsafe class MeshInstance
{
    /// <summary>
    /// What kind of data is represented by a series of bytes in a buffer inside the graphics card
    /// </summary>
    public enum VertexAttribute
    {
        ///X, Y, and Z position (floats)
        POSITION_3D,
        ///X and Y position (floats)
        POSITION_2D,
        ///Normal vector of vertex (X, Y, and Z floats)
        NORMAL,
        ///UV texture coordinates
        UV,
        ///R, G, B, A values (byte)
        COLOR,
        ///Bone ID (UINT_32) Bone weight (float) (x4)
        BONE_WEIGHT,
        ///Size of the VertexAttribute enum
        VERTEX_ATTRIBUTE_ENUM_SIZE
    }
    /// <summary>
    /// The size of a single entry in the index buffer
    /// </summary>
    public enum IndexSize
    {
        UINT_16,
        UINT_32
    }
    internal IntPtr _pointer = IntPtr.Zero;
    
#pragma warning disable 0649
    private static delegate* unmanaged<ref IntPtr,byte*,UInt64, void> _meshInstanceInitializeSerialized_ptr;
    private static delegate* unmanaged<ref IntPtr, string, void> _meshInstanceFromFile_ptr;
    private static delegate* unmanaged<ref IntPtr, byte*, UInt32, IndexSize, byte*, UInt32, byte*, UInt32, byte*, UInt32, byte*, UInt32, byte*, UInt32, void> _meshInstanceFromMemory_ptr;
    private static delegate* unmanaged<IntPtr, void> _meshInstanceCleanup_ptr;
#pragma warning restore 0649
    /// <summary>
    /// Load a mesh instance from data, formatted according to the crucible mesh chunk specification
    /// </summary>
    /// <param name="meshData"></param>
    /// <exception cref="InvalidDataException"></exception>
    public MeshInstance(Span<byte> meshData)
    {
        fixed (byte* ptr = meshData)
        {
            _meshInstanceInitializeSerialized_ptr(ref _pointer, ptr, (UInt64)meshData.Length);
        }

        if (_pointer == IntPtr.Zero)
        {
            throw new InvalidDataException("Unable to create mesh from data");
        }
    }
    /// <summary>
    /// Load a mesh instance from a crucible mesh file
    /// </summary>
    /// <param name="modelFile"></param>
    /// <exception cref="InvalidDataException"></exception>
    public MeshInstance(string modelFile)
    {
        _meshInstanceFromFile_ptr(ref _pointer, modelFile);
        if (_pointer == IntPtr.Zero)
        {
            throw new InvalidDataException("Unable to create mesh from data");
        }
    }
    /// <summary>
    /// Load a mesh instance by directly supplying the raw data that will be uploaded to the GPU
    /// </summary>
    /// <param name="indexData">Raw bytes that represent the indices of a draw</param>
    /// <param name="indexBufferSize">Size of the <paramref name="indexData"/> byte span</param>
    /// <param name="indexSize">If the entries in <paramref name="indexData"/> are 16 bit or 32 bit width indices</param>
    /// <param name="vertexData">Raw bytes that represent the 3d positions of the vertices</param>
    /// <param name="vertexDataSize">Size of the <paramref name="vertexData"/> span</param>
    /// <param name="normals">Raw bytes that represent the normals of the vertices</param>
    /// <param name="normalDataSize">Size of the <paramref name="normals"/> span, can be zero if not supplying normals</param>
    /// <param name="uvs">Raw bytes that represent the UV coordinates of the vertices</param>
    /// <param name="uvDataSize">Size of the <paramref name="uvs"/> span, can be zero if not supplying uv coordinates</param>
    /// <param name="colors">Raw bytes that represent vertex colors</param>
    /// <param name="colorDataSize">Size of the <paramref name="colors"/> span, can be zero if not supplying vertex colors</param>
    /// <param name="boneWeights">Raw bytes that represent bone weights</param>
    /// <param name="boneWeightDataSize">Size of the <paramref name="boneWeights"/> span, can be zero if not supplying bone weights</param>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="InvalidDataException"></exception>
    public MeshInstance(Span<byte> indexData, UInt32 indexBufferSize, IndexSize indexSize, Span<byte> vertexData, UInt32 vertexDataSize, Span<byte> normals, UInt32 normalDataSize, Span<byte> uvs, UInt32 uvDataSize, Span<byte> colors, UInt32 colorDataSize, Span<byte> boneWeights, UInt32 boneWeightDataSize)
    {
        var indexCount = indexData.Length / (indexSize == IndexSize.UINT_16 ? sizeof(UInt16) : sizeof(UInt32));
        if (indexCount < 3 || indexCount % 3 != 0)
        {
            throw new ArgumentException("Mesh must have at least 3 indices, and have a multiple of 3 total");
        }
        var vertexCount = vertexDataSize / (sizeof(float) * 3);
        if (vertexCount < 3)
        {
            throw new ArgumentException("Mesh must be made of at least 3 vertices");
        }
        var normalCount = normalDataSize / (sizeof(float) * 3);
        if (normalCount != 0 && normalCount != vertexCount)
        {
            throw new ArgumentException("Number of Mesh normals must match number of vertices, or be zero");
        }
        var uvCount = uvDataSize / (sizeof(float) * 2);
        if (uvCount != 0 && uvCount != vertexCount)
        {
            throw new ArgumentException("Number of Mesh UV's must match number of vertices, or be zero");
        }
        var colorCount = colorDataSize / (sizeof(char) * 4);
        if (colorCount != 0 && colorCount != vertexCount)
        {
            throw new ArgumentException("Number of Mesh vertex colors must match number of vertices, or be zero");
        }
        var boneWeightCount = boneWeightDataSize / ((sizeof(UInt16) + sizeof(float)) * 4);
        if (boneWeightCount != 0 && boneWeightCount != vertexCount)
        {
            throw new ArgumentException("Number of Mesh bone weights must match number of vertices, or be zero");
        }
        
        fixed (byte* i = indexData)
        {
            fixed (byte* v = vertexData)
            {
                fixed (byte* n = normals)
                {
                    fixed (byte* u = uvs)
                    {
                        fixed (byte* c = colors)
                        {
                            fixed (byte* b = boneWeights)
                            {
                                _meshInstanceFromMemory_ptr(ref _pointer,i,indexBufferSize, indexSize, v, vertexDataSize, n, normalDataSize, u, uvDataSize, c,colorDataSize, b,boneWeightDataSize);
                            }
                        }
                    }
                }
            }
        }
        
        if (_pointer == IntPtr.Zero)
        {
            throw new InvalidDataException("Unable to create mesh from data");
        }
    }

    ~MeshInstance()
    {
        _meshInstanceCleanup_ptr(_pointer);
    }
}