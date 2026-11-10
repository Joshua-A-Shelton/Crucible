// The FBX reader (ADR 0044): ufbx converts each file to glTF's convention
// (right-handed, Y up, +Z front, meters) as it loads, and this reads the
// result into a SourceScene; the mesh builder does the rest, as for glTF.

#include "MeshBuilder.h"

#include "Common.h"

#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <ufbx.h>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace crucible::importers {

namespace {

// ufbx through Crucible's allocator. ufbx passes sizes back when freeing.
void* allocateFbx(void*, size_t size)
{
    return tryAllocate(std::max<size_t>(size, 1), 16, MemoryTag::Assets);
}

void freeFbx(void*, void* pointer, size_t size)
{
    if (pointer != nullptr)
    {
        free(pointer, std::max<size_t>(size, 1), MemoryTag::Assets);
    }
}

ufbx_load_opts loadOptions()
{
    ufbx_load_opts options{};
    for (ufbx_allocator_opts* allocator : {&options.temp_allocator, &options.result_allocator})
    {
        allocator->allocator.alloc_fn = allocateFbx;
        allocator->allocator.free_fn = freeFbx;
    }
    // Textures and caches in other files are C#'s to find; ufbx opens nothing.
    options.load_external_files = false;
    // Units and axes baked into geometry and transforms (ADR 0044): meters,
    // Y up, +Z front, with no hidden root scale.
    options.target_axes = ufbx_axes_right_handed_y_up;
    options.target_unit_meters = 1.0;
    options.space_conversion = UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY;
    // Transforms that apply to a node's mesh but not its children (3ds Max's
    // pivots) go into the vertices, with helper nodes only for instanced meshes.
    options.geometry_transform_handling = UFBX_GEOMETRY_TRANSFORM_HANDLING_MODIFY_GEOMETRY;
    // Maya's "segment scale compensate": undone in the children where it can
    // be, with helper nodes where it can't.
    options.inherit_mode_handling = UFBX_INHERIT_MODE_HANDLING_COMPENSATE;
    return options;
}

std::string_view view(ufbx_string text)
{
    return {text.data, text.length};
}

// A texture's file as the FBX stores it: the path relative to the FBX if
// there is one, else the absolute one; C# also looks for its file name
// beside the FBX (ADR 0044).
std::string_view texturePath(const ufbx_texture* texture)
{
    if (texture == nullptr)
    {
        return {};
    }
    if (texture->type != UFBX_TEXTURE_FILE && texture->file_textures.count > 0)
    {
        texture = texture->file_textures.data[0];
    }
    return texture->relative_filename.length > 0 ? view(texture->relative_filename) : view(texture->absolute_filename);
}

void readMaterial(const ufbx_material& material, SourceScene& scene)
{
    const ufbx_material_pbr_maps& pbr = material.pbr;
    const char* name = nameOf(material.name.data, "(unnamed)");
    SourceMaterial out{};
    MaterialEntry& entry = out.entry;
    const float baseFactor = pbr.base_factor.has_value ? static_cast<float>(pbr.base_factor.value_real) : 1.0f;
    for (int c = 0; c < 3; ++c)
    {
        entry.baseColor[c] = static_cast<float>(pbr.base_color.value_vec4.v[c]) * baseFactor;
    }
    entry.baseColor[3] = pbr.opacity.has_value ? static_cast<float>(pbr.opacity.value_real) : 1.0f;
    // Materials without a specular model (Lambert) are fully rough.
    entry.metallic = pbr.metalness.has_value ? static_cast<float>(pbr.metalness.value_real) : 0.0f;
    entry.roughness = pbr.roughness.has_value ? static_cast<float>(pbr.roughness.value_real) : 1.0f;
    if (material.features.roughness_as_glossiness.enabled)
    {
        entry.roughness = 1.0f - entry.roughness;
    }
    const float emissionFactor =
        pbr.emission_factor.has_value ? static_cast<float>(pbr.emission_factor.value_real) : 1.0f;
    for (int c = 0; c < 3; ++c)
    {
        entry.emissive[c] = static_cast<float>(pbr.emission_color.value_vec3.v[c]) * emissionFactor;
    }
    entry.alphaMode = entry.baseColor[3] < 1.0f || pbr.opacity.texture != nullptr ? 2u : 0u;
    entry.alphaCutoff = 0.5f;
    entry.doubleSided = material.features.double_sided.enabled ? 1u : 0u;
    out.name = scene.strings.add(view(material.name));

    // glTF's packed metallic-roughness texture has no FBX counterpart: FBX
    // keeps metalness and roughness apart.
    if (pbr.metalness.texture != nullptr || pbr.roughness.texture != nullptr)
    {
        scene.warnings.addFormat("material %s: separate metalness and roughness textures aren't combined yet, so "
                                 "they are left out",
                                 name);
    }
    const ufbx_texture* textures[5] = {pbr.base_color.texture, nullptr, pbr.normal_map.texture,
                                       pbr.ambient_occlusion.texture, pbr.emission_color.texture};
    for (int role = 0; role < 5; ++role)
    {
        const std::string_view path = texturePath(textures[role]);
        if (textures[role] != nullptr && textures[role]->content.size > 0)
        {
            // Tools that embed textures often also extract them beside the
            // FBX (an .fbm folder), which the stored path then finds.
            scene.warnings.addFormat("material %s: texture %.*s is embedded in the FBX, which isn't imported yet; "
                                     "it is looked for as a file instead",
                                     name, static_cast<int>(path.size()), path.data());
        }
        else if (textures[role] != nullptr && path.empty())
        {
            scene.warnings.addFormat("material %s: a texture has no file", name);
        }
        out.textures[role] = scene.strings.add(path);
    }
    scene.materials.pushBack(out);
}

void pushVec3(Vector<float>& stream, ufbx_vec3 value)
{
    stream.pushBack(static_cast<float>(value.x));
    stream.pushBack(static_cast<float>(value.y));
    stream.pushBack(static_cast<float>(value.z));
}

// FBX's UV origin is the bottom left, glTF's the top left: V flips.
void pushUv(Vector<float>& stream, const ufbx_vertex_vec2* uvs, uint32_t index)
{
    if (uvs == nullptr)
    {
        stream.pushBack(0.0f);
        stream.pushBack(0.0f);
        return;
    }
    const ufbx_vec2 uv = ufbx_get_vertex_vec2(uvs, index);
    stream.pushBack(static_cast<float>(uv.x));
    stream.pushBack(static_cast<float>(1.0 - uv.y));
}

// A ufbx matrix (column vectors) as Matrix4x4's row-major one for row vectors.
void rowMajor(const ufbx_matrix& m, float* out)
{
    for (int row = 0; row < 4; ++row)
    {
        out[row * 4 + 0] = static_cast<float>(m.cols[row].x);
        out[row * 4 + 1] = static_cast<float>(m.cols[row].y);
        out[row * 4 + 2] = static_cast<float>(m.cols[row].z);
        out[row * 4 + 3] = row == 3 ? 1.0f : 0.0f;
    }
}

// A corner's influences (ADR 0078): the vertex's eight strongest, as joints
// of the scene's skeleton; zeros without a skin.
void pushSkin(const ufbx_mesh& mesh, uint32_t index, const Vector<int32_t>& jointOfNode, Corners& corners)
{
    struct Influence
    {
        int32_t joint;
        float weight;
    };
    Influence strongest[8] = {};
    uint32_t used = 0;
    if (mesh.skin_deformers.count > 0)
    {
        const ufbx_skin_deformer& skin = *mesh.skin_deformers.data[0];
        const uint32_t vertex = mesh.vertex_indices.data[index];
        if (vertex < skin.vertices.count)
        {
            const ufbx_skin_vertex& weights = skin.vertices.data[vertex];
            for (uint32_t w = 0; w < weights.num_weights; ++w)
            {
                const ufbx_skin_weight& weight = skin.weights.data[weights.weight_begin + w];
                const ufbx_skin_cluster* cluster = skin.clusters.data[weight.cluster_index];
                if (cluster->bone_node == nullptr || jointOfNode[cluster->bone_node->typed_id] < 0)
                {
                    continue;
                }
                const Influence influence{jointOfNode[cluster->bone_node->typed_id], static_cast<float>(weight.weight)};
                if (used < 8)
                {
                    strongest[used++] = influence;
                }
                else
                {
                    // Full: replace the weakest if this is stronger.
                    uint32_t weakest = 0;
                    for (uint32_t k = 1; k < 8; ++k)
                    {
                        weakest = strongest[k].weight < strongest[weakest].weight ? k : weakest;
                    }
                    if (influence.weight > strongest[weakest].weight)
                    {
                        strongest[weakest] = influence;
                    }
                }
            }
        }
    }
    for (uint32_t k = 0; k < 8; ++k)
    {
        corners.joints.pushBack(static_cast<float>(k < used ? strongest[k].joint : 0));
        corners.weights.pushBack(k < used ? strongest[k].weight : 0.0f);
    }
}

// One FBX mesh's polygons, triangulated, one submesh per material.
void readMesh(const ufbx_mesh& mesh, const Vector<int32_t>& jointOfNode, SourceMesh& out, StringList& warnings)
{
    const char* name = nameOf(mesh.name.data, "(unnamed)");
    if (mesh.blend_deformers.count > 0)
    {
        warnings.addFormat("mesh %s: blend shapes aren't imported yet", name);
    }
    if (mesh.skin_deformers.count > 1)
    {
        warnings.addFormat("mesh %s: has %zu skins; only the first is imported", name, mesh.skin_deformers.count);
    }
    const bool hasSkin = mesh.skin_deformers.count > 0;
    if (mesh.num_line_faces > 0 || mesh.num_point_faces > 0)
    {
        warnings.addFormat("mesh %s: points and lines are left out", name);
    }
    const ufbx_vertex_vec2* uv1 = mesh.uv_sets.count > 1 ? &mesh.uv_sets.data[1].vertex_uv : nullptr;
    const bool hasNormals = mesh.vertex_normal.exists;
    const bool hasUv0 = mesh.vertex_uv.exists;
    const bool hasUv1 = uv1 != nullptr && uv1->exists;
    const bool hasColors = mesh.vertex_color.exists;
    const bool hasTangents = hasNormals && hasUv0 && mesh.vertex_tangent.exists && mesh.vertex_bitangent.exists;

    Corners& corners = out.corners;
    Vector<uint32_t> triangle(MemoryTag::Assets);
    triangle.resize(mesh.max_face_triangles * 3);
    for (size_t p = 0; p < mesh.material_parts.count; ++p)
    {
        const ufbx_mesh_part& part = mesh.material_parts.data[p];
        if (part.num_triangles == 0)
        {
            continue;
        }
        const int32_t material =
            part.index < mesh.materials.count ? static_cast<int32_t>(mesh.materials.data[part.index]->typed_id) : -1;
        const uint32_t first = corners.count();
        for (size_t f = 0; f < part.num_faces; ++f)
        {
            const ufbx_face face = mesh.faces.data[part.face_indices.data[f]];
            const uint32_t count = ufbx_triangulate_face(triangle.data(), triangle.size(), &mesh, face);
            for (uint32_t k = 0; k < count * 3; ++k)
            {
                const uint32_t i = triangle[k];
                pushVec3(corners.positions, ufbx_get_vertex_vec3(&mesh.vertex_position, i));
                ufbx_vec3 normal{};
                if (hasNormals)
                {
                    normal = ufbx_get_vertex_vec3(&mesh.vertex_normal, i);
                }
                pushVec3(corners.normals, normal);
                if (hasTangents)
                {
                    // FBX stores the bitangent where glTF stores its sign.
                    // Flipping V below turns the bitangent around, so the
                    // sign is the opposite of the stored bitangent's side.
                    const ufbx_vec3 tangent = ufbx_get_vertex_vec3(&mesh.vertex_tangent, i);
                    const ufbx_vec3 bitangent = ufbx_get_vertex_vec3(&mesh.vertex_bitangent, i);
                    const double side[3] = {normal.y * tangent.z - normal.z * tangent.y,
                                            normal.z * tangent.x - normal.x * tangent.z,
                                            normal.x * tangent.y - normal.y * tangent.x};
                    const double dot = side[0] * bitangent.x + side[1] * bitangent.y + side[2] * bitangent.z;
                    pushVec3(corners.tangents, tangent);
                    corners.tangents.pushBack(dot < 0 ? 1.0f : -1.0f);
                }
                else
                {
                    for (int c = 0; c < 4; ++c)
                    {
                        corners.tangents.pushBack(0.0f);
                    }
                }
                // FBX's UV origin is the bottom left, glTF's the top left.
                pushUv(corners.uv0, hasUv0 ? &mesh.vertex_uv : nullptr, i);
                pushUv(corners.uv1, hasUv1 ? uv1 : nullptr, i);
                if (hasColors)
                {
                    // Vertex colours as painted: sRGB, as the tools show them.
                    const ufbx_vec4 color = ufbx_get_vertex_vec4(&mesh.vertex_color, i);
                    for (int c = 0; c < 3; ++c)
                    {
                        corners.colors.pushBack(srgbToLinear(static_cast<float>(color.v[c])));
                    }
                    corners.colors.pushBack(static_cast<float>(color.w));
                }
                else
                {
                    for (int c = 0; c < 4; ++c)
                    {
                        corners.colors.pushBack(1.0f);
                    }
                }
                pushSkin(mesh, i, jointOfNode, corners);
            }
        }
        out.submeshes.pushBack(
            {first, corners.count() - first, material, hasNormals, hasTangents, hasUv0, hasUv1, hasColors, hasSkin});
    }
}

} // namespace

Result<MeshImport*> importFbx(std::span<const uint8_t> bytes, MeshSettings settings)
{
    clearError();
    const ufbx_load_opts options = loadOptions();
    ufbx_error error{};
    ufbx_scene* fbx = ufbx_load_memory(bytes.data(), bytes.size(), &options, &error);
    if (fbx == nullptr)
    {
        return fail("can't read the FBX: %.*s%s%s", static_cast<int>(error.description.length), error.description.data,
                    error.info_length > 0 ? ": " : "", error.info);
    }

    SourceScene scene;
    if (fbx->metadata.version < 7000)
    {
        scene.warnings.addFormat("the FBX is version %u, older than 7.0: re-export it from a newer tool if "
                                 "anything looks wrong",
                                 fbx->metadata.version);
    }
    // Nodes are numbered without ufbx's implicit root: its children become roots.
    Vector<int32_t> nodeIndex(MemoryTag::Assets);
    nodeIndex.resize(fbx->nodes.count);
    int32_t next = 0;
    for (size_t n = 0; n < fbx->nodes.count; ++n)
    {
        nodeIndex[n] = fbx->nodes.data[n]->is_root ? -1 : next++;
    }
    // The skeleton (ADR 0078): every skin cluster's bone, each once, bound by
    // its geometry-to-bone matrix (the inverse bind matrix).
    Vector<int32_t> jointOfNode(MemoryTag::Assets);
    jointOfNode.resize(fbx->nodes.count);
    for (int32_t& joint : jointOfNode)
    {
        joint = -1;
    }
    for (size_t c = 0; c < fbx->skin_clusters.count; ++c)
    {
        const ufbx_skin_cluster& cluster = *fbx->skin_clusters.data[c];
        if (cluster.bone_node == nullptr || nodeIndex[cluster.bone_node->typed_id] < 0 ||
            jointOfNode[cluster.bone_node->typed_id] >= 0)
        {
            continue;
        }
        SourceJoint joint{nodeIndex[cluster.bone_node->typed_id], {}};
        rowMajor(cluster.geometry_to_bone, joint.inverseBind);
        jointOfNode[cluster.bone_node->typed_id] = static_cast<int32_t>(scene.joints.size());
        scene.joints.pushBack(joint);
    }
    for (size_t m = 0; m < fbx->meshes.count; ++m)
    {
        const ufbx_mesh& mesh = *fbx->meshes.data[m];
        readMesh(mesh, jointOfNode, scene.addMesh(view(mesh.name)), scene.warnings);
    }
    for (size_t i = 0; i < fbx->materials.count; ++i)
    {
        readMaterial(*fbx->materials.data[i], scene);
    }

    for (size_t n = 0; n < fbx->nodes.count; ++n)
    {
        const ufbx_node& node = *fbx->nodes.data[n];
        if (node.is_root)
        {
            continue;
        }
        SourceNode out{};
        out.parent = node.parent != nullptr ? nodeIndex[node.parent->typed_id] : -1;
        // With a geometry-transform helper, the mesh hangs off the helper.
        out.mesh = node.mesh != nullptr && node.geometry_transform_helper == nullptr
                       ? static_cast<int32_t>(node.mesh->typed_id)
                       : -1;
        const ufbx_transform& transform = node.local_transform;
        for (int c = 0; c < 3; ++c)
        {
            out.translation[c] = static_cast<float>(transform.translation.v[c]);
            out.scale[c] = static_cast<float>(transform.scale.v[c]);
        }
        for (int c = 0; c < 4; ++c)
        {
            out.rotation[c] = static_cast<float>(transform.rotation.v[c]);
        }
        out.name = scene.strings.add(view(node.name));
        scene.nodes.pushBack(out);
    }

    // Each animation stack a clip: every joint's local transform, evaluated
    // by ufbx at the clip rate.
    const float sampleRate = std::clamp(settings.clipSampleRate, 1.0f, 240.0f);
    for (size_t a = 0; a < fbx->anim_stacks.count && !scene.joints.empty(); ++a)
    {
        const ufbx_anim_stack& stack = *fbx->anim_stacks.data[a];
        SourceClip& clip = scene.addClip(view(stack.name));
        clip.duration = static_cast<float>(std::max(stack.time_end - stack.time_begin, 0.0));
        clip.sampleRate = sampleRate;
        clip.sampleCount = static_cast<uint32_t>(std::ceil(clip.duration * sampleRate - 1e-3f)) + 1;
        for (size_t n = 0; n < fbx->nodes.count; ++n)
        {
            if (jointOfNode[n] < 0)
            {
                continue;
            }
            SourceTrack* track = create<SourceTrack>(MemoryTag::Assets);
            track->node = nodeIndex[n];
            for (uint32_t i = 0; i < clip.sampleCount; ++i)
            {
                const double time = stack.time_begin +
                                    std::min(static_cast<double>(i) / sampleRate, static_cast<double>(clip.duration));
                const ufbx_transform transform = ufbx_evaluate_transform(stack.anim, fbx->nodes.data[n], time);
                for (int c = 0; c < 4; ++c)
                {
                    track->rotations.pushBack(static_cast<float>(transform.rotation.v[c]));
                }
                for (int c = 0; c < 3; ++c)
                {
                    track->translations.pushBack(static_cast<float>(transform.translation.v[c]));
                    track->scales.pushBack(static_cast<float>(transform.scale.v[c]));
                }
            }
            clip.tracks.pushBack(track);
        }
    }
    ufbx_free_scene(fbx);
    return buildMeshImport(scene, settings);
}

} // namespace crucible::importers
