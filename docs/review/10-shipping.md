# Review 10: shipping

How a project becomes something a player runs: running standalone,
cooking into an archive, loading from it, the JIT and NativeAOT packages,
the Shipping configuration and logging, and the platforms they're for.
Decisions: ADRs 0008, 0031, 0073, 0098, 0099, 0100, 0101, 0102, 0103,
0110, 0123.

## What it does

Build (the menu, or the editor's `--build` headless) builds the game's
code, cooks the assets its scenes and include list reach into one archive,
and packages it for the platform the editor runs on: a folder with an
executable named after the game that needs nothing installed.

```
 Project ── Build Settings (scenes, includes, runtime, configuration)
   │
   ├─ code:   dotnet publish (JIT, ReadyToRun)  or  NativeAOT (one executable,
   │          the C++ module linked in)
   ├─ assets: references from the scenes and includes ─► Game.crpak
   │          (sorted index, 4 KiB aligned, zstd 19)
   └─ engine: Shipping (information messages compiled out) or Development
        ▼
 Build/<Platform>/JIT | AOT [-Development]/
   Walker(.exe), Data/ (Walker.dll, Game.crpak, .NET runtime, …)
        ▼ player runs it ─► assets loaded on jobs ─► log file in their data folder
```

## How it works

### Running and cooking (ADR 0073, 0098)

- **Run Standalone** starts the game launcher on the project in place (its
  last good build and imported files) with the open scene as it is, its
  output in the Log.
- **Cooking follows references:** importers record the asset IDs their
  output names; from the Build settings' scenes (the first starts the
  game) and include list (what code loads by ID), everything reached is
  packed. Assets nothing reaches stay out.
- **One archive, Crucible's own format:** an index sorted by ID for binary
  search, entries on 4 KiB boundaries, recompressed small. The header has
  room for archives that aren't a game's own (patches, DLC, mods).

### Loading (ADR 0099)

- **The archive is the asset source** (thread-safe), so meshes, textures,
  sounds, shapes, and clips load on jobs and appear when ready; an
  instance draws once all its parts are.
- **`Scene.LoadAsync`** acquires a scene's listed assets, then adds its
  entities in one frame; replacing a scene is the game's to do.

### Packages (ADR 0100, 0101, 0103)

- **JIT** (the default): the game's assembly and the engine's beside a
  self-contained .NET runtime, ReadyToRun-compiled, so mods can load
  code. **NativeAOT:** one executable holding launcher, core, engine, and
  game (its C++ module linked in), only the archive beside it; no mods.
- **The executable finds its game by its own name,** no launch file.
- **Shipping or Development:** Shipping compiles information messages out
  of the engine (from the `shipping` preset's build); Development keeps
  them. All four packages can sit side by side.
- **Logs:** C++ and C# log into one stream, `Console` output caught into
  it, written to the player's data folder (the last run's kept); a clean
  Shipping run writes no file. Debug information stays out of the package,
  kept for reading crash reports.
- **Checked end to end:** `samples.walker-packaged` builds both packages
  and plays the Walker's level in each, comparing hit times and arrival
  against the editor's run.

### Platforms (ADR 0008, 0031)

- **Windows and Linux on x64,** the editor at parity on both; **Steam Deck
  an explicit target;** consoles possible, never named.
- **Pinned toolchains:** the .NET SDK exactly (`global.json`), the static
  C runtime on Windows; .NET's runtime packages downloaded on the first
  build.

## Why it was built this way

- **References, not folders,** so a package holds what the game uses and
  nothing the project happens to contain.
- **Own archive format:** one file, binary-searched, read on jobs, and
  layerable later; nothing a general zip offers was needed.
- **JIT by default, NativeAOT by choice:** JIT keeps mods and reflection
  possible; NativeAOT gives one executable and the fastest start where
  mods aren't wanted.
- **Checked by playing:** packaging bugs show up as a game that runs
  differently, so the check plays the same level three ways and compares.

## What I'd question

1. **The Steam Deck target is untested, and Slag may rule it out.** Slag
   requires Vulkan 1.4 with `VK_EXT_descriptor_heap`, on RADV only from
   Mesa 26.2. Whether SteamOS ships that Mesa (and when) isn't known, and
   the Deck has no D3D12 to fall back to. Every performance budget on
   page 4 is measured against a device that may not run the engine yet;
   worth checking SteamOS's Mesa before the first game commits to it.
2. **Packages build only for the editor's own platform.** A Windows
   package needs the editor on Windows, and a Linux one on Linux. That's
   the sync-and-build routine done by hand today; releasing for both
   needs either cross-building or a documented second-machine (or CI)
   step.
3. **No crash reporting.** Debug information is kept for reading crash
   reports, but nothing writes one: no crash handler, no minidump on
   Windows or core summary on Linux, nothing in the log file about a
   crash. A player's crash leaves a log that just stops.
4. **No store integration.** Steamworks (achievements, cloud saves,
   overlay, Steam Input, the Deck's on-screen keyboard) isn't there, and
   it would touch the platform layer, save games (page 3), and input
   (page 2). Probably a plugin, but its shape affects those systems.
5. **One archive per game, no patches.** The format has
   room for patches and DLC, but there's no layering yet, no patch built
   from what changed, and a game update is the whole archive again. This
   ties into the mods finding (page 3), which wants the same layering.
