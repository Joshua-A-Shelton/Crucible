// Skins (ADR 0078): the Walker sample's glTF skeleton and weights, packed
// for the vertex shader.

#include "MeshTestSupport.h"

using namespace crucible;
using namespace crucible::importers;
using namespace crucible::importers::testing;

TEST_CASE("The Walker's skeleton lists each joint after its parent")
{
    const Imported walker(importWalker());
    REQUIRE(walker.has("SKIN"));
    const std::vector<assets::JointEntry> joints = walker.table<assets::JointEntry>("JNTS");
    REQUIRE(joints.size() == 13);
    CHECK(walker.table<assets::SkinInfo>("SKIN")[0].jointCount == 13);
    CHECK(walker.name(joints[0].nameOffset, joints[0].nameLength) == "Hips");
    CHECK(joints[0].parent == -1);
    const std::vector<MeshNode> nodes = walker.table<MeshNode>("NODE");
    for (size_t j = 0; j < joints.size(); ++j)
    {
        CAPTURE(j);
        CHECK(joints[j].parent < static_cast<int32_t>(j));
        // A joint's parent joint is its node's parent node.
        if (joints[j].parent >= 0)
        {
            CHECK(nodes[joints[j].node].parent == joints[joints[j].parent].node);
        }
    }
    // The mesh's node isn't a joint.
    const MeshEntry mesh = walker.table<MeshEntry>("MSHS")[0];
    CHECK((mesh.attributes & static_cast<uint32_t>(MeshAttribute::Skinned)) != 0);
}

TEST_CASE("Skinned vertices keep their strongest influences summing to 255")
{
    for (const uint32_t influences : {4u, 8u})
    {
        CAPTURE(influences);
        const Imported walker(importWalker(withClips(settings(), 30.0f, influences)));
        CHECK(walker.table<assets::SkinInfo>("SKIN")[0].influences == influences);
        const std::vector<uint16_t> indices = walker.table<uint16_t>("JIDX");
        const std::vector<uint8_t> weights = walker.table<uint8_t>("JWGT");
        const size_t vertexCount = walker.sections.at(binary::tag("POSN")).size() / 12;
        REQUIRE(indices.size() == vertexCount * influences);
        REQUIRE(weights.size() == vertexCount * influences);
        uint32_t shared = 0;
        for (size_t v = 0; v < vertexCount; ++v)
        {
            uint32_t sum = 0, used = 0;
            for (uint32_t k = 0; k < influences; ++k)
            {
                sum += weights[v * influences + k];
                used += weights[v * influences + k] > 0 ? 1 : 0;
                CHECK(indices[v * influences + k] < 13);
            }
            CHECK(sum == 255);
            shared += used == 2 ? 1 : 0;
        }
        // The torso's lower corners are half Spine, half Hips.
        CHECK(shared > 0);
    }
}

TEST_CASE("The rest pose skins every vertex to where it was")
{
    // The inverse binds undo the joints' rest transforms, mirrored with them.
    const Imported walker(importWalker());
    const std::vector<Vec3> posed = skinned(walker, nullptr, 0);
    for (uint32_t v = 0; v < posed.size(); ++v)
    {
        const Vec3 d = minus(posed[v], walker.position(v));
        CHECK(dot(d, d) < 1e-8f);
    }
}

TEST_CASE("A model without a skin has no skin sections or clips")
{
    const Imported cube(importUfbx("maya_cube_7500_binary.fbx"));
    CHECK_FALSE(cube.has("JIDX"));
    CHECK_FALSE(cube.has("JWGT"));
    CHECK_FALSE(cube.has("SKIN"));
    CHECK_FALSE(cube.has("JNTS"));
    CHECK(meshClipCount(*cube.mesh) == 0);
}
