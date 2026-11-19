# 0105 — A fixed-step clock for checks

**Status:** Accepted (2026-11-18); built. The design's names were settled
before building; those added while building were settled by the owner
as drafted (docs/naming.md).

## Context

The owner found test runs slow. Measured, a run of the whole suite took
about 400 seconds per configuration, about 340 of them in three checks:
samples.walker-level, samples.walker-archive, and samples.walker-packaged
(ADR 0097, 0099, 0102) ran the Walker's Level for 45 seconds of game time,
seven times between them, each in real time, one after another (they
share the Walker project's cache lock). Everything else finished in about
a minute beside them. The time was spent waiting on the clock, not
testing.

## Options

- **A fixed-step clock:** the clock moves on exactly one frame per frame,
  so a run takes as long as its frames; about 340 s down to about 30.
- **Side by side:** each check copies the project first, so the lock could
  go: about 150 s, real time kept.
- **Merged:** one check sharing its run from the Cache: about 225 s.

## Decision

Decided by the owner (2026-11-18): a fixed-step clock.

- The launcher's `--fixed-clock` (`LaunchOptions.fixedClock`) needs
  `--frame-rate`: the platform clock (`timestampNow`, which input events,
  the frame loop, and game code all read) stands still, and the frame loop
  moves it on exactly one `--frame-rate` period each frame
  (`platform::useFixedClock(periodNs)`, `platform::advanceFixedClock()`)
  instead of waiting for real time.
- The three Walker checks pass it on every run. What they check is
  unchanged.

## Why

A game sees time only through its clock; stepping the clock by exactly a
frame gives it the frame lengths a fast enough machine would, so it plays
the same, without waiting. Runs become exactly repeatable as well: the
Cache, JIT, and NativeAOT runs now land their kick at the same microsecond
and arrive at the same frame. Real-time behaviour (pacing against the
display, input-to-photon latency) isn't what these checks test, and has
its own tests (the frame loop's, the latency harness's).

## How it works

- The platform clock is in the core (Platform.cpp): `useFixedClock`
  starts it at the real time and fixes it; `timestampNow` returns it;
  `advanceFixedClock` adds the period. `fixedClock()` says whether it's
  fixed; 0 goes back to real time.
- The launcher fixes it after the platform starts, before anything reads
  time; the frame loop (`FrameLoop.FixedClock`) advances it after each
  frame, where it would otherwise wait out the frame's period.
- Pacing (`WaitForNextFrame`) waits for the GPU, not the clock, so it is
  unchanged.

## What it found

The three checks take about 48 seconds together (about 20, 19, and 8 each,
still one after another). On Linux the 30 fps run arrives at 34.967 s and
lands its kick 116.456 ms into the Strike window, identically from the
Cache, the archive, and both packages; the 240 fps run lands it 0.142 ms
later, inside the half millisecond allowed.

## As built

- Native: `platform::useFixedClock`, `advanceFixedClock`, `fixedClock`;
  `LaunchOptions.fixedClock`; the launcher's `--fixed-clock`, refused
  without `--frame-rate`.
- C#: `FrameLoop.FixedClock`, set by the host from the launch options;
  LevelChecks' runs pass `--fixed-clock`.
- Tests: native "A fixed clock stands still and moves on by its step"; C#
  `FixedClockTests` (a loop on it steps exactly a frame a frame without
  waiting); CTest launcher.fixed-clock-needs-frame-rate.
