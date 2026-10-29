// The render schedule (ADR 0048): editing steps by name, and the blackboard.
// The "gpu" suite renders through it on every backend this build has.

#include <crucible/Platform.h>
#include <crucible/Render.h>
#include <crucible/RenderSchedule.h>

#include <doctest/doctest.h>
#include <slag/Instance.h>

// doctest prints a string_view with operator<<, which MSVC declares only in
// <ostream>.
#include <ostream>
#include <string>
#include <vector>

using namespace crucible;

namespace {

void addNothing(render::RenderStepContext&, void*) {}

render::RenderStep step(const char* name)
{
    return {.name = name, .addPasses = addNothing};
}

std::vector<std::string> names(const render::RenderSchedule& schedule)
{
    std::vector<std::string> result;
    for (uint32_t i = 0; i < schedule.stepCount(); ++i)
    {
        result.emplace_back(schedule.stepName(i));
    }
    return result;
}

} // namespace

TEST_CASE("Render steps are edited by name")
{
    render::RenderSchedule schedule;
    REQUIRE(schedule.add(step("Opaque")));
    REQUIRE(schedule.add(step("Post")));
    REQUIRE(schedule.insertBefore("Opaque", step("Depth")));
    REQUIRE(schedule.insertAfter("Opaque", step("Sky")));
    CHECK(names(schedule) == std::vector<std::string>{"Depth", "Opaque", "Sky", "Post"});

    REQUIRE(schedule.replace("Sky", step("Clouds")));
    REQUIRE(schedule.replace("Post", step("Post"))); // the same name again
    REQUIRE(schedule.remove("Depth"));
    CHECK(names(schedule) == std::vector<std::string>{"Opaque", "Clouds", "Post"});

    CHECK(schedule.add(step("Opaque")).status() == Status::InvalidData); // names are unique
    CHECK(schedule.replace("Clouds", step("Post")).status() == Status::InvalidData);
    CHECK(schedule.insertBefore("Missing", step("A")).status() == Status::NotFound);
    CHECK(schedule.insertAfter("Missing", step("A")).status() == Status::NotFound);
    CHECK(schedule.replace("Missing", step("A")).status() == Status::NotFound);
    CHECK(schedule.remove("Missing").status() == Status::NotFound);
    CHECK(schedule.add({.name = "NoFunction"}).status() == Status::InvalidData);
    CHECK(names(schedule) == std::vector<std::string>{"Opaque", "Clouds", "Post"});
}

TEST_CASE("The renderer starts with Clear then Triangle")
{
    CHECK(names(render::renderSchedule()) == std::vector<std::string>{"Clear", "Triangle"});
}

TEST_CASE("Blackboard keys are registered once by name")
{
    const render::BlackboardKey first = render::registerBlackboardKey("Test.Shadows");
    CHECK(render::registerBlackboardKey("Test.Shadows").value == first.value);
    CHECK(render::registerBlackboardKey("Test.Bloom").value != first.value);
    CHECK(render::blackboardKeyName(first) == "Test.Shadows");
    CHECK(render::blackboardKeyName(render::backbufferKey()) == "Backbuffer");
}

namespace {

// A step publishing the backbuffer again under another key, and a step that
// reads it: what plugins sharing a resource do.
render::BlackboardKey sharedKey;
int reads = 0;

void publishShared(render::RenderStepContext& context, void*)
{
    context.publish(sharedKey, context.texture(render::backbufferKey()));
}

void readShared(render::RenderStepContext& context, void*)
{
    const slag::rendergraph::TextureHandle texture = context.texture(sharedKey);
    reads += texture.index != UINT32_MAX ? 1 : 0;
}

void readMissing(render::RenderStepContext& context, void*)
{
    (void)context.texture(render::registerBlackboardKey("Test.NeverPublished"));
}

int failedPrepares = 0;

Result<void> failPrepare(slag::GraphicsDevice&, void*)
{
    ++failedPrepares;
    return Status::Unsupported;
}

} // namespace

TEST_SUITE("gpu")
{
    TEST_CASE("Steps share resources through the blackboard on every backend")
    {
        REQUIRE(platform::initialize());
        sharedKey = render::registerBlackboardKey("Test.Shared");
        for (slag::Backend backend : {slag::Backend::D3D12, slag::Backend::Vulkan})
        {
            if (!slag::isBackendAvailable(backend) || !render::initialize(backend))
            {
                continue;
            }
            crucible::Result<render::SurfaceHandle> surface = render::createOffscreenSurface(32, 32);
            REQUIRE(surface);
            render::RenderSchedule& schedule = render::renderSchedule();

            reads = 0;
            REQUIRE(schedule.insertBefore("Triangle", {.name = "Test.Publish", .addPasses = publishShared}));
            REQUIRE(schedule.insertAfter("Triangle", {.name = "Test.Read", .addPasses = readShared}));
            REQUIRE(render::renderFrame(surface.value(), 0, 0, 0, 1));
            CHECK(reads == 1);
            REQUIRE(schedule.remove("Test.Read"));

            // A step added to a running renderer is prepared at once; a
            // failed prepare leaves it out.
            CHECK(schedule.add({.name = "Test.Broken", .addPasses = addNothing, .prepare = failPrepare}).status() ==
                  Status::Unsupported);
            CHECK(failedPrepares > 0);

            // Reading what no step published is reported and fails the frame.
            REQUIRE(schedule.insertBefore("Test.Publish", {.name = "Test.Missing", .addPasses = readMissing}));
            CHECK(render::renderFrame(surface.value(), 0, 0, 0, 1).status() == Status::InvalidData);
            REQUIRE(schedule.remove("Test.Missing"));
            REQUIRE(schedule.remove("Test.Publish"));
            CHECK(render::renderFrame(surface.value(), 0, 0, 0, 1));

            render::destroySurface(surface.value());
            render::shutdown();
        }
        platform::shutdown();
    }
}
