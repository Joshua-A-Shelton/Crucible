#pragma once

// The imported model file, .crmesh (ADR 0043, 0049): written by the mesh and
// FBX importers, read by the runtime (RenderAssets.h). Names are in
// docs/naming.md.
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

#include <crucible/BinaryFile.h>
#include <crucible/Expose.h>

#include <cstdint>

namespace crucible::assets {

inline constexpr uint32_t meshFileKind = binary::tag("MESH");
inline constexpr uint32_t meshFileVersion = 1;

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

} // namespace crucible::assets
