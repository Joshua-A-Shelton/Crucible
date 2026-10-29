#pragma once

// Mesh import (ADR 0043, 0044): glTF through cgltf and FBX through ufbx,
// mirrored into Crucible's left-handed space on X (ADR 0024's amendment),
// with missing normals and tangents generated (tangents by MikkTSpace),
// welded, optimized and given LODs by meshoptimizer, and packed into one
// stream per attribute. The C# mesh and FBX importers write the result as
// a MESH binary file (ADR 0039), laid out as MeshFile.h describes.
// Editor-only. Names are placeholders (docs/naming.md).
//

#include <crucible/Expose.h>
#include <crucible/MeshFile.h>
#include <crucible/Result.h>

#include <cstdint>
#include <span>
#include <string_view>

namespace crucible::importers {

// The file's layout is the core's (MeshFile.h): the runtime reads it too.
using assets::MaterialEntry;
using assets::MeshAttribute;
using assets::MeshEntry;
using assets::MeshFileInfo;
using assets::MeshLod;
using assets::MeshNode;
using assets::MeshRange;
using assets::TextureRole;

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
