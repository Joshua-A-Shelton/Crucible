# 0111 — Unpaced check runs

**Status:** Accepted (2026-11-19). Names added are marked `*` until the
owner settles them (docs/naming.md).

## Context

After ADR 0110 the largest cost left in the Windows suite was the Walker's
headless frames: `samples.walker-level`'s 240 fps run, 10,802 frames, took
about 14 s inside the test, about 1.3 ms a frame on D3D12 (RTX 2070
SUPER), against about 0.45 ms on Linux's Vulkan (RX 7900 XT). ADR 0110
read that as the backend's cost. It isn't.

The standalone run timed by itself (same arguments as the check,
`--headless --fixed-clock --frame-rate N`), per frame between the 30 fps
run (1,352 frames) and the 240 fps run, so startup is left out:

| | Paced, alone | `--no-pacing`, alone | Paced, editor beside | `--no-pacing`, editor beside |
|---|---|---|---|---|
| Windows D3D12 | 0.50 ms | 0.25 ms | **1.22 ms** | 0.54 ms |
| Windows Vulkan | 0.47 ms | 0.24 ms | 0.64 ms | 0.52 ms |
| Linux Vulkan | 0.42 ms | 0.14 ms | 0.42 ms | 0.16 ms |

"Editor beside" is an editor started as the checks start it (`--headless`,
the warm Walker open), rendering while the run goes on. Two things add up:

1. **The editor never stops rendering.** While a check waits for its
   standalone run, the editor's own loop renders its frames uncapped
   (no `--frame-rate`), so two processes share the GPU.
2. **The run paces every frame.** The pacing wait
   (`render::waitForNextFrame`, ADR 0003) on an offscreen surface waits
   for the GPU to finish the previous frame, so each frame pays a round
   trip to the GPU and back, about 0.25 ms even alone. With another
   process on the GPU, Windows switches between them, and the round trip
   waits behind the editor's work: on D3D12 it costs 0.7 ms more a frame
   (with the editor on Vulkan too: 12.1 s instead of 13.2 s). Linux's
   driver barely notices the second process.

Release builds have no backend validation (`Render.cpp` sets it only
without `NDEBUG`), and nothing else in the frame differs between the
backends enough to matter.

## Options

1. **The runs skip the pacing wait** (`--no-pacing`, which exists for this
   kind of run). The clock is fixed (ADR 0105), so the game plays out the
   same: on Linux and Windows the hits land at 116.456 ms (30 fps) and
   116.598 ms (240 fps) into the window, as before. Pacing has its own
   tests (the frame loop's, the latency harness's).
2. **Also cap the editor's frames** (`--frame-rate 60` on the three Walker
   checks' editor command). The editor only waits while a run goes on;
   its checks count waits in frames, not time, and poll every two or three
   frames.
3. **A fixed clock implies no pacing**, in the engine. The same gain as 1
   today (only these checks use `--fixed-clock`), but it changes what
   `--fixed-clock` means for everyone.

Other engines' headless test runs (Unity's `-batchmode -nographics`,
Unreal's `-nullrhi`, Godot's `--headless`) don't render at all, so they
never wait on a GPU; Crucible's headless runs render, so RenderSubmit is
exercised on the GPU (`Host.RunHeadless`), and only the wait goes.

## Decision

Decided by the owner (2026-11-19): options 1 and 2, test-only; the engine
doesn't change.

- `LevelChecks.RunOptions`* gives each standalone, cooked, and package run
  `--headless --fixed-clock --no-pacing --frame-rate N`.
- `samples.walker-level`, `samples.walker-archive`, and
  `samples.walker-packaged` run their editor with `--frame-rate 60`.

## Why

The time was spent waiting, not working: the frame cost on D3D12 alone is
the same as on Vulkan, and neither the pacing wait nor the editor's frames
are what these checks test. Given up: the checks no longer exercise the
pacing wait on an offscreen surface, which no player sees anyway.

## Results

Single runs of the 240 fps standalone run (startup included):

| Windows D3D12 | Before | After |
|---|---|---|
| Alone | 6.4 s | 3.6 s |
| Editor beside, uncapped / at 60 fps | 13.2 s | 3.8 s |

The suites (release; Windows `-j8`, Linux `-j16`):

| | Before | After |
|---|---|---|
| Windows release, wall time | 129.4, 124.6 s | 110.0, 116.3, 115.7 s |
| Windows release, test time | 889–909 s | 831–841 s |
| `samples.walker-level` (Windows, in the suite) | 25.3–32.7 s | 10.5–15.1 s |
| `samples.walker-archive` | 23.6–25.8 s | 13.5–14.1 s |
| `samples.walker-packaged` | 60.0–72.6 s | 54.0–58.5 s |
| Linux release, wall time | 25 s (ADR 0110) | 25.2 s |

Linux gains nothing in wall time: its suite waits on the real-time checks
(`samples.walker`, 17 s), not on these. `samples.walker-packaged` gains
least: most of it is building the two packages.

In the first Windows run after the change, `samples.walker` (real time,
not on the fixed clock) heard one footstep late at 30 fps, as it can on a
loaded machine; it passed alone and in the four other full runs. The
unpaced runs finish sooner, so they load the machine harder while they
last; if that check fails more often, look here first.

## Consequences

- A new check that runs a game on the fixed clock should pass
  `--no-pacing` too, and an editor that waits on such runs should cap its
  frames.
- ADR 0110's "1.3 ms a frame on D3D12" was the frame beside the editor, not
  alone.
