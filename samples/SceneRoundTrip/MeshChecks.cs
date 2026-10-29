using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Crucible.Assets;
using Crucible.Assets.Importers;
using Crucible.Binary;
using Crucible.Importers;

namespace Crucible.Samples.SceneRoundTrip;

/// <summary>
/// Reads an imported MESH file (ADR 0043) as a renderer would, for the
/// mirroring checks: faces, volumes, and where the model's nodes end up.
/// </summary>
internal sealed class MeshChecks
{
    private readonly MeshEntry[] _meshes;
    private readonly MeshLod[] _lods;
    private readonly MeshRange[] _ranges;
    private readonly Vector3[] _positions;
    private readonly short[] _normals;
    private readonly byte[] _indices;
    private readonly MeshNode[] _nodes;
    private readonly byte[] _names;

    public MeshChecks(string path)
    {
        using ImportedFile file = ImportedFile.Open(path, MeshImporter.Kind, MeshImporter.KindVersion);
        byte[] Section(string tag) => file.Find(Tag.Of(tag)) is int index and >= 0 ? file.Read(index) : [];
        _meshes = MemoryMarshal.Cast<byte, MeshEntry>(Section("MSHS")).ToArray();
        _lods = MemoryMarshal.Cast<byte, MeshLod>(Section("LODS")).ToArray();
        _ranges = MemoryMarshal.Cast<byte, MeshRange>(Section("RNGS")).ToArray();
        _positions = MemoryMarshal.Cast<byte, Vector3>(Section("POSN")).ToArray();
        _normals = MemoryMarshal.Cast<byte, short>(Section("NRML")).ToArray();
        _indices = Section("INDX");
        _nodes = MemoryMarshal.Cast<byte, MeshNode>(Section("NODE")).ToArray();
        _names = Section("NAME");
    }

    public int MeshCount => _meshes.Length;

    public int LodCount => _meshes.Sum(mesh => (int)mesh.LodCount);

    /// <summary>
    /// Faces whose clockwise normal (Crucible's front faces, ADR 0024)
    /// disagrees with their vertex normals, over every mesh and level.
    /// Faces with no area have no orientation and are skipped.
    /// </summary>
    public (int InsideOut, int Faces) CountInsideOutFaces()
    {
        int insideOut = 0, faces = 0;
        for (int m = 0; m < _meshes.Length; ++m)
        {
            for (uint level = 0; level < _meshes[m].LodCount; ++level)
            {
                foreach ((int a, int b, int c) in Triangles(m, level))
                {
                    Vector3 face = Vector3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
                    if (face.LengthSquared() < 1e-14f)
                    {
                        continue;
                    }
                    ++faces;
                    insideOut += Vector3.Dot(face, Normal(a) + Normal(b) + Normal(c)) > 0 ? 0 : 1;
                }
            }
        }
        return (insideOut, faces);
    }

    /// <summary>
    /// Each mesh's enclosed volume at LOD 0: positive when its clockwise faces
    /// point outward. Only meaningful for closed meshes.
    /// </summary>
    public IEnumerable<(string Name, float Volume)> Volumes()
    {
        for (int m = 0; m < _meshes.Length; ++m)
        {
            float volume = 0;
            foreach ((int a, int b, int c) in Triangles(m, 0))
            {
                volume += Vector3.Dot(_positions[a], Vector3.Cross(_positions[b], _positions[c])) / 6;
            }
            yield return (Name(_meshes[m].NameOffset, _meshes[m].NameLength), volume);
        }
    }

    /// <summary>A node's position in the model, through its parents; null if there is no such node.</summary>
    public Vector3? NodePosition(string name)
    {
        int node = Array.FindIndex(_nodes, node => Name(node.NameOffset, node.NameLength) == name);
        if (node < 0)
        {
            return null;
        }
        Vector3 position = Vector3.Zero;
        for (; node >= 0; node = _nodes[node].Parent)
        {
            MeshNode n = _nodes[node];
            Quaternion rotation = new(n.Rotation[0], n.Rotation[1], n.Rotation[2], n.Rotation[3]);
            position = Vector3.Transform(position * new Vector3(n.Scale[0], n.Scale[1], n.Scale[2]), rotation) +
                       new Vector3(n.Translation[0], n.Translation[1], n.Translation[2]);
        }
        return position;
    }

    private string Name(uint offset, uint length) => Encoding.UTF8.GetString(_names, (int)offset, (int)length);

    // A level's triangles, as vertex indices into the shared streams.
    private IEnumerable<(int A, int B, int C)> Triangles(int mesh, uint level)
    {
        MeshEntry entry = _meshes[mesh];
        MeshLod lod = _lods[entry.LodOffset + level];
        for (uint s = 0; s < entry.SubmeshCount; ++s)
        {
            MeshRange range = _ranges[lod.RangeOffset + s];
            for (uint i = 0; i + 2 < range.IndexCount; i += 3)
            {
                yield return (Index(entry, range, i), Index(entry, range, i + 1), Index(entry, range, i + 2));
            }
        }
    }

    private int Index(MeshEntry entry, MeshRange range, uint i)
    {
        int at = (int)(range.IndexByteOffset + i * entry.IndexSize);
        uint index = entry.IndexSize == 2 ? BitConverter.ToUInt16(_indices, at) : BitConverter.ToUInt32(_indices, at);
        return (int)(entry.VertexOffset + index);
    }

    // Octahedral decoding, as a shader does it.
    private Vector3 Normal(int vertex)
    {
        float x = Math.Max(_normals[vertex * 2] / 32767f, -1f), y = Math.Max(_normals[vertex * 2 + 1] / 32767f, -1f);
        float z = 1 - Math.Abs(x) - Math.Abs(y);
        if (z < 0)
        {
            (x, y) = ((1 - Math.Abs(y)) * (x >= 0 ? 1 : -1), (1 - Math.Abs(x)) * (y >= 0 ? 1 : -1));
        }
        return Vector3.Normalize(new Vector3(x, y, z));
    }
}
