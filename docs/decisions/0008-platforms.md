# 0008 — Platforms

**Status:** Accepted (2026-10-24).

## Context

Slag's platforms (Slag CLAUDE.md §3): Windows 10/11 (D3D12, Vulkan) and
Linux (Vulkan); x64 supported, ARM64 must compile but isn't supported in
v1; consoles possible through private, out-of-tree backends. .NET's CoreCLR
and NativeAOT support Windows and Linux on x64 and ARM64, so .NET adds no
limits beyond Slag's. Consoles generally forbid JIT, so console builds
would be AOT-only, which ADR 0001's per-game choice already allows.

## Decision

- **Game platforms match Slag's:** Windows and Linux on x64. Consoles stay
  possible and follow Slag's rules: never referenced by name in public
  code.
- **Steam Deck is an explicit target.** It can't be tested yet (no device
  available); see below.
- **The editor runs on Windows and Linux, with 1:1 feature parity.** This
  is a hard requirement: no editor feature exists on one and not the other.
  Parity covers Crucible's own features; integrations with external tools
  that only exist on one platform (e.g. PIX, Windows and D3D12 only) may
  exist where the tool does.
- **ARM64 matches Slag:** must compile, not supported in v1.

## Why

Steam Deck is a fixed, widely owned piece of hardware, which makes it a
concrete device to measure the speed and latency goals against. Editor
parity from the start is far cheaper than porting later; it rules out
platform-specific editor technology (e.g. Windows-only UI frameworks).

## Steam Deck notes

- Its GPU is RDNA2. Slag has run on RADV with an RDNA2 iGPU (Raphael), so
  `VK_EXT_descriptor_heap` support on the architecture looks fine.
- Slag needs Mesa 26.2+ (RADV). Whether, and when, SteamOS ships that
  version needs checking.
- It also sets a hardware floor for the speed goal (open item: hardware
  baseline).
