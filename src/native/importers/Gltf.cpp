// The glTF reader (ADR 0043): cgltf's scene into a SourceScene, which is
// already in glTF's convention.

#include "MeshBuilder.h"

#include "Common.h"

#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <cgltf.h>

#include <algorithm>
#include <cmath>
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

// Reads one glTF mesh's triangle primitives into corners; skinned with
// `skin` (a node using the mesh names it), whose joints are the scene's by
// `jointOfNode`.
Status readCorners(const cgltf_data& data, const cgltf_mesh& mesh, const cgltf_skin* skin,
                   const Vector<int32_t>& jointOfNode, SourceMesh& out, StringList& warnings)
{
    Corners& corners = out.corners;
    const char* meshName = nameOf(mesh.name, "(unnamed)");
    bool warnedMorph = false;
    for (cgltf_size p = 0; p < mesh.primitives_count; ++p)
    {
        const cgltf_primitive& primitive = mesh.primitives[p];
        if (primitive.type != cgltf_primitive_type_triangles)
        {
            warnings.addFormat(
                "mesh %s: primitive %u isn't triangles (points, lines, strips, or fans), so it is left out", meshName,
                static_cast<unsigned>(p));
            continue;
        }
        if (!warnedMorph && primitive.targets_count > 0)
        {
            warnings.addFormat("mesh %s: morph targets aren't imported yet", meshName);
            warnedMorph = true;
        }
        // Skins (ADR 0078): two sets of four influences at most.
        const cgltf_accessor* jointSets[2] = {findAttribute(primitive, cgltf_attribute_type_joints, 0),
                                              findAttribute(primitive, cgltf_attribute_type_joints, 1)};
        Vector<float> weightSets[2] = {unpack(findAttribute(primitive, cgltf_attribute_type_weights, 0), 4),
                                       unpack(findAttribute(primitive, cgltf_attribute_type_weights, 1), 4)};
        const bool skinned = skin != nullptr && jointSets[0] != nullptr && !weightSets[0].empty();
        if (jointSets[0] != nullptr && skin == nullptr)
        {
            warnings.addFormat("mesh %s: has joints but no node skins it, so it isn't skinned", meshName);
        }
        const cgltf_accessor* positionAccessor = findAttribute(primitive, cgltf_attribute_type_position, 0);
        Vector<float> positions = unpack(positionAccessor, 3);
        if (positions.empty())
        {
            warnings.addFormat("mesh %s: primitive %u has no positions, so it is left out", meshName,
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
        for (size_t triangle = 0; triangle < indexCount / 3; ++triangle)
        {
            for (size_t k = 0; k < 3; ++k)
            {
                const size_t i = triangle * 3 + k;
                const size_t v = primitive.indices != nullptr ? cgltf_accessor_read_index(primitive.indices, i) : i;
                if (v >= vertexCount)
                {
                    return fail("mesh %s: primitive %u has index %zu past its %zu vertices", meshName,
                                static_cast<unsigned>(p), v, vertexCount);
                }
                for (int c = 0; c < 3; ++c)
                {
                    corners.positions.pushBack(positions[v * 3 + c]);
                    corners.normals.pushBack(normals.empty() ? 0.0f : normals[v * 3 + c]);
                }
                for (int c = 0; c < 4; ++c)
                {
                    corners.tangents.pushBack(tangents.empty() ? 0.0f : tangents[v * 4 + c]);
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
                for (int set = 0; set < 2; ++set)
                {
                    cgltf_uint local[4] = {0, 0, 0, 0};
                    const bool present = skinned && jointSets[set] != nullptr && !weightSets[set].empty();
                    if (present)
                    {
                        cgltf_accessor_read_uint(jointSets[set], v, local, 4);
                    }
                    for (int c = 0; c < 4; ++c)
                    {
                        const float weight = present ? weightSets[set][v * 4 + c] : 0.0f;
                        int32_t joint = 0;
                        if (present && local[c] < skin->joints_count)
                        {
                            joint = jointOfNode[cgltf_node_index(&data, skin->joints[local[c]])];
                        }
                        corners.joints.pushBack(static_cast<float>(std::max(joint, 0)));
                        corners.weights.pushBack(weight);
                    }
                }
            }
        }
        out.submeshes.pushBack({first, corners.count() - first, material, !normals.empty(), !tangents.empty(),
                                !uv0.empty(), !uv1.empty(), !colors.empty(), skinned});
    }
    return Status::Ok;
}

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

// ---------------------------------------------------------------------------
// Animation (ADR 0078)
// ---------------------------------------------------------------------------

// A sampler's value at time t: `width` floats (4 for rotations, renormalized),
// as its interpolation says (glTF 2.0 §3.11).
void evaluate(const cgltf_animation_sampler& sampler, const Vector<float>& times, const Vector<float>& values,
              uint32_t width, float t, float* out)
{
    const bool cubic = sampler.interpolation == cgltf_interpolation_type_cubic_spline;
    const size_t stride = cubic ? width * 3 : width;
    const size_t keys = times.size();
    // The value of key k (for cubic splines, the middle of its three).
    auto value = [&](size_t k, uint32_t c) {
        return values[k * stride + (cubic ? width : 0) + c];
    };
    if (keys == 0 || values.size() < keys * stride)
    {
        return;
    }
    if (t <= times[0] || keys == 1)
    {
        for (uint32_t c = 0; c < width; ++c)
        {
            out[c] = value(0, c);
        }
        return;
    }
    if (t >= times[keys - 1])
    {
        for (uint32_t c = 0; c < width; ++c)
        {
            out[c] = value(keys - 1, c);
        }
        return;
    }
    size_t k = 0;
    while (k + 2 < keys && times[k + 1] <= t)
    {
        ++k;
    }
    const float span = times[k + 1] - times[k];
    const float f = span > 0 ? (t - times[k]) / span : 0.0f;
    if (sampler.interpolation == cgltf_interpolation_type_step)
    {
        for (uint32_t c = 0; c < width; ++c)
        {
            out[c] = value(k, c);
        }
        return;
    }
    if (cubic)
    {
        // Hermite: the out-tangent of k and the in-tangent of k + 1, scaled by the span.
        const float f2 = f * f, f3 = f2 * f;
        for (uint32_t c = 0; c < width; ++c)
        {
            const float p0 = value(k, c), p1 = value(k + 1, c);
            const float m0 = values[k * stride + width * 2 + c] * span;
            const float m1 = values[(k + 1) * stride + c] * span;
            out[c] = (2 * f3 - 3 * f2 + 1) * p0 + (f3 - 2 * f2 + f) * m0 + (-2 * f3 + 3 * f2) * p1 + (f3 - f2) * m1;
        }
    }
    else if (width == 4)
    {
        // Rotations: slerp, the shorter way round.
        float a[4], b[4], dot = 0.0f;
        for (uint32_t c = 0; c < 4; ++c)
        {
            a[c] = value(k, c);
            b[c] = value(k + 1, c);
            dot += a[c] * b[c];
        }
        if (dot < 0)
        {
            dot = -dot;
            for (float& c : b)
            {
                c = -c;
            }
        }
        float wa = 1.0f - f, wb = f;
        if (dot < 0.9995f)
        {
            const float angle = std::acos(std::min(dot, 1.0f));
            wa = std::sin((1.0f - f) * angle) / std::sin(angle);
            wb = std::sin(f * angle) / std::sin(angle);
        }
        for (uint32_t c = 0; c < 4; ++c)
        {
            out[c] = wa * a[c] + wb * b[c];
        }
    }
    else
    {
        for (uint32_t c = 0; c < width; ++c)
        {
            out[c] = value(k, c) + (value(k + 1, c) - value(k, c)) * f;
        }
    }
    if (width == 4)
    {
        const float length = std::sqrt(out[0] * out[0] + out[1] * out[1] + out[2] * out[2] + out[3] * out[3]);
        for (uint32_t c = 0; c < 4 && length > 0; ++c)
        {
            out[c] /= length;
        }
    }
}

// Every animation, each targeted node's transform sampled at the clip rate;
// channels an animation doesn't drive hold the node's own transform.
void readAnimations(const cgltf_data& data, float sampleRate, SourceScene& scene)
{
    for (cgltf_size a = 0; a < data.animations_count; ++a)
    {
        const cgltf_animation& animation = data.animations[a];
        float duration = 0.0f;
        for (cgltf_size s = 0; s < animation.samplers_count; ++s)
        {
            const Vector<float> times = unpack(animation.samplers[s].input, 1);
            if (!times.empty())
            {
                duration = std::max(duration, times[times.size() - 1]);
            }
        }
        char fallback[32];
        std::snprintf(fallback, sizeof(fallback), "Animation %u", static_cast<unsigned>(a));
        SourceClip& clip = scene.addClip(nameOf(animation.name, fallback));
        clip.duration = duration;
        clip.sampleRate = sampleRate;
        clip.sampleCount = static_cast<uint32_t>(std::ceil(duration * sampleRate - 1e-3f)) + 1;
        Vector<int32_t> trackOfNode(MemoryTag::Assets);
        trackOfNode.resize(data.nodes_count);
        for (int32_t& track : trackOfNode)
        {
            track = -1;
        }
        bool warnedWeights = false;
        for (cgltf_size c = 0; c < animation.channels_count; ++c)
        {
            const cgltf_animation_channel& channel = animation.channels[c];
            if (channel.target_node == nullptr)
            {
                continue;
            }
            if (channel.target_path == cgltf_animation_path_type_weights)
            {
                if (!warnedWeights)
                {
                    scene.warnings.addFormat("animation %s: morph target weights aren't imported yet",
                                             nameOf(animation.name, fallback));
                    warnedWeights = true;
                }
                continue;
            }
            const size_t node = cgltf_node_index(&data, channel.target_node);
            if (trackOfNode[node] < 0)
            {
                // A new track, holding the node's own transform until a channel drives it.
                SourceTrack* track = create<SourceTrack>(MemoryTag::Assets);
                track->node = static_cast<int32_t>(node);
                const SourceNode& rest = scene.nodes[node];
                for (uint32_t i = 0; i < clip.sampleCount; ++i)
                {
                    for (int k = 0; k < 4; ++k)
                    {
                        track->rotations.pushBack(rest.rotation[k]);
                    }
                    for (int k = 0; k < 3; ++k)
                    {
                        track->translations.pushBack(rest.translation[k]);
                        track->scales.pushBack(rest.scale[k]);
                    }
                }
                trackOfNode[node] = static_cast<int32_t>(clip.tracks.size());
                clip.tracks.pushBack(track);
            }
            SourceTrack& track = *clip.tracks[static_cast<size_t>(trackOfNode[node])];
            const uint32_t width = channel.target_path == cgltf_animation_path_type_rotation ? 4 : 3;
            Vector<float>& out = channel.target_path == cgltf_animation_path_type_rotation      ? track.rotations
                                 : channel.target_path == cgltf_animation_path_type_translation ? track.translations
                                                                                                : track.scales;
            const Vector<float> times = unpack(channel.sampler->input, 1);
            const Vector<float> values = unpack(channel.sampler->output, width);
            for (uint32_t i = 0; i < clip.sampleCount; ++i)
            {
                const float t = std::min(static_cast<float>(i) / sampleRate, duration);
                evaluate(*channel.sampler, times, values, width, t, out.data() + i * width);
            }
        }
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

    SourceScene scene;
    // The skeleton (ADR 0078): every skin's joints, each node once.
    Vector<int32_t> jointOfNode(MemoryTag::Assets);
    jointOfNode.resize(data.nodes_count);
    for (int32_t& joint : jointOfNode)
    {
        joint = -1;
    }
    for (cgltf_size s = 0; s < data.skins_count; ++s)
    {
        const cgltf_skin& skin = data.skins[s];
        for (cgltf_size j = 0; j < skin.joints_count; ++j)
        {
            // Read as is, glTF's column-major matrix for column vectors is
            // the row-major one for row vectors that Matrix4x4 is.
            SourceJoint joint{static_cast<int32_t>(cgltf_node_index(&data, skin.joints[j])),
                              {1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1}};
            if (skin.inverse_bind_matrices != nullptr)
            {
                cgltf_accessor_read_float(skin.inverse_bind_matrices, j, joint.inverseBind, 16);
            }
            int32_t& index = jointOfNode[static_cast<size_t>(joint.node)];
            if (index < 0)
            {
                index = static_cast<int32_t>(scene.joints.size());
                scene.joints.pushBack(joint);
            }
            else if (std::memcmp(scene.joints[static_cast<size_t>(index)].inverseBind, joint.inverseBind, 64) != 0)
            {
                scene.warnings.addFormat("joint %s: two skins bind it differently; the first one's is kept",
                                         nameOf(skin.joints[j]->name, "(unnamed)"));
            }
        }
    }
    for (cgltf_size m = 0; m < data.meshes_count; ++m)
    {
        const cgltf_mesh& mesh = data.meshes[m];
        // The skin of the first node using the mesh with one.
        const cgltf_skin* skin = nullptr;
        for (cgltf_size n = 0; n < data.nodes_count && skin == nullptr; ++n)
        {
            if (data.nodes[n].mesh == &mesh)
            {
                skin = data.nodes[n].skin;
            }
        }
        SourceMesh& out = scene.addMesh(mesh.name != nullptr ? mesh.name : "");
        if (const Status status = readCorners(data, mesh, skin, jointOfNode, out, scene.warnings); status != Status::Ok)
        {
            return status;
        }
    }

    for (cgltf_size n = 0; n < data.nodes_count; ++n)
    {
        const cgltf_node& node = data.nodes[n];
        SourceNode out{};
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
        out.name = scene.strings.add(node.name != nullptr ? node.name : "");
        scene.nodes.pushBack(out);
    }
    readAnimations(data, std::clamp(settings.clipSampleRate, 1.0f, 240.0f), scene);

    for (cgltf_size i = 0; i < data.materials_count; ++i)
    {
        const cgltf_material& material = data.materials[i];
        SourceMaterial out{};
        MaterialEntry& entry = out.entry;
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
        out.name = scene.strings.add(material.name != nullptr ? material.name : "");

        const cgltf_texture_view* views[5] = {&pbr.base_color_texture, &pbr.metallic_roughness_texture,
                                              &material.normal_texture, &material.occlusion_texture,
                                              &material.emissive_texture};
        for (int role = 0; role < 5; ++role)
        {
            const cgltf_texture_view* view = views[role];
            const cgltf_image* image = view->texture != nullptr ? view->texture->image : nullptr;
            cgltf_size length = 0;
            Vector<char> decoded(MemoryTag::Assets);
            if (image != nullptr && image->uri != nullptr && !isDataUri(image->uri))
            {
                decoded.assign(std::span<const char>(image->uri, std::strlen(image->uri) + 1));
                length = cgltf_decode_uri(decoded.data());
            }
            else if (image != nullptr)
            {
                scene.warnings.addFormat("material %s: a texture is embedded in the glTF, which isn't imported yet; "
                                         "save it as its own image file",
                                         nameOf(material.name, "(unnamed)"));
            }
            out.textures[role] = scene.strings.add({decoded.data(), length});
        }
        scene.materials.pushBack(out);
    }
    return buildMeshImport(scene, settings);
}

} // namespace crucible::importers
