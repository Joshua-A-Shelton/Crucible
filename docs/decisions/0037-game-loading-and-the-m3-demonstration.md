# 0037 — Loading a game, and the M3 demonstration

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

M3 is done when thousands of entities are updated by a C# and a C++
system, in parallel through the job system, with structural changes
applied at phase ends, timed (docs/milestones.md). Every piece exists
(ADRs 0014, 0023, 0035, 0036), but the launcher had never loaded a game:
`GameLoader` was only used by the hot reload tests, and `IGameEntry` is an
M1 placeholder. A C# and a C++ system also need components both can name.

## Options

**Form of the demonstration:** a sample game the launcher loads with
`--game` (ADR 0011's JIT path); a demo mode built into the engine; or a
standalone benchmark executable that skips the launcher.

**Components shared by C++ and C#:** a C++ struct mirrored into C# by the
binding generator, with C++ registering it under the mirror's name; matching
structs written twice by hand; or a C# struct whose ID C++ looks up by name.

**Where the C++ systems go:** in the core behind opt-in registration (ADR
0036 keeps C++ systems in-engine until M7), or in a separate library.

**How a game sets up its world:** in a system's `Start`, or through a new
game entry API. **How a game gets options:** through the launcher, or an
environment variable.

## Decision

1. **The demonstration is a sample game,** `samples/Particles`
   (`Crucible.Samples.Particles`), loaded by the launcher with
   `--game <file>`. The host loads it through `GameLoader`, adds its
   systems with `LoadedGame.RegisterSystems`, and builds the schedule.
   Only a JIT engine loads games; a NativeAOT engine refuses `--game`, and
   the sample's AOT coverage comes from `Crucible.Engine.Tests`, which runs
   it under both. The generated AOT bootstrap stays an open item.
2. **Games set up their world in a system's `Start`.** No new game API
   yet: `IGameEntry` stays an unused placeholder for the launcher path and
   becomes optional for `GameLoader`. A system's constructor runs while the
   schedule is being registered, which is when a game registers C++
   systems (they join when it's built).
3. **Game options come from the launcher,** the only command-line parser
   (ADR 0011): `--game-arg key=value`, repeatable, read back by the host
   and passed to systems as `FrameContext.GameArguments`. The engine
   doesn't read them.
4. **Shared components are C++ structs** (`crucible/Demo.h`), mirrored by
   the binding generator with layout checks, and registered by C++ under
   the C# mirror's full name (`Crucible.Demo.Position`), so both languages
   get the same ID with the same size.
5. **The C++ systems live in the core behind `registerDemoSystems()`,**
   like the binding probe: nothing runs unless a game asks. They move out
   once C++ code outside the engine can register systems (M7).

## Why

The sample exercises the real path, launcher to host to loader to schedule,
which every later game will use, and is the first game the launcher ever
loaded. Setting up in `Start` and passing options as strings commit to no
game API before plugins (M7) and services (M4) inform one. One C++
declaration with a generated mirror keeps a single source for each shared
layout, checked on both sides.

## As built (M3 step 9)

- **The simulation** (per fixed step, at 60 Hz):

  | Phase | System | Language |
  |---|---|---|
  | PrePhysics | `crucible.demo.Respawn`: records creations up to the population | C++ |
  | Physics | `crucible.demo.Steer`: velocity += (-pull p + spin (Y × p) - drag v) step, one job per chunk | C++ |
  | Physics, after Steer | `IntegrateSystem`: position and lifetime, one job per chunk | C# |
  | PostPhysics | `ExpireSystem`: records a destroy per expired particle, one job per chunk | C# |
  | Update | `ParticleReport`: samples the timings, quits after `steps` | C# |

  `ParticleSetup` (BeginFrame) registers everything in its constructor,
  spawns the first particles in `Start`, and unregisters the C++ systems in
  `Stop`. Options: `particles` (100,000), `steps` (600), `seed`.
- **Timing:** each system's `LastRunTicks`, and a new
  `FramePhase.LastSyncTicks` for each sync point; `FrameContext.Schedule`
  lets a system find them. The report prints the median, 95th percentile,
  and maximum per step.
- **Two ECS problems found by the demonstration:**
  - `Vector::resize` reserved the exact size, so growing one element at a
    time copied the whole array each time. Entity records grow that way,
    so applying ~340 creations took 183 ms. `resize` now grows
    geometrically (0.25 ms).
  - Recorded creations never reused destroyed entities' indices (ADR
    0023's amendment, now amended again), so a game spawning through
    command buffers grew its records forever.
- **Results** (release, 100,000 particles, 600 steps; medians per step):
  - Linux, Ryzen 9 7900X (24 threads): 0.43 ms with 1 worker, 0.47 ms with 23.
  - Windows, Core i7-9700 (8 threads): 0.70 ms with 1 worker, 0.54 ms with 7.
  - Linux, 1,000,000 particles: 4.96 ms with 1 worker, 3.93 ms with 23;
    Steer and Integrate each drop from ~1.5 to ~0.9 ms, at about 40 GB/s
    of memory traffic: memory bandwidth, not the job system, is the limit.
  - At 100,000 each parallel system takes ~0.1 ms, about 1 ns per
    particle, too little to pay for waking workers (ADR 0023's note that
    parallelism pays for heavier systems). The maxima (~4 ms) are the
    first steps, while the JIT compiles.
  - The same seed gives the same counts on both machines (228,487
    respawned, 99,560 alive at the end).
- **Tests:**
  - `Crucible.Engine.Tests` (JIT and NativeAOT) runs the sample through the
    loop on a stepped clock: one particle's velocity and position match the
    formulas every step (a sign flipped in Steer fails on the first step),
    the population is exactly the target after every Respawn sync, nothing
    expired survives a PostPhysics sync, C++ and C# share the component
    IDs, settings parse, and frames allocate nothing.
  - Launcher: the sample through `--game` (JIT), a malformed `--game-arg`,
    and the AOT launcher refusing `--game`.
  - Native: `resize` growth, recorded creations reusing indices, and
    parallel recording handing out each free index once (also under TSan).
