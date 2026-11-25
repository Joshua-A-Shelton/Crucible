# Review 2: the platform layer

What sits between the engine and the operating system, plus the
conventions every other system follows: windows and input, timing and
latency, math and coordinates, errors, memory, and logging. Decisions:
ADRs 0003, 0010, 0018, 0024, 0030, 0031, 0032, 0076, 0103.

## What it does

The platform layer opens windows, reads the keyboard, mouse, and gamepads,
and handles the clipboard, file dialogs, and window moves and drags. It
timestamps input so latency can be measured. Alongside it are the
ground rules the rest of the engine is written to: one coordinate system,
one math library, errors as values, one allocator.

```
 game / editor C#           C# engine layer             C++ core                      OS
 ────────────────           ───────────────             ────────                      ──
 context.Events  ◄──────── FrameContext ◄── Input  ◄── InputSource (installable)  ◄── SDL3
 Platform.Api (windows,                     phase       platform:: windows,            + X11 / Wayland /
 clipboard, dialogs) ────────────────────────────────►  clipboard, dialogs, drags      Windows extras
```

## How it works

### SDL3, behind Crucible's own API (ADR 0018)

- **SDL3 does the platform work:** windows, events, input, clipboard, and
  dialogs. Not its GPU API or 2D renderer: Slag renders.
- **Nobody else calls SDL.** Games and plugins see Crucible's own types
  (`WindowHandle`, `InputEvent`, `Keycode`), generated into C#. A window is
  an ID, not a pointer, so it crosses to C# as plain data.
- **Where SDL falls short, Crucible adds its own per-OS code:** window moves
  and drags across windows for the editor's docking (`PlatformX11.cpp`,
  `PlatformWayland.cpp`, `PlatformWindows.cpp`), and a patch to SDL's
  Wayland drag handling (open items, "Recheck before every release").
- **Everything runs on the main thread**, as SDL requires for windows and
  events.

### Input (ADR 0018, 0076)

- **One event buffer per frame.** The Input phase's system fills it from
  the installed `InputSource`; systems read it as `context.Events` (key,
  mouse, gamepad, text, window events, each with a timestamp). Text typed
  comes separately (`PolledText`), as typed characters, not keys.
- **The source is replaceable:** the platform's is the default, and a
  replacement (recorded input played back, a network or test stream) can
  wrap it and keep the window's own events.
- **Timestamps come from SDL's events.** How close they are to the hardware
  varies by platform; a latency harness measures input to present.

### Latency and pacing (ADR 0003)

- **Latency is the headline metric:** input to photon, and how far what's
  shown lags the simulation.
- **Just-in-time pacing is on by default:** the frame waits as late as it
  can before reading input, so input is fresh when the frame is drawn.
- **Present mode is the player's,** Mailbox by default (shows the newest
  frame, never tears), falling back to double-buffered Fifo.
- **Measured so far** (open items): paced Mailbox on Linux reads input
  about 4 ms after it arrives and finishes presenting about 11 ms after.
  On Windows, Vulkan paces the same way, but D3D12's latency waitable under
  Mailbox doesn't hold frames to the refresh, so its frames come in bursts.

### Math and coordinates (ADR 0024)

- **Crucible's own math library,** with **storage types** (plain structs,
  byte-identical to `System.Numerics`, used in components and files) apart
  from **compute types** (SIMD registers, and wide 4 or 8 lane types for
  batches over chunks). Every SIMD path has a scalar fallback.
- **Row vectors, row-major, everywhere** (C++, C#, shaders): `p * M`, and
  composition reads in order: `world = local * parent`.
- **Left-handed, Y up, +Z forward, meters.** Importers flip right-handed
  sources (glTF, most FBX) on the way in.

### Errors, memory, and logging (ADR 0030, 0032, 0103)

- **No exceptions or RTTI in C++.** Errors are values (`Result<T>`);
  programmer mistakes are asserts; crashes go to the crash handler. **C#
  follows .NET's conventions:** unexpected failures throw, expected ones
  use the Try pattern, and per-frame calls never throw.
- **One allocator, tagged by subsystem** (`Jobs`, `Ecs`, `Render`, …), so
  memory can be counted per system; Slag's allocations go through it too.
  **Running out of memory is fatal,** so containers never check for it;
  the few big allocations that may fail use `tryAllocate`.
- **The core's own containers** (`Vector`, `SmallVector`, `HashMap`,
  pools), adapted from Slag's, and only standard headers that don't
  allocate.
- **Logging** goes through one log with levels; the Shipping configuration
  compiles information messages out (ADR 0103).

## Why it was built this way

- **SDL3** is the most portable and battle-tested window and input layer,
  zlib licensed, and supports Steam Deck and both Linux display servers.
  Hiding it keeps it replaceable.
- **One coordinate system** avoids the bugs that come from converting
  between conventions; importers convert once, on the way in.
- **Fatal out-of-memory** makes every container call simpler; for a game,
  running out of memory isn't recoverable in practice anyway.
- **Latency first** comes from the action RPG's feel: tight parry windows
  need input that's fresh when the frame is shown.

## What I'd question

1. **Games have no input layer above raw events.** A game reads the frame's
   event list and keeps its own state (is jump held, was it just pressed),
   with no actions ("Jump" bound to Space and the A button), no rebinding,
   and no dead zones or axis smoothing for sticks. The first game will need
   all of it, and a shared one would keep it consistent between keyboard
   and gamepad.
2. **The hardware baseline is untested for input.** The first game targets
   Steam Deck (ADR 0010), but gamepad timestamp accuracy and the Deck's
   input timestamps (gamescope, often XWayland) haven't been measured
   (open items).
3. **Pacing on D3D12 isn't settled.** Under Mailbox, D3D12's latency
   waitable doesn't hold frames to the refresh, and whether pacing stays on
   by default under Mailbox is still an open decision. Windows players
   using D3D12 get different frame timing from Vulkan.
4. **Per-OS window code is growing.** Window drags and moves for the
   editor needed X11, Wayland, and Windows code beside SDL, and a patch to
   SDL itself. It works, but each SDL update needs the patch checked, and
   it's code only the editor uses that lives in the core.
5. **Text input and keys are separate streams.** Correct (typed text isn't
   keys), but a game's text field needs both, and the UI library handles
   that join today; a game not using the UI library would redo it.
