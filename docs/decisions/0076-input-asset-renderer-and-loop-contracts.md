# 0076 — Input, asset, renderer, and loop contracts

**Status:** Accepted (2026-11-09); M7 step 7d built. Names settled by the
owner as drafted; those added while building are placeholders
(docs/naming.md).

## Context

ADR 0075 defined the physics and audio contracts in ADR 0006's shape
(components, systems under fixed names, a service, in C++ bound to C#). The
owner split the rest into step 7d: input, runtime assets, the renderer, and
the loop driver. Unlike physics and audio, these existed, but each was
replaceable only partly or not at all: input only by swapping the C# input
system; assets wired three ways (the renderer's reader, prefabs' read
function, scene files by path); the renderer already in that shape but
undocumented; and the loop driver ADR 0013 promised replaceable was
constructed directly by the host.

## Decision

Decided by the owner (2026-11-09):

1. **Input: a C++ event source.** `InputSource` (`poll`, `text`) fills the
   frame's event buffer; `pollEvents` and `polledText` read through the
   installed one (`installInputSource`, `inputSource`). The platform's
   (SDL) is the default and stays available (`platformInputSource`), so a
   replacement (recorded input played back, a network or test stream) can
   wrap it and keep the window's own events. The Input-phase system and
   everything reading the buffer stay.
2. **Runtime assets: one source.** `AssetSource` (`read`: an asset's bytes
   by ID) in crucible/AssetSource.h, which `AssetId` moved to; the renderer
   reads through it, and so do prefabs (`PrefabLibrary.FromAssetSource()`).
   C# installs one with `AssetSource.Install`, which replaces
   `RenderAssets.SetReader`, and reads through whatever is installed with
   `AssetSource.Read`, so a native module's source serves C# too. The
   editor installs its asset database; a game run from a project, its
   imported files with `AssetSource.InstallDefault`, which leaves a native
   module's source (pack files, say) in place.
3. **The renderer: written down.** It already is components (MeshInstance,
   Camera, the lights, Sky, PostProcess), steps by name in a surface's
   render schedule (ADR 0048), and a service (RenderAssets). The default
   steps' names became constants (`render::steps::…`, C# `RenderSteps.…`).
4. **The loop driver: C#.** `ILoopDriver` (`Schedule`, `Run`); `FrameLoop`
   is the engine's; a game names its own with
   `[assembly: LoopDriver(typeof(...))]`, a class with a public constructor
   taking the `Schedule`, and the host runs it instead. C#, because the
   frame is C#'s (ADR 0011) and the schedule's systems are C# objects; a
   native driver would cross the boundary for every system.

## Why

Each seam becomes one thing to replace, installed once, in the language its
replacements would be written in. A single asset source closes the gap
where a pack-file source would have had to be plugged into three places;
an input source makes recorded input (for tests and bug reports) possible
without touching the systems that read input.

## Details

- **Defaults and order:** like the physics systems (ADR 0075), a default
  never displaces a replacement: the platform's input source is installed
  until something replaces it; a game's asset source is installed only if
  none is (`assetSourceInstalled`).
- **Reading assets from C#** goes through the native source (`assetSize`,
  then `copyAsset`, which reuses the bytes the size call read), so it
  doesn't matter which language installed the source.
- **The editor's play mode** runs the game's systems in the editor's own
  loop, not a game's driver.
- **NativeAOT:** a game's driver is named by an attribute on the game
  assembly, which the JIT host reads; an AOT build compiles its game in,
  and naming its driver there comes with packaging (ADR 0021).

## As built

- **Core:** `InputSource`, `installInputSource`, `inputSource`,
  `platformInputSource` (crucible/Platform.h); crucible/AssetSource.h and
  AssetSource.cpp (`AssetId`, `AssetSource`, `AssetDelivery`,
  `installAssetSource`, `assetSource`, `assetSourceInstalled`,
  `setAssetReader` moved here, `assetSize`, `copyAsset`); RenderAssets
  reads through the source; `render::steps` in crucible/RenderSchedule.h,
  used by the default and editor steps.
- **Engine:** `AssetSource` (`Install`, `InstallDefault`, `IsInstalled`,
  `Read`), `PrefabLibrary.FromAssetSource`; `RenderSteps`; `ILoopDriver`,
  `LoopDriverAttribute`, `FrameLoop : ILoopDriver`,
  `LoadedGame.CreateLoopDriver`; the host runs the game's driver if it
  names one; `ProjectStartup` installs its files as the default source and
  reads prefabs through it.
- **Callers:** the editor, the Diorama and UI showcase samples, and the
  test games install with `AssetSource.Install`.
- **Tests:**
  - **`InputSourceTests`** (native): polling reads an installed source's
    events and text, and the platform's is installed by default and again
    after.
  - **`AssetSourceTests`** (C#): reads go through an installed source,
    through the native side; a default doesn't displace it; a default is
    installed when nothing else is; prefabs read through it.
  - **`LoopDriverTests`** (C#): `FrameLoop` is a driver; a game naming a
    driver gets it for its schedule, and it runs the frames.
