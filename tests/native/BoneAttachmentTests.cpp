// Bone attachments (ADR 0096): an entity riding a bone of the nearest
// Animator above it, at an offset, keeping its own scale; an attachment under
// an attachment follows both; without a bone to ride, it's left where it is.

#include "AnimationTestSupport.h"

#include <crucible/Transform.h>

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

ecs::Entity attached(Character& walker, ecs::Entity parent, BoneAttachment attachment, Vector3 scale = {1, 1, 1})
{
    const ecs::ComponentId components[] = {ecs::componentIdOf<BoneAttachment>,
                                           ecs::componentIdOf<scenes::LocalTransform>,
                                           ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(*walker.world, components);
    *ecs::get<BoneAttachment>(*walker.world, entity) = attachment;
    *ecs::get<scenes::LocalTransform>(*walker.world, entity) = {{0, 0, 0}, Quaternion::identity(), scale};
    REQUIRE(ecs::setParent(*walker.world, entity, parent));
    return entity;
}

Vector3 positionOf(const Matrix4x4& m)
{
    return {m.rows[3].x, m.rows[3].y, m.rows[3].z};
}

Vector3 axisOf(const Matrix4x4& m, int row)
{
    return normalize(Vector3{m.rows[row].x, m.rows[row].y, m.rows[row].z});
}

// Where the offset from a bone is in the world, as the attachment should be.
Matrix4x4 expectedAt(Character& walker, const char* bone, Vector3 offset, Quaternion turn)
{
    const int32_t index = findBone(*walker.world, walker.entity, bone);
    REQUIRE(index >= 0);
    Matrix4x4 at = Matrix4x4::rotation(turn);
    at.rows[3] = {offset.x, offset.y, offset.z, 1};
    return at * walker.model(index) * scenes::computeWorldTransform(*walker.world, walker.entity);
}

void checkPlaced(Character& walker, ecs::Entity entity, const Matrix4x4& expected, float scale)
{
    const Matrix4x4 placed = scenes::computeWorldTransform(*walker.world, entity);
    CHECK(length(positionOf(placed) - positionOf(expected)) < 1e-4f);
    for (int row = 0; row < 3; ++row)
    {
        CHECK(length(axisOf(placed, row) - axisOf(expected, row)) < 1e-4f);
        CHECK(length(Vector3{placed.rows[row].x, placed.rows[row].y, placed.rows[row].z}) ==
              doctest::Approx(scale).epsilon(1e-4));
    }
}

} // namespace

TEST_CASE("An attachment rides its bone at its offset and keeps its own scale")
{
    Character walker;
    *ecs::get<scenes::LocalTransform>(*walker.world, walker.entity) = {
        {1, 0, 2}, Quaternion::fromAxisAngle({0, 1, 0}, 0.7f), {1, 1, 1}};
    const Quaternion tilt = Quaternion::fromAxisAngle({1, 0, 0}, 0.4f);
    const ecs::Entity sword =
        attached(walker, walker.entity, {eventName("RightLowerArm"), {0, -0.3f, 0.05f}, tilt}, {2, 2, 2});
    for (const float seconds : {0.1f, 0.35f, 0.2f})
    {
        walker.step(seconds);
        placeBoneAttachments(*walker.world);
        checkPlaced(walker, sword, expectedAt(walker, "RightLowerArm", {0, -0.3f, 0.05f}, tilt), 2);
    }
    CHECK(ecs::read<scenes::LocalTransform>(*walker.world, sword)->scale.x == 2);
}

TEST_CASE("An attachment under an attachment follows both however the entities were made")
{
    Character walker;
    // The inner one is made first, so it comes first unless parents are placed first.
    const ecs::Entity tip = attached(walker, walker.entity, {eventName("RightFoot"), {0, 0, 0.2f}, {0, 0, 0, 1}});
    const ecs::Entity foot = attached(walker, walker.entity, {eventName("RightLowerLeg"), {}, {0, 0, 0, 1}});
    REQUIRE(ecs::setParent(*walker.world, tip, foot));
    *ecs::get<scenes::LocalTransform>(*walker.world, walker.entity) = {{0, 0, 0}, Quaternion::identity(), {1, 1, 1}};
    walker.step(0.3f);
    placeBoneAttachments(*walker.world);
    checkPlaced(walker, foot, expectedAt(walker, "RightLowerLeg", {}, Quaternion::identity()), 1);
    checkPlaced(walker, tip, expectedAt(walker, "RightFoot", {0, 0, 0.2f}, Quaternion::identity()), 1);
}

TEST_CASE("An attachment without a bone to ride is left where it is")
{
    Character walker;
    const ecs::Entity unnamed = attached(walker, walker.entity, {0, {}, {0, 0, 0, 1}});
    const ecs::Entity missing = attached(walker, walker.entity, {eventName("Tail"), {}, {0, 0, 0, 1}});
    const ecs::ComponentId components[] = {ecs::componentIdOf<scenes::LocalTransform>,
                                           ecs::componentIdOf<scenes::WorldTransform>};
    const ecs::Entity plain = ecs::createEntity(*walker.world, components);
    *ecs::get<scenes::LocalTransform>(*walker.world, plain) = {{0, 0, 0}, Quaternion::identity(), {1, 1, 1}};
    const ecs::Entity orphan = attached(walker, plain, {eventName("RightFoot"), {}, {0, 0, 0, 1}});
    for (const ecs::Entity entity : {unnamed, missing, orphan})
    {
        ecs::get<scenes::LocalTransform>(*walker.world, entity)->position = {5, 6, 7};
    }
    walker.step(0.2f);
    placeBoneAttachments(*walker.world);
    for (const ecs::Entity entity : {unnamed, missing, orphan})
    {
        const Vector3 at = ecs::read<scenes::LocalTransform>(*walker.world, entity)->position;
        CHECK(at.x == 5);
        CHECK(at.y == 6);
        CHECK(at.z == 7);
    }
}
