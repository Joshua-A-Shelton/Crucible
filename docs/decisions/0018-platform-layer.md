# 0018 — Platform layer

**Status:** Accepted (2026-10-25). Amended 2026-10-26 (M2): input timestamp
accuracy.

## Context

Crucible needs windows (Win32, X11, Wayland; several for the editor; DPI,
cursors, display modes), native window handles for Slag's swapchain
(`NativeWindow`), keyboard and mouse (raw mouse for cameras), gamepads
(Xbox, DualSense, Switch Pro, Steam Deck; rumble), text input and IME,
clipboard, drag and drop, file dialogs, and input timestamps (ADR 0010).
The UI system depends on all of it.

## Options

| Option | License | Notes |
|---|---|---|
| SDL3 | zlib | Covers everything above, Wayland and IME included; large controller database; nanosecond event timestamps; Steam Input on Steam Deck is built on it |
| GLFW | zlib | Lighter; weaker or missing IME, no file dialogs |
| Our own | zlib | Wayland, IME, and controllers are each a large project |

## Decision

1. **SDL3 provides the platform layer,** used from the native C++ launcher
   and core (ADR 0011). Only its window, input, event, clipboard, and
   dialog parts are used; not its GPU API or 2D renderer (Slag renders).
   Audio is decided separately.
2. **SDL sits behind Crucible's own platform API.** Game and plugin code
   never call SDL; they see Crucible's window and input types through the
   binding generator (ADR 0017). The platform layer is a replaceable
   subsystem (ADR 0006).
3. **Input timestamps come from SDL's event timestamps.** How close they
   are to the hardware event varies by platform (OS event time vs. the time
   SDL read the event), so accuracy is measured on Windows, X11, Wayland,
   and Steam Deck.

## Why

IME is required once a game is localized or anyone types non-Latin text
into the editor, which rules out GLFW. Writing our own would spend months
on work that isn't what Crucible is about. SDL3 is zlib-licensed, matching
the license preference, and is the best-supported option on Steam Deck.

Given up: a platform layer shaped exactly to Crucible; SDL's conventions
are hidden behind Crucible's API instead.

## Amendment (2026-10-26): input timestamp accuracy

Settled with the project owner at the end of M2. Measured by injecting real
OS input at known times (`/dev/uinput` on Linux, `SendInput` on Windows), so
it crosses the whole input stack, and pairing each injection with its SDL
event, polling 8 ms late as a game polls once a frame
(`tests/native/InputTimingTests.cpp`, run by hand). Timestamp minus the
actual send time, in ms, median (p5 to p95):

| Path | Mouse (relative) | Keyboard |
|---|---|---|
| Wayland (KWin 2026-10, RADV machine) | +0.03 (+0.03 to +0.04) | -0.46 (-0.93 to +0.01), whole ms |
| Windows, window messages (SDL's default) | +0.16 (+0.06 to +0.37) | +2.1 (-5.0 to +9.0) |
| Windows, raw keyboard | +0.16 | +0.45 (+0.23 to +0.61) |
| X11 (XWayland) | about +-2 s (broken) | +8.08: when SDL processed it |

Why, from SDL 3.4.18's source:

- **Wayland:** the compositor's event times. Relative pointer motion carries
  microseconds; keyboard and absolute pointer events carry milliseconds
  unless the compositor offers `zwp_input_timestamps_v1` (KWin here
  doesn't). SDL aligns them to its clock assuming the first event arrived
  "now" and never lets one be in the future.
- **Windows:** window messages carry `GetMessageTime()`, milliseconds on the
  system tick (~15.6 ms), aligned the same way. Relative mouse mode, and the
  keyboard when SDL's raw keyboard hint is on, are read by a time-critical
  raw input thread that stamps each event when it wakes. While text input
  is active, key events come from window messages either way.
- **X11:** keyboard events are stamped with `SDL_GetTicksNS()` when SDL
  processes them (a FIXME in SDL), so up to a frame late; raw mouse motion
  passes the X server's millisecond time through as nanoseconds, unconverted
  (an SDL bug).

Decisions:

- **Raw keyboard on Windows by default:** `platform::initialize` sets
  `SDL_HINT_WINDOWS_RAW_KEYBOARD`; the `SDL_WINDOWS_RAW_KEYBOARD`
  environment variable still turns it off. Text input is unaffected.
- **Wayland stays preferred on Linux** (SDL's default). Under X11,
  `platform::eventTimestampsAreArrivals()` is false, so timing code treats a
  timestamp as "no earlier than the poll". Fixing SDL's X11 timestamps is an
  open item.
- **Gamepads and the Steam Deck are carried forward:** gamepad timestamps
  get the same check when gamepad input lands; the Deck (gamescope, often
  XWayland) is measured in M10 with the device.

For parry windows of 100 ms or more, every path but X11 is accurate enough;
the raw keyboard turns Windows' +-7 ms into well under a millisecond.
