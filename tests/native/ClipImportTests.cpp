// Animation clips (ADR 0078): resampled, quantized, and posing a skin as
// the source posed it, against the reference OBJ exports ufbx ships beside
// its FBX samples.

#include "MeshTestSupport.h"

using namespace crucible;
using namespace crucible::importers;
using namespace crucible::importers::testing;

TEST_CASE("Animations become clips resampled at the clip rate")
{
    const Imported walker(importWalker(withClips(settings(), 30.0f)));
    REQUIRE(meshClipCount(*walker.mesh) == 3);
    const Clip walk(*walker.mesh, 0), idle(*walker.mesh, 1);
    CHECK(walk.name == "Walk");
    CHECK(idle.name == "Idle");
    CHECK(Clip(*walker.mesh, 2).name == "WalkForward");
    CHECK(walk.info().duration == doctest::Approx(1.0f));
    CHECK(walk.info().sampleRate == 30.0f);
    CHECK(walk.info().sampleCount == 31);
    CHECK(idle.info().duration == doctest::Approx(2.0f));
    CHECK(idle.info().sampleCount == 61);

    // Idle moves only the spine, and only rotates it.
    const std::vector<assets::ClipTrack> tracks = idle.table<assets::ClipTrack>("TRCK");
    REQUIRE(tracks.size() == 1);
    CHECK(idle.trackName(tracks[0]) == "Spine");
    CHECK(tracks[0].constant == (static_cast<uint32_t>(assets::ClipChannel::Translation) |
                                 static_cast<uint32_t>(assets::ClipChannel::Scale)));
    CHECK(idle.table<uint16_t>("ROTS").size() == 61 * 4);
    CHECK(idle.table<uint16_t>("POSS").size() == 3);

    // Each sample's rotation is a unit quaternion, and the clip loops: its
    // last sample is its first.
    for (const assets::ClipTrack& track : walk.table<assets::ClipTrack>("TRCK"))
    {
        CAPTURE(walk.trackName(track));
        const std::array<float, 4> first = walk.rotation(track, 0), last = walk.rotation(track, 30);
        float same = 0.0f;
        for (int c = 0; c < 4; ++c)
        {
            same += first[c] * last[c];
        }
        CHECK(std::fabs(same) == doctest::Approx(1.0f).epsilon(1e-4));
    }

    // A slower rate, fewer samples.
    const Imported slow(importWalker(withClips(settings(), 10.0f)));
    CHECK(Clip(*slow.mesh, 0).info().sampleCount == 11);
}

TEST_CASE("FBX skins and animation stacks pose like their reference exports")
{
    // ufbx's 3ds Max skin, posed by its clip at frames 5 and 15 (30 per
    // second), against Max's OBJ exports of those frames: Y up and
    // centimeters there, so (x, y, z) is (-x, y, z) / 100 in Crucible's space.
    const Imported imported(importUfbx("max7_skin_5000_binary.fbx", withClips(settings(), 30.0f)));
    REQUIRE(imported.has("JNTS"));
    REQUIRE(meshClipCount(*imported.mesh) >= 1);
    const Clip clip(*imported.mesh, 0);
    std::vector<Vec3> expected;
    for (const uint32_t frame : {5u, 15u})
    {
        CAPTURE(frame);
        expected.clear();
        for (const Vec3& p : skinObjVertices("max7_skin_" + std::to_string(frame) + ".obj"))
        {
            expected.push_back({-p[0] * 0.01f, p[1] * 0.01f, p[2] * 0.01f});
        }
        Vec3 lo{1e30f, 1e30f, 1e30f}, hi{-1e30f, -1e30f, -1e30f};
        for (const Vec3& p : expected)
        {
            for (int c = 0; c < 3; ++c)
            {
                lo[c] = std::min(lo[c], p[c]);
                hi[c] = std::max(hi[c], p[c]);
            }
        }
        const float size = std::sqrt(dot(minus(hi, lo), minus(hi, lo)));
        // Within half a percent of the model's size: 16-bit rotations and
        // 8-bit weights.
        CHECK(farthest(skinned(imported, &clip, frame), expected) < size * 0.005f);
    }
    // And frame 15 isn't the rest pose.
    CHECK(farthest(skinned(imported, nullptr, 0), expected) > 0.02f);
}
