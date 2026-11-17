// Swept hitboxes (ADR 0027, 0096), 3D and 2D, on the installed services'
// shapeCastAll, so a replacement physics gets them too. Each run, for each
// hitbox: the stretches of the frame it was active in (from its window's
// open and close events, or its enabled flag), and for each stretch a sweep
// from where it was then to where it was at the stretch's end. Its place in
// between is last frame's carried to this one's as a screw: a steady turn
// about an axis, with a steady slide along it. A hitbox swung round a pivot
// (a foot round a hip, a sword round a shoulder) follows its circle; one
// moved without turning goes in a straight line. The sweep is split so no
// piece turns more than maxPieceTurn, each piece a straight cast at the
// piece's middle turn. Kept between
// runs, per hitbox: its last place, whether its window was open and it was
// active, and what its activation has hit.

#include "Hitboxes.h"
#include "TransformParts.h"

#include <crucible/Animation.h>
#include <crucible/HashMap.h>
#include <crucible/Memory.h>
#include <crucible/Physics.h>
#include <crucible/Physics2D.h>
#include <crucible/Systems.h>
#include <crucible/Transform.h>
#include <crucible/Vector.h>

#include <algorithm>
#include <cmath>

namespace crucible::physics {

namespace {

constexpr MemoryTag tag = MemoryTag::Physics;
constexpr float maxPieceTurn = 0.2617994f; // 15 degrees
constexpr uint32_t maxPieces = 24;         // a whole turn in one frame
constexpr uint32_t maxStretches = 8;       // a window opening and closing within a frame, four times over
constexpr float pi = 3.14159265f;

struct Kept
{
    bool placed = false; // swept before: its place is last frame's
    Vector3 position{};
    Quaternion rotation = Quaternion::identity();
    float angle = 0;              // 2D's turn
    bool open = false;            // its window, at the end of the last run
    bool active = false;          // the hitbox, likewise
    uint64_t run = 0;             // the last run that saw it
    Vector<uint64_t> struck{tag}; // its activation's targets
};

struct WorldHitboxes
{
    const ecs::World* world = nullptr;
    HashMap<uint64_t, Kept> kept{tag};
    HashMap<uint64_t, Kept> kept2D{tag};
    uint64_t run = 0;
    uint64_t run2D = 0;
    Vector<HitboxHit> hits{tag};
    Vector<physics2d::HitboxHit2D> hits2D{tag};
};

Vector<WorldHitboxes*> worlds{tag};

WorldHitboxes* find(const ecs::World& world)
{
    for (WorldHitboxes* entry : worlds)
    {
        if (entry->world == &world)
        {
            return entry;
        }
    }
    return nullptr;
}

WorldHitboxes& keptFor(const ecs::World& world)
{
    if (WorldHitboxes* entry = find(world))
    {
        return *entry;
    }
    WorldHitboxes* entry = create<WorldHitboxes>(tag);
    entry->world = &world;
    worlds.pushBack(entry);
    return *entry;
}

// The frame a run covers, from the last frame's start to this one's, timed
// as animation events are.
struct FrameSpan
{
    uint64_t start;
    double delta;

    uint64_t at(float share) const
    {
        const double back = (1.0 - share) * delta * 1e9;
        return back >= static_cast<double>(start) ? 0 : start - static_cast<uint64_t>(back);
    }

    float shareOf(uint64_t timestamp) const
    {
        if (delta <= 0 || timestamp >= start)
        {
            return 1;
        }
        return static_cast<float>(std::clamp(1.0 - static_cast<double>(start - timestamp) / (delta * 1e9), 0.0, 1.0));
    }
};

// A stretch of the frame the hitbox was active in, as shares (0 the last
// frame's start, 1 this one's); a fresh one starts an activation.
struct Stretch
{
    float from;
    float to;
    bool fresh;
};

// The nearest Animator at or above the entity, or the top of its hierarchy.
ecs::Entity ownerOf(const ecs::World& world, ecs::Entity entity)
{
    const ecs::ComponentId animator = ecs::componentIdOf<animation::Animator>; // invalid without animation
    ecs::Entity top = entity;
    for (ecs::Entity at = entity; at.value != 0; at = ecs::parentOf(world, at))
    {
        if (animator != ecs::invalidComponent && ecs::hasComponent(world, at, animator))
        {
            return at;
        }
        top = at;
    }
    return top;
}

// Whether the entity is the owner or under it.
bool ownedBy(const ecs::World& world, ecs::Entity entity, ecs::Entity owner)
{
    for (ecs::Entity at = entity; at.value != 0; at = ecs::parentOf(world, at))
    {
        if (at.value == owner.value)
        {
            return true;
        }
    }
    return false;
}

bool struck(const Kept& kept, ecs::Entity target)
{
    return std::find(kept.struck.begin(), kept.struck.end(), target.value) != kept.struck.end();
}

// When in the frame the hitbox was active; updates `kept`'s open and active.
uint32_t stretchesOf(const ecs::World& world, std::span<const animation::AnimationEvent> events, ecs::Entity owner,
                     uint32_t window, bool enabled, const FrameSpan& frame, Kept& kept, Stretch (&out)[maxStretches])
{
    uint32_t count = 0;
    const bool wasActive = kept.active;
    // A stretch while the window was open; one carried over from the last
    // frame, by a hitbox that wasn't active then (it was just enabled),
    // starts now instead.
    auto add = [&](float from, float to, bool fresh) {
        if (!enabled || count == maxStretches)
        {
            return;
        }
        if (!fresh && !wasActive)
        {
            from = to;
            fresh = true;
        }
        out[count++] = {from, to, fresh};
    };
    if (window == 0)
    {
        add(0, 1, false);
        kept.open = false;
        kept.active = enabled;
        return count;
    }
    bool open = kept.open;
    float openedAt = 0;
    bool fresh = false;
    for (const animation::AnimationEvent& event : events)
    {
        if (event.entity.value != owner.value || event.name != window)
        {
            continue;
        }
        if (event.kind == animation::AnimationEventKind::WindowOpen && !open)
        {
            open = true;
            openedAt = frame.shareOf(event.timestamp);
            fresh = true;
        }
        else if (event.kind == animation::AnimationEventKind::WindowClose && open)
        {
            add(openedAt, frame.shareOf(event.timestamp), fresh);
            open = false;
        }
    }
    // What the pose says now settles it (a window its clip left without an
    // event, a pose started over).
    animation::OpenWindow windows[16];
    const uint32_t listed = std::min<uint32_t>(animation::openWindows(world, owner, windows), 16);
    bool openNow = false;
    for (uint32_t i = 0; i < listed; ++i)
    {
        openNow = openNow || windows[i].name == window;
    }
    if (open)
    {
        add(openedAt, 1, fresh);
    }
    else if (openNow)
    {
        add(1, 1, true);
    }
    kept.open = openNow;
    kept.active = enabled && openNow;
    return count;
}

Vector<animation::AnimationEvent> eventsOf(const ecs::World& world)
{
    Vector<animation::AnimationEvent> events(tag);
    events.resize(animation::events(world, {}));
    animation::events(world, events.span());
    return events;
}

// The entities with a component, in a list (sweeping writes nothing, but
// reads other entities' transforms).
template <class T>
Vector<ecs::Entity> entitiesWith(ecs::World& world)
{
    Vector<ecs::Entity> entities(tag);
    const ecs::ComponentId required[] = {ecs::componentIdOf<T>};
    ecs::Query* query = ecs::createQuery(world, {.required = required});
    ecs::forEachChunk(
        *query,
        [](const ecs::ChunkView& view, void* data) {
            const ecs::Entity* chunk = ecs::entitiesOf(view);
            for (uint32_t i = 0; i < ecs::entityCountOf(view); ++i)
            {
                static_cast<Vector<ecs::Entity>*>(data)->pushBack(chunk[i]);
            }
        },
        &entities);
    ecs::destroyQuery(query);
    return entities;
}

// Forgets the hitboxes the last run didn't see.
void dropUnseen(HashMap<uint64_t, Kept>& kept, uint64_t run)
{
    Vector<uint64_t> gone(tag);
    for (const auto& entry : kept)
    {
        if (entry.value.run != run)
        {
            gone.pushBack(entry.key);
        }
    }
    for (const uint64_t entity : gone)
    {
        kept.erase(entity);
    }
}

// How many pieces a stretch turning this far (radians) is cast in.
uint32_t piecesFor(float turned)
{
    return std::clamp<uint32_t>(static_cast<uint32_t>(std::ceil(turned / maxPieceTurn)), 1, maxPieces);
}

// --- 3D ---------------------------------------------------------------------

QueryShape scaled(const Hitbox& hitbox, Vector3 scale)
{
    const Vector3 s{std::fabs(scale.x), std::fabs(scale.y), std::fabs(scale.z)};
    const float largest = std::max({s.x, s.y, s.z});
    switch (hitbox.shape)
    {
    case ShapeKind::Box:
        return {hitbox.shape, {hitbox.size.x * s.x, hitbox.size.y * s.y, hitbox.size.z * s.z}};
    case ShapeKind::Sphere:
        return {hitbox.shape, {hitbox.size.x * largest, 0, 0}};
    case ShapeKind::Capsule:
        return {hitbox.shape, {hitbox.size.x * largest, hitbox.size.y * largest, 0}};
    default:
        return {hitbox.shape, hitbox.size}; // not a query shape: finds nothing
    }
}

// Last frame's place carried to this one's: turned by `turn` about the axis
// through `center`, slid by `slide` along it; without a turn, moved by
// `slide`.
struct Screw
{
    Vector3 from;
    Quaternion fromRotation;
    Quaternion turn = Quaternion::identity();
    float angle = 0;
    Vector3 center{};
    Vector3 slide{};

    Vector3 positionAt(float share) const
    {
        if (angle == 0)
        {
            return from + slide * share;
        }
        return rotate(from - center, slerp(Quaternion::identity(), turn, share)) + center + slide * share;
    }

    Quaternion rotationAt(float share) const
    {
        return normalize(slerp(Quaternion::identity(), turn, share) * fromRotation);
    }
};

Screw screwBetween(Vector3 p0, Quaternion q0, Vector3 p1, Quaternion q1)
{
    Screw screw{p0, q0};
    screw.slide = p1 - p0;
    Quaternion turn = normalize(q1 * conjugate(q0)); // q1 = turn after q0
    if (turn.w < 0)
    {
        turn = {-turn.x, -turn.y, -turn.z, -turn.w}; // the short way
    }
    const float sinHalf = length(Vector3{turn.x, turn.y, turn.z});
    const float angle = 2 * std::atan2(sinHalf, turn.w);
    screw.turn = turn;
    if (angle < 1e-4f)
    {
        screw.turn = Quaternion::identity();
        return screw;
    }
    const Vector3 axis = Vector3{turn.x, turn.y, turn.z} * (1.0f / sinHalf);
    // p1 = turned p0 + t; t's part along the axis is the slide, and the
    // part across it places the axis: (I - R) center = across.
    const Vector3 t = p1 - rotate(p0, turn);
    const Vector3 along = axis * dot(t, axis);
    const Vector3 across = t - along;
    const Vector3 u =
        normalize(std::fabs(axis.x) < 0.9f ? cross(axis, Vector3{1, 0, 0}) : cross(axis, Vector3{0, 1, 0}));
    const Vector3 w = cross(axis, u);
    // In (u, w), R is [[a, -b], [b, a]]: R u = a u + b w.
    const Vector3 turnedU = rotate(u, turn);
    const float a = dot(turnedU, u), b = dot(turnedU, w);
    const float tu = dot(across, u), tw = dot(across, w);
    const float det = (1 - a) * (1 - a) + b * b;
    screw.center = u * (((1 - a) * tu - b * tw) / det) + w * ((b * tu + (1 - a) * tw) / det);
    screw.slide = along;
    screw.angle = angle;
    return screw;
}

struct Sweep
{
    ecs::Entity hitbox;
    ecs::Entity owner;
    QueryShape shape;
    QueryFilter filter;
    Screw motion;
};

void sweepStretch(ecs::World& world, const Sweep& sweep, const Stretch& stretch, const FrameSpan& frame, Kept& kept,
                  Vector<RayHit>& found, Vector<HitboxHit>& hits)
{
    if (stretch.fresh)
    {
        kept.struck.clear();
    }
    const uint32_t pieces = piecesFor(sweep.motion.angle * (stretch.to - stretch.from));
    for (uint32_t i = 0; i < pieces; ++i)
    {
        const float s0 = stretch.from + (stretch.to - stretch.from) * static_cast<float>(i) / pieces;
        const float s1 = stretch.from + (stretch.to - stretch.from) * static_cast<float>(i + 1) / pieces;
        const Vector3 p0 = sweep.motion.positionAt(s0);
        Vector3 direction = sweep.motion.positionAt(s1) - p0;
        float distance = length(direction);
        if (distance > 1e-6f)
        {
            direction = direction * (1.0f / distance);
        }
        else
        {
            direction = {0, 0, 1};
            distance = 0; // an overlap where it is
        }
        const Quaternion turn = sweep.motion.rotationAt((s0 + s1) * 0.5f);
        uint32_t count = shapeCastAll(world, sweep.shape, p0, turn, direction, distance, sweep.filter, found.span());
        if (count > found.size())
        {
            found.resize(count);
            count = shapeCastAll(world, sweep.shape, p0, turn, direction, distance, sweep.filter, found.span());
        }
        for (uint32_t j = 0; j < count && j < found.size(); ++j)
        {
            const RayHit& hit = found[j];
            if (ownedBy(world, hit.entity, sweep.owner) || struck(kept, hit.entity))
            {
                continue;
            }
            const float along = distance > 0 ? std::clamp(hit.distance / distance, 0.0f, 1.0f) : 0.0f;
            kept.struck.pushBack(hit.entity.value);
            hits.pushBack(
                {sweep.hitbox, sweep.owner, hit.entity, hit.point, hit.normal, frame.at(s0 + along * (s1 - s0))});
        }
    }
}

void sweepWorld(ecs::World& world, float deltaSeconds, uint64_t frameStartNs)
{
    WorldHitboxes& all = keptFor(world);
    const uint64_t run = ++all.run;
    all.hits.clear();
    const FrameSpan frame{frameStartNs, deltaSeconds};
    const Vector<animation::AnimationEvent> events = eventsOf(world);
    Vector<RayHit> found(tag);
    found.resize(16);
    for (const ecs::Entity entity : entitiesWith<Hitbox>(world))
    {
        const Hitbox hitbox = *ecs::read<Hitbox>(world, entity);
        const TransformParts now = decompose(scenes::computeWorldTransform(world, entity));
        Kept& kept = *all.kept.tryEmplace(entity.value).first;
        kept.run = run;
        const ecs::Entity owner = ownerOf(world, entity);
        Stretch stretches[maxStretches];
        const uint32_t count =
            stretchesOf(world, events.span(), owner, hitbox.window, hitbox.enabled, frame, kept, stretches);
        const Sweep sweep{entity,
                          owner,
                          scaled(hitbox, now.scale),
                          {hitbox.mask, entity, true},
                          screwBetween(kept.placed ? kept.position : now.position,
                                       kept.placed ? kept.rotation : now.rotation, now.position, now.rotation)};
        for (uint32_t i = 0; i < count; ++i)
        {
            sweepStretch(world, sweep, stretches[i], frame, kept, found, all.hits);
        }
        kept.placed = true;
        kept.position = now.position;
        kept.rotation = now.rotation;
    }
    dropUnseen(all.kept, run);
    std::stable_sort(all.hits.begin(), all.hits.end(),
                     [](const HitboxHit& a, const HitboxHit& b) { return a.timestamp < b.timestamp; });
}

void system3D(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr)
    {
        sweepWorld(*context.world, static_cast<float>(context.deltaSeconds), context.frameStartNs);
    }
}

// --- 2D ---------------------------------------------------------------------

float wrapAngle(float angle)
{
    angle = std::fmod(angle + pi, 2 * pi);
    return (angle < 0 ? angle + 2 * pi : angle) - pi;
}

struct Place2D
{
    Vector2 position;
    float angle;
    Vector2 scale;
};

// As Box2D's bodies take it: a mirror turns the angle, not the shape.
Place2D place2D(const Matrix4x4& m)
{
    const bool mirrored = m.rows[0].x * m.rows[1].y - m.rows[0].y * m.rows[1].x < 0;
    const float sign = mirrored ? -1.0f : 1.0f;
    return {{m.rows[3].x, m.rows[3].y},
            std::atan2(sign * m.rows[0].y, sign * m.rows[0].x),
            {std::sqrt(m.rows[0].x * m.rows[0].x + m.rows[0].y * m.rows[0].y),
             std::sqrt(m.rows[1].x * m.rows[1].x + m.rows[1].y * m.rows[1].y)}};
}

physics2d::QueryShape2D scaled(const physics2d::Hitbox2D& hitbox, Vector2 scale)
{
    const float largest = std::max(scale.x, scale.y);
    switch (hitbox.shape)
    {
    case physics2d::ShapeKind2D::Box:
        return {hitbox.shape, {hitbox.size.x * scale.x, hitbox.size.y * scale.y}};
    case physics2d::ShapeKind2D::Circle:
        return {hitbox.shape, {hitbox.size.x * largest, 0}};
    case physics2d::ShapeKind2D::Capsule:
        return {hitbox.shape, {hitbox.size.x * largest, hitbox.size.y * largest}};
    default:
        return {hitbox.shape, hitbox.size}; // not a query shape: finds nothing
    }
}

// 3D's Screw in the plane: turned by `turn` radians (the short way) about
// `center`; without a turn, moved by `slide`.
struct Screw2D
{
    Vector2 from;
    float fromAngle;
    float turn = 0;
    Vector2 center{};
    Vector2 slide{};

    Vector2 positionAt(float share) const
    {
        if (turn == 0)
        {
            return from + slide * share;
        }
        const float c = std::cos(turn * share), s = std::sin(turn * share);
        const Vector2 r = from - center;
        return center + Vector2{r.x * c - r.y * s, r.x * s + r.y * c};
    }

    float angleAt(float share) const
    {
        return fromAngle + turn * share;
    }
};

Screw2D screwBetween(Vector2 p0, float a0, Vector2 p1, float a1)
{
    Screw2D screw{p0, a0};
    screw.slide = p1 - p0;
    const float turn = wrapAngle(a1 - a0);
    if (std::fabs(turn) < 1e-4f)
    {
        return screw;
    }
    // p1 = center + R (p0 - center): (I - R) center = p1 - R p0.
    const float c = std::cos(turn), s = std::sin(turn);
    const Vector2 t = p1 - Vector2{p0.x * c - p0.y * s, p0.x * s + p0.y * c};
    const float det = (1 - c) * (1 - c) + s * s;
    screw.center = {((1 - c) * t.x - s * t.y) / det, (s * t.x + (1 - c) * t.y) / det};
    screw.turn = turn;
    return screw;
}

struct Sweep2D
{
    ecs::Entity hitbox;
    ecs::Entity owner;
    physics2d::QueryShape2D shape;
    QueryFilter filter;
    Screw2D motion;
};

void sweepStretch2D(ecs::World& world, const Sweep2D& sweep, const Stretch& stretch, const FrameSpan& frame, Kept& kept,
                    Vector<physics2d::RayHit2D>& found, Vector<physics2d::HitboxHit2D>& hits)
{
    if (stretch.fresh)
    {
        kept.struck.clear();
    }
    const uint32_t pieces = piecesFor(std::fabs(sweep.motion.turn) * (stretch.to - stretch.from));
    for (uint32_t i = 0; i < pieces; ++i)
    {
        const float s0 = stretch.from + (stretch.to - stretch.from) * static_cast<float>(i) / pieces;
        const float s1 = stretch.from + (stretch.to - stretch.from) * static_cast<float>(i + 1) / pieces;
        const Vector2 p0 = sweep.motion.positionAt(s0);
        Vector2 direction = sweep.motion.positionAt(s1) - p0;
        float distance = length(direction);
        if (distance > 1e-6f)
        {
            direction = direction * (1.0f / distance);
        }
        else
        {
            direction = {1, 0};
            distance = 0;
        }
        const float angle = sweep.motion.angleAt((s0 + s1) * 0.5f);
        uint32_t count =
            physics2d::shapeCastAll(world, sweep.shape, p0, angle, direction, distance, sweep.filter, found.span());
        if (count > found.size())
        {
            found.resize(count);
            count =
                physics2d::shapeCastAll(world, sweep.shape, p0, angle, direction, distance, sweep.filter, found.span());
        }
        for (uint32_t j = 0; j < count && j < found.size(); ++j)
        {
            const physics2d::RayHit2D& hit = found[j];
            if (ownedBy(world, hit.entity, sweep.owner) || struck(kept, hit.entity))
            {
                continue;
            }
            const float along = distance > 0 ? std::clamp(hit.distance / distance, 0.0f, 1.0f) : 0.0f;
            kept.struck.pushBack(hit.entity.value);
            hits.pushBack(
                {sweep.hitbox, sweep.owner, hit.entity, hit.point, hit.normal, frame.at(s0 + along * (s1 - s0))});
        }
    }
}

void sweepWorld2D(ecs::World& world, float deltaSeconds, uint64_t frameStartNs)
{
    WorldHitboxes& all = keptFor(world);
    const uint64_t run = ++all.run2D;
    all.hits2D.clear();
    const FrameSpan frame{frameStartNs, deltaSeconds};
    const Vector<animation::AnimationEvent> events = eventsOf(world);
    Vector<physics2d::RayHit2D> found(tag);
    found.resize(16);
    for (const ecs::Entity entity : entitiesWith<physics2d::Hitbox2D>(world))
    {
        const physics2d::Hitbox2D hitbox = *ecs::read<physics2d::Hitbox2D>(world, entity);
        const Place2D now = place2D(scenes::computeWorldTransform(world, entity));
        Kept& kept = *all.kept2D.tryEmplace(entity.value).first;
        kept.run = run;
        const ecs::Entity owner = ownerOf(world, entity);
        Stretch stretches[maxStretches];
        const uint32_t count =
            stretchesOf(world, events.span(), owner, hitbox.window, hitbox.enabled, frame, kept, stretches);
        const Vector2 from = kept.placed ? Vector2{kept.position.x, kept.position.y} : now.position;
        const float fromAngle = kept.placed ? kept.angle : now.angle;
        const Sweep2D sweep{entity,
                            owner,
                            scaled(hitbox, now.scale),
                            {hitbox.mask, entity, true},
                            screwBetween(from, fromAngle, now.position, now.angle)};
        for (uint32_t i = 0; i < count; ++i)
        {
            sweepStretch2D(world, sweep, stretches[i], frame, kept, found, all.hits2D);
        }
        kept.placed = true;
        kept.position = {now.position.x, now.position.y, 0};
        kept.angle = now.angle;
    }
    dropUnseen(all.kept2D, run);
    std::stable_sort(
        all.hits2D.begin(), all.hits2D.end(),
        [](const physics2d::HitboxHit2D& a, const physics2d::HitboxHit2D& b) { return a.timestamp < b.timestamp; });
}

void system2D(const loop::SystemContext& context, void*)
{
    if (context.world != nullptr)
    {
        sweepWorld2D(*context.world, static_cast<float>(context.deltaSeconds), context.frameStartNs);
    }
}

constexpr std::string_view afterSockets[] = {animation::socketSystemName};

} // namespace

void registerHitboxes()
{
    ecs::registerComponent<Hitbox>("Crucible.Physics.Hitbox");
    loop::registerDefaultSystem({.name = hitboxSystemName,
                                 .phase = "Animation",
                                 .function = system3D,
                                 .after = afterSockets,
                                 .gameplay = true});
}

void registerHitboxes2D()
{
    ecs::registerComponent<physics2d::Hitbox2D>("Crucible.Physics2D.Hitbox2D");
    loop::registerDefaultSystem({.name = physics2d::hitboxSystemName,
                                 .phase = "Animation",
                                 .function = system2D,
                                 .after = afterSockets,
                                 .gameplay = true});
}

bool hitboxActive(const ecs::World& world, ecs::Entity entity)
{
    const WorldHitboxes* all = find(world);
    if (all == nullptr)
    {
        return false;
    }
    const Kept* kept = all->kept.find(entity.value);
    const Kept* kept2D = all->kept2D.find(entity.value);
    return (kept != nullptr && kept->active) || (kept2D != nullptr && kept2D->active);
}

void forgetHitboxes(const ecs::World& world)
{
    for (size_t i = 0; i < worlds.size(); ++i)
    {
        if (worlds[i]->world == &world)
        {
            destroy(tag, worlds[i]);
            worlds.removeSwap(i);
            return;
        }
    }
}

uint32_t hitboxHits(const ecs::World& world, std::span<HitboxHit> into)
{
    const WorldHitboxes* all = find(world);
    if (all == nullptr)
    {
        return 0;
    }
    std::copy_n(all->hits.begin(), std::min(all->hits.size(), into.size()), into.begin());
    return static_cast<uint32_t>(all->hits.size());
}

void sweepHitboxes(ecs::World& world, float deltaSeconds, uint64_t frameStartNs)
{
    sweepWorld(world, deltaSeconds, frameStartNs);
}

} // namespace crucible::physics

namespace crucible::physics2d {

uint32_t hitboxHits(const ecs::World& world, std::span<HitboxHit2D> into)
{
    const physics::WorldHitboxes* all = physics::find(world);
    if (all == nullptr)
    {
        return 0;
    }
    std::copy_n(all->hits2D.begin(), std::min(all->hits2D.size(), into.size()), into.begin());
    return static_cast<uint32_t>(all->hits2D.size());
}

void sweepHitboxes(ecs::World& world, float deltaSeconds, uint64_t frameStartNs)
{
    physics::sweepWorld2D(world, deltaSeconds, frameStartNs);
}

} // namespace crucible::physics2d
