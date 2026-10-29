#pragma once

// Mesh import (ADR 0043, 0044): glTF through cgltf and FBX through ufbx,
// mirrored into Crucible's left-handed space on X (ADR 0024's amendment),
// with missing normals and tangents generated (tangents by MikkTSpace),
// welded, optimized and given LODs by meshoptimizer, and packed into one
// stream per attribute. The C# mesh and FBX importers write the result as
// a MESH binary file (ADR 0039).
// Editor-only. Names are placeholders (docs/naming.md).
//
// A MESH file (kind tag("MESH"), version 1) holds every mesh of one source:
//   INFO  MeshFileInfo
//   MSHS  MeshEntry, one per mesh
//   LODS  MeshLod, each mesh's levels, LOD 0 first
//   RNGS  MeshRange, each level's index range per submesh
//   POSN  float x3            every vertex of every mesh, mesh after mesh
//   NRML  int16 x2            normal, octahedral
//   TNGT  int16 x4            tangent, octahedral (x, y), handedness (±32767), 0
//   UV0_, UV1_  half x2, or float x2 when MeshFileInfo::floatUvs says so
//   COLR  uint8 x4            sRGB colour, linear alpha
//   INDX  each mesh's indices, 16-bit if it has 65,535 vertices or fewer, else 32-bit
//   NODE  MeshNode, the source's node hierarchy
//   MATL  MaterialEntry (written by C#, which knows the textures' asset IDs)
//   NAME  UTF-8 names, referenced by offset and length
// A stream is present when any mesh has the attribute; meshes without it
// hold zeros there (MeshEntry::attributes says which are real).
// Positions are in meters; front faces are clockwise.

#include <crucible/Expose.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::importers {

// Bits of MeshEntry::attributes and MeshFileInfo::streams.
enum class CRUCIBLE_EXPOSE MeshAttribute : uint32_t
{
    Normals = 1,
    Tangents = 2,
    Uv0 = 4,
    Uv1 = 8,
    Colors = 16,
};

struct CRUCIBLE_EXPOSE MeshFileInfo
{
    uint32_t meshCount;
    uint32_t nodeCount;
    uint32_t materialCount;
    uint32_t vertexCount; // in every stream
    uint32_t streams;     // MeshAttribute bits: which optional streams exist
    uint32_t floatUvs;    // bit 0: UV0_ holds floats, bit 1: UV1_ (UVs past ±4, where halves lose precision)
};

struct CRUCIBLE_EXPOSE MeshEntry
{
    uint32_t vertexOffset; // into every stream
    uint32_t vertexCount;
    uint32_t indexSize;  // 2 or 4 bytes
    uint32_t attributes; // MeshAttribute bits
    uint32_t lodOffset;  // into LODS
    uint32_t lodCount;
    uint32_t submeshCount; // ranges per level
    uint32_t nameOffset;   // into NAME
    uint32_t nameLength;
    float boundsMin[3];
    float boundsMax[3];
};

struct CRUCIBLE_EXPOSE MeshLod
{
    uint32_t rangeOffset; // into RNGS: the mesh's submeshCount ranges for this level
    float error;          // meters of deviation from LOD 0, for choosing a level by screen size
};

struct CRUCIBLE_EXPOSE MeshRange
{
    uint32_t indexByteOffset; // into INDX
    uint32_t indexCount;
    int32_t material; // into MATL, or -1
};

struct CRUCIBLE_EXPOSE MeshNode
{
    int32_t parent; // into NODE, or -1 for a root
    int32_t mesh;   // into MSHS, or -1
    float translation[3];
    float rotation[4]; // x, y, z, w
    float scale[3];
    uint32_t nameOffset;
    uint32_t nameLength;
};

enum class CRUCIBLE_EXPOSE TextureRole : uint32_t
{
    BaseColor,
    MetallicRoughness,
    Normal,
    Occlusion,
    Emissive,
};

// glTF's metallic-roughness material, as imported; M5's materials build on it.
struct CRUCIBLE_EXPOSE MaterialEntry
{
    float baseColor[4]; // linear
    float metallic;
    float roughness;
    float emissive[3];
    uint32_t alphaMode; // 0 opaque, 1 mask (alpha-tested), 2 blend
    float alphaCutoff;
    uint32_t doubleSided;
    // Texture asset IDs (GUID bytes), all zero for none, one per TextureRole.
    uint8_t baseColorTexture[16];
    uint8_t metallicRoughnessTexture[16];
    uint8_t normalTexture[16];
    uint8_t occlusionTexture[16];
    uint8_t emissiveTexture[16];
    uint32_t nameOffset;
    uint32_t nameLength;
};

struct CRUCIBLE_EXPOSE MeshSettings
{
    bool optimize;         // vertex cache, overdraw, and fetch order
    bool generateTangents; // with MikkTSpace, where the source has none and has UVs
    bool generateLods;
    uint32_t maxLods;  // levels after LOD 0
    float lodRatio;    // each level keeps about this share of the last's triangles
    float lodMaxError; // stop when a level deviates more than this share of the mesh's size
};

struct CRUCIBLE_EXPOSE MeshSectionInfo
{
    uint32_t tag;
    uint32_t version;
    uint64_t size;
};

struct CRUCIBLE_EXPOSE GltfSource;
struct CRUCIBLE_EXPOSE MeshImport;

// Parses a .gltf (JSON) or .glb file. Buffers in other files are listed by
// gltfBufferCount/gltfBufferUri and handed over with setGltfBuffer before
// importing; embedded ones (.glb, data: URIs) are decoded here. InvalidData
// with a reason in lastImportError() if it can't.
CRUCIBLE_EXPOSE Result<GltfSource*> parseGltf(std::span<const uint8_t> bytes);
CRUCIBLE_EXPOSE void destroyGltfSource(GltfSource* source);
CRUCIBLE_EXPOSE uint32_t gltfBufferCount(const GltfSource& source);
// The buffer's file, relative to the glTF's folder, URI escapes decoded.
CRUCIBLE_EXPOSE std::string_view gltfBufferUri(const GltfSource& source, uint32_t index);
CRUCIBLE_EXPOSE Result<void> setGltfBuffer(GltfSource& source, uint32_t index, std::span<const uint8_t> bytes);

CRUCIBLE_EXPOSE Result<MeshImport*> importGltf(GltfSource& source, MeshSettings settings);
// Reads a binary or ASCII .fbx (ADR 0044), converted to the same space as
// glTF: meters, Y up, with its geometry transforms baked into the vertices.
// Textures are left for C# to find, as for glTF.
CRUCIBLE_EXPOSE Result<MeshImport*> importFbx(std::span<const uint8_t> bytes, MeshSettings settings);
CRUCIBLE_EXPOSE void destroyMeshImport(MeshImport* mesh);

// The file's sections, MATL aside.
CRUCIBLE_EXPOSE uint32_t meshSectionCount(const MeshImport& mesh);
CRUCIBLE_EXPOSE MeshSectionInfo meshSection(const MeshImport& mesh, uint32_t index);
CRUCIBLE_EXPOSE void copyMeshSection(const MeshImport& mesh, uint32_t index, std::span<uint8_t> destination);

// Materials, with texture IDs left zero for C# to fill: each texture's file
// (relative to the source's folder; for FBX, possibly absolute) or empty.
// Textures embedded in the source aren't supported yet: they are reported as
// warnings.
CRUCIBLE_EXPOSE uint32_t meshMaterialCount(const MeshImport& mesh);
CRUCIBLE_EXPOSE MaterialEntry meshMaterial(const MeshImport& mesh, uint32_t index);
CRUCIBLE_EXPOSE std::string_view meshMaterialTexture(const MeshImport& mesh, uint32_t material, TextureRole role);

// What the import left out or worked around (lines, skins, embedded
// textures...), for the editor to show.
CRUCIBLE_EXPOSE uint32_t meshWarningCount(const MeshImport& mesh);
CRUCIBLE_EXPOSE std::string_view meshWarning(const MeshImport& mesh, uint32_t index);

} // namespace crucible::importers
