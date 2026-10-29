#include "RenderInternal.h"

#include <crucible/Camera.h>
#include <crucible/Ecs.h>
#include <crucible/Transform.h>

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
            chosen = {&cameras[i], worlds[i].matrix, index};
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

} // namespace

void registerCamera()
{
    ecs::registerComponent<Camera>("Crucible.Render.Camera");
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
    return constants;
}

} // namespace crucible::render
