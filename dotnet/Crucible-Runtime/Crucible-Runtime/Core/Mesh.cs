using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Core.Exceptions;
using Crucible.Core.Serialization;
using Vector2 = Crucible.Core.Math.Vector2;
using Vector3 = Crucible.Core.Math.Vector3;

namespace Crucible.Core;


/// <summary>
/// A collection of vertex data and indexes usable for drawing with shaders
/// </summary>
public unsafe partial class Mesh
{
    private IntPtr _handle =  IntPtr.Zero;
    [Flags]
    public enum VertexAttributes: UInt16
    {
        ///X, Y, and Z position (floats)
        Position = 1,
        ///Normal vector of vertex (X, Y, and Z floats)
        Normal=1<<1,
        ///Tangent vector of vertex (X,Y, and X floats)
        Tangent=1<<2,
        ///R, G, B, A values (byte)
        Color=1<<3,
        ///Bone ID (UINT_16)(x4), Bone weight (float)(x4)
        BoneWeight=1<<4,
        ///Primary UV texture coordinates (U and V floats)
        UV=1<<5,
        ///Second set of UV texture coordinates (U and V floats)
        UV2=1<<6,
        ///Third set of UV texture coordinates (U and V floats)
        UV3=1<<7,
        ///Fourth set of UV texture coordinates (U and V floats)
        UV4=1<<8
    }
    public enum IndexSize: Int32
    {
        UInt16=0,
        UInt32=1,
    }

    private Mesh(IntPtr handle)
    {
        _handle = handle;
    }

    ~Mesh()
    {
        CRUCIBLE_NATIVE_MeshDelete(_handle);
    }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct MeshAttributeData
    {
        public byte* positionBuffer = null;
        public byte* normalBuffer = null;
        public byte* tangentBuffer = null;
        public byte* colorBuffer = null;
        public byte* boneWeightBuffer = null;
        public byte* uvBuffer = null;
        public byte* uv2Buffer = null;
        public byte* uv3Buffer = null;
        public byte* uv4Buffer = null;
        public UInt32 vertexCount = 0;
        public MeshAttributeData(){}
    }
    
    public struct MeshBufferAccessibility
    {
        public Buffer.Accessibility Index = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Position = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Normal = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Tangent = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Color = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility BoneWeight = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Uv = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Uv2 = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Uv3 = Buffer.Accessibility.Gpu;
        public Buffer.Accessibility Uv4 = Buffer.Accessibility.Gpu;
        public MeshBufferAccessibility(){}
    }
    
    private class MeshDeferredInit: IDeferredInit
    {
        public Mesh? Mesh { get; set; }
        public Action<Mesh>? Callback { get; set; }
        
        public void Initialize()
        {
            Callback(Mesh);
        }
    }


    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_MeshNew(ref MeshAttributeData attributeData, UInt32 vertexCount, void* indexData, IndexSize indexSize, UInt32 indexCount, ref MeshBufferAccessibility bufferAccessibility);
    [LibraryImport("Crucible")]
    private static partial IntPtr CRUCIBLE_NATIVE_MeshNewBatchedInit(ref MeshAttributeData attributeData, UInt32 vertexCount, void* indexData, IndexSize indexSize, UInt32 indexCount, ref MeshBufferAccessibility bufferAccessibility, IntPtr deferredQueueHandle, IntPtr IDeferredInitHandle);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshDelete(IntPtr handle);
    [LibraryImport("Crucible")]
    private static partial VertexAttributes CRUCIBLE_NATIVE_MeshDefinedVertexAttributeFlags(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial UInt32 CRUCIBLE_NATIVE_MeshVertexCount(IntPtr handle);
    [LibraryImport("Crucible")]
    private static partial UInt32 CRUCIBLE_NATIVE_MeshIndexCount(IntPtr handle);
    [LibraryImport("Crucible")]
    private static partial IndexSize CRUCIBLE_NATIVE_MeshIndexSize(IntPtr handle);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyPositionData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyNormalData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyTangentData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyColorData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyBoneWeightData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyUVData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyUV2Data(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyUV3Data(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyUV4Data(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial void CRUCIBLE_NATIVE_MeshCopyIndexData(IntPtr handle, void* array);
    [LibraryImport("Crucible")]
    private static partial Buffer.Accessibility CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(IntPtr meshHandle,VertexAttributes attribute);
    [LibraryImport("Crucible")]
    private static partial Buffer.Accessibility CRUCIBLE_NATIVE_MeshIndexAttributeBufferAccess(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshPositionBufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshNormalBufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshTangentBufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshColorBufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshBoneWeightBufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshUVBufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshUV2BufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshUV3BufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshUV4BufferCpuHandle(IntPtr meshHandle);
    [LibraryImport("Crucible")]
    private static partial void* CRUCIBLE_NATIVE_MeshIndexBufferCpuHandle(IntPtr meshHandle);
    
    /// <summary>
    /// Create a mesh from binary data
    /// </summary>
    /// <param name="reader">Binary reader of mesh data</param>
    /// <param name="accessibility">Accessibility of mesh data buffers</param>
    /// <returns></returns>
    internal static Mesh ReadFromStream(BinaryReader reader, MeshBufferAccessibility accessibility)
    {
        return new Mesh(MeshConstruct(reader, accessibility,null,null));
    }
    /// <summary>
    /// Create a mesh from binary data, but delay initialization into a GPUBatchInitQueue
    /// </summary>
    /// <param name="reader">Binary reader of mesh data</param>
    /// <param name="accessibility">Accessibility of mesh data buffers</param>
    /// <param name="initQueue">Queue that batches a series of GPU operations</param>
    /// <param name="onFinish">Action to perform when initialization is finished</param>
    internal static void ReadFromStreamBatchedInit(BinaryReader reader, MeshBufferAccessibility accessibility, GPUBatchInitQueue initQueue, Action<Mesh> onFinish)
    {
        MeshConstruct(reader, accessibility, initQueue, onFinish);
    }
    //TODO: this is a really messy method. It works, but it's dumb, but it is the way it is because it's responsible for complex decoding and memory management that shouldn't be duplicated. It should probably be refactored so it's not actually doing two different things
    private static IntPtr MeshConstruct(BinaryReader reader, MeshBufferAccessibility accessibility, GPUBatchInitQueue? initQueue, Action<Mesh>? onFinish)
    {
        var magicNumber = reader.ReadChars(5);
        if (magicNumber[0] != 'c' && magicNumber[1] != 'm' && magicNumber[2] != 's' && magicNumber[3] != 'h' &&
            magicNumber[4] != '\n')
        {
            throw new FormatException("Invalid mesh format");
        }

        var attributeFlags = reader.ReadUInt16();
        var vertexCount = reader.ReadUInt32();
        var vertexTypeRaw = reader.ReadUInt32();
        var vertexSize = IndexSize.UInt16;
        if (vertexTypeRaw != 0)
        {
            vertexSize = IndexSize.UInt32;
        }

        var indexCount = reader.ReadUInt32();

        if ((attributeFlags & (UInt16)VertexAttributes.Position) == 0)
        {
            throw new FormatException("Mesh must define vertex positions");
        }

        if (vertexCount < 3)
        {
            throw new FormatException("Mesh must have at least 3 vertices");
        }

        if (indexCount % 3 != 0 || indexCount == 0)
        {
            throw new FormatException("Mesh must have multiple of 3 indices");
        }

        int requiredAttributeCount = BitOperations.PopCount(attributeFlags);
        int foundAttributes = 0;
        byte[]? positionBuffer = null;
        byte[]? normalBuffer = null;
        byte[]? tangentBuffer = null;
        byte[]? colorBuffer = null;
        byte[]? boneWeightBuffer = null;
        byte[]? uvBuffer = null;
        byte[]? uv2Buffer = null;
        byte[]? uv3Buffer = null;
        byte[]? uv4Buffer = null;
        while (foundAttributes < requiredAttributeCount)
        {
            var attribute = reader.ReadUInt16();
            int attributeDefinitions = BitOperations.PopCount(attribute);
            if (attributeDefinitions != 1 || attribute > (UInt16)VertexAttributes.UV4)
            {
                throw new FormatException("Invalid attribute defined in mesh");
            }

            VertexAttributes attributesType = (VertexAttributes)attribute;

            Int32 compressedLength = reader.ReadInt32();
            Int32 uncompressedLength = reader.ReadInt32();
            var compressedData = reader.ReadBytes(compressedLength);
            byte[] data = LZ4.Decompress(compressedData,uncompressedLength);
            

            switch (attributesType)
            {
                case VertexAttributes.Position:
                    if (positionBuffer != null)
                    {
                        throw new FormatException("Mesh position attribute is already initialized");
                    }

                    positionBuffer = data;
                    break;
                case VertexAttributes.Normal:
                    if (normalBuffer != null)
                    {
                        throw new FormatException("Mesh normal attribute is already initialized");
                    }

                    normalBuffer = data;
                    break;
                case VertexAttributes.Tangent:
                    if (tangentBuffer != null)
                    {
                        throw new FormatException("Mesh tangent attribute is already initialized");
                    }

                    tangentBuffer = data;
                    break;
                case VertexAttributes.Color:
                    if (colorBuffer != null)
                    {
                        throw new FormatException("Mesh color attribute is already initialized");
                    }

                    colorBuffer = data;
                    break;
                case VertexAttributes.BoneWeight:
                    if (boneWeightBuffer != null)
                    {
                        throw new FormatException("Mesh bone weight attribute is already initialized");
                    }

                    boneWeightBuffer = data;
                    break;
                case VertexAttributes.UV:
                    if (uvBuffer != null)
                    {
                        throw new FormatException("Mesh first uv attribute is already initialized");
                    }

                    uvBuffer = data;
                    break;
                case VertexAttributes.UV2:
                    if (uv2Buffer != null)
                    {
                        throw new FormatException("Mesh second uv attribute is already initialized");
                    }

                    uv2Buffer = data;
                    break;
                case VertexAttributes.UV3:
                    if (uv3Buffer != null)
                    {
                        throw new FormatException("Mesh third uv attribute is already initialized");
                    }

                    uv3Buffer = data;
                    break;
                case VertexAttributes.UV4:
                    if (uv4Buffer != null)
                    {
                        throw new FormatException("Mesh fourth uv attribute is already initialized");
                    }

                    uv4Buffer = data;
                    break;
            }

            foundAttributes++;
        }

        Int32 indexCompressedLength = reader.ReadInt32();
        Int32 indexUncompressedLength = reader.ReadInt32();
        if (indexUncompressedLength != (vertexTypeRaw == 0 ? sizeof(UInt16) : sizeof(UInt32)) * indexCount)
        {
            throw new FormatException("Mesh Index data is corrupt");
        }

        var indexData = reader.ReadBytes(indexCompressedLength);
        var indexBuffer = LZ4.Decompress(indexData,indexUncompressedLength);
        fixed (byte* positionPtr = positionBuffer, normalPtr = normalBuffer, tangentPtr = tangentBuffer, colorPtr =
                   colorBuffer, boneWeightPtr = boneWeightBuffer, uvPtr = uvBuffer, uv2Ptr = uv2Buffer, uv3Ptr =
                   uv3Buffer, uv4Ptr = uv4Buffer, indexPtr = indexBuffer)
        {
            MeshAttributeData attributeData = new MeshAttributeData();
            attributeData.positionBuffer = positionPtr;
            attributeData.normalBuffer = normalPtr;
            attributeData.tangentBuffer = tangentPtr;
            attributeData.colorBuffer = colorPtr;
            attributeData.boneWeightBuffer = boneWeightPtr;
            attributeData.uvBuffer = uvPtr;
            attributeData.uv2Buffer = uv2Ptr;
            attributeData.uv3Buffer = uv3Ptr;
            attributeData.uv4Buffer = uv4Ptr;
            attributeData.vertexCount = vertexCount;

            if (initQueue!=null)
            {
                MeshDeferredInit deferredInit = new MeshDeferredInit();
                GCHandle handle = GCHandle.Alloc(deferredInit);
                var meshHandle = CRUCIBLE_NATIVE_MeshNewBatchedInit(ref attributeData, vertexCount, indexPtr, vertexSize, indexCount, ref accessibility,initQueue.DeferredJobQueueHandle,GCHandle.ToIntPtr(handle));
                Mesh mesh = new Mesh(meshHandle);
                deferredInit.Mesh = mesh;
                deferredInit.Callback = onFinish;
                //don't return a real handle, we already created a mesh responsible for managing its lifetime
                return IntPtr.Zero;
            }
            else
            {
                return CRUCIBLE_NATIVE_MeshNew(ref attributeData, vertexCount, indexPtr, vertexSize, indexCount, ref accessibility);
            }
        }
    }
    /// <summary>
    /// Take a mesh and serialize it
    /// </summary>
    /// <param name="mesh"></param>
    /// <returns></returns>
    internal static byte[] Serialize(Mesh mesh)
    {
        
        SortedDictionary<VertexAttributes, LZ4CompressionData> attributeData = new SortedDictionary<VertexAttributes, LZ4CompressionData>();
        var definedAttributes = mesh.DefinedAttributes;
        
        if ((definedAttributes & VertexAttributes.Position) == VertexAttributes.Position)
        {
            byte[] positionData = new byte[mesh.VertexCount*sizeof(Vector3)];
            fixed (byte* positionPtr = positionData)
            {
                CRUCIBLE_NATIVE_MeshCopyPositionData(mesh._handle,positionPtr);
            }
            
            attributeData.Add(VertexAttributes.Position, new LZ4CompressionData(positionData));
        }
        
        if ((definedAttributes & VertexAttributes.Normal) == VertexAttributes.Normal)
        {
            byte[] normalData = new byte[mesh.VertexCount*sizeof(Vector3)];
            fixed (byte* normalPtr = normalData)
            {
                CRUCIBLE_NATIVE_MeshCopyNormalData(mesh._handle,normalPtr);
            }
            attributeData.Add(VertexAttributes.Normal, new LZ4CompressionData(normalData));
        }
        
        if ((definedAttributes & VertexAttributes.Tangent) == VertexAttributes.Tangent)
        {
            byte[] tangentData = new byte[mesh.VertexCount*sizeof(Vector3)];
            fixed (byte* tangentPtr = tangentData)
            {
                CRUCIBLE_NATIVE_MeshCopyTangentData(mesh._handle,tangentPtr);
            }
            attributeData.Add(VertexAttributes.Tangent, new LZ4CompressionData(tangentData));
        }
        
        if ((definedAttributes & VertexAttributes.Color) == VertexAttributes.Color)
        {
            byte[] colorData = new byte[mesh.VertexCount*sizeof(byte)*4];
            fixed (byte* colorPtr = colorData)
            {
                CRUCIBLE_NATIVE_MeshCopyColorData(mesh._handle,colorPtr);
            }
            attributeData.Add(VertexAttributes.Color, new LZ4CompressionData(colorData));
        }
        
        if ((definedAttributes & VertexAttributes.BoneWeight) == VertexAttributes.BoneWeight)
        {
            byte[] boneWeightData = new byte[mesh.VertexCount*sizeof(BoneWeights)];
            fixed (byte* boneWeightPtr = boneWeightData)
            {
                CRUCIBLE_NATIVE_MeshCopyBoneWeightData(mesh._handle,boneWeightPtr);
            }
            attributeData.Add(VertexAttributes.BoneWeight, new LZ4CompressionData(boneWeightData));
        }
        
        if ((definedAttributes & VertexAttributes.UV) == VertexAttributes.UV)
        {
            byte[] uvData = new byte[mesh.VertexCount*sizeof(Vector2)];
            fixed (byte* uvPtr = uvData)
            {
                CRUCIBLE_NATIVE_MeshCopyUVData(mesh._handle,uvPtr);
            }
            attributeData.Add(VertexAttributes.UV, new LZ4CompressionData(uvData));
        }
        
        if ((definedAttributes & VertexAttributes.UV2) == VertexAttributes.UV2)
        {
            byte[] uv2Data = new byte[mesh.VertexCount*sizeof(Vector2)];
            fixed (byte* uv2Ptr = uv2Data)
            {
                CRUCIBLE_NATIVE_MeshCopyUV2Data(mesh._handle,uv2Ptr);
            }
            attributeData.Add(VertexAttributes.UV2, new LZ4CompressionData(uv2Data));
        }
        
        if ((definedAttributes & VertexAttributes.UV3) == VertexAttributes.UV3)
        {
            byte[] uv3Data = new byte[mesh.VertexCount*sizeof(Vector2)];
            fixed (byte* uv3Ptr = uv3Data)
            {
                CRUCIBLE_NATIVE_MeshCopyUV3Data(mesh._handle,uv3Ptr);
            }
            attributeData.Add(VertexAttributes.UV3, new LZ4CompressionData(uv3Data));
        }
        
        if ((definedAttributes & VertexAttributes.UV4) == VertexAttributes.UV4)
        {
            byte[] uv4Data = new byte[mesh.VertexCount*sizeof(Vector2)];
            fixed (byte* uv4Ptr = uv4Data)
            {
                CRUCIBLE_NATIVE_MeshCopyUV4Data(mesh._handle,uv4Ptr);
            }
            attributeData.Add(VertexAttributes.UV4, new LZ4CompressionData(uv4Data));
        }
        
        byte[] indexData = new byte[(mesh.VertexIndexSize == IndexSize.UInt16? sizeof(UInt16) : sizeof(UInt32)) * mesh.IndexCount];
        fixed (byte* indexPtr = indexData)
        {
            CRUCIBLE_NATIVE_MeshCopyIndexData(mesh._handle,indexPtr);
        }

        var compressedIndexData = new LZ4CompressionData(indexData);

        Int32 totalSize = 5 + //magic number
                          sizeof(VertexAttributes) + //definedAttributes
                          sizeof(UInt32) + //vertex count
                          sizeof(Int32) + //index type
                          sizeof(UInt32); //index count
        foreach (var lz4CompressionData in attributeData)
        {
            totalSize += sizeof(VertexAttributes) + //vertex attribute flag
                         sizeof(Int32) + // compressed size
                         sizeof(Int32) + // uncompressed size
                         lz4CompressionData.Value.CompressedDataSize; //compressed data
        }
        
        totalSize += sizeof(Int32); //compressed index size
        totalSize += sizeof(Int32); //uncompressed index size
        totalSize += compressedIndexData.CompressedDataSize; // compressed index data
        
        byte[] serializedData = new byte[totalSize];
        serializedData[0] = (byte)'c';
        serializedData[1] = (byte)'m';
        serializedData[2] = (byte)'s';
        serializedData[3] = (byte)'h';
        serializedData[4] = (byte)'\n';
        int currentIndex = 5;
        currentIndex += serializedData.Insert(definedAttributes,currentIndex);
        currentIndex += serializedData.Insert(mesh.VertexCount,currentIndex);
        currentIndex += serializedData.Insert(mesh.VertexIndexSize,currentIndex);
        currentIndex += serializedData.Insert(mesh.VertexCount,currentIndex);
        foreach (var lz4CompressionData in attributeData)
        {
            currentIndex += serializedData.Insert(lz4CompressionData.Key,currentIndex);
            currentIndex += serializedData.Insert(lz4CompressionData.Value.CompressedDataSize,currentIndex);
            currentIndex += serializedData.Insert(lz4CompressionData.Value.UncompressedDataSize,currentIndex);
            System.Buffer.BlockCopy(serializedData,currentIndex,lz4CompressionData.Value.CompressedData,0,lz4CompressionData.Value.CompressedDataSize);
            currentIndex += lz4CompressionData.Value.CompressedDataSize;
        }
        
        currentIndex += serializedData.Insert(compressedIndexData.CompressedDataSize,currentIndex);
        currentIndex += serializedData.Insert(compressedIndexData.UncompressedDataSize,currentIndex);
        System.Buffer.BlockCopy(serializedData,currentIndex,compressedIndexData.CompressedData,0,compressedIndexData.CompressedDataSize);

        return serializedData;
    }
    /// <summary>
    /// Combinations of Vertex Attribute flags that this mesh contains data for
    /// </summary>
    public VertexAttributes DefinedAttributes
    {
        get { return CRUCIBLE_NATIVE_MeshDefinedVertexAttributeFlags(_handle); }
    }
    /// <summary>
    /// Number of vertices in this mesh
    /// </summary>
    public UInt32 VertexCount
    {
        get { return CRUCIBLE_NATIVE_MeshVertexCount(_handle); }
    }
    /// <summary>
    /// Number of indices in this mesh
    /// </summary>
    public UInt32 IndexCount
    {
        get { return CRUCIBLE_NATIVE_MeshIndexCount(_handle); }
    }
    /// <summary>
    /// Size of the indexes
    /// </summary>
    public IndexSize VertexIndexSize
    {
        get { return CRUCIBLE_NATIVE_MeshIndexSize(_handle); }
    }
    /// <summary>
    /// Gets the accessibility level of a given vertex attribute
    /// </summary>
    /// <param name="attributes">Attribute to check the accessibility for</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException">More than one attribute flag is defined</exception>
    /// <exception cref="NullReferenceException">Vertex attribute is not defined for this mesh</exception>
    public Buffer.Accessibility AttributeAccessibility(VertexAttributes attribute)
    {
        if (BitOperations.PopCount((UInt16)attribute) > 1)
        {
            throw new ArgumentException(nameof(attribute)+" must not be combination of attributes");
        }

        if ((UInt16)(DefinedAttributes & attribute) == 0)
        {
            throw new NullReferenceException("Given vertex attribute is not defined on this mesh");
        }
        return CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle,attribute);
    }
    /// <summary>
    /// Gets the accessibility level of the index buffer
    /// </summary>
    /// <returns></returns>
    public Buffer.Accessibility IndexAccessibility()
    {
        return CRUCIBLE_NATIVE_MeshIndexAttributeBufferAccess(_handle);
    }
    /// <summary>
    /// Gets the buffer of the position data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector3> LocalPositionData()
    {
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.Position) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("Position buffer is not CPU accessible");
        }

        return new Span<Math.Vector3>(CRUCIBLE_NATIVE_MeshPositionBufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the normal data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have a normal vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector3> LocalNormalData()
    {
        if ((DefinedAttributes & VertexAttributes.Normal) != VertexAttributes.Normal)
        {
            throw new NullReferenceException("Normal buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.Normal) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("Normal buffer is not CPU accessible");
        }

        return new Span<Math.Vector3>(CRUCIBLE_NATIVE_MeshNormalBufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the tangent data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have a tangent vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector3> LocalTangentData()
    {
        if ((DefinedAttributes & VertexAttributes.Tangent) != VertexAttributes.Tangent)
        {
            throw new NullReferenceException("Tangent buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.Tangent) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("Tangent buffer is not CPU accessible");
        }

        return new Span<Math.Vector3>(CRUCIBLE_NATIVE_MeshTangentBufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the color data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have a color vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Core.Color> LocalColorData()
    {
        if ((DefinedAttributes & VertexAttributes.Color) != VertexAttributes.Color)
        {
            throw new NullReferenceException("Color buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.Color) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("Color buffer is not CPU accessible");
        }

        return new Span<Core.Color>(CRUCIBLE_NATIVE_MeshColorBufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the bone weight data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have a bone weight vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Core.BoneWeights> LocalBoneWeightsData()
    {
        if ((DefinedAttributes & VertexAttributes.BoneWeight) != VertexAttributes.BoneWeight)
        {
            throw new NullReferenceException("Bone Weights buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.BoneWeight) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("Bone Weights buffer is not CPU accessible");
        }

        return new Span<Core.BoneWeights>(CRUCIBLE_NATIVE_MeshBoneWeightBufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the uv data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have an uv vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector2> LocalUVData()
    {
        if ((DefinedAttributes & VertexAttributes.UV) != VertexAttributes.UV)
        {
            throw new NullReferenceException("UV buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.UV) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("UV buffer is not CPU accessible");
        }

        return new Span<Math.Vector2>(CRUCIBLE_NATIVE_MeshUVBufferCpuHandle(_handle), (int)VertexCount);
    }
    
    /// <summary>
    /// Gets the buffer of the uv2 data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have an uv2 vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector2> LocalUV2Data()
    {
        if ((DefinedAttributes & VertexAttributes.UV2) != VertexAttributes.UV2)
        {
            throw new NullReferenceException("UV2 buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.UV2) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("UV2 buffer is not CPU accessible");
        }

        return new Span<Math.Vector2>(CRUCIBLE_NATIVE_MeshUV2BufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the uv3 data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have an uv3 vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector2> LocalUV3Data()
    {
        if ((DefinedAttributes & VertexAttributes.UV3) != VertexAttributes.UV3)
        {
            throw new NullReferenceException("UV3 buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.UV3) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("UV3 buffer is not CPU accessible");
        }

        return new Span<Math.Vector2>(CRUCIBLE_NATIVE_MeshUV3BufferCpuHandle(_handle), (int)VertexCount);
    }
    /// <summary>
    /// Gets the buffer of the uv4 data if it's CPU accessible
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NullReferenceException">The mesh doesn't have an uv4 vertex buffer</exception>
    /// <exception cref="BufferNotLocalException">The buffer is not CPU accessible</exception>
    public Span<Math.Vector2> LocalUV4Data()
    {
        if ((DefinedAttributes & VertexAttributes.UV4) != VertexAttributes.UV4)
        {
            throw new NullReferenceException("UV4 buffer is not defined for this mesh");
        }
        if (CRUCIBLE_NATIVE_MeshVertexAttributeBufferAccess(_handle, VertexAttributes.UV4) ==
            Buffer.Accessibility.Gpu)
        {
            throw new BufferNotLocalException("UV4 buffer is not CPU accessible");
        }

        return new Span<Math.Vector2>(CRUCIBLE_NATIVE_MeshUV4BufferCpuHandle(_handle), (int)VertexCount);
    }
    
}
