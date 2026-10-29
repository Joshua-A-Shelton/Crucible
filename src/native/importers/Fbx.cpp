// The FBX reader (ADR 0044): ufbx converts each file to glTF's convention
// (right-handed, Y up, +Z front, meters) as it loads, and this reads the
// result into a SourceScene; the mesh builder does the rest, as for glTF.

#include "MeshBuilder.h"

#include "Common.h"

#include <crucible/Memory.h>
#include <crucible/Vector.h>

#include <ufbx.h>

#include <algorithm>
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

// One FBX mesh's polygons, triangulated, one submesh per material.
void readMesh(const ufbx_mesh& mesh, SourceMesh& out, StringList& warnings)
{
    const char* name = nameOf(mesh.name.data, "(unnamed)");
    if (mesh.skin_deformers.count > 0 || mesh.blend_deformers.count > 0)
    {
        warnings.addFormat("mesh %s: skins and blend shapes aren't imported yet", name);
    }
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
            }
        }
        out.submeshes.pushBack(
            {first, corners.count() - first, material, hasNormals, hasTangents, hasUv0, hasUv1, hasColors});
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
    if (fbx->anim_curves.count > 0)
    {
        scene.warnings.add("animation isn't imported yet");
    }
    for (size_t m = 0; m < fbx->meshes.count; ++m)
    {
        const ufbx_mesh& mesh = *fbx->meshes.data[m];
        readMesh(mesh, scene.addMesh(view(mesh.name)), scene.warnings);
    }
    for (size_t i = 0; i < fbx->materials.count; ++i)
    {
        readMaterial(*fbx->materials.data[i], scene);
    }

    // Nodes, without ufbx's implicit root: its children become roots.
    Vector<int32_t> nodeIndex(MemoryTag::Assets);
    nodeIndex.resize(fbx->nodes.count);
    int32_t next = 0;
    for (size_t n = 0; n < fbx->nodes.count; ++n)
    {
        nodeIndex[n] = fbx->nodes.data[n]->is_root ? -1 : next++;
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
    ufbx_free_scene(fbx);
    return buildMeshImport(scene, settings);
}

} // namespace crucible::importers
