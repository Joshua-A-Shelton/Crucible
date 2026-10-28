# When parallel jobs don't make things faster

A learning note from the M3 demonstration (ADR 0037). Read this before
expecting a parallel system to scale with the number of cores.

## What the demonstration measured

The particle sample runs two simple systems over every particle, each one
job per chunk: Steer (C++) updates velocity, and Integrate (C#) updates
position and lifetime. Release build, median time per fixed step:

| Particles | Workers | Steer | Integrate |
|---|---|---|---|
| 100,000 | 1 | 0.13 ms | 0.09 ms |
| 100,000 | 23 | 0.11 ms | 0.10 ms |
| 1,000,000 | 1 | 1.53 ms | 1.41 ms |
| 1,000,000 | 23 | 0.87 ms | 0.88 ms |

With 23 workers instead of one, 100,000 particles get no faster at all,
and a million get less than twice as fast. Why?

## Two different limits

**Overhead (small work).** At 100,000 particles, each system does about
1 ns of work per particle: a few multiply-adds. The whole system takes
0.1 ms. Splitting it into ~170 chunk jobs means waking workers, handing out
jobs, and waiting for the last one; that costs tens of microseconds, about
as much as the work saved. One thread (plus the waiting main thread, which
runs jobs too) is already nearly as fast as it gets.

**Memory bandwidth (big work).** At a million particles the work is large
enough to split, but each particle's data is streamed from memory once per
step and the math on it is trivial. Steer reads positions and velocities
(24 MB) and writes velocities (12 MB) in 0.87 ms: about 40 GB/s, a large
share of what a desktop's dual-channel memory delivers in practice. More cores can't read
memory faster than the memory can supply it. The CPU is mostly waiting.

## What this means for systems

- Parallelism pays when **each element does real work**: physics
  constraints, animation blending, culling with many planes, pathfinding.
  ADR 0023 said as much ("parallelism pays for heavier systems").
- For light, streaming work, **fewer bytes beat more threads**: smaller
  components (16-bit or packed data), touching fewer columns, or fusing two
  passes over the same data into one so it's read once.
- A system that takes 0.1 ms on one thread may simply not be worth
  splitting. Run it sequentially (`ForEachChunk`) and let the schedule run
  other work in parallel when automatic parallelism between systems
  arrives (ADR 0013).
- Measure: the sample's report shows each system per step. Change one
  thing, run it with `--workers 1` and with the default, and compare.

## Reproducing it

```bash
build/release/src/native/launcher/crucible_launcher --managed-dir build/release/artifacts/bin/Crucible.Engine/release --headless --workers 1 --game build/release/artifacts/bin/Crucible.Samples.Particles/release/Crucible.Samples.Particles.dll --game-arg particles=1000000 --game-arg steps=300
```

Run it again without `--workers 1` to use every core. Set
`SDL_VIDEO_DRIVER=offscreen` if there's no display.
