// Chain colliders (ADR 0095): a CHAN file's points as Box2D's chain,
// solid on the right of the points' direction, read through the asset
// source and read again on a reload.

#include "Physics2DTestSupport.h"

#include <crucible/BinaryFile.h>
#include <crucible/ChainFile.h>
#include <crucible/Memory.h>

#include <algorithm>
#include <map>

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

namespace {

// Chains served as assets, by the first byte of their ID.
struct ChainAssets
{
    inline static std::map<uint8_t, std::vector<uint8_t>> files;

    // The CHAN file the C# importer would write.
    static std::vector<uint8_t> file(const std::vector<Vector2>& points, bool loop)
    {
        binary::Writer* writer = binary::createWriter(assets::chainFileKind, assets::chainFileVersion);
        const assets::ChainInfo info{static_cast<uint32_t>(points.size()), loop ? 1u : 0u};
        REQUIRE(binary::addSection(*writer, binary::tag("INFO"), 1,
                                   std::span(reinterpret_cast<const uint8_t*>(&info), sizeof(info)), 16));
        REQUIRE(binary::addSection(
            *writer, binary::tag("PNTS"), 1,
            std::span(reinterpret_cast<const uint8_t*>(points.data()), points.size() * sizeof(Vector2)), 16));
        Vector<std::byte> out(MemoryTag::General);
        REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, out));
        binary::destroyWriter(writer);
        const auto* begin = reinterpret_cast<const uint8_t*>(out.data());
        return {begin, begin + out.size()};
    }

    static assets::AssetId add(uint8_t n, const std::vector<Vector2>& points, bool loop = false)
    {
        files[n] = file(points, loop);
        assets::installAssetSource({[](const assets::AssetId* id, void* context, assets::AssetDelivery deliver) {
            const auto found = files.find(id->bytes[0]);
            if (found == files.end() || id->bytes[1] != 0xc4)
            {
                return -1;
            }
            deliver(context, found->second.data(), found->second.size());
            return 0;
        }});
        assets::AssetId id{};
        id.bytes[0] = n;
        id.bytes[1] = 0xc4; // apart from other tests' IDs
        return id;
    }
};

Collider2D chain2D(assets::AssetId chain)
{
    Collider2D collider{};
    collider.shape = ShapeKind2D::Chain;
    collider.chain = chain;
    collider.friction = 0.6f;
    return collider;
}

// Flat ground at y = 0 from x = 10 to -10 (right to left: solid below),
// in short segments.
std::vector<Vector2> flatGround(float step = 0.5f)
{
    std::vector<Vector2> points;
    for (float x = 10; x >= -10; x -= step)
    {
        points.push_back({x, 0});
    }
    return points;
}

} // namespace

TEST_CASE("A ball rests on a chain run right to left")
{
    Scene2D scene;
    scene.add(chain2D(ChainAssets::add(1, flatGround())), {0, 0});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {0.3f, 2}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y == doctest::Approx(0.25f).epsilon(0.02));
}

TEST_CASE("A chain run left to right lets a ball fall through from above")
{
    Scene2D scene;
    std::vector<Vector2> points = flatGround();
    std::reverse(points.begin(), points.end());
    scene.add(chain2D(ChainAssets::add(2, points)), {0, 0});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {0.3f, 2}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y < -2);
}

TEST_CASE("A box slides along a chain's many joins without catching")
{
    Scene2D scene;
    scene.add(chain2D(ChainAssets::add(3, flatGround(0.25f))), {0, 0});
    Collider2D slippery = box2D({0.5f, 0.5f});
    slippery.friction = 0;
    RigidBody2D body = dynamic2D();
    body.linearVelocity = {4, 0};
    const ecs::Entity box = scene.add(slippery, {-6, 0.25f}, &body);
    scene.seconds(2);
    CHECK(scene.transform(box).position.x == doctest::Approx(2).epsilon(0.05));
    CHECK(scene.transform(box).position.y == doctest::Approx(0.25f).epsilon(0.02));
    CHECK(scene.transform(box).angle == doctest::Approx(0).epsilon(0.01));
}

TEST_CASE("A clockwise loop is a cave that holds a ball inside")
{
    Scene2D scene;
    const std::vector<Vector2> cave{{-2, -1}, {-2, 3}, {2, 3}, {2, -1}};
    scene.add(chain2D(ChainAssets::add(4, cave, true)), {0, 0});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {0, 1}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y == doctest::Approx(-0.75f).epsilon(0.02));
}

TEST_CASE("A mirrored chain keeps its drawn side solid")
{
    Scene2D scene;
    const ecs::Entity ground = scene.add(chain2D(ChainAssets::add(5, flatGround())), {0, 0});
    ecs::get<scenes::Transform2D>(*scene.world, ground)->scale = {-1, 1};
    scenes::updateWorldTransforms(*scene.world);
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {0.3f, 2}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y == doctest::Approx(0.25f).epsilon(0.02));
}

TEST_CASE("Queries and contacts see a chain as its entity")
{
    Scene2D scene;
    const ecs::Entity ground = scene.add(chain2D(ChainAssets::add(6, flatGround())), {0, 0});
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {0.3f, 1}, &body);
    scene.step();
    const RayHit2D hit = physics2d::raycast(*scene.world, {3, 5}, {0, -1}, 10, physics::defaultQueryFilter());
    REQUIRE(hit.hit);
    CHECK(hit.entity.value == ground.value);
    CHECK(hit.point.y == doctest::Approx(0).epsilon(0.01));
    bool began = false;
    for (int i = 0; i < 90 && !began; ++i)
    {
        scene.step();
        for (const ContactEvent2D& event : scene.events())
        {
            began = began || (event.kind == physics::ContactKind::Begin &&
                              ((event.a.value == ball.value && event.b.value == ground.value) ||
                               (event.a.value == ground.value && event.b.value == ball.value)));
        }
    }
    CHECK(began);
}

TEST_CASE("A chain on a dynamic body or as a trigger makes nothing")
{
    Scene2D scene;
    const RigidBody2D body = dynamic2D();
    scene.add(chain2D(ChainAssets::add(7, flatGround())), {0, 0}, &body);
    Collider2D trigger = chain2D(ChainAssets::add(7, flatGround()));
    trigger.isTrigger = true;
    scene.add(trigger, {0, -3});
    scene.step();
    CHECK_FALSE(physics2d::raycast(*scene.world, {3, 5}, {0, -1}, 20, physics::defaultQueryFilter()).hit);
}

TEST_CASE("Reloading a chain rebuilds the colliders made from it")
{
    Scene2D scene;
    const assets::AssetId id = ChainAssets::add(8, flatGround());
    scene.add(chain2D(id), {0, 0});
    scene.step();
    CHECK(physics2d::raycast(*scene.world, {3, 5}, {0, -1}, 10, physics::defaultQueryFilter()).point.y ==
          doctest::Approx(0).epsilon(0.01));
    std::vector<Vector2> raised = flatGround();
    for (Vector2& point : raised)
    {
        point.y = 1;
    }
    ChainAssets::add(8, raised);
    CHECK(reloadChainAsset(id));
    scene.step();
    CHECK(physics2d::raycast(*scene.world, {3, 5}, {0, -1}, 10, physics::defaultQueryFilter()).point.y ==
          doctest::Approx(1).epsilon(0.01));
    assets::AssetId unread{};
    unread.bytes[0] = 99;
    CHECK_FALSE(reloadChainAsset(unread));
}
