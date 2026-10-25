# 0018 — Platform layer

**Status:** Accepted (2026-10-25).

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
