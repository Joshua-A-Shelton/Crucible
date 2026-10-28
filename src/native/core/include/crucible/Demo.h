#pragma once

// The M3 demonstration's C++ half (docs/milestones.md, ADR 0037): components
// shared with C#, and two C++ systems that run beside the sample game's C#
// ones. Opt-in, like the binding probe: nothing is registered until a game
// asks. It lives in the core until C++ code from outside the engine can
// register systems (M7). Names are placeholders (docs/naming.md).

#include <crucible/Expose.h>
#include <crucible/Math.h>

#include <cstdint>

namespace crucible::demo {

// Components shared with C# (ADR 0012): declared once here, mirrored into C#
// by the binding generator with layout checks, and registered by both
// languages under the C# mirror's full name ("Crucible.Demo.Position"), so
// both get the same ID.
struct CRUCIBLE_EXPOSE Position
{
    Vector3 value;
};

struct CRUCIBLE_EXPOSE Velocity
{
    Vector3 value;
};

// Seconds left; the C# side destroys the particle when it runs out.
struct CRUCIBLE_EXPOSE Lifetime
{
    float remaining;
};

struct CRUCIBLE_EXPOSE DemoSettings
{
    uint32_t population; // Respawn tops the particles up to this many
    uint32_t seed;       // for Respawn's random particles
    float pull;          // Steer: acceleration toward the origin, per unit of distance
    float spin;          // Steer: acceleration around the Y axis, per unit of distance
    float drag;          // Steer: deceleration, per unit of speed
    float radius;        // Respawn: new particles start on a ring this wide
    float minLifetime;   // Respawn: new particles live between these, in seconds
    float maxLifetime;
};

// Registers Position, Velocity, and Lifetime. Registering again is harmless.
CRUCIBLE_EXPOSE void registerDemoComponents();

// Registers the C++ systems, with the components:
// - "crucible.demo.Respawn" in PrePhysics: records creations until the
//   particles number `population` again (destroys were applied at the last
//   sync point).
// - "crucible.demo.Steer" in Physics: velocity += (-pull * p + spin * (Y × p)
//   - drag * v) * step, over every particle, one job per chunk.
// Register before the schedule is built (ADR 0036). Settings start at zero:
// configure them before the first frame.
CRUCIBLE_EXPOSE void registerDemoSystems();

// Replaces the settings; call between frames.
CRUCIBLE_EXPOSE void configureDemo(DemoSettings settings);

// Removes the systems and frees their queries; call before destroying the
// world they ran on.
CRUCIBLE_EXPOSE void unregisterDemoSystems();

// How many particles Respawn has recorded since the systems were registered.
CRUCIBLE_EXPOSE uint64_t demoRespawned();

} // namespace crucible::demo
