#include "RenderInternal.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/PostProcess.h>
#include <crucible/Transform.h>

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace crucible::render {

namespace {

// Without a camera: at the origin looking along +Z, 60 degrees high.
constexpr Camera defaultCamera{.projection = CameraProjection::Perspective,
                               .fieldOfViewY = 1.0471976f,
                               .orthographicHeight = 10.0f,
                               .nearPlane = 0.1f,
                               .farPlane = 1000.0f,
                               .priority = 0};

struct Chosen
{
    const Camera* camera = nullptr;
    ecs::Entity entity{};
    Matrix4x4 world = Matrix4x4::identity();
    uint32_t index = 0;
};

void chooseCamera(const ecs::ChunkView& view, void* data)
{
    auto& chosen = *static_cast<Chosen*>(data);
    const std::span<const Camera> cameras = ecs::readColumn<Camera>(view);
    const std::span<const scenes::WorldTransform> worlds = ecs::readColumn<scenes::WorldTransform>(view);
    const ecs::Entity* entities = ecs::entitiesOf(view);
    for (size_t i = 0; i < cameras.size(); ++i)
    {
        const uint32_t index = ecs::indexOf(entities[i]);
        if (chosen.camera == nullptr || cameras[i].priority > chosen.camera->priority ||
            (cameras[i].priority == chosen.camera->priority && index < chosen.index))
        {
            chosen = {&cameras[i], entities[i], worlds[i].matrix, index};
        }
    }
}

// The camera's world matrix without scale (or the shear a non-uniformly
// scaled parent adds): its axes normalized, its position kept.
Matrix4x4 withoutScale(const Matrix4x4& world)
{
    Matrix4x4 result = world;
    for (int axis = 0; axis < 3; ++axis)
    {
        const Vector3 direction = normalize(Vector3{world.rows[axis].x, world.rows[axis].y, world.rows[axis].z});
        result.rows[axis] = {direction.x, direction.y, direction.z, 0};
    }
    return result;
}

// White balance (as in Unity's): the shift from D65 to the white point
// `temperature` and `tint` describe, done in LMS cone space; one matrix for
// row vectors.
Matrix4x4 whiteBalance(float temperature, float tint)
{
    const float t1 = std::clamp(temperature, -1.0f, 1.0f) * 10.0f / 6.0f;
    const float t2 = std::clamp(tint, -1.0f, 1.0f) * 10.0f / 6.0f;
    const float x = 0.31271f - t1 * (t1 < 0 ? 0.1f : 0.05f);
    const float y = 2.87f * x - 3.0f * x * x - 0.27509507f + t2 * 0.05f;
    const float X = x / y, Y = 1.0f, Z = (1.0f - x - y) / y;
    const float white[3] = {0.7328f * X + 0.4296f * Y - 0.1624f * Z, -0.7036f * X + 1.6975f * Y + 0.0061f * Z,
                            0.0030f * X + 0.0136f * Y + 0.9834f * Z};
    const float d65[3] = {0.949237f, 1.03542f, 1.08728f};
    // Column-vector matrices: linear sRGB to LMS, and back.
    const float toLms[3][3] = {{3.90405e-1f, 5.49941e-1f, 8.92632e-3f},
                               {7.08416e-2f, 9.63172e-1f, 1.35775e-3f},
                               {2.31082e-2f, 1.28021e-1f, 9.36245e-1f}};
    const float fromLms[3][3] = {{2.85847e+0f, -1.62879e+0f, -2.48910e-2f},
                                 {-2.10182e-1f, 1.15820e+0f, 3.24281e-4f},
                                 {-4.18120e-2f, -1.18169e-1f, 1.06867e+0f}};
    // fromLms * diag(d65 / white) * toLms, transposed for row vectors.
    Matrix4x4 result = Matrix4x4::identity();
    for (int r = 0; r < 3; ++r)
    {
        for (int c = 0; c < 3; ++c)
        {
            float sum = 0;
            for (int k = 0; k < 3; ++k)
            {
                sum += fromLms[r][k] * (d65[k] / white[k]) * toLms[k][c];
            }
            result.rows[c][r] = sum;
        }
    }
    return result;
}

void finishing(ViewConstants& constants, const PostProcess& post)
{
    constants.exposureScale = std::exp2(post.exposure);
    constants.tonemapper = static_cast<uint32_t>(post.tonemapper);
    constants.bloomStrength = std::clamp(post.bloomStrength, 0.0f, 1.0f);
    constants.bloomThreshold = std::max(post.bloomThreshold, 0.0f);
    constants.whiteBalance =
        post.temperature == 0 && post.tint == 0 ? Matrix4x4::identity() : whiteBalance(post.temperature, post.tint);
    constants.contrast = std::max(post.contrast, -0.99f);
    constants.saturation = std::max(post.saturation, -1.0f);
    constants.lift = post.lift;
    constants.gamma = max(post.gamma, Vector3{-0.99f, -0.99f, -0.99f});
    constants.gain = post.gain;
}

} // namespace

std::array<Plane, 6> frustumPlanes(const Matrix4x4& m)
{
    // clip = p * M, so each plane is a sum of M's columns.
    auto column = [&](int j) {
        return Vector4{m.rows[0][j], m.rows[1][j], m.rows[2][j], m.rows[3][j]};
    };
    const Vector4 x = column(0), y = column(1), z = column(2), w = column(3);
    const Vector4 sums[6] = {w + x, w - x, w + y, w - y, w - z, z};
    std::array<Plane, 6> planes{};
    for (int i = 0; i < 6; ++i)
    {
        const float length = std::sqrt(sums[i].x * sums[i].x + sums[i].y * sums[i].y + sums[i].z * sums[i].z);
        const float scale = length > 0 ? 1.0f / length : 0.0f;
        planes[i] = {{sums[i].x * scale, sums[i].y * scale, sums[i].z * scale}, sums[i].w * scale};
    }
    return planes;
}

void registerCamera()
{
    ecs::registerComponent<Camera>("Crucible.Render.Camera");
    ecs::registerComponent<PostProcess>("Crucible.Render.PostProcess");
}

ViewConstants viewConstants(ecs::World* world, uint32_t width, uint32_t height, float time)
{
    Chosen chosen;
    if (world != nullptr)
    {
        const ecs::ComponentId required[] = {ecs::componentIdOf<Camera>, ecs::componentIdOf<scenes::WorldTransform>};
        if (required[0] != ecs::invalidComponent && required[1] != ecs::invalidComponent)
        {
            ecs::Query* cameras = ecs::createQuery(*world, {.required = required});
            ecs::forEachChunk(*cameras, chooseCamera, &chosen);
            ecs::destroyQuery(cameras);
        }
    }
    const Camera& camera = chosen.camera != nullptr ? *chosen.camera : defaultCamera;
    const float aspectRatio = height != 0 ? static_cast<float>(width) / static_cast<float>(height) : 1.0f;

    ViewConstants constants{};
    constants.inverseView = withoutScale(chosen.world);
    (void)invert(constants.inverseView, constants.view);
    constants.projection =
        camera.projection == CameraProjection::Orthographic
            ? Matrix4x4::orthographicReverseZ(camera.orthographicHeight * aspectRatio, camera.orthographicHeight,
                                              camera.nearPlane, camera.farPlane)
            : Matrix4x4::perspectiveReverseZInfinite(camera.fieldOfViewY, aspectRatio, camera.nearPlane);
    (void)invert(constants.projection, constants.inverseProjection);
    constants.viewProjection = constants.view * constants.projection;
    constants.inverseViewProjection = constants.inverseProjection * constants.inverseView;
    const Vector4 position = constants.inverseView.rows[3];
    constants.cameraPosition = {position.x, position.y, position.z};
    constants.time = time;
    constants.viewportSize = {static_cast<float>(width), static_cast<float>(height)};
    const PostProcess* post = chosen.camera != nullptr && ecs::componentIdOf<PostProcess> != ecs::invalidComponent
                                  ? ecs::read<PostProcess>(*world, chosen.entity)
                                  : nullptr;
    finishing(constants, post != nullptr ? *post : PostProcess{});
    // The bloom chain halves from half size down to about 8 texels, at most
    // maxBloomLevels levels.
    constants.bloomLevels = 0;
    const uint32_t half = std::min(width, height) / 2;
    if (constants.bloomStrength > 0 && half >= 8)
    {
        const auto levels = static_cast<uint32_t>(std::floor(std::log2(static_cast<float>(half)))) - 2;
        constants.bloomLevels = std::clamp(levels, 1u, maxBloomLevels);
    }
    return constants;
}

} // namespace crucible::render
