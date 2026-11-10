#pragma once

// What every model format's reader fills (ADR 0044): a source scene in
// glTF's convention (right-handed, Y up, +Z front, meters, counter-clockwise
// front faces, UV origin top left). buildMeshImport mirrors it into
// Crucible's space and does the rest (ADR 0043). Internal.

#include <crucible/importers/Mesh.h>

#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <cstdint>
#include <string_view>

namespace crucible::importers {

// Strings back to back, each ending where `ends` says.
struct StringList
{
    Vector<char> text{MemoryTag::Assets};
    Vector<uint32_t> ends{MemoryTag::Assets};

    uint32_t add(std::string_view value);
    // printf-style, cut at 511 bytes.
    uint32_t addFormat(const char* format, ...);
    uint32_t count() const
    {
        return static_cast<uint32_t>(ends.size());
    }
    std::string_view at(uint32_t index) const;
};

// A mesh's triangles before welding: every corner with all its attributes,
// so normals and tangents can be generated per face.
struct Corners
{
    Vector<float> positions{MemoryTag::Assets}; // 3 per corner
    Vector<float> normals{MemoryTag::Assets};   // 3
    Vector<float> tangents{MemoryTag::Assets};  // 4
    Vector<float> uv0{MemoryTag::Assets};       // 2
    Vector<float> uv1{MemoryTag::Assets};       // 2
    Vector<float> colors{MemoryTag::Assets};    // 4, linear
    // Skins (ADR 0078): up to 8 influences, as joint indices (into
    // SourceScene::joints, held as floats so they weld like the rest) and
    // weights; zeros where a corner isn't skinned.
    Vector<float> joints{MemoryTag::Assets};  // 8
    Vector<float> weights{MemoryTag::Assets}; // 8

    uint32_t count() const
    {
        return static_cast<uint32_t>(positions.size() / 3);
    }
};

// A run of corners with one material. The has* flags say which attributes
// the source gave it; missing ones are zeros (colours: white).
struct Submesh
{
    uint32_t firstCorner;
    uint32_t cornerCount;
    int32_t material;
    bool hasNormals;
    bool hasTangents;
    bool hasUv0;
    bool hasUv1;
    bool hasColors;
    bool hasSkin = false;
};

struct SourceMesh
{
    Corners corners;
    Vector<Submesh> submeshes{MemoryTag::Assets};
    uint32_t name; // into SourceScene::strings
};

struct SourceNode
{
    int32_t parent; // or -1
    int32_t mesh;   // or -1
    float translation[3];
    float rotation[4]; // x, y, z, w
    float scale[3];
    uint32_t name;
};

struct SourceMaterial
{
    MaterialEntry entry; // texture IDs and name left for later
    uint32_t name;
    uint32_t textures[5]; // per TextureRole, into strings; an empty string for none
};

// A skeleton joint: a node, and its inverse bind matrix (row vectors,
// row-major, as Matrix4x4).
struct SourceJoint
{
    int32_t node;
    float inverseBind[16];
};

// One node's transform at each of a clip's samples, relative to its parent.
struct SourceTrack
{
    int32_t node = -1;
    Vector<float> rotations{MemoryTag::Assets};    // 4 per sample, x, y, z, w
    Vector<float> translations{MemoryTag::Assets}; // 3
    Vector<float> scales{MemoryTag::Assets};       // 3
};

// An animation, sampled by its reader at sampleRate: samples at i / sampleRate,
// for as many as reach the duration (the last at or past it, holding the end).
struct SourceClip
{
    uint32_t name = 0; // into SourceScene::strings
    float duration = 0;
    float sampleRate = 30;
    uint32_t sampleCount = 0;
    Vector<SourceTrack*> tracks{MemoryTag::Assets};
};

struct SourceScene
{
    Vector<SourceMesh*> meshes{MemoryTag::Assets};
    Vector<SourceNode> nodes{MemoryTag::Assets};
    Vector<SourceMaterial> materials{MemoryTag::Assets};
    Vector<SourceJoint> joints{MemoryTag::Assets};
    Vector<SourceClip*> clips{MemoryTag::Assets};
    StringList strings;
    StringList warnings;

    SourceScene() = default;
    SourceScene(const SourceScene&) = delete;
    SourceScene& operator=(const SourceScene&) = delete;
    ~SourceScene();

    SourceMesh& addMesh(std::string_view name);
    SourceClip& addClip(std::string_view name);
};

// Binary sections, laid out back to back: an import's output.
struct SectionBlob
{
    struct Section
    {
        uint32_t tag;
        uint32_t version;
        uint64_t offset;
        uint64_t size;
    };
    Vector<uint8_t> bytes{MemoryTag::Assets};
    Vector<Section> sections{MemoryTag::Assets};

    void addSection(uint32_t tag, const Vector<uint8_t>& section)
    {
        sections.pushBack({tag, 1, bytes.size(), section.size()});
        for (uint8_t value : section)
        {
            bytes.pushBack(value);
        }
    }
};

struct MeshImport : SectionBlob
{
    Vector<MaterialEntry> materials{MemoryTag::Assets};
    StringList textures; // five per material
    StringList warnings;
    // The animation clips (ADR 0078), each a CLIP file's sections, by name.
    Vector<SectionBlob*> clips{MemoryTag::Assets};
    StringList clipNames;

    MeshImport() = default;
    MeshImport(const MeshImport&) = delete;
    MeshImport& operator=(const MeshImport&) = delete;
    ~MeshImport();
};

// Mirrors the scene on X with the winding reversed (ADR 0024's amendment),
// then builds, optimizes and packs every mesh.
Result<MeshImport*> buildMeshImport(SourceScene& scene, MeshSettings settings);

const char* nameOf(const char* name, const char* fallback);

} // namespace crucible::importers
