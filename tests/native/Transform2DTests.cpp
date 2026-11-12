// Transform2D (ADR 0093): the world matrix of a position, angle, scale, and
// depth; parenting either way with LocalTransform; LocalTransform winning
// when an entity has both.

#include <crucible/Transform.h>

#include <doctest/doctest.h>

using namespace crucible;
using namespace crucible::scenes;

namespace {

ecs::Entity flat(ecs::World& world, Transform2D transform)
{
    const ecs::ComponentId set[] = {ecs::componentIdOf<Transform2D>, ecs::componentIdOf<WorldTransform>};
    const ecs::Entity entity = ecs::createEntity(world, set);
    *ecs::get<Transform2D>(world, entity) = transform;
    return entity;
}

} // namespace

TEST_CASE("A Transform2D makes the world matrix of its position angle scale and depth")
{
    registerTransforms();
    ecs::World* world = ecs::createWorld();
    const ecs::Entity entity = flat(*world, {{1, 2}, 1.5707963f, {2, 3}, 5});
    // +X, scaled by 2 and turned a quarter (+X toward +Y), then moved.
    const Vector3 point = transformPoint(Vector3{1, 0, 0}, computeWorldTransform(*world, entity));
    CHECK(point.x == doctest::Approx(1));
    CHECK(point.y == doctest::Approx(2 + 2));
    CHECK(point.z == doctest::Approx(5));
    updateWorldTransforms(*world);
    const Vector3 propagated = transformPoint(Vector3{0, 1, 0}, ecs::read<WorldTransform>(*world, entity)->matrix);
    CHECK(propagated.x == doctest::Approx(1 - 3)); // +Y scaled by 3, turned to -X
    CHECK(propagated.y == doctest::Approx(2));
    ecs::destroyWorld(world);
}

TEST_CASE("2D entities parent under 2D and 3D ones")
{
    registerTransforms();
    ecs::World* world = ecs::createWorld();
    const ecs::Entity parent = flat(*world, {{10, 0}, 0, {1, 1}, 0});
    const ecs::Entity child = flat(*world, {{1, 0}, 0, {1, 1}, 0});
    REQUIRE(ecs::setParent(*world, child, parent));
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<WorldTransform>};
    const ecs::Entity holder = ecs::createEntity(*world, set);
    *ecs::get<LocalTransform>(*world, holder) = {{0, 0, 7}, Quaternion::identity(), {1, 1, 1}};
    const ecs::Entity sprite = flat(*world, {{2, 3}, 0, {1, 1}, 0});
    REQUIRE(ecs::setParent(*world, sprite, holder));
    updateWorldTransforms(*world);
    const Matrix4x4& placed = ecs::read<WorldTransform>(*world, child)->matrix;
    CHECK(placed.rows[3].x == doctest::Approx(11));
    const Matrix4x4& held = ecs::read<WorldTransform>(*world, sprite)->matrix;
    CHECK(held.rows[3].x == doctest::Approx(2));
    CHECK(held.rows[3].z == doctest::Approx(7));
    ecs::destroyWorld(world);
}

TEST_CASE("LocalTransform wins when an entity has both")
{
    registerTransforms();
    ecs::World* world = ecs::createWorld();
    const ecs::ComponentId set[] = {ecs::componentIdOf<LocalTransform>, ecs::componentIdOf<Transform2D>,
                                    ecs::componentIdOf<WorldTransform>};
    const ecs::Entity both = ecs::createEntity(*world, set);
    *ecs::get<LocalTransform>(*world, both) = {{3, 0, 0}, Quaternion::identity(), {1, 1, 1}};
    *ecs::get<Transform2D>(*world, both) = {{9, 9}, 0, {1, 1}, 0};
    updateWorldTransforms(*world);
    CHECK(ecs::read<WorldTransform>(*world, both)->matrix.rows[3].x == doctest::Approx(3));
    CHECK(computeWorldTransform(*world, both).rows[3].x == doctest::Approx(3));
    ecs::destroyWorld(world);
}
