// Frame animation (ADR 0107): sprite clips and state machines read through
// the asset source and cached by ID, and a player per SpriteAnimator that
// moves through them and sets its Sprite's texture and region. Advanced by
// the pose runtime's update (Animation.cpp), so frame events join 3D ones.

#include "SpriteAnimation.h"

#include "StateMachine.h"

#include <crucible/AssetLoads.h>
#include <crucible/BinaryFile.h>
#include <crucible/ClipFile.h>
#include <crucible/HashMap.h>
#include <crucible/Sprite.h>
#include <crucible/SpriteClipFile.h>

#include <algorithm>
#include <cmath>
#include <cstring>

namespace crucible::render2d {

using animation::StateMachine;
using assets::AssetId;

namespace {

constexpr MemoryTag tag = MemoryTag::Animation;

struct AssetIdHash
{
    uint64_t operator()(const AssetId& id) const
    {
        uint64_t low, high;
        std::memcpy(&low, id.bytes, 8);
        std::memcpy(&high, id.bytes + 8, 8);
        return Hash<uint64_t>()(low ^ (high * 0x9e3779b97f4a7c15ull));
    }
};

bool isNone(const AssetId& id)
{
    return std::all_of(std::begin(id.bytes), std::end(id.bytes), [](uint8_t byte) { return byte == 0; });
}

// --- Assets ----------------------------------------------------------------

struct SpriteClip
{
    assets::SpriteClipInfo info{};
    Vector<assets::SpriteClipFrame> frames{tag};
    Vector<assets::ClipEvent> events{tag};
};

SpriteClip* parseSpriteClip(std::span<const uint8_t> bytes)
{
    Result<binary::File*> opened = binary::parseFile(bytes, assets::spriteClipFileKind, assets::spriteClipFileVersion);
    if (!opened)
    {
        return nullptr;
    }
    binary::File* file = opened.value();
    SpriteClip* clip = create<SpriteClip>(tag);
    Vector<assets::SpriteClipInfo> info(tag);
    const bool read = animation::readTable(*file, "INFO", info) && animation::readTable(*file, "FRMS", clip->frames);
    (void)animation::readTable(*file, "EVNT", clip->events); // optional
    binary::closeFile(file);
    if (!read || info.empty() || clip->frames.empty() || info[0].frameCount != clip->frames.size())
    {
        destroy(tag, clip);
        return nullptr;
    }
    clip->info = info[0];
    return clip;
}

void destroySpriteClip(SpriteClip* clip)
{
    destroy(tag, clip);
}

// What's been read, by ID; null for an asset that couldn't be, so it isn't
// read again every frame (reloadSpriteAnimationAsset forgets it).
HashMap<AssetId, SpriteClip*, AssetIdHash> clips{tag};
HashMap<AssetId, StateMachine*, AssetIdHash> machines{tag};
// Loads in flight (ADR 0099); an animator using one holds still meanwhile.
HashMap<AssetId, bool, AssetIdHash> loading{tag};

bool isLoading(const AssetId& id)
{
    return !isNone(id) && loading.find(id) != nullptr;
}

template <class T>
struct SpriteLoad
{
    AssetId id;
    HashMap<AssetId, T*, AssetIdHash>* into = nullptr;
    T* (*parse)(std::span<const uint8_t>) = nullptr;
    T* made = nullptr;
};

// The asset, or null: it couldn't be read, or it's loading (a load started
// now if need be; without job workers it's read before this returns).
template <class T>
T* cachedOrLoaded(HashMap<AssetId, T*, AssetIdHash>& into, const AssetId& id, T* (*parse)(std::span<const uint8_t>))
{
    if (isNone(id))
    {
        return nullptr;
    }
    if (T** found = into.find(id))
    {
        return *found;
    }
    if (loading.find(id) != nullptr)
    {
        return nullptr;
    }
    loading.insertOrAssign(id, true);
    auto* load = create<SpriteLoad<T>>(tag);
    load->id = id;
    load->into = &into;
    load->parse = parse;
    assets::startLoad({
        .id = id,
        .context = load,
        .prepare =
            [](void* context, std::span<const uint8_t> bytes, bool found) {
                auto& self = *static_cast<SpriteLoad<T>*>(context);
                self.made = found ? self.parse(bytes) : nullptr;
            },
        .publish =
            [](void* context) {
                auto* self = static_cast<SpriteLoad<T>*>(context);
                loading.erase(self->id);
                self->into->insertOrAssign(self->id, self->made);
                destroy(tag, self);
            },
    });
    T** found = into.find(id);
    return found != nullptr ? *found : nullptr;
}

const SpriteClip* clipOf(const AssetId& id)
{
    return cachedOrLoaded(clips, id, parseSpriteClip);
}

const StateMachine* machineOf(const AssetId& id)
{
    const StateMachine* machine = cachedOrLoaded(machines, id, animation::parseStateMachine);
    if (machine != nullptr)
    {
        // Its clips start loading as soon as it's read.
        for (const assets::StateMotion& motion : machine->motions)
        {
            (void)clipOf(motion.clip);
        }
    }
    return machine;
}

// --- Players ---------------------------------------------------------------

// One SpriteAnimator's place: a clip's time, or a state machine's state and
// how far through it (a share, so a blend space's clips keep in step).
struct Player
{
    const ecs::World* world = nullptr;
    ecs::Entity entity{};
    AssetId clipId{};
    AssetId machineId{};
    const StateMachine* machine = nullptr;
    Vector<float> parameters{tag};
    int32_t state = -1;
    float time = 0;  // seconds into the clip, without a machine
    float phase = 0; // 0 to 1 through the state, with one
    bool fresh = true;
};

struct PlayerKey
{
    const ecs::World* world;
    uint64_t entity;

    bool operator==(const PlayerKey&) const = default;
};

struct PlayerKeyHash
{
    uint64_t operator()(const PlayerKey& key) const
    {
        return Hash<uint64_t>()(static_cast<uint64_t>(reinterpret_cast<uintptr_t>(key.world)) * 0x9e3779b97f4a7c15ull ^
                                key.entity);
    }
};

HashMap<PlayerKey, Player*, PlayerKeyHash> players{tag};

Player* findPlayer(const ecs::World& world, ecs::Entity entity)
{
    Player** found = players.find({&world, entity.value});
    return found != nullptr ? *found : nullptr;
}

void freePlayer(Player* player)
{
    players.erase({player->world, player->entity.value});
    destroy(tag, player);
}

// The animator's player, made if need be, started over if it names another
// clip or machine.
Player* sync(ecs::World& world, ecs::Entity entity, const SpriteAnimator& animator)
{
    Player* player = findPlayer(world, entity);
    if (player == nullptr)
    {
        player = create<Player>(tag);
        player->world = &world;
        player->entity = entity;
        players.insertOrAssign({&world, entity.value}, player);
    }
    if (!(animator.clip == player->clipId))
    {
        player->clipId = animator.clip;
        player->time = 0;
        player->fresh = true;
    }
    const StateMachine* machine = machineOf(animator.stateMachine);
    if (!(animator.stateMachine == player->machineId) || machine != player->machine)
    {
        player->machineId = animator.stateMachine;
        player->machine = machine;
        player->parameters.clear();
        player->state = -1;
        player->phase = 0;
        player->fresh = true;
        if (machine != nullptr)
        {
            for (const assets::StateParameter& parameter : machine->parameters)
            {
                player->parameters.pushBack(parameter.defaultValue);
            }
            player->state = static_cast<int32_t>(machine->info.start);
        }
    }
    return player;
}

// The clip a state shows at its parameters: a blend space's with the most
// weight (frames can't be blended, so it picks the nearest).
AssetId stateClipId(const Player& player)
{
    animation::ClipWeight weights[animation::maxStateClips];
    const uint32_t count =
        animation::stateClips(*player.machine, static_cast<uint32_t>(player.state), player.parameters.span(), weights);
    uint32_t best = 0;
    for (uint32_t i = 1; i < count; ++i)
    {
        best = weights[i].weight > weights[best].weight ? i : best;
    }
    return count > 0 ? player.machine->motions[weights[best].motion].clip : AssetId{};
}

const SpriteClip* stateClip(const Player& player)
{
    return clipOf(stateClipId(player));
}

bool waiting(const Player& player)
{
    if (isLoading(player.clipId) || isLoading(player.machineId))
    {
        return true;
    }
    return player.machine != nullptr && player.state >= 0 && isLoading(stateClipId(player));
}

// A stretch of a clip one update plays: from `time`, `amount` seconds on.
struct Stretch
{
    float time;
    float amount;
    float duration;
    bool loop;
    bool fresh; // just started: its start counts as passed
};

// Whether the stretch passes `at` on the clip, and how far through it (0
// to 1), the first time; as the pose runtime judges it.
bool passes(const Stretch& s, float at, float& share)
{
    if (s.amount <= 0 || s.duration <= 0)
    {
        return false;
    }
    if (s.loop)
    {
        while (s.fresh ? at < s.time : at <= s.time)
        {
            at += s.duration;
        }
    }
    else if (s.fresh ? at < s.time : at <= s.time)
    {
        return false;
    }
    if (at > s.time + s.amount)
    {
        return false;
    }
    share = (at - s.time) / s.amount;
    return true;
}

// Times within the update: `start` seconds after it began, `length` long;
// the update ends at `frameStart`, `delta` after it began.
struct Segment
{
    uint64_t frameStart;
    float delta;
    float start;
    float length;

    uint64_t at(float share) const
    {
        const double back = (static_cast<double>(delta) - (start + share * length)) * 1e9;
        return back <= 0                                 ? frameStart
               : back >= static_cast<double>(frameStart) ? 0
                                                         : frameStart - static_cast<uint64_t>(back);
    }
};

void passEvents(const Player& player, const SpriteClip& clip, const Stretch& stretch, const Segment& segment,
                Vector<animation::AnimationEvent>& out)
{
    float share = 0;
    for (const assets::ClipEvent& event : clip.events)
    {
        if (passes(stretch, event.time, share))
        {
            out.pushBack(
                {player.entity, animation::AnimationEventKind::Event, event.name, event.time, segment.at(share)});
        }
    }
}

float advanceTime(float time, float amount, float duration, bool loop)
{
    time += amount;
    if (duration <= 0)
    {
        return 0;
    }
    if (loop)
    {
        time = std::fmod(time, duration);
        return time < 0 ? time + duration : time;
    }
    return std::clamp(time, 0.0f, duration);
}

const assets::SpriteClipFrame& frameAt(const SpriteClip& clip, float time)
{
    for (const assets::SpriteClipFrame& frame : clip.frames)
    {
        if (time < frame.start + frame.duration)
        {
            return frame;
        }
    }
    return clip.frames[clip.frames.size() - 1];
}

// The machine's update: the state plays, a transition taken at an exit time
// partway through hands the rest of the update to the state it goes to (a
// few times at most, so a cycle of instant transitions ends). Returns the
// clip it shows and sets `time` to where on it.
const SpriteClip* advanceMachine(Player& player, float delta, float speed, uint64_t frameStart,
                                 Vector<animation::AnimationEvent>& out, float& time)
{
    float start = 0;
    float remaining = delta;
    const SpriteClip* clip = nullptr;
    for (uint32_t step = 0; step < 4; ++step)
    {
        clip = stateClip(player);
        if (clip == nullptr)
        {
            break;
        }
        const assets::StateEntry& entry = player.machine->states[static_cast<uint32_t>(player.state)];
        const float duration = clip->info.duration;
        animation::StateProgress progress{player.phase, player.phase, 0, entry.loop != 0, player.phase, 0};
        if (duration > 0)
        {
            progress.rate = entry.speed * speed / duration;
            progress.raw = player.phase + remaining * progress.rate;
            if (progress.loop)
            {
                const float turns = std::floor(progress.raw);
                player.phase = progress.raw - turns;
                progress.wraps = turns > 0 ? static_cast<uint32_t>(turns) : 0;
            }
            else
            {
                player.phase = std::clamp(progress.raw, 0.0f, 1.0f);
            }
        }
        progress.after = player.phase;
        const Stretch stretch{progress.before * duration, (progress.raw - progress.before) * duration, duration,
                              progress.loop, player.fresh};
        passEvents(player, *clip, stretch, {frameStart, delta, start, remaining}, out);
        player.fresh = false;
        const int32_t taken = animation::chooseTransition(*player.machine, static_cast<uint32_t>(player.state),
                                                          player.parameters.span(), progress);
        if (taken < 0)
        {
            break;
        }
        animation::consumeTriggers(*player.machine, static_cast<uint32_t>(taken), player.parameters.span());
        const assets::StateTransition& transition = player.machine->transitions[static_cast<uint32_t>(taken)];
        const float leftover = std::min(animation::pastExit(transition, progress), remaining);
        player.state = static_cast<int32_t>(transition.to);
        player.phase = 0;
        player.fresh = true;
        clip = stateClip(player);
        start += remaining - leftover;
        remaining = leftover;
        if (remaining <= 0)
        {
            break;
        }
    }
    time = clip != nullptr ? player.phase * clip->info.duration : 0;
    return clip;
}

void show(ecs::World& world, ecs::Entity entity, const SpriteClip& clip, float time)
{
    const assets::SpriteClipFrame& frame = frameAt(clip, time);
    const SpriteRegion region{frame.x, frame.y, frame.width, frame.height};
    const Sprite* now = ecs::read<Sprite>(world, entity);
    const bool sameTexture = isNone(clip.info.texture) || now->texture == clip.info.texture;
    if (sameTexture && now->region.x == region.x && now->region.y == region.y && now->region.width == region.width &&
        now->region.height == region.height)
    {
        return; // unchanged, so its chunk isn't marked written
    }
    Sprite* sprite = ecs::get<Sprite>(world, entity);
    if (!isNone(clip.info.texture))
    {
        sprite->texture = clip.info.texture;
    }
    sprite->region = region;
}

void gather(ecs::World& world, Vector<ecs::Entity>& out)
{
    out.clear();
    const ecs::ComponentId required[] = {ecs::componentIdOf<SpriteAnimator>, ecs::componentIdOf<Sprite>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::Entity* entities = ecs::entitiesOf(view);
            for (uint32_t i = 0; i < ecs::entityCountOf(view); ++i)
            {
                static_cast<Vector<ecs::Entity>*>(data)->pushBack(entities[i]);
            }
        },
        &out);
    ecs::destroyQuery(query);
    // Players whose entity is gone, or lost its animator or sprite, go.
    Vector<Player*> gone(tag);
    for (const auto& entry : players)
    {
        const Player* player = entry.value;
        if (player->world == &world && (!ecs::isAlive(world, player->entity) ||
                                        !ecs::hasComponent(world, player->entity, ecs::componentIdOf<SpriteAnimator>) ||
                                        !ecs::hasComponent(world, player->entity, ecs::componentIdOf<Sprite>)))
        {
            gone.pushBack(entry.value);
        }
    }
    for (Player* player : gone)
    {
        freePlayer(player);
    }
}

// The load starter (ADR 0099): every SpriteAnimator's clip, and its state
// machine's clips.
uint32_t startSpriteAnimatorLoads(ecs::World& world)
{
    if (ecs::componentIdOf<SpriteAnimator> == ecs::invalidComponent)
    {
        return 0;
    }
    const ecs::ComponentId required[] = {ecs::componentIdOf<SpriteAnimator>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    uint32_t count = 0;
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            for (const SpriteAnimator& animator : ecs::readColumn<SpriteAnimator>(view))
            {
                (void)clipOf(animator.clip);
                const StateMachine* machine = machineOf(animator.stateMachine);
                bool loads = isLoading(animator.clip) || isLoading(animator.stateMachine);
                if (machine != nullptr)
                {
                    for (const assets::StateMotion& motion : machine->motions)
                    {
                        loads = loads || isLoading(motion.clip);
                    }
                }
                *static_cast<uint32_t*>(data) += loads ? 1 : 0;
            }
        },
        &count);
    ecs::destroyQuery(query);
    return count;
}

} // namespace

void advanceSpriteAnimators(ecs::World& world, float delta, uint64_t frameStart,
                            Vector<animation::AnimationEvent>& events)
{
    if (ecs::componentIdOf<SpriteAnimator> == ecs::invalidComponent ||
        ecs::componentIdOf<Sprite> == ecs::invalidComponent)
    {
        return;
    }
    Vector<ecs::Entity> entities(tag);
    gather(world, entities);
    for (const ecs::Entity entity : entities)
    {
        const SpriteAnimator animator = *ecs::read<SpriteAnimator>(world, entity);
        Player& player = *sync(world, entity, animator);
        if (waiting(player))
        {
            continue;
        }
        if (player.machine != nullptr && player.state >= 0)
        {
            float time = 0;
            if (const SpriteClip* clip = advanceMachine(player, delta, animator.speed, frameStart, events, time))
            {
                show(world, entity, *clip, time);
            }
            continue;
        }
        const SpriteClip* clip = clipOf(player.clipId);
        if (clip == nullptr)
        {
            continue;
        }
        const bool loop = clip->info.loop != 0;
        const Stretch stretch{player.time, delta * animator.speed, clip->info.duration, loop, player.fresh};
        passEvents(player, *clip, stretch, {frameStart, delta, 0, delta}, events);
        player.time = advanceTime(player.time, stretch.amount, clip->info.duration, loop);
        player.fresh = false;
        show(world, entity, *clip, player.time);
    }
}

Result<void> setSpriteParameter(ecs::World& world, ecs::Entity entity, std::string_view name,
                                assets::ParameterType type, float value)
{
    const SpriteAnimator* animator = ecs::componentIdOf<SpriteAnimator> != ecs::invalidComponent
                                         ? ecs::read<SpriteAnimator>(world, entity)
                                         : nullptr;
    if (animator == nullptr)
    {
        return Status::NotFound;
    }
    Player* player = sync(world, entity, *animator);
    const int32_t index = player->machine != nullptr ? player->machine->findParameter(name) : -1;
    if (index < 0 || player->machine->parameters[static_cast<uint32_t>(index)].type != type)
    {
        return Status::NotFound;
    }
    player->parameters[static_cast<uint32_t>(index)] = value;
    return {};
}

int32_t spriteCurrentState(const ecs::World& world, ecs::Entity entity)
{
    const Player* player = findPlayer(world, entity);
    return player != nullptr && player->machine != nullptr ? player->state : -1;
}

int32_t spriteFindState(ecs::World& world, ecs::Entity entity, std::string_view name)
{
    const SpriteAnimator* animator = ecs::componentIdOf<SpriteAnimator> != ecs::invalidComponent
                                         ? ecs::read<SpriteAnimator>(world, entity)
                                         : nullptr;
    if (animator == nullptr)
    {
        return -1;
    }
    const Player* player = sync(world, entity, *animator);
    return player->machine != nullptr ? player->machine->findState(name) : -1;
}

bool reloadSpriteAnimationAsset(const AssetId& asset)
{
    bool loaded = false;
    if (SpriteClip** clip = clips.find(asset))
    {
        loaded = *clip != nullptr;
        if (*clip != nullptr)
        {
            destroySpriteClip(*clip);
        }
        clips.erase(asset);
    }
    if (StateMachine** machine = machines.find(asset))
    {
        loaded = loaded || *machine != nullptr;
        for (const auto& entry : players)
        {
            if (entry.value->machine == *machine)
            {
                entry.value->machine = nullptr; // read again by sync, which starts it over
            }
        }
        if (*machine != nullptr)
        {
            animation::destroyStateMachine(*machine);
        }
        machines.erase(asset);
    }
    return loaded;
}

void releaseSpriteAnimators(const ecs::World& world)
{
    Vector<Player*> gone(tag);
    for (const auto& entry : players)
    {
        if (entry.value->world == &world)
        {
            gone.pushBack(entry.value);
        }
    }
    for (Player* player : gone)
    {
        freePlayer(player);
    }
}

void registerSpriteAnimator()
{
    ecs::registerComponent<SpriteAnimator>("Crucible.Render2D.SpriteAnimator");
    static const bool starter = [] {
        assets::registerLoadStarter(startSpriteAnimatorLoads);
        return true;
    }();
    (void)starter;
}

} // namespace crucible::render2d
