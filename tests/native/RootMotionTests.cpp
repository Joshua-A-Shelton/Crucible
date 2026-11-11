// Root motion (ADR 0081): the root's travel moves the entity through
// moveCharacter and is taken out of the pose, the same at any frame rate,
// turned and scaled with the entity, and weighted in a blend space.

#include "AnimationTestSupport.h"

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

constexpr float strideSpeed = 1.7f; // the WalkForward clip's, make_walker.py

Vector3 hips(const Character& walker, int32_t joint)
{
    const Vector4& row = walker.model(joint).rows[3];
    return {row.x, row.y, row.z};
}

const scenes::LocalTransform& placed(const Character& walker)
{
    return *ecs::read<scenes::LocalTransform>(*walker.world, walker.entity);
}

void moveByRoot(Character& walker)
{
    ecs::get<Animator>(*walker.world, walker.entity)->rootMotion = true;
}

} // namespace

TEST_CASE("Root motion moves the entity and keeps the root over it")
{
    const Reference reference;
    const int32_t root = reference.joint("Hips");
    Character walker(4); // WalkForward
    moveByRoot(walker);
    walker.step(0.5f);
    CHECK(placed(walker).position.z == doctest::Approx(0.5f * strideSpeed).epsilon(1e-3));
    CHECK(std::fabs(hips(walker, root).z) < 1e-3f);
    // Only along the ground: the hips still bob.
    const Matrix expected = reference.modelOf(reference.sample(reference.clip(2), 0.5f))[root];
    CHECK(hips(walker, root).y == doctest::Approx(expected[13]).epsilon(1e-4));

    walker.step(0.75f); // round the loop's end
    CHECK(placed(walker).position.z == doctest::Approx(1.25f * strideSpeed).epsilon(1e-3));
    CHECK(std::fabs(hips(walker, root).z) < 1e-3f);
    const RootMotion moved = rootMotion(*walker.world, walker.entity);
    CHECK(moved.translation.z == doctest::Approx(0.75f * strideSpeed).epsilon(1e-3));
    CHECK(moved.rotation.w == doctest::Approx(1.0f));
}

TEST_CASE("Without root motion the root travels and the entity stays")
{
    const Reference reference;
    Character walker(4);
    walker.step(0.5f);
    CHECK(placed(walker).position.z == 0.0f);
    CHECK(hips(walker, reference.joint("Hips")).z == doctest::Approx(0.5f * strideSpeed).epsilon(1e-3));
    CHECK(rootMotion(*walker.world, walker.entity).translation.z == 0.0f);
}

TEST_CASE("Root motion goes as far at any frame rate")
{
    // Metres a second, over the 2.4 s or so that 72 and 345 frames make.
    auto speed = [](int frames, float frame) {
        Character walker(4);
        moveByRoot(walker);
        for (int i = 0; i < frames; ++i)
        {
            walker.step(frame);
        }
        return placed(walker).position.z / (static_cast<float>(frames) * frame);
    };
    CHECK(speed(72, 1.0f / 30) == doctest::Approx(strideSpeed).epsilon(1e-3));
    CHECK(speed(345, 1.0f / 144) == doctest::Approx(speed(72, 1.0f / 30)).epsilon(1e-4));
}

TEST_CASE("Root motion goes the way the entity faces and at its scale")
{
    Character walker(4);
    moveByRoot(walker);
    scenes::LocalTransform* local = ecs::get<scenes::LocalTransform>(*walker.world, walker.entity);
    local->rotation = Quaternion::fromAxisAngle({0, 1, 0}, 3.14159265f / 2); // +z turned to +x
    local->scale = {2, 2, 2};
    walker.step(1.0f);
    CHECK(placed(walker).position.x == doctest::Approx(2 * strideSpeed).epsilon(1e-3));
    CHECK(std::fabs(placed(walker).position.z) < 1e-3f);
}

TEST_CASE("A blend space's root motion is its clips' weighted")
{
    MachineBuilder machine;
    const uint32_t speed = machine.parameter("Speed", assets::ParameterType::Float, 0.85f);
    machine.blend("Move", static_cast<int32_t>(speed), {{2, 0.0f}, {4, 1.7f}}); // in place, then travelling
    machine.install(12);
    Character walker;
    moveByRoot(walker);
    ecs::get<Animator>(*walker.world, walker.entity)->stateMachine = idOf(12);
    walker.step(1.0f);
    CHECK(placed(walker).position.z == doctest::Approx(0.5f * strideSpeed).epsilon(1e-3));
}
