using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using Crucible.Core;
using Vector2 = Crucible.Core.Vector2;
using Vector3 = Crucible.Core.Vector3;

namespace Crucible.Rendering;

public unsafe partial class Mesh
{
    [Flags]
    public enum VertexAttributeFlags
    {
        None = 0,
        Position = 1,
        Normal = 1<<1,
        UV = 1<<2,
        Tangent = 1<<3,
        Color = 1<<4,
        BoneWeights = 1<<5,
    }

    public enum IndexBufferType
    {
        UINT_16,
        UINT_32,
    }

    private enum MeshLoadResult
    {
        Success = 0,
        Failure = 1,
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct VertexDataStreams
    {
        public Vector3* positions;
        public Vector3* normals;
        public Vector2* uvs;
        public Vector4* tangents;
        public Vector4* colors;
        public BoneWeights* boneWeights;
        public void* indices;
        public UInt32 vertexCount;
        public IndexBufferType indexBufferType;
        
    }
    
    private IntPtr _handle =  IntPtr.Zero;

    public Mesh(string filename)
    {
        throw new NotImplementedException();
    }

    internal Mesh(IntPtr dataStreamStart, out IntPtr lastRead)
    {
        throw new NotImplementedException();
    }
    
    //Native Methods
    
    [LibraryImport("Crucible")]
    private static partial MeshLoadResult CRUCIBLE_API_createMeshFromFile(byte* data, VertexAttributeFlags cpuAccessibleAttributes, out IntPtr handle);

    [LibraryImport("Crucible")]
    private static partial MeshLoadResult CRUCIBLE_API_createMeshFromVertexData(ref VertexDataStreams streams, VertexAttributeFlags cpuAccessibleAttributes, out IntPtr handle);

}