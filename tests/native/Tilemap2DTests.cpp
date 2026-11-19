// Tilemap colliders (ADR 0108): a Collider2D of shape Tilemap collides with
// the outlines baked into its entity's map (a TMAP file), read through the
// asset source like a chain's points and read again on a reload.

#include "Physics2DTestSupport.h"

#include <crucible/BinaryFile.h>
#include <crucible/Memory.h>
#include <crucible/Sprite.h>
#include <crucible/Tilemap.h>
#include <crucible/TilemapFile.h>

#include <map>

using namespace crucible;
using namespace crucible::physics2d;
using namespace crucible::physics2d::testing;

namespace {

// Maps served as assets, by the first byte of their ID.
struct MapAssets
{
    inline static std::map<uint8_t, std::vector<uint8_t>> files;

    // The TMAP file the C# importer would write: one cell (collision only
    // matters here) and the outlines given.
    static std::vector<uint8_t> file(const std::vector<std::vector<Vector2>>& outlines)
    {
        binary::Writer* writer = binary::createWriter(assets::tilemapFileKind, assets::tilemapFileVersion);
        std::vector<assets::TilemapLoop> loops;
        std::vector<Vector2> points;
        for (const std::vector<Vector2>& outline : outlines)
        {
            loops.push_back({static_cast<uint32_t>(points.size()), static_cast<uint32_t>(outline.size())});
            points.insert(points.end(), outline.begin(), outline.end());
        }
        const assets::TilemapInfo info{
            {}, 16, 16, 1, 1, 1, static_cast<uint32_t>(loops.size()), static_cast<uint32_t>(points.size()), 0};
        const assets::TilemapCell cell{0, 0, 0, 0, 0, 0};
        auto add = [&](const char (&tag)[5], const void* data, size_t size) {
            REQUIRE(binary::addSection(*writer, binary::tag(tag), 1, std::span(static_cast<const uint8_t*>(data), size),
                                       16));
        };
        add("INFO", &info, sizeof(info));
        add("CELL", &cell, sizeof(cell));
        if (!loops.empty())
        {
            add("LOOP", loops.data(), loops.size() * sizeof(assets::TilemapLoop));
            add("PNTS", points.data(), points.size() * sizeof(Vector2));
        }
        Vector<std::byte> out(MemoryTag::General);
        REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, out));
        binary::destroyWriter(writer);
        const auto* begin = reinterpret_cast<const uint8_t*>(out.data());
        return {begin, begin + out.size()};
    }

    static assets::AssetId add(uint8_t n, const std::vector<std::vector<Vector2>>& outlines)
    {
        files[n] = file(outlines);
        assets::installAssetSource({[](const assets::AssetId* id, void* context, assets::AssetDelivery deliver) {
            const auto found = files.find(id->bytes[0]);
            if (found == files.end() || id->bytes[1] != 0x7e)
            {
                return -1;
            }
            deliver(context, found->second.data(), found->second.size());
            return 0;
        }});
        assets::AssetId id{};
        id.bytes[0] = n;
        id.bytes[1] = 0x7e; // apart from other tests' IDs
        return id;
    }
};

// A static tilemap entity at the origin: its map, and a Tilemap collider.
ecs::Entity addTilemap(Scene2D& scene, assets::AssetId map)
{
    render2d::registerSprite();
    Collider2D collider{};
    collider.shape = ShapeKind2D::Tilemap;
    collider.friction = 0.6f;
    const ecs::Entity entity = scene.add(collider, {0, 0});
    ecs::addComponent(*scene.world, entity, ecs::componentIdOf<render2d::Tilemap>);
    ecs::get<render2d::Tilemap>(*scene.world, entity)->map = map;
    return entity;
}

// Counterclockwise around solid ground: a floor from x -10 to 10, its top at
// y 0, and a block on it from x 2 to 3 (one outline, as merged on import).
const std::vector<Vector2> floorWithBlock{{-10, -1}, {10, -1}, {10, 0}, {3, 0}, {3, 1}, {2, 1}, {2, 0}, {-10, 0}};

} // namespace

TEST_CASE("A ball rests on a tilemap's outline")
{
    Scene2D scene;
    addTilemap(scene, MapAssets::add(1, {floorWithBlock}));
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {-1, 2}, &body);
    const ecs::Entity onBlock = scene.add(circle2D(0.25f), {2.5f, 3}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y == doctest::Approx(0.25f).epsilon(0.02));
    CHECK(scene.transform(onBlock).position.y == doctest::Approx(1.25f).epsilon(0.02));
}

TEST_CASE("A box slides along a tilemap's floor without catching and stops at a block")
{
    Scene2D scene;
    addTilemap(scene, MapAssets::add(2, {floorWithBlock}));
    Collider2D slippery = box2D({0.5f, 0.5f});
    slippery.friction = 0;
    RigidBody2D body = dynamic2D();
    body.linearVelocity = {4, 0};
    const ecs::Entity box = scene.add(slippery, {-6, 0.25f}, &body);
    scene.seconds(3);
    CHECK(scene.transform(box).position.x == doctest::Approx(1.75f).epsilon(0.02));
    CHECK(scene.transform(box).position.y == doctest::Approx(0.25f).epsilon(0.02));
    CHECK(scene.transform(box).angle == doctest::Approx(0).epsilon(0.01));
}

TEST_CASE("Each of a tilemap's outlines is a loop even with three points and a hole holds a ball")
{
    Scene2D scene;
    // A triangle on its own, and a block with a hole (clockwise inside).
    const std::vector<Vector2> triangle{{-6, 0}, {-4, 0}, {-4, 2}};
    const std::vector<Vector2> outer{{-2, -2}, {2, -2}, {2, 2}, {-2, 2}};
    const std::vector<Vector2> hole{{-1, -1}, {-1, 1}, {1, 1}, {1, -1}};
    addTilemap(scene, MapAssets::add(3, {triangle, outer, hole}));
    const RigidBody2D body = dynamic2D();
    const ecs::Entity inside = scene.add(circle2D(0.25f), {0, 0.5f}, &body);
    const ecs::Entity onSlope = scene.add(circle2D(0.25f), {-4.2f, 4}, &body);
    scene.seconds(2);
    CHECK(scene.transform(inside).position.y == doctest::Approx(-0.75f).epsilon(0.02));
    CHECK(scene.transform(onSlope).position.x < -4.2f); // rolled down the slope
}

TEST_CASE("A tilemap read again moves its collision")
{
    Scene2D scene;
    const assets::AssetId map = MapAssets::add(4, {floorWithBlock});
    addTilemap(scene, map);
    scene.seconds(0.1f);
    // The floor drops by 2 m.
    std::vector<Vector2> lower = floorWithBlock;
    for (Vector2& point : lower)
    {
        point.y -= 2;
    }
    MapAssets::add(4, {lower});
    CHECK(reloadChainAsset(map));
    const RigidBody2D body = dynamic2D();
    const ecs::Entity ball = scene.add(circle2D(0.25f), {-1, 1}, &body);
    scene.seconds(2);
    CHECK(scene.transform(ball).position.y == doctest::Approx(-1.75f).epsilon(0.02));
}
