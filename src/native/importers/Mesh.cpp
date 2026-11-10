#include "MeshBuilder.h"

#include "Common.h"

#include <crucible/BinaryFile.h>

#include <meshoptimizer.h>
#include <mikktspace.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace crucible::importers {

uint32_t StringList::add(std::string_view value)
{
    for (char c : value)
    {
        text.pushBack(c);
    }
    ends.pushBack(static_cast<uint32_t>(text.size()));
    return count() - 1;
}

uint32_t StringList::addFormat(const char* format, ...)
{
    char line[512];
    va_list arguments;
    va_start(arguments, format);
    std::vsnprintf(line, sizeof(line), format, arguments);
    va_end(arguments);
    return add(line);
}

std::string_view StringList::at(uint32_t index) const
{
    if (index >= ends.size())
    {
        return {};
    }
    const uint32_t begin = index == 0 ? 0 : ends[index - 1];
    return {text.data() + begin, ends[index] - begin};
}

SourceScene::~SourceScene()
{
    for (SourceMesh* mesh : meshes)
    {
        destroy(MemoryTag::Assets, mesh);
    }
    for (SourceClip* clip : clips)
    {
        for (SourceTrack* track : clip->tracks)
        {
            destroy(MemoryTag::Assets, track);
        }
        destroy(MemoryTag::Assets, clip);
    }
}

SourceClip& SourceScene::addClip(std::string_view name)
{
    SourceClip* clip = create<SourceClip>(MemoryTag::Assets);
    clip->name = strings.add(name);
    clips.pushBack(clip);
    return *clip;
}

MeshImport::~MeshImport()
{
    for (SectionBlob* clip : clips)
    {
        destroy(MemoryTag::Assets, clip);
    }
}

SourceMesh& SourceScene::addMesh(std::string_view name)
{
    SourceMesh* mesh = create<SourceMesh>(MemoryTag::Assets);
    mesh->name = strings.add(name);
    meshes.pushBack(mesh);
    return *mesh;
}

const char* nameOf(const char* name, const char* fallback)
{
    return name != nullptr && name[0] != '\0' ? name : fallback;
}

namespace {

// ---------------------------------------------------------------------------
// Building one mesh, corner by corner
// ---------------------------------------------------------------------------

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
    Vector<float> joints{MemoryTag::Assets};  // 8 per vertex
    Vector<float> weights{MemoryTag::Assets}; // 8 per vertex
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

// Builds one mirrored source mesh; its corners are used up.
void buildMesh(SourceMesh& mesh, const char* meshName, const MeshSettings& settings, BuiltMesh& built,
               StringList& warnings)
{
    Corners& corners = mesh.corners;
    const Vector<Submesh>& submeshes = mesh.submeshes;

    // Which attributes the mesh has: any submesh's attribute makes it.
    bool anyTangents = false, anyUv0 = false, anyUv1 = false, anyColors = false, anySkin = false;
    for (const Submesh& submesh : submeshes)
    {
        anyTangents |= submesh.hasTangents;
        anyUv0 |= submesh.hasUv0;
        anyUv1 |= submesh.hasUv1;
        anyColors |= submesh.hasColors;
        anySkin |= submesh.hasSkin;
    }
    // Readers that don't skin leave the skin streams empty: zeros.
    corners.joints.resize(static_cast<size_t>(corners.count()) * 8);
    corners.weights.resize(static_cast<size_t>(corners.count()) * 8);
    const bool tangentsWanted = anyTangents || (settings.generateTangents && anyUv0);

    // Normals: flat ones where the source has none, as glTF asks (FBX too).
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
                warnings.addFormat("mesh %s: a primitive has no UVs, so it gets no tangents (normal maps won't apply)",
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

    built.attributes = bit(MeshAttribute::Normals) | bit(MeshAttribute::OutlineNormals) |
                       (tangentsWanted ? bit(MeshAttribute::Tangents) : 0) | (anyUv0 ? bit(MeshAttribute::Uv0) : 0) |
                       (anyUv1 ? bit(MeshAttribute::Uv1) : 0) | (anyColors ? bit(MeshAttribute::Colors) : 0) |
                       (anySkin ? bit(MeshAttribute::Skinned) : 0);
    built.submeshCount = static_cast<uint32_t>(submeshes.size());
    const uint32_t cornerCount = corners.count();
    if (cornerCount == 0)
    {
        built.lodErrors.pushBack(0.0f);
        for (const Submesh& submesh : submeshes)
        {
            built.ranges.pushBack({0, 0, submesh.material});
        }
        return;
    }

    // Weld identical corners into shared vertices. Welding compares bytes,
    // and mirroring (and the cross products above) can leave -0.0 where
    // another corner has 0.0: adding 0.0 turns every -0.0 into 0.0 first.
    for (Vector<float>* stream : {&corners.positions, &corners.normals, &corners.tangents, &corners.uv0, &corners.uv1,
                                  &corners.colors, &corners.joints, &corners.weights})
    {
        for (float& value : *stream)
        {
            value += 0.0f;
        }
    }
    meshopt_Stream streams[8] = {
        {corners.positions.data(), 12, 12}, {corners.normals.data(), 12, 12}, {corners.tangents.data(), 16, 16},
        {corners.uv0.data(), 8, 8},         {corners.uv1.data(), 8, 8},       {corners.colors.data(), 16, 16},
        {corners.joints.data(), 32, 32},    {corners.weights.data(), 32, 32},
    };
    Vector<uint32_t> remap(MemoryTag::Assets);
    remap.resize(cornerCount);
    const size_t vertexCount =
        meshopt_generateVertexRemapMulti(remap.data(), nullptr, cornerCount, cornerCount, streams, 8);
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
    weld(corners.joints, 8, built.joints);
    weld(corners.weights, 8, built.weights);

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
        reorder(built.joints, 8);
        reorder(built.weights, 8);
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
}

// ---------------------------------------------------------------------------
// Packing
// ---------------------------------------------------------------------------

// Outline normals (ADR 0055): every vertex at the same position gets the
// normalized sum of their normals, so inverted hulls push a hard edge's
// faces out together instead of splitting at it. Vertices are grouped by
// sorting on their positions' bits.
void outlineNormals(const BuiltMesh& mesh, Vector<float>& out)
{
    out.resize(size_t{mesh.vertexCount} * 3);
    Vector<uint32_t> order(MemoryTag::Assets);
    for (uint32_t v = 0; v < mesh.vertexCount; ++v)
    {
        order.pushBack(v);
    }
    auto key = [&](uint32_t v) {
        uint32_t bits[3];
        std::memcpy(bits, mesh.positions.data() + v * 3, sizeof(bits));
        return std::array<uint32_t, 3>{bits[0], bits[1], bits[2]};
    };
    std::sort(order.data(), order.data() + order.size(), [&](uint32_t a, uint32_t b) { return key(a) < key(b); });
    for (size_t first = 0; first < order.size();)
    {
        size_t last = first;
        float sum[3] = {0, 0, 0};
        while (last < order.size() && key(order[last]) == key(order[first]))
        {
            for (int c = 0; c < 3; ++c)
            {
                sum[c] += mesh.normals[order[last] * 3 + c];
            }
            ++last;
        }
        const float length = std::sqrt(sum[0] * sum[0] + sum[1] * sum[1] + sum[2] * sum[2]);
        for (size_t i = first; i < last; ++i)
        {
            for (int c = 0; c < 3; ++c)
            {
                // Opposite normals that cancel keep the vertex's own.
                out[order[i] * 3 + c] = length > 1e-4f ? sum[c] / length : mesh.normals[order[i] * 3 + c];
            }
        }
        first = last;
    }
}

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

// Mirroring on X (ADR 0024's amendment): x, and the tangent's handedness,
// change sign, and each triangle's last two corners swap so front faces
// stay front faces, now clockwise.
void swapCorners(Vector<float>& stream, uint32_t components, uint32_t a, uint32_t b)
{
    for (uint32_t c = 0; c < components; ++c)
    {
        std::swap(stream[a * components + c], stream[b * components + c]);
    }
}

void mirror(SourceScene& scene)
{
    for (SourceMesh* mesh : scene.meshes)
    {
        Corners& corners = mesh->corners;
        for (uint32_t i = 0; i < corners.count(); ++i)
        {
            corners.positions[i * 3] = -corners.positions[i * 3];
            corners.normals[i * 3] = -corners.normals[i * 3];
            corners.tangents[i * 4] = -corners.tangents[i * 4];
            corners.tangents[i * 4 + 3] = -corners.tangents[i * 4 + 3];
        }
        for (uint32_t i = 0; i + 2 < corners.count(); i += 3)
        {
            swapCorners(corners.positions, 3, i + 1, i + 2);
            swapCorners(corners.normals, 3, i + 1, i + 2);
            swapCorners(corners.tangents, 4, i + 1, i + 2);
            swapCorners(corners.uv0, 2, i + 1, i + 2);
            swapCorners(corners.uv1, 2, i + 1, i + 2);
            swapCorners(corners.colors, 4, i + 1, i + 2);
            swapCorners(corners.joints, 8, i + 1, i + 2);
            swapCorners(corners.weights, 8, i + 1, i + 2);
        }
    }
    for (SourceNode& node : scene.nodes)
    {
        node.translation[0] = -node.translation[0];
        node.rotation[1] = -node.rotation[1];
        node.rotation[2] = -node.rotation[2];
    }
    // A matrix M becomes S M S, S mirroring X: the first row's and column's
    // other elements change sign.
    for (SourceJoint& joint : scene.joints)
    {
        for (int i : {1, 2, 3, 4, 8, 12})
        {
            joint.inverseBind[i] = -joint.inverseBind[i];
        }
    }
    for (SourceClip* clip : scene.clips)
    {
        for (SourceTrack* track : clip->tracks)
        {
            for (size_t i = 0; i < track->translations.size(); i += 3)
            {
                track->translations[i] = -track->translations[i];
            }
            for (size_t i = 0; i < track->rotations.size(); i += 4)
            {
                track->rotations[i + 1] = -track->rotations[i + 1];
                track->rotations[i + 2] = -track->rotations[i + 2];
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Skins and clips (ADR 0078)
// ---------------------------------------------------------------------------

// The skeleton in an order where every joint comes after its parent joint,
// the nearest ancestor node that is one.
struct Skeleton
{
    Vector<int32_t> order{MemoryTag::Assets};    // new index to the scene's
    Vector<int32_t> newIndex{MemoryTag::Assets}; // the scene's index to new
    Vector<int32_t> parent{MemoryTag::Assets};   // by new index: the parent's new index, or -1
};

void orderJoints(const SourceScene& scene, Skeleton& skeleton)
{
    const size_t count = scene.joints.size();
    Vector<int32_t> jointOfNode(MemoryTag::Assets);
    jointOfNode.resize(scene.nodes.size());
    for (int32_t& joint : jointOfNode)
    {
        joint = -1;
    }
    for (size_t j = 0; j < count; ++j)
    {
        jointOfNode[static_cast<size_t>(scene.joints[j].node)] = static_cast<int32_t>(j);
    }
    Vector<int32_t> parentOf(MemoryTag::Assets);
    for (size_t j = 0; j < count; ++j)
    {
        int32_t node = scene.nodes[static_cast<size_t>(scene.joints[j].node)].parent;
        while (node >= 0 && jointOfNode[static_cast<size_t>(node)] < 0)
        {
            node = scene.nodes[static_cast<size_t>(node)].parent;
        }
        parentOf.pushBack(node >= 0 ? jointOfNode[static_cast<size_t>(node)] : -1);
    }
    skeleton.newIndex.resize(count);
    for (int32_t& index : skeleton.newIndex)
    {
        index = -1;
    }
    // Repeatedly take, in the scene's order, every joint whose parent is placed.
    while (skeleton.order.size() < count)
    {
        bool placed = false;
        for (size_t j = 0; j < count; ++j)
        {
            if (skeleton.newIndex[j] < 0 &&
                (parentOf[j] < 0 || skeleton.newIndex[static_cast<size_t>(parentOf[j])] >= 0))
            {
                skeleton.newIndex[j] = static_cast<int32_t>(skeleton.order.size());
                skeleton.order.pushBack(static_cast<int32_t>(j));
                skeleton.parent.pushBack(parentOf[j] < 0 ? -1 : skeleton.newIndex[static_cast<size_t>(parentOf[j])]);
                placed = true;
            }
        }
        if (!placed)
        {
            break; // a cycle can't happen in a node tree; this only guards the loop
        }
    }
}

// A vertex's strongest influences, renormalized to weights summing to 255.
void packSkin(const BuiltMesh& mesh, uint32_t vertex, uint32_t influences, const Skeleton& skeleton, Blob& joints,
              Blob& weights)
{
    struct Influence
    {
        uint32_t joint;
        float weight;
    };
    Influence all[8];
    uint32_t used = 0;
    for (uint32_t k = 0; k < 8; ++k)
    {
        const float weight = mesh.weights[vertex * 8 + k];
        if (weight > 0.0f)
        {
            all[used++] = {static_cast<uint32_t>(mesh.joints[vertex * 8 + k]), weight};
        }
    }
    std::sort(all, all + used, [](const Influence& a, const Influence& b) { return a.weight > b.weight; });
    used = std::min(used, influences);
    float total = 0.0f;
    for (uint32_t k = 0; k < used; ++k)
    {
        total += all[k].weight;
    }
    uint8_t quantized[8] = {};
    int sum = 0;
    for (uint32_t k = 0; k < used; ++k)
    {
        quantized[k] = static_cast<uint8_t>(std::lround(all[k].weight / total * 255.0f));
        sum += quantized[k];
    }
    if (used > 0)
    {
        quantized[0] = static_cast<uint8_t>(quantized[0] + (255 - sum)); // the strongest takes the rounding
    }
    for (uint32_t k = 0; k < influences; ++k)
    {
        const uint32_t joint = k < used ? all[k].joint : 0;
        joints.add(static_cast<uint16_t>(joint < skeleton.newIndex.size() ? skeleton.newIndex[joint] : 0));
        weights.add(quantized[k]);
    }
}

uint16_t unorm16(float value)
{
    return static_cast<uint16_t>(std::lround(std::clamp(value, 0.0f, 1.0f) * 65535.0f));
}

// Smallest three: the largest component dropped (the quaternion negated if
// it was negative), the others stored from [-1/√2, 1/√2], then its index.
void packRotation(const float* rotation, Blob& out)
{
    float q[4];
    const float length = std::sqrt(rotation[0] * rotation[0] + rotation[1] * rotation[1] + rotation[2] * rotation[2] +
                                   rotation[3] * rotation[3]);
    for (int c = 0; c < 4; ++c)
    {
        q[c] = length > 0 ? rotation[c] / length : (c == 3 ? 1.0f : 0.0f);
    }
    int largest = 0;
    for (int c = 1; c < 4; ++c)
    {
        if (std::fabs(q[c]) > std::fabs(q[largest]))
        {
            largest = c;
        }
    }
    const float sign = q[largest] < 0 ? -1.0f : 1.0f;
    constexpr float range = 0.70710678f;
    for (int c = 0; c < 4; ++c)
    {
        if (c != largest)
        {
            out.add(unorm16((q[c] * sign / range) * 0.5f + 0.5f));
        }
    }
    out.add(static_cast<uint16_t>(largest));
}

bool constantRotations(const Vector<float>& values, uint32_t samples)
{
    for (uint32_t i = 1; i < samples; ++i)
    {
        float dot = 0.0f;
        for (int c = 0; c < 4; ++c)
        {
            dot += values[c] * values[i * 4 + c];
        }
        if (std::fabs(dot) < 1.0f - 1e-6f)
        {
            return false;
        }
    }
    return true;
}

bool constantVectors(const Vector<float>& values, uint32_t samples)
{
    for (uint32_t i = 1; i < samples; ++i)
    {
        for (int c = 0; c < 3; ++c)
        {
            if (std::fabs(values[i * 3 + c] - values[c]) > 1e-5f)
            {
                return false;
            }
        }
    }
    return true;
}

// Vectors within their own range, as unorm16; one sample if constant.
void packVectors(const Vector<float>& values, uint32_t samples, bool constant, float* min, float* max, Blob& out)
{
    const uint32_t stored = constant ? 1 : samples;
    for (int c = 0; c < 3; ++c)
    {
        min[c] = values[c];
        max[c] = values[c];
    }
    for (uint32_t i = 0; i < stored; ++i)
    {
        for (int c = 0; c < 3; ++c)
        {
            min[c] = std::min(min[c], values[i * 3 + c]);
            max[c] = std::max(max[c], values[i * 3 + c]);
        }
    }
    for (uint32_t i = 0; i < stored; ++i)
    {
        for (int c = 0; c < 3; ++c)
        {
            const float span = max[c] - min[c];
            out.add(span > 0 ? unorm16((values[i * 3 + c] - min[c]) / span) : uint16_t{0});
        }
    }
}

SectionBlob* packClip(const SourceClip& clip, const SourceScene& scene)
{
    Blob info, tracks, rotations, translations, scales, names;
    for (const SourceTrack* track : clip.tracks)
    {
        assets::ClipTrack out{};
        const std::string_view name = scene.strings.at(scene.nodes[static_cast<size_t>(track->node)].name);
        out.nameOffset = static_cast<uint32_t>(names.bytes.size());
        out.nameLength = static_cast<uint32_t>(name.size());
        for (char c : name)
        {
            names.bytes.pushBack(static_cast<uint8_t>(c));
        }
        const bool rotationConstant = constantRotations(track->rotations, clip.sampleCount);
        const bool translationConstant = constantVectors(track->translations, clip.sampleCount);
        const bool scaleConstant = constantVectors(track->scales, clip.sampleCount);
        out.constant = (rotationConstant ? static_cast<uint32_t>(assets::ClipChannel::Rotation) : 0) |
                       (translationConstant ? static_cast<uint32_t>(assets::ClipChannel::Translation) : 0) |
                       (scaleConstant ? static_cast<uint32_t>(assets::ClipChannel::Scale) : 0);
        out.rotationOffset = static_cast<uint32_t>(rotations.bytes.size() / 8);
        out.translationOffset = static_cast<uint32_t>(translations.bytes.size() / 6);
        out.scaleOffset = static_cast<uint32_t>(scales.bytes.size() / 6);
        for (uint32_t i = 0; i < (rotationConstant ? 1 : clip.sampleCount); ++i)
        {
            packRotation(track->rotations.data() + i * 4, rotations);
        }
        packVectors(track->translations, clip.sampleCount, translationConstant, out.translationMin, out.translationMax,
                    translations);
        packVectors(track->scales, clip.sampleCount, scaleConstant, out.scaleMin, out.scaleMax, scales);
        tracks.add(out);
    }
    info.add(
        assets::ClipInfo{clip.duration, clip.sampleRate, clip.sampleCount, static_cast<uint32_t>(clip.tracks.size())});
    SectionBlob* result = create<SectionBlob>(MemoryTag::Assets);
    result->addSection(binary::tag("INFO"), info.bytes);
    result->addSection(binary::tag("TRCK"), tracks.bytes);
    result->addSection(binary::tag("ROTS"), rotations.bytes);
    result->addSection(binary::tag("POSS"), translations.bytes);
    result->addSection(binary::tag("SCLS"), scales.bytes);
    result->addSection(binary::tag("NAME"), names.bytes);
    return result;
}

} // namespace

// ---------------------------------------------------------------------------
// The API
// ---------------------------------------------------------------------------

Result<MeshImport*> buildMeshImport(SourceScene& scene, MeshSettings settings)
{
    settings.lodRatio = std::clamp(settings.lodRatio, 0.05f, 0.95f);
    settings.maxLods = std::min(settings.maxLods, 8u);
    settings.maxInfluences = settings.maxInfluences > 4 ? 8u : 4u;
    mirror(scene);
    Skeleton skeleton;
    orderJoints(scene, skeleton);

    MeshImport* result = create<MeshImport>(MemoryTag::Assets);
    result->warnings = static_cast<StringList&&>(scene.warnings);
    Blob names;
    auto addName = [&](uint32_t name, uint32_t& offset, uint32_t& length) {
        const std::string_view text = scene.strings.at(name);
        offset = static_cast<uint32_t>(names.bytes.size());
        length = static_cast<uint32_t>(text.size());
        for (char c : text)
        {
            names.bytes.pushBack(static_cast<uint8_t>(c));
        }
    };

    // Every mesh, built and appended to the shared streams.
    Vector<BuiltMesh*> built(MemoryTag::Assets);
    for (SourceMesh* source : scene.meshes)
    {
        BuiltMesh* mesh = create<BuiltMesh>(MemoryTag::Assets);
        built.pushBack(mesh);
        const std::string_view name = scene.strings.at(source->name);
        char meshName[128];
        std::snprintf(meshName, sizeof(meshName), "%.*s", static_cast<int>(name.size()), name.data());
        buildMesh(*source, nameOf(meshName, "(unnamed)"), settings, *mesh, result->warnings);
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

    Blob info, entries, lods, ranges, positions, normals, tangents, uv0, uv1, colors, outline, indices, nodes;
    Blob jointIndices, jointWeights, skin, joints;
    uint32_t vertexOffset = 0;
    for (uint32_t m = 0; m < built.size(); ++m)
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
        addName(scene.meshes[m]->name, entry.nameOffset, entry.nameLength);
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

        Vector<float> smoothed(MemoryTag::Assets);
        outlineNormals(mesh, smoothed);
        for (uint32_t v = 0; v < mesh.vertexCount; ++v)
        {
            for (int c = 0; c < 3; ++c)
            {
                positions.add(mesh.positions[v * 3 + c]);
            }
            int16_t edge[2];
            octahedral(smoothed.data() + v * 3, edge);
            outline.add(edge[0]);
            outline.add(edge[1]);
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
            if ((streams & bit(MeshAttribute::Skinned)) != 0)
            {
                packSkin(mesh, v, settings.maxInfluences, skeleton, jointIndices, jointWeights);
            }
        }
        vertexOffset += mesh.vertexCount;
    }

    for (const SourceNode& node : scene.nodes)
    {
        MeshNode out{node.parent, node.mesh};
        std::memcpy(out.translation, node.translation, 12);
        std::memcpy(out.rotation, node.rotation, 16);
        std::memcpy(out.scale, node.scale, 12);
        addName(node.name, out.nameOffset, out.nameLength);
        nodes.add(out);
    }

    // The skeleton, each joint after its parent; its name is its node's.
    for (uint32_t j = 0; j < skeleton.order.size(); ++j)
    {
        const SourceJoint& source = scene.joints[static_cast<size_t>(skeleton.order[j])];
        assets::JointEntry out{skeleton.parent[j], source.node};
        std::memcpy(out.inverseBind, source.inverseBind, sizeof(out.inverseBind));
        addName(scene.nodes[static_cast<size_t>(source.node)].name, out.nameOffset, out.nameLength);
        joints.add(out);
    }
    skin.add(assets::SkinInfo{static_cast<uint32_t>(skeleton.order.size()), settings.maxInfluences});

    // Materials, with each texture's file for C# to turn into an asset ID.
    for (const SourceMaterial& material : scene.materials)
    {
        MaterialEntry entry = material.entry;
        addName(material.name, entry.nameOffset, entry.nameLength);
        result->materials.pushBack(entry);
        for (uint32_t texture : material.textures)
        {
            result->textures.add(scene.strings.at(texture));
        }
    }

    const MeshFileInfo fileInfo{static_cast<uint32_t>(built.size()),
                                static_cast<uint32_t>(scene.nodes.size()),
                                static_cast<uint32_t>(scene.materials.size()),
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
    if ((streams & bit(MeshAttribute::OutlineNormals)) != 0)
    {
        result->addSection(binary::tag("ONRM"), outline.bytes);
    }
    if ((streams & bit(MeshAttribute::Colors)) != 0)
    {
        result->addSection(binary::tag("COLR"), colors.bytes);
    }
    if ((streams & bit(MeshAttribute::Skinned)) != 0)
    {
        result->addSection(binary::tag("JIDX"), jointIndices.bytes);
        result->addSection(binary::tag("JWGT"), jointWeights.bytes);
    }
    result->addSection(binary::tag("INDX"), indices.bytes);
    result->addSection(binary::tag("NODE"), nodes.bytes);
    if (!skeleton.order.empty())
    {
        result->addSection(binary::tag("SKIN"), skin.bytes);
        result->addSection(binary::tag("JNTS"), joints.bytes);
    }
    result->addSection(binary::tag("NAME"), names.bytes);

    for (BuiltMesh* mesh : built)
    {
        destroy(MemoryTag::Assets, mesh);
    }

    // The animation clips, each a CLIP file of its own.
    for (const SourceClip* clip : scene.clips)
    {
        if (clip->sampleCount == 0 || clip->tracks.empty())
        {
            continue;
        }
        result->clips.pushBack(packClip(*clip, scene));
        result->clipNames.add(scene.strings.at(clip->name));
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
    return mesh.textures.at(static_cast<uint32_t>(slot));
}

uint32_t meshWarningCount(const MeshImport& mesh)
{
    return mesh.warnings.count();
}

std::string_view meshWarning(const MeshImport& mesh, uint32_t index)
{
    return mesh.warnings.at(index);
}

uint32_t meshClipCount(const MeshImport& mesh)
{
    return static_cast<uint32_t>(mesh.clips.size());
}

std::string_view meshClipName(const MeshImport& mesh, uint32_t clip)
{
    return mesh.clipNames.at(clip);
}

uint32_t meshClipSectionCount(const MeshImport& mesh, uint32_t clip)
{
    return clip < mesh.clips.size() ? static_cast<uint32_t>(mesh.clips[clip]->sections.size()) : 0;
}

MeshSectionInfo meshClipSection(const MeshImport& mesh, uint32_t clip, uint32_t index)
{
    if (clip >= mesh.clips.size() || index >= mesh.clips[clip]->sections.size())
    {
        return {};
    }
    const SectionBlob::Section& section = mesh.clips[clip]->sections[index];
    return {section.tag, section.version, section.size};
}

void copyMeshClipSection(const MeshImport& mesh, uint32_t clip, uint32_t index, std::span<uint8_t> destination)
{
    if (clip >= mesh.clips.size() || index >= mesh.clips[clip]->sections.size())
    {
        return;
    }
    const SectionBlob& blob = *mesh.clips[clip];
    const SectionBlob::Section& section = blob.sections[index];
    std::memcpy(destination.data(), blob.bytes.data() + section.offset,
                std::min<size_t>(destination.size(), static_cast<size_t>(section.size)));
}

} // namespace crucible::importers
