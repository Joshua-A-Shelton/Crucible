// Collision cooked at import (ADR 0086): the Walker imported with its
// Collision setting gets a COLM or COLH section, one entry per mesh, Box3D's
// format; served as an asset, its colliders collide.

#include "MeshTestSupport.h"
#include "PhysicsTestSupport.h"

#include <crucible/AssetSource.h>

#include <map>

using namespace crucible;
using namespace crucible::importers;
using namespace crucible::importers::testing;
using namespace crucible::physics;
using namespace crucible::physics::testing;

namespace {

MeshSettings withCollision(CollisionImport collision)
{
    MeshSettings options = settings();
    options.collision = collision;
    options.maxHullVertices = 32;
    return options;
}

// The import as a MESH file, served as asset `n`.
assets::AssetId serve(uint8_t n, MeshImport& mesh)
{
    static std::map<uint8_t, std::vector<uint8_t>> files;
    binary::Writer* writer = binary::createWriter(assets::meshFileKind, assets::meshFileVersion);
    for (uint32_t i = 0; i < meshSectionCount(mesh); ++i)
    {
        const MeshSectionInfo info = meshSection(mesh, i);
        std::vector<uint8_t> bytes(info.size);
        copyMeshSection(mesh, i, bytes);
        REQUIRE(binary::addSection(*writer, info.tag, info.version, bytes, 16));
    }
    Vector<std::byte> out(MemoryTag::General);
    REQUIRE(binary::writeToMemory(*writer, binary::Compression::None, out));
    binary::destroyWriter(writer);
    const auto* data = reinterpret_cast<const uint8_t*>(out.data());
    files[n] = {data, data + out.size()};
    assets::installAssetSource({[](const assets::AssetId* id, void* context, assets::AssetDelivery deliver) {
        const auto found = files.find(id->bytes[0]);
        if (found == files.end() || id->bytes[1] != 0xc0)
        {
            return -1;
        }
        deliver(context, found->second.data(), found->second.size());
        return 0;
    }});
    assets::AssetId id{};
    id.bytes[0] = n;
    id.bytes[1] = 0xc0;
    return id;
}

} // namespace

TEST_CASE("The Collision setting cooks each mesh in Box3D's format")
{
    {
        const Imported none(importWalker(settings()));
        CHECK(none.sections.count(binary::tag("COLM")) == 0);
        CHECK(none.sections.count(binary::tag("COLH")) == 0);
    }
    for (const CollisionImport kind : {CollisionImport::Mesh, CollisionImport::Convex})
    {
        CAPTURE(static_cast<int>(kind));
        const Imported walker(importWalker(withCollision(kind)));
        const std::vector<uint8_t> section = walker.table<uint8_t>(kind == CollisionImport::Mesh ? "COLM" : "COLH");
        REQUIRE(section.size() >= sizeof(assets::CollisionEntry));
        assets::CollisionEntry entry;
        std::memcpy(&entry, section.data(), sizeof(entry)); // the Walker has one mesh
        CHECK(entry.size > 0);
        CHECK(entry.offset % 16 == 0);
        CHECK(entry.offset + entry.size <= section.size());
    }
}

TEST_CASE("Cooked colliders collide when their model is served")
{
    Result<MeshImport*> asMesh = importWalker(withCollision(CollisionImport::Mesh));
    Result<MeshImport*> asHull = importWalker(withCollision(CollisionImport::Convex));
    REQUIRE(asMesh);
    REQUIRE(asHull);
    const assets::AssetId meshModel = serve(1, *asMesh.value());
    const assets::AssetId hullModel = serve(2, *asHull.value());
    destroyMeshImport(asMesh.value());
    destroyMeshImport(asHull.value());

    Scene scene;
    ground(scene);
    // The Walker's mesh as level geometry: a ball lands on its head (the
    // head box's top at 1.89 m).
    scene.add({ShapeKind::Mesh, {}, {}, 0.6f, 0, false, meshModel, 0}, {5, 0, 0});
    const RigidBody body = dynamic();
    const ecs::Entity ball = scene.add(sphere(0.05f), {5, 3, 0}, &body);
    // Its hull, falling: it lands and comes to rest (a 32-vertex hull of the
    // whole figure simplifies its small feet, so not to the centimetre).
    const ecs::Entity figure = scene.add({ShapeKind::Convex, {}, {}, 0.6f, 0, false, hullModel, 0}, {0, 1, 0}, &body);
    scene.seconds(3.0);
    CHECK(bodyPose(*scene.world, ball).position.y == doctest::Approx(1.89f + 0.05f).epsilon(0.02));
    const float standing = bodyPose(*scene.world, figure).position.y;
    CHECK(standing > -0.05f);
    CHECK(standing < 0.2f);
    CHECK(length(ecs::read<RigidBody>(*scene.world, figure)->linearVelocity) < 0.05f);
    (void)reloadCollisionAsset(meshModel);
    (void)reloadCollisionAsset(hullModel);
}
