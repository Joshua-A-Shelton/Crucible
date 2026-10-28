#include <crucible/importers/Mesh.h>

#include "Common.h"

#include <crucible/BinaryFile.h>
#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <cgltf.h>
#include <meshoptimizer.h>
#include <mikktspace.h>

#include <algorithm>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace crucible::importers {

namespace {

// ---------------------------------------------------------------------------
// cgltf through Crucible's allocator
// ---------------------------------------------------------------------------

constexpr size_t sizePrefix = 16;

void* allocateSized(void*, cgltf_size size)
{
    auto* block = static_cast<std::byte*>(tryAllocate(size + sizePrefix, 16, MemoryTag::Assets));
    if (block == nullptr)
    {
        return nullptr;
    }
    std::memcpy(block, &size, sizeof(size));
    return block + sizePrefix;
}

void freeSized(void*, void* pointer)
{
    if (pointer == nullptr)
    {
        return;
    }
    std::byte* block = static_cast<std::byte*>(pointer) - sizePrefix;
    size_t size;
    std::memcpy(&size, block, sizeof(size));
    free(block, size + sizePrefix, MemoryTag::Assets);
}

// Buffers in other files come from C#, which records them as dependencies;
// cgltf never opens a file itself.
cgltf_result refuseFile(const cgltf_memory_options*, const cgltf_file_options*, const char*, cgltf_size*, void**)
{
    return cgltf_result_io_error;
}

cgltf_options options()
{
    cgltf_options result{};
    result.memory.alloc_func = allocateSized;
    result.memory.free_func = freeSized;
    result.file.read = refuseFile;
    return result;
}

const char* describe(cgltf_result result)
{
    switch (result)
    {
    case cgltf_result_data_too_short:
        return "the file is cut short";
    case cgltf_result_unknown_format:
        return "it isn't glTF JSON or a .glb";
    case cgltf_result_invalid_json:
        return "its JSON doesn't parse";
    case cgltf_result_invalid_gltf:
        return "its JSON isn't valid glTF";
    case cgltf_result_invalid_options:
        return "invalid options";
    case cgltf_result_file_not_found:
    case cgltf_result_io_error:
        return "a file can't be read";
    case cgltf_result_out_of_memory:
        return "out of memory";
    case cgltf_result_legacy_gltf:
        return "it is glTF 1.0, which isn't supported";
    default:
        return "unknown error";
    }
}

bool isDataUri(const char* uri)
{
    return std::strncmp(uri, "data:", 5) == 0;
}

// ---------------------------------------------------------------------------
// Building one mesh, corner by corner
// ---------------------------------------------------------------------------

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

    uint32_t count() const
    {
        return static_cast<uint32_t>(positions.size() / 3);
    }
};

struct Submesh
{
    uint32_t firstCorner;
    uint32_t cornerCount;
    int32_t material;
    bool hasNormals;
    bool hasTangents;
    bool hasUv0;
};

// A welded, optimized mesh, ready to pack.
struct BuiltMesh
{
    uint32_t vertexCount = 0;
    uint32_t attributes = 0;
    Vector<float> positions{MemoryTag::Assets};
    Vector<float> normals{MemoryTag::Assets};
    Vector<float> tangents{MemoryTag::Assets};
    Vector<float> uv0{MemoryTag::Assets};
    Vector<float> uv1{MemoryTag::Assets};
    Vector<float> colors{MemoryTag::Assets};
    // Every level's indices, back to back; per level and submesh, a range.
    Vector<uint32_t> indices{MemoryTag::Assets};
    struct Range
    {
        uint32_t first;
        uint32_t count;
        int32_t material;
    };
    Vector<Range> ranges{MemoryTag::Assets}; // levels x submeshes
    Vector<float> lodErrors{MemoryTag::Assets};
    uint32_t submeshCount = 0;
    float boundsMin[3] = {0, 0, 0};
    float boundsMax[3] = {0, 0, 0};
};

uint32_t bit(MeshAttribute attribute)
{
    return static_cast<uint32_t>(attribute);
}

const cgltf_accessor* findAttribute(const cgltf_primitive& primitive, cgltf_attribute_type type, int index)
{
    for (cgltf_size i = 0; i < primitive.attributes_count; ++i)
    {
        if (primitive.attributes[i].type == type && primitive.attributes[i].index == index)
        {
            return primitive.attributes[i].data;
        }
    }
    return nullptr;
}

// An accessor's values as floats (normalized integers become [0, 1] or
// [-1, 1]), `components` per element; empty if absent or unreadable.
Vector<float> unpack(const cgltf_accessor* accessor, uint32_t components)
{
    Vector<float> values(MemoryTag::Assets);
    if (accessor == nullptr || cgltf_num_components(accessor->type) != components)
    {
        return values;
    }
    values.resize(accessor->count * components);
    if (cgltf_accessor_unpack_floats(accessor, values.data(), values.size()) != values.size())
    {
        values.clear();
    }
    return values;
}

// The face normal of the stored (clockwise-front) triangle: cross(B - A,
// C - A) points outward. With X mirrored and the winding reversed, this is
// the source's own outward normal, mirrored.
void faceNormal(const float* a, const float* b, const float* c, float* normal)
{
    const float u[3] = {b[0] - a[0], b[1] - a[1], b[2] - a[2]};
    const float v[3] = {c[0] - a[0], c[1] - a[1], c[2] - a[2]};
    normal[0] = u[1] * v[2] - u[2] * v[1];
    normal[1] = u[2] * v[0] - u[0] * v[2];
    normal[2] = u[0] * v[1] - u[1] * v[0];
    const float length = std::sqrt(normal[0] * normal[0] + normal[1] * normal[1] + normal[2] * normal[2]);
    for (int i = 0; i < 3; ++i)
    {
        normal[i] = length > 0 ? normal[i] / length : (i == 1 ? 1.0f : 0.0f);
    }
}

// MikkTSpace over one submesh's corners.
struct TangentContext
{
    Corners* corners;
    uint32_t firstCorner;
    uint32_t cornerCount;
};

int faceCount(const SMikkTSpaceContext* context)
{
    return static_cast<int>(static_cast<TangentContext*>(context->m_pUserData)->cornerCount / 3);
}

int faceSize(const SMikkTSpaceContext*, int)
{
    return 3;
}

uint32_t cornerOf(const SMikkTSpaceContext* context, int face, int vertex)
{
    return static_cast<TangentContext*>(context->m_pUserData)->firstCorner + static_cast<uint32_t>(face * 3 + vertex);
}

void tangentPosition(const SMikkTSpaceContext* context, float out[], int face, int vertex)
{
    const uint32_t corner = cornerOf(context, face, vertex);
    std::memcpy(out, static_cast<TangentContext*>(context->m_pUserData)->corners->positions.data() + corner * 3, 12);
}

void tangentNormal(const SMikkTSpaceContext* context, float out[], int face, int vertex)
{
    const uint32_t corner = cornerOf(context, face, vertex);
    std::memcpy(out, static_cast<TangentContext*>(context->m_pUserData)->corners->normals.data() + corner * 3, 12);
}

void tangentUv(const SMikkTSpaceContext* context, float out[], int face, int vertex)
{
    const uint32_t corner = cornerOf(context, face, vertex);
    std::memcpy(out, static_cast<TangentContext*>(context->m_pUserData)->corners->uv0.data() + corner * 2, 8);
}

void tangentSet(const SMikkTSpaceContext* context, const float tangent[], float sign, int face, int vertex)
{
    float* out = static_cast<TangentContext*>(context->m_pUserData)->corners->tangents.data() +
                 cornerOf(context, face, vertex) * 4;
    out[0] = tangent[0];
    out[1] = tangent[1];
    out[2] = tangent[2];
    out[3] = sign;
}

struct Warnings
{
    Vector<char> text{MemoryTag::Assets};
    Vector<uint32_t> ends{MemoryTag::Assets};

    void add(const char* format, ...)
    {
        char line[512];
        va_list arguments;
        va_start(arguments, format);
        std::vsnprintf(line, sizeof(line), format, arguments);
        va_end(arguments);
        for (const char* c = line; *c != '\0'; ++c)
        {
            text.pushBack(*c);
        }
        ends.pushBack(static_cast<uint32_t>(text.size()));
    }
};

const char* nameOf(const char* name, const char* fallback)
{
    return name != nullptr && name[0] != '\0' ? name : fallback;
}

// Reads one glTF mesh's triangle primitives into corners, mirrored on X with
// the winding reversed (ADR 0024's amendment).
Status readCorners(const cgltf_data& data, const cgltf_mesh& mesh, Corners& corners, Vector<Submesh>& submeshes,
                   Warnings& warnings)
{
    const char* meshName = nameOf(mesh.name, "(unnamed)");
    bool warnedSkin = false;
    for (cgltf_size p = 0; p < mesh.primitives_count; ++p)
    {
        const cgltf_primitive& primitive = mesh.primitives[p];
        if (primitive.type != cgltf_primitive_type_triangles)
        {
            warnings.add("mesh %s: primitive %u isn't triangles (points, lines, strips, or fans), so it is left out",
                         meshName, static_cast<unsigned>(p));
            continue;
        }
        if (!warnedSkin && (primitive.targets_count > 0 || findAttribute(primitive, cgltf_attribute_type_joints, 0)))
        {
            warnings.add("mesh %s: skins and morph targets aren't imported yet", meshName);
            warnedSkin = true;
        }
        const cgltf_accessor* positionAccessor = findAttribute(primitive, cgltf_attribute_type_position, 0);
        Vector<float> positions = unpack(positionAccessor, 3);
        if (positions.empty())
        {
            warnings.add("mesh %s: primitive %u has no positions, so it is left out", meshName,
                         static_cast<unsigned>(p));
            continue;
        }
        const size_t vertexCount = positionAccessor->count;
        const Vector<float> normals = unpack(findAttribute(primitive, cgltf_attribute_type_normal, 0), 3);
        const Vector<float> tangents = unpack(findAttribute(primitive, cgltf_attribute_type_tangent, 0), 4);
        const Vector<float> uv0 = unpack(findAttribute(primitive, cgltf_attribute_type_texcoord, 0), 2);
        const Vector<float> uv1 = unpack(findAttribute(primitive, cgltf_attribute_type_texcoord, 1), 2);
        const cgltf_accessor* colorAccessor = findAttribute(primitive, cgltf_attribute_type_color, 0);
        const uint32_t colorComponents =
            colorAccessor != nullptr ? static_cast<uint32_t>(cgltf_num_components(colorAccessor->type)) : 0;
        const Vector<float> colors = unpack(colorAccessor, colorComponents == 3 ? 3 : 4);

        const size_t indexCount = primitive.indices != nullptr ? primitive.indices->count : vertexCount;
        if (indexCount % 3 != 0)
        {
            return fail("mesh %s: primitive %u has %zu indices, not a multiple of 3", meshName,
                        static_cast<unsigned>(p), indexCount);
        }
        const int32_t material =
            primitive.material != nullptr ? static_cast<int32_t>(cgltf_material_index(&data, primitive.material)) : -1;
        const uint32_t first = corners.count();
        // Reversing the winding: each triangle's corners in the order 0, 2, 1.
        constexpr int order[3] = {0, 2, 1};
        for (size_t triangle = 0; triangle < indexCount / 3; ++triangle)
        {
            for (int k : order)
            {
                const size_t i = triangle * 3 + static_cast<size_t>(k);
                const size_t v = primitive.indices != nullptr ? cgltf_accessor_read_index(primitive.indices, i) : i;
                if (v >= vertexCount)
                {
                    return fail("mesh %s: primitive %u has index %zu past its %zu vertices", meshName,
                                static_cast<unsigned>(p), v, vertexCount);
                }
                // Mirroring on X: x, and the tangent's handedness, change sign.
                corners.positions.pushBack(-positions[v * 3]);
                corners.positions.pushBack(positions[v * 3 + 1]);
                corners.positions.pushBack(positions[v * 3 + 2]);
                for (int c = 0; c < 3; ++c)
                {
                    corners.normals.pushBack(normals.empty() ? 0.0f : (c == 0 ? -1.0f : 1.0f) * normals[v * 3 + c]);
                }
                for (int c = 0; c < 4; ++c)
                {
                    corners.tangents.pushBack(
                        tangents.empty() ? 0.0f : (c == 0 || c == 3 ? -1.0f : 1.0f) * tangents[v * 4 + c]);
                }
                for (int c = 0; c < 2; ++c)
                {
                    corners.uv0.pushBack(uv0.empty() ? 0.0f : uv0[v * 2 + c]);
                    corners.uv1.pushBack(uv1.empty() ? 0.0f : uv1[v * 2 + c]);
                }
                for (uint32_t c = 0; c < 4; ++c)
                {
                    corners.colors.pushBack(colors.empty()        ? 1.0f
                                            : c < colorComponents ? colors[v * colorComponents + c]
                                                                  : 1.0f);
                }
            }
        }
        submeshes.pushBack(
            {first, corners.count() - first, material, !normals.empty(), !tangents.empty(), !uv0.empty()});
    }
    return Status::Ok;
}

Status buildMesh(const cgltf_data& data, const cgltf_mesh& mesh, const MeshSettings& settings, BuiltMesh& built,
                 Warnings& warnings)
{
    const char* meshName = nameOf(mesh.name, "(unnamed)");
    Corners corners;
    Vector<Submesh> submeshes(MemoryTag::Assets);
    if (const Status status = readCorners(data, mesh, corners, submeshes, warnings); status != Status::Ok)
    {
        return status;
    }

    // Which attributes the mesh has: any primitive's attribute makes it.
    bool anyTangents = false, anyUv0 = false, anyUv1 = false, anyColors = false;
    for (cgltf_size p = 0; p < mesh.primitives_count; ++p)
    {
        const cgltf_primitive& primitive = mesh.primitives[p];
        if (primitive.type != cgltf_primitive_type_triangles)
        {
            continue;
        }
        anyTangents |= findAttribute(primitive, cgltf_attribute_type_tangent, 0) != nullptr;
        anyUv0 |= findAttribute(primitive, cgltf_attribute_type_texcoord, 0) != nullptr;
        anyUv1 |= findAttribute(primitive, cgltf_attribute_type_texcoord, 1) != nullptr;
        anyColors |= findAttribute(primitive, cgltf_attribute_type_color, 0) != nullptr;
    }
    const bool tangentsWanted = anyTangents || (settings.generateTangents && anyUv0);

    // Normals: flat ones where the source has none, as glTF asks.
    for (const Submesh& submesh : submeshes)
    {
        if (submesh.hasNormals)
        {
            continue;
        }
        for (uint32_t corner = submesh.firstCorner; corner < submesh.firstCorner + submesh.cornerCount; corner += 3)
        {
            float normal[3];
            const float* p = corners.positions.data() + corner * 3;
            faceNormal(p, p + 3, p + 6, normal);
            for (uint32_t k = 0; k < 3; ++k)
            {
                std::memcpy(corners.normals.data() + (corner + k) * 3, normal, 12);
            }
        }
    }

    // Tangents: MikkTSpace where the source has none and has UVs.
    if (tangentsWanted)
    {
        for (const Submesh& submesh : submeshes)
        {
            if (submesh.hasTangents || submesh.cornerCount == 0)
            {
                continue;
            }
            if (!submesh.hasUv0)
            {
                warnings.add("mesh %s: a primitive has no UVs, so it gets no tangents (normal maps won't apply)",
                             meshName);
                continue;
            }
            TangentContext context{&corners, submesh.firstCorner, submesh.cornerCount};
            SMikkTSpaceInterface callbacks{};
            callbacks.m_getNumFaces = faceCount;
            callbacks.m_getNumVerticesOfFace = faceSize;
            callbacks.m_getPosition = tangentPosition;
            callbacks.m_getNormal = tangentNormal;
            callbacks.m_getTexCoord = tangentUv;
            callbacks.m_setTSpaceBasic = tangentSet;
            SMikkTSpaceContext mikk{&callbacks, &context};
            genTangSpaceDefault(&mikk);
        }
    }

    built.attributes = bit(MeshAttribute::Normals) | (tangentsWanted ? bit(MeshAttribute::Tangents) : 0) |
                       (anyUv0 ? bit(MeshAttribute::Uv0) : 0) | (anyUv1 ? bit(MeshAttribute::Uv1) : 0) |
                       (anyColors ? bit(MeshAttribute::Colors) : 0);
    built.submeshCount = static_cast<uint32_t>(submeshes.size());
    const uint32_t cornerCount = corners.count();
    if (cornerCount == 0)
    {
        built.lodErrors.pushBack(0.0f);
        for (const Submesh& submesh : submeshes)
        {
            built.ranges.pushBack({0, 0, submesh.material});
        }
        return Status::Ok;
    }

    // Weld identical corners into shared vertices. Welding compares bytes,
    // and mirroring (and the cross products above) can leave -0.0 where
    // another corner has 0.0: adding 0.0 turns every -0.0 into 0.0 first.
    for (Vector<float>* stream :
         {&corners.positions, &corners.normals, &corners.tangents, &corners.uv0, &corners.uv1, &corners.colors})
    {
        for (float& value : *stream)
        {
            value += 0.0f;
        }
    }
    meshopt_Stream streams[6] = {
        {corners.positions.data(), 12, 12}, {corners.normals.data(), 12, 12}, {corners.tangents.data(), 16, 16},
        {corners.uv0.data(), 8, 8},         {corners.uv1.data(), 8, 8},       {corners.colors.data(), 16, 16},
    };
    Vector<uint32_t> remap(MemoryTag::Assets);
    remap.resize(cornerCount);
    const size_t vertexCount =
        meshopt_generateVertexRemapMulti(remap.data(), nullptr, cornerCount, cornerCount, streams, 6);
    Vector<uint32_t> lod0(MemoryTag::Assets);
    lod0.resize(cornerCount);
    meshopt_remapIndexBuffer(lod0.data(), nullptr, cornerCount, remap.data());
    auto weld = [&](Vector<float>& from, size_t components, Vector<float>& to) {
        to.resize(vertexCount * components);
        meshopt_remapVertexBuffer(to.data(), from.data(), cornerCount, components * sizeof(float), remap.data());
    };
    weld(corners.positions, 3, built.positions);
    weld(corners.normals, 3, built.normals);
    weld(corners.tangents, 4, built.tangents);
    weld(corners.uv0, 2, built.uv0);
    weld(corners.uv1, 2, built.uv1);
    weld(corners.colors, 4, built.colors);

    // LOD 0: each submesh ordered for the vertex cache, then for overdraw.
    Vector<uint32_t> scratch(MemoryTag::Assets);
    if (settings.optimize)
    {
        for (const Submesh& submesh : submeshes)
        {
            uint32_t* range = lod0.data() + submesh.firstCorner;
            scratch.assign(std::span<const uint32_t>(range, submesh.cornerCount));
            meshopt_optimizeVertexCache(range, scratch.data(), submesh.cornerCount, vertexCount);
            scratch.assign(std::span<const uint32_t>(range, submesh.cornerCount));
            meshopt_optimizeOverdraw(range, scratch.data(), submesh.cornerCount, built.positions.data(), vertexCount,
                                     12, 1.05f);
        }
    }
    for (const Submesh& submesh : submeshes)
    {
        built.ranges.pushBack({submesh.firstCorner, submesh.cornerCount, submesh.material});
    }
    built.indices.assign(lod0.span());
    built.lodErrors.pushBack(0.0f);

    // LODs: each level simplified from LOD 0 towards a smaller target, with
    // the error capped; a submesh that won't shrink further repeats its last
    // level. Levels stop when none shrinks.
    if (settings.generateLods)
    {
        const float scale = meshopt_simplifyScale(built.positions.data(), vertexCount, 12);
        float target = 1.0f;
        for (uint32_t level = 1; level <= settings.maxLods; ++level)
        {
            target *= settings.lodRatio;
            bool shrank = false;
            float levelError = 0.0f;
            const size_t previousLevel = built.ranges.size() - submeshes.size();
            for (uint32_t s = 0; s < submeshes.size(); ++s)
            {
                const Submesh& submesh = submeshes[s];
                const BuiltMesh::Range previous = built.ranges[previousLevel + s];
                const size_t wanted = std::max<size_t>(3, static_cast<size_t>(submesh.cornerCount * target) / 3 * 3);
                scratch.resize(submesh.cornerCount);
                float error = 0.0f;
                size_t count =
                    meshopt_simplify(scratch.data(), lod0.data() + submesh.firstCorner, submesh.cornerCount,
                                     built.positions.data(), vertexCount, 12, wanted, settings.lodMaxError, 0, &error);
                const uint32_t first = static_cast<uint32_t>(built.indices.size());
                if (count == 0 || count >= previous.count * 95 / 100)
                {
                    // No real progress: this submesh keeps its last level.
                    for (uint32_t i = 0; i < previous.count; ++i)
                    {
                        built.indices.pushBack(built.indices[previous.first + i]);
                    }
                    built.ranges.pushBack({first, previous.count, submesh.material});
                    levelError = std::max(levelError, built.lodErrors[built.lodErrors.size() - 1]);
                    continue;
                }
                if (settings.optimize)
                {
                    Vector<uint32_t> ordered(MemoryTag::Assets);
                    ordered.resize(count);
                    meshopt_optimizeVertexCache(ordered.data(), scratch.data(), count, vertexCount);
                    std::memcpy(scratch.data(), ordered.data(), count * 4);
                }
                for (size_t i = 0; i < count; ++i)
                {
                    built.indices.pushBack(scratch[i]);
                }
                built.ranges.pushBack({first, static_cast<uint32_t>(count), submesh.material});
                levelError = std::max(levelError, error * scale);
                shrank = true;
            }
            if (!shrank)
            {
                built.ranges.resize(built.ranges.size() - submeshes.size());
                built.indices.resize(built.ranges[built.ranges.size() - 1].first +
                                     built.ranges[built.ranges.size() - 1].count);
                break;
            }
            built.lodErrors.pushBack(levelError);
        }
    }

    // Vertices in the order every level first uses them, LOD 0 first;
    // vertices no level uses are dropped.
    if (settings.optimize)
    {
        Vector<uint32_t> order(MemoryTag::Assets);
        order.resize(vertexCount);
        const size_t kept =
            meshopt_optimizeVertexFetchRemap(order.data(), built.indices.data(), built.indices.size(), vertexCount);
        meshopt_remapIndexBuffer(built.indices.data(), built.indices.data(), built.indices.size(), order.data());
        auto reorder = [&](Vector<float>& stream, size_t components) {
            Vector<float> moved(MemoryTag::Assets);
            moved.resize(kept * components);
            meshopt_remapVertexBuffer(moved.data(), stream.data(), vertexCount, components * sizeof(float),
                                      order.data());
            stream = static_cast<Vector<float>&&>(moved);
        };
        reorder(built.positions, 3);
        reorder(built.normals, 3);
        reorder(built.tangents, 4);
        reorder(built.uv0, 2);
        reorder(built.uv1, 2);
        reorder(built.colors, 4);
        built.vertexCount = static_cast<uint32_t>(kept);
    }
    else
    {
        built.vertexCount = static_cast<uint32_t>(vertexCount);
    }

    for (int c = 0; c < 3; ++c)
    {
        built.boundsMin[c] = built.positions[c];
        built.boundsMax[c] = built.positions[c];
    }
    for (uint32_t v = 0; v < built.vertexCount; ++v)
    {
        for (int c = 0; c < 3; ++c)
        {
            built.boundsMin[c] = std::min(built.boundsMin[c], built.positions[v * 3 + c]);
            built.boundsMax[c] = std::max(built.boundsMax[c], built.positions[v * 3 + c]);
        }
    }
    return Status::Ok;
}

// ---------------------------------------------------------------------------
// Packing
// ---------------------------------------------------------------------------

// Octahedral encoding: the direction's octant folded onto a square, as two
// 16-bit numbers. Decoding is a few instructions in a shader.
void octahedral(const float* direction, int16_t* out)
{
    float x = direction[0], y = direction[1];
    const float z = direction[2];
    const float sum = std::fabs(x) + std::fabs(y) + std::fabs(z);
    if (sum == 0.0f)
    {
        out[0] = out[1] = 0;
        return;
    }
    x /= sum;
    y /= sum;
    if (z < 0.0f)
    {
        const float foldedX = (1.0f - std::fabs(y)) * (x >= 0.0f ? 1.0f : -1.0f);
        const float foldedY = (1.0f - std::fabs(x)) * (y >= 0.0f ? 1.0f : -1.0f);
        x = foldedX;
        y = foldedY;
    }
    out[0] = static_cast<int16_t>(std::lround(std::clamp(x, -1.0f, 1.0f) * 32767.0f));
    out[1] = static_cast<int16_t>(std::lround(std::clamp(y, -1.0f, 1.0f) * 32767.0f));
}

struct Blob
{
    Vector<uint8_t> bytes{MemoryTag::Assets};

    template <class T>
    void add(const T& value)
    {
        const size_t at = bytes.size();
        bytes.resize(at + sizeof(T));
        std::memcpy(bytes.data() + at, &value, sizeof(T));
    }

    void align(size_t alignment)
    {
        bytes.resize((bytes.size() + alignment - 1) / alignment * alignment);
    }
};

// Decomposes a column-major matrix into translation, rotation, and scale.
void decompose(const float* m, float* translation, float* rotation, float* scale)
{
    translation[0] = m[12];
    translation[1] = m[13];
    translation[2] = m[14];
    float columns[3][3];
    for (int c = 0; c < 3; ++c)
    {
        scale[c] = std::sqrt(m[c * 4] * m[c * 4] + m[c * 4 + 1] * m[c * 4 + 1] + m[c * 4 + 2] * m[c * 4 + 2]);
        for (int r = 0; r < 3; ++r)
        {
            columns[c][r] = scale[c] > 0 ? m[c * 4 + r] / scale[c] : 0.0f;
        }
    }
    // A mirroring matrix: put the reflection into one scale.
    const float determinant = columns[0][0] * (columns[1][1] * columns[2][2] - columns[2][1] * columns[1][2]) -
                              columns[1][0] * (columns[0][1] * columns[2][2] - columns[2][1] * columns[0][2]) +
                              columns[2][0] * (columns[0][1] * columns[1][2] - columns[1][1] * columns[0][2]);
    if (determinant < 0)
    {
        scale[0] = -scale[0];
        for (int r = 0; r < 3; ++r)
        {
            columns[0][r] = -columns[0][r];
        }
    }
    // Rotation matrix (element row r, column c is columns[c][r]) to quaternion.
    const float trace = columns[0][0] + columns[1][1] + columns[2][2];
    if (trace > 0)
    {
        const float s = std::sqrt(trace + 1.0f) * 2.0f;
        rotation[3] = 0.25f * s;
        rotation[0] = (columns[1][2] - columns[2][1]) / s;
        rotation[1] = (columns[2][0] - columns[0][2]) / s;
        rotation[2] = (columns[0][1] - columns[1][0]) / s;
    }
    else if (columns[0][0] > columns[1][1] && columns[0][0] > columns[2][2])
    {
        const float s = std::sqrt(1.0f + columns[0][0] - columns[1][1] - columns[2][2]) * 2.0f;
        rotation[3] = (columns[1][2] - columns[2][1]) / s;
        rotation[0] = 0.25f * s;
        rotation[1] = (columns[1][0] + columns[0][1]) / s;
        rotation[2] = (columns[2][0] + columns[0][2]) / s;
    }
    else if (columns[1][1] > columns[2][2])
    {
        const float s = std::sqrt(1.0f + columns[1][1] - columns[0][0] - columns[2][2]) * 2.0f;
        rotation[3] = (columns[2][0] - columns[0][2]) / s;
        rotation[0] = (columns[1][0] + columns[0][1]) / s;
        rotation[1] = 0.25f * s;
        rotation[2] = (columns[2][1] + columns[1][2]) / s;
    }
    else
    {
        const float s = std::sqrt(1.0f + columns[2][2] - columns[0][0] - columns[1][1]) * 2.0f;
        rotation[3] = (columns[0][1] - columns[1][0]) / s;
        rotation[0] = (columns[2][0] + columns[0][2]) / s;
        rotation[1] = (columns[2][1] + columns[1][2]) / s;
        rotation[2] = 0.25f * s;
    }
}

} // namespace

// ---------------------------------------------------------------------------
// The API
// ---------------------------------------------------------------------------

struct GltfSource
{
    Vector<uint8_t> bytes{MemoryTag::Assets}; // cgltf points into it
    cgltf_data* data = nullptr;
    Vector<uint32_t> external{MemoryTag::Assets}; // buffers in other files
    Vector<char> uris{MemoryTag::Assets};         // their decoded URIs, back to back
    Vector<uint32_t> uriEnds{MemoryTag::Assets};
    Vector<void*> provided{MemoryTag::Assets}; // what setGltfBuffer copied, to free
};

struct MeshImport
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
    Vector<MaterialEntry> materials{MemoryTag::Assets};
    Vector<char> textureUris{MemoryTag::Assets}; // five per material, back to back
    Vector<uint32_t> textureUriEnds{MemoryTag::Assets};
    Warnings warnings;

    void addSection(uint32_t tag, const Vector<uint8_t>& section)
    {
        sections.pushBack({tag, 1, bytes.size(), section.size()});
        for (uint8_t value : section)
        {
            bytes.pushBack(value);
        }
    }
};

Result<GltfSource*> parseGltf(std::span<const uint8_t> bytes)
{
    clearError();
    GltfSource* source = create<GltfSource>(MemoryTag::Assets);
    source->bytes.assign(bytes);
    const cgltf_options parseOptions = options();
    const cgltf_result result = cgltf_parse(&parseOptions, source->bytes.data(), source->bytes.size(), &source->data);
    if (result != cgltf_result_success)
    {
        destroyGltfSource(source);
        return fail("can't read the glTF: %s", describe(result));
    }
    for (cgltf_size i = 0; i < source->data->buffers_count; ++i)
    {
        const char* uri = source->data->buffers[i].uri;
        if (uri == nullptr || isDataUri(uri))
        {
            continue;
        }
        source->external.pushBack(static_cast<uint32_t>(i));
        // Decoded in a copy: "my%20mesh.bin" is the file "my mesh.bin".
        Vector<char> decoded(MemoryTag::Assets);
        decoded.assign(std::span<const char>(uri, std::strlen(uri) + 1));
        const cgltf_size length = cgltf_decode_uri(decoded.data());
        for (cgltf_size c = 0; c < length; ++c)
        {
            source->uris.pushBack(decoded[c]);
        }
        source->uriEnds.pushBack(static_cast<uint32_t>(source->uris.size()));
    }
    return source;
}

void destroyGltfSource(GltfSource* source)
{
    if (source == nullptr)
    {
        return;
    }
    if (source->data != nullptr)
    {
        cgltf_free(source->data);
    }
    for (void* provided : source->provided)
    {
        freeSized(nullptr, provided);
    }
    destroy(MemoryTag::Assets, source);
}

uint32_t gltfBufferCount(const GltfSource& source)
{
    return static_cast<uint32_t>(source.external.size());
}

std::string_view gltfBufferUri(const GltfSource& source, uint32_t index)
{
    if (index >= source.uriEnds.size())
    {
        return {};
    }
    const uint32_t begin = index == 0 ? 0 : source.uriEnds[index - 1];
    return {source.uris.data() + begin, source.uriEnds[index] - begin};
}

Result<void> setGltfBuffer(GltfSource& source, uint32_t index, std::span<const uint8_t> bytes)
{
    if (index >= source.external.size())
    {
        return fail("there is no external buffer %u", index);
    }
    cgltf_buffer& buffer = source.data->buffers[source.external[index]];
    if (bytes.size() < buffer.size)
    {
        return fail("buffer %.*s has %zu bytes, but the glTF says %zu",
                    static_cast<int>(gltfBufferUri(source, index).size()), gltfBufferUri(source, index).data(),
                    bytes.size(), static_cast<size_t>(buffer.size));
    }
    void* copy = allocateSized(nullptr, bytes.size() == 0 ? 1 : bytes.size());
    if (copy == nullptr)
    {
        return Status::OutOfHostMemory;
    }
    std::memcpy(copy, bytes.data(), bytes.size());
    source.provided.pushBack(copy);
    buffer.data = copy;
    buffer.data_free_method = cgltf_data_free_method_none; // freed with the source
    return {};
}

Result<MeshImport*> importGltf(GltfSource& source, MeshSettings settings)
{
    clearError();
    cgltf_data& data = *source.data;
    for (uint32_t i = 0; i < source.external.size(); ++i)
    {
        if (data.buffers[source.external[i]].data == nullptr)
        {
            const std::string_view uri = gltfBufferUri(source, i);
            return fail("buffer %.*s wasn't provided", static_cast<int>(uri.size()), uri.data());
        }
    }
    const cgltf_options loadOptions = options();
    if (cgltf_load_buffers(&loadOptions, &data, nullptr) != cgltf_result_success)
    {
        return fail("can't read the glTF's embedded buffers");
    }
    if (const cgltf_result valid = cgltf_validate(&data); valid != cgltf_result_success)
    {
        return fail("the glTF isn't valid (%s): does an accessor or view point outside its buffer?", describe(valid));
    }
    settings.lodRatio = std::clamp(settings.lodRatio, 0.05f, 0.95f);
    settings.maxLods = std::min(settings.maxLods, 8u);

    MeshImport* result = create<MeshImport>(MemoryTag::Assets);
    Blob names;
    auto addName = [&](const char* name, uint32_t& offset, uint32_t& length) {
        offset = static_cast<uint32_t>(names.bytes.size());
        length = name != nullptr ? static_cast<uint32_t>(std::strlen(name)) : 0;
        for (uint32_t c = 0; c < length; ++c)
        {
            names.bytes.pushBack(static_cast<uint8_t>(name[c]));
        }
    };

    // Every mesh, built and appended to the shared streams.
    Vector<BuiltMesh*> built(MemoryTag::Assets);
    for (cgltf_size m = 0; m < data.meshes_count; ++m)
    {
        BuiltMesh* mesh = create<BuiltMesh>(MemoryTag::Assets);
        built.pushBack(mesh);
        if (const Status status = buildMesh(data, data.meshes[m], settings, *mesh, result->warnings);
            status != Status::Ok)
        {
            for (BuiltMesh* done : built)
            {
                destroy(MemoryTag::Assets, done);
            }
            destroyMeshImport(result);
            return status;
        }
    }

    uint32_t streams = 0, totalVertices = 0;
    bool floatUv0 = false, floatUv1 = false;
    for (const BuiltMesh* mesh : built)
    {
        streams |= mesh->attributes;
        totalVertices += mesh->vertexCount;
        for (uint32_t i = 0; i < mesh->vertexCount * 2; ++i)
        {
            floatUv0 |= std::fabs(mesh->uv0[i]) > 4.0f;
            floatUv1 |= std::fabs(mesh->uv1[i]) > 4.0f;
        }
    }

    Blob info, entries, lods, ranges, positions, normals, tangents, uv0, uv1, colors, indices, nodes;
    uint32_t vertexOffset = 0;
    for (cgltf_size m = 0; m < built.size(); ++m)
    {
        const BuiltMesh& mesh = *built[m];
        MeshEntry entry{};
        entry.vertexOffset = vertexOffset;
        entry.vertexCount = mesh.vertexCount;
        entry.indexSize = mesh.vertexCount <= 65535 ? 2 : 4;
        entry.attributes = mesh.vertexCount > 0 ? mesh.attributes : 0;
        entry.lodOffset = static_cast<uint32_t>(lods.bytes.size() / sizeof(MeshLod));
        entry.lodCount = static_cast<uint32_t>(mesh.lodErrors.size());
        entry.submeshCount = mesh.submeshCount;
        addName(data.meshes[m].name, entry.nameOffset, entry.nameLength);
        std::memcpy(entry.boundsMin, mesh.boundsMin, 12);
        std::memcpy(entry.boundsMax, mesh.boundsMax, 12);
        entries.add(entry);

        // Indices: each range's at its own offset, in the mesh's index size.
        indices.align(4);
        for (uint32_t level = 0; level < entry.lodCount; ++level)
        {
            lods.add(MeshLod{static_cast<uint32_t>(ranges.bytes.size() / sizeof(MeshRange)), mesh.lodErrors[level]});
            for (uint32_t s = 0; s < mesh.submeshCount; ++s)
            {
                const BuiltMesh::Range range = mesh.ranges[level * mesh.submeshCount + s];
                ranges.add(MeshRange{static_cast<uint32_t>(indices.bytes.size()), range.count, range.material});
                for (uint32_t i = 0; i < range.count; ++i)
                {
                    const uint32_t index = mesh.indices[range.first + i];
                    if (entry.indexSize == 2)
                    {
                        indices.add(static_cast<uint16_t>(index));
                    }
                    else
                    {
                        indices.add(index);
                    }
                }
            }
        }

        for (uint32_t v = 0; v < mesh.vertexCount; ++v)
        {
            for (int c = 0; c < 3; ++c)
            {
                positions.add(mesh.positions[v * 3 + c]);
            }
            int16_t packed[4] = {0, 0, 0, 0};
            octahedral(mesh.normals.data() + v * 3, packed);
            normals.add(packed[0]);
            normals.add(packed[1]);
            octahedral(mesh.tangents.data() + v * 4, packed);
            packed[2] = mesh.tangents[v * 4 + 3] < 0.0f ? int16_t{-32767} : int16_t{32767};
            for (int16_t value : packed)
            {
                tangents.add(value);
            }
            for (int c = 0; c < 2; ++c)
            {
                if (floatUv0)
                {
                    uv0.add(mesh.uv0[v * 2 + c]);
                }
                else
                {
                    uv0.add(toHalf(mesh.uv0[v * 2 + c]));
                }
                if (floatUv1)
                {
                    uv1.add(mesh.uv1[v * 2 + c]);
                }
                else
                {
                    uv1.add(toHalf(mesh.uv1[v * 2 + c]));
                }
            }
            const float* color = mesh.colors.data() + v * 4;
            colors.add(toByte(linearToSrgb(color[0])));
            colors.add(toByte(linearToSrgb(color[1])));
            colors.add(toByte(linearToSrgb(color[2])));
            colors.add(toByte(color[3]));
        }
        vertexOffset += mesh.vertexCount;
    }

    // Nodes, mirrored like everything else.
    for (cgltf_size n = 0; n < data.nodes_count; ++n)
    {
        const cgltf_node& node = data.nodes[n];
        MeshNode out{};
        out.parent = node.parent != nullptr ? static_cast<int32_t>(cgltf_node_index(&data, node.parent)) : -1;
        out.mesh = node.mesh != nullptr ? static_cast<int32_t>(cgltf_mesh_index(&data, node.mesh)) : -1;
        if (node.has_matrix)
        {
            decompose(node.matrix, out.translation, out.rotation, out.scale);
        }
        else
        {
            const float none[3] = {0, 0, 0}, identity[4] = {0, 0, 0, 1}, one[3] = {1, 1, 1};
            std::memcpy(out.translation, node.has_translation ? node.translation : none, 12);
            std::memcpy(out.rotation, node.has_rotation ? node.rotation : identity, 16);
            std::memcpy(out.scale, node.has_scale ? node.scale : one, 12);
        }
        out.translation[0] = -out.translation[0];
        out.rotation[1] = -out.rotation[1];
        out.rotation[2] = -out.rotation[2];
        addName(node.name, out.nameOffset, out.nameLength);
        nodes.add(out);
    }

    // Materials, with each texture's file for C# to turn into an asset ID.
    for (cgltf_size i = 0; i < data.materials_count; ++i)
    {
        const cgltf_material& material = data.materials[i];
        MaterialEntry entry{};
        const cgltf_pbr_metallic_roughness& pbr = material.pbr_metallic_roughness;
        const float white[4] = {1, 1, 1, 1};
        std::memcpy(entry.baseColor, material.has_pbr_metallic_roughness ? pbr.base_color_factor : white, 16);
        entry.metallic = material.has_pbr_metallic_roughness ? pbr.metallic_factor : 1.0f;
        entry.roughness = material.has_pbr_metallic_roughness ? pbr.roughness_factor : 1.0f;
        std::memcpy(entry.emissive, material.emissive_factor, 12);
        entry.alphaMode = material.alpha_mode == cgltf_alpha_mode_mask    ? 1u
                          : material.alpha_mode == cgltf_alpha_mode_blend ? 2u
                                                                          : 0u;
        entry.alphaCutoff = material.alpha_cutoff;
        entry.doubleSided = material.double_sided ? 1u : 0u;
        addName(material.name, entry.nameOffset, entry.nameLength);
        result->materials.pushBack(entry);

        const cgltf_texture_view* views[5] = {&pbr.base_color_texture, &pbr.metallic_roughness_texture,
                                              &material.normal_texture, &material.occlusion_texture,
                                              &material.emissive_texture};
        for (const cgltf_texture_view* view : views)
        {
            const cgltf_image* image = view->texture != nullptr ? view->texture->image : nullptr;
            if (image != nullptr && image->uri != nullptr && !isDataUri(image->uri))
            {
                Vector<char> decoded(MemoryTag::Assets);
                decoded.assign(std::span<const char>(image->uri, std::strlen(image->uri) + 1));
                const cgltf_size length = cgltf_decode_uri(decoded.data());
                for (cgltf_size c = 0; c < length; ++c)
                {
                    result->textureUris.pushBack(decoded[c]);
                }
            }
            else if (image != nullptr)
            {
                result->warnings.add("material %s: a texture is embedded in the glTF, which isn't imported yet; "
                                     "save it as its own image file",
                                     nameOf(material.name, "(unnamed)"));
            }
            result->textureUriEnds.pushBack(static_cast<uint32_t>(result->textureUris.size()));
        }
    }

    const MeshFileInfo fileInfo{static_cast<uint32_t>(built.size()),
                                static_cast<uint32_t>(data.nodes_count),
                                static_cast<uint32_t>(data.materials_count),
                                totalVertices,
                                streams,
                                (floatUv0 ? 1u : 0u) | (floatUv1 ? 2u : 0u)};
    info.add(fileInfo);
    result->addSection(binary::tag("INFO"), info.bytes);
    result->addSection(binary::tag("MSHS"), entries.bytes);
    result->addSection(binary::tag("LODS"), lods.bytes);
    result->addSection(binary::tag("RNGS"), ranges.bytes);
    result->addSection(binary::tag("POSN"), positions.bytes);
    if ((streams & bit(MeshAttribute::Normals)) != 0)
    {
        result->addSection(binary::tag("NRML"), normals.bytes);
    }
    if ((streams & bit(MeshAttribute::Tangents)) != 0)
    {
        result->addSection(binary::tag("TNGT"), tangents.bytes);
    }
    if ((streams & bit(MeshAttribute::Uv0)) != 0)
    {
        result->addSection(binary::tag("UV0_"), uv0.bytes);
    }
    if ((streams & bit(MeshAttribute::Uv1)) != 0)
    {
        result->addSection(binary::tag("UV1_"), uv1.bytes);
    }
    if ((streams & bit(MeshAttribute::Colors)) != 0)
    {
        result->addSection(binary::tag("COLR"), colors.bytes);
    }
    result->addSection(binary::tag("INDX"), indices.bytes);
    result->addSection(binary::tag("NODE"), nodes.bytes);
    result->addSection(binary::tag("NAME"), names.bytes);

    for (BuiltMesh* mesh : built)
    {
        destroy(MemoryTag::Assets, mesh);
    }
    return result;
}

void destroyMeshImport(MeshImport* mesh)
{
    if (mesh != nullptr)
    {
        destroy(MemoryTag::Assets, mesh);
    }
}

uint32_t meshSectionCount(const MeshImport& mesh)
{
    return static_cast<uint32_t>(mesh.sections.size());
}

MeshSectionInfo meshSection(const MeshImport& mesh, uint32_t index)
{
    if (index >= mesh.sections.size())
    {
        return {};
    }
    return {mesh.sections[index].tag, mesh.sections[index].version, mesh.sections[index].size};
}

void copyMeshSection(const MeshImport& mesh, uint32_t index, std::span<uint8_t> destination)
{
    if (index >= mesh.sections.size())
    {
        return;
    }
    const MeshImport::Section& section = mesh.sections[index];
    if (section.size > 0)
    {
        std::memcpy(destination.data(), mesh.bytes.data() + section.offset,
                    std::min<size_t>(section.size, destination.size()));
    }
}

uint32_t meshMaterialCount(const MeshImport& mesh)
{
    return static_cast<uint32_t>(mesh.materials.size());
}

MaterialEntry meshMaterial(const MeshImport& mesh, uint32_t index)
{
    return index < mesh.materials.size() ? mesh.materials[index] : MaterialEntry{};
}

std::string_view meshMaterialTexture(const MeshImport& mesh, uint32_t material, TextureRole role)
{
    const size_t slot = size_t{material} * 5 + static_cast<size_t>(role);
    if (material >= mesh.materials.size() || static_cast<uint32_t>(role) > 4)
    {
        return {};
    }
    const uint32_t begin = slot == 0 ? 0 : mesh.textureUriEnds[slot - 1];
    return {mesh.textureUris.data() + begin, mesh.textureUriEnds[slot] - begin};
}

uint32_t meshWarningCount(const MeshImport& mesh)
{
    return static_cast<uint32_t>(mesh.warnings.ends.size());
}

std::string_view meshWarning(const MeshImport& mesh, uint32_t index)
{
    if (index >= mesh.warnings.ends.size())
    {
        return {};
    }
    const uint32_t begin = index == 0 ? 0 : mesh.warnings.ends[index - 1];
    return {mesh.warnings.text.data() + begin, mesh.warnings.ends[index] - begin};
}

} // namespace crucible::importers
