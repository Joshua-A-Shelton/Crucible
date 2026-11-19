// Frame animation (ADR 0107): a SpriteAnimator playing a sprite clip on its
// Sprite, frame events through the animation events, and state machines
// choosing clips by parameters and exit times.

#include "AnimationTestSupport.h"

#include <crucible/Sprite.h>
#include <crucible/SpriteClipFile.h>

using namespace crucible;
using namespace crucible::animation;
using namespace crucible::animation::testing;

namespace {

constexpr uint8_t sheetId = 20, walkId = 21, jumpId = 22, machineId = 23;

struct Frame
{
    uint32_t x;
    float duration;
    std::vector<std::string> events;
};

// A clip of 16x16 frames along the top of a sheet, laid out as the texture
// importer lays it out.
void installClip(uint8_t id, const std::vector<Frame>& frames, bool loop)
{
    WalkerAssets::install();
    std::vector<assets::SpriteClipFrame> baked;
    std::vector<assets::ClipEvent> events;
    float start = 0;
    for (const Frame& frame : frames)
    {
        baked.push_back({frame.x, 0, 16, 16, start, frame.duration});
        for (const std::string& name : frame.events)
        {
            events.push_back({start, eventName(name)});
        }
        start += frame.duration;
    }
    const assets::SpriteClipInfo info{idOf(sheetId), start, static_cast<uint32_t>(baked.size()), loop ? 1u : 0u, 0};
    std::vector<MeshSectionInfo> infos = {
        {binary::tag("INFO"), 1, sizeof(info)},
        {binary::tag("FRMS"), 1, baked.size() * sizeof(assets::SpriteClipFrame)},
    };
    std::vector<std::vector<uint8_t>> sections = {bytesOf(std::vector<assets::SpriteClipInfo>{info}), bytesOf(baked)};
    if (!events.empty())
    {
        infos.push_back({binary::tag("EVNT"), 1, events.size() * sizeof(assets::ClipEvent)});
        sections.push_back(bytesOf(events));
    }
    WalkerAssets::files[id] =
        WalkerAssets::file(assets::spriteClipFileKind, assets::spriteClipFileVersion, infos, sections);
    (void)reloadAnimationAsset(idOf(id));
}

// Walk: four frames of 0.1 s, looping, a step on the first and third.
// Jump: two frames, 0.2 s then 0.3 s, holding the last.
void installClips()
{
    installClip(walkId, {{0, 0.1f, {"Step"}}, {16, 0.1f, {}}, {32, 0.1f, {"Step"}}, {48, 0.1f, {}}}, true);
    installClip(jumpId, {{64, 0.2f, {"Leave"}}, {80, 0.3f, {"Land"}}}, false);
}

struct Hero
{
    ecs::World* world = nullptr;
    ecs::Entity entity{};
    uint64_t clock = 1'000'000'000;

    explicit Hero(uint8_t clip, uint8_t machine = 0)
    {
        installClips();
        registerAnimation();
        render2d::registerSprite();
        world = ecs::createWorld();
        const ecs::ComponentId components[] = {ecs::componentIdOf<render2d::Sprite>,
                                               ecs::componentIdOf<render2d::SpriteAnimator>};
        entity = ecs::createEntity(*world, components);
        *ecs::get<render2d::SpriteAnimator>(*world, entity) = {clip == 0 ? assets::AssetId{} : idOf(clip), 1.0f,
                                                               machine == 0 ? assets::AssetId{} : idOf(machine)};
    }

    ~Hero()
    {
        releasePoses(*world);
        ecs::destroyWorld(world);
    }

    Hero(const Hero&) = delete;
    Hero& operator=(const Hero&) = delete;

    void step(float seconds)
    {
        clock += static_cast<uint64_t>(static_cast<double>(seconds) * 1e9);
        advancePoses(*world, seconds, clock);
    }

    render2d::SpriteRegion region() const
    {
        return ecs::read<render2d::Sprite>(*world, entity)->region;
    }

    std::vector<AnimationEvent> passed() const
    {
        std::vector<AnimationEvent> found(events(*world, {}));
        (void)events(*world, found);
        return found;
    }
};

} // namespace

TEST_CASE("A sprite clip sets its sheet and each frame's region in turn and loops")
{
    Hero hero(walkId);
    hero.step(0.05f);
    const render2d::Sprite& sprite = *ecs::read<render2d::Sprite>(*hero.world, hero.entity);
    CHECK(sprite.texture == idOf(sheetId));
    CHECK(hero.region().x == 0);
    CHECK(hero.region().width == 16);
    CHECK(hero.region().height == 16);
    hero.step(0.1f); // 0.15 s
    CHECK(hero.region().x == 16);
    hero.step(0.1f); // 0.25 s
    CHECK(hero.region().x == 32);
    hero.step(0.1f); // 0.35 s
    CHECK(hero.region().x == 48);
    hero.step(0.1f); // 0.45 s: around again
    CHECK(hero.region().x == 0);
}

TEST_CASE("A sprite clip that doesn't loop holds its last frame and speed scales its time")
{
    Hero hero(jumpId);
    hero.step(0.1f);
    CHECK(hero.region().x == 64);
    hero.step(1.0f);
    CHECK(hero.region().x == 80);
    hero.step(1.0f);
    CHECK(hero.region().x == 80);

    Hero fast(walkId);
    ecs::get<render2d::SpriteAnimator>(*fast.world, fast.entity)->speed = 2;
    fast.step(0.075f); // 0.15 s of clip
    CHECK(fast.region().x == 16);
    ecs::get<render2d::SpriteAnimator>(*fast.world, fast.entity)->speed = 0;
    fast.step(1.0f);
    CHECK(fast.region().x == 16);
}

TEST_CASE("Frame events are reported as animation events at their frame's start")
{
    Hero hero(walkId);
    hero.step(0.05f); // the first frame's event: the clip just started
    std::vector<AnimationEvent> found = hero.passed();
    REQUIRE(found.size() == 1);
    CHECK(found[0].entity.value == hero.entity.value);
    CHECK(found[0].kind == AnimationEventKind::Event);
    CHECK(found[0].name == eventName("Step"));
    CHECK(found[0].clipTime == doctest::Approx(0));
    hero.step(0.1f); // 0.05 to 0.15: nothing
    CHECK(hero.passed().empty());
    hero.step(0.1f); // past 0.2: the third frame's, halfway through the update
    found = hero.passed();
    REQUIRE(found.size() == 1);
    CHECK(found[0].clipTime == doctest::Approx(0.2f));
    CHECK(static_cast<double>(hero.clock - found[0].timestamp) == doctest::Approx(0.05e9).epsilon(1e-3));
    hero.step(0.2f); // 0.25 to 0.45: around the loop, the first frame's again
    found = hero.passed();
    REQUIRE(found.size() == 1);
    CHECK(found[0].clipTime == doctest::Approx(0));
}

TEST_CASE("A state machine chooses sprite clips on parameters and exit times")
{
    MachineBuilder machine;
    const uint32_t jump = machine.parameter("Jump", assets::ParameterType::Trigger);
    const uint32_t walk = machine.state("Walk", walkId);
    const uint32_t air = machine.state("Jump", jumpId, 1, false);
    machine.transition(static_cast<int32_t>(walk), air, 0, -1, {{jump, assets::ConditionOp::Set, 0}});
    // Back to walking once the jump has played through.
    machine.transition(static_cast<int32_t>(air), walk, 0, 1.0f);
    machine.install(machineId);

    Hero hero(0, machineId);
    hero.step(0.05f);
    CHECK(currentState(*hero.world, hero.entity) == static_cast<int32_t>(walk));
    CHECK(findState(*hero.world, hero.entity, "Jump") == static_cast<int32_t>(air));
    CHECK(findState(*hero.world, hero.entity, "Run") == -1);
    CHECK(hero.region().x == 0);

    CHECK(trigger(*hero.world, hero.entity, "Jump"));
    CHECK(trigger(*hero.world, hero.entity, "Fly").status() == Status::NotFound);
    CHECK(setFloat(*hero.world, hero.entity, "Jump", 1).status() == Status::NotFound);
    hero.step(0.05f);
    CHECK(currentState(*hero.world, hero.entity) == static_cast<int32_t>(air));
    hero.step(0.1f); // the jump's first frame, its event passed as it started
    CHECK(hero.region().x == 64);
    std::vector<AnimationEvent> found = hero.passed();
    REQUIRE(found.size() == 1);
    CHECK(found[0].name == eventName("Leave"));
    hero.step(0.2f); // 0.3 s in: the second frame
    CHECK(hero.region().x == 80);
    // 0.5 s is its end: the exit time passes 0.2 s into this update, and
    // the walk plays the other 0.15 s from its start, its first step then.
    hero.step(0.35f);
    CHECK(currentState(*hero.world, hero.entity) == static_cast<int32_t>(walk));
    CHECK(hero.region().x == 16);
    found = hero.passed();
    REQUIRE(found.size() == 1);
    CHECK(found[0].name == eventName("Step"));
    CHECK(static_cast<double>(hero.clock - found[0].timestamp) == doctest::Approx(0.15e9).epsilon(1e-3));
}

TEST_CASE("A sprite animator without a state machine has no state and lets go of its entity")
{
    Hero hero(walkId);
    hero.step(0.05f);
    CHECK(currentState(*hero.world, hero.entity) == -1);
    CHECK(setBool(*hero.world, hero.entity, "Grounded", true).status() == Status::NotFound);
    ecs::destroyEntity(*hero.world, hero.entity);
    hero.step(0.05f);
    CHECK(hero.passed().empty());
}

TEST_CASE("A sprite clip read again is played as changed")
{
    Hero hero(walkId);
    hero.step(0.05f);
    CHECK(hero.region().x == 0);
    installClip(walkId, {{96, 0.1f, {}}}, true);
    hero.step(0.05f);
    CHECK(hero.region().x == 96);
}
