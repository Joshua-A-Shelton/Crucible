# 0062 — UI input

**Status:** Accepted (2026-11-03); built. Names settled by the owner
(docs/naming.md).

## Context

ADR 0020 settles the UI's input model: events route from the element
under the pointer up through its parents; keyboard focus and tab order;
gamepad directional navigation that moves focus spatially, with explicit
per-element overrides; focus scopes that trap focus in modal dialogs and
menus. Platform input comes from SDL3 (ADR 0018) as flat `InputEvent`s
polled each frame. M6 step 6 builds it; ADR 0060's tree and hit testing,
and ADR 0061's `:hover`, `:pressed` and `:focus` states, are what it
drives.

## Options

**Who feeds roots:** an engine system in the Input phase; or the game,
from a system of its own.

**Handlers:** virtual `On…` methods plus C# events; virtual methods only;
C# events only.

**Arrows and the D-pad:** given to the focused element first, navigating
when unhandled; or always navigating first.

**Gamepad mapping:** defaults that a root can remap; or fixed defaults.

## Decision

1. **`UIInputSystem`,** in the Input phase after the events are read,
   gives every root attached to a window (`UIRoot.Window`) that window's
   events, and gamepads' always. Roots report `HandledPointer` and
   `HandledKeyboard`, so a game ignores input the UI used.
2. **Elements override `On…` methods** (`OnPointerDown`, `OnKeyDown`,
   `OnNavigate`, `OnActivate`, `OnCancel`, `OnGotFocus`, …); game code
   subscribes to C# events (`PointerDown`, `Activated`, …), raised after
   the override. Events bubble up through enabled parents until one sets
   `Handled`.
3. **Unhandled keys navigate:** key and gamepad input goes to the focused
   element first (a slider or list keeps its arrows); unhandled, it
   becomes a `NavigationEvent` on the focused element, and unhandled
   again, focus moves.
4. **Gamepads map through `UIInputMap`:** the D-pad and left stick
   navigate (with repeat), South activates, East cancels, the shoulders
   move like Shift+Tab and Tab; a root can remap buttons and tune the
   stick's threshold and repeat.

## Details

- **Platform:** new events `MouseWheel`, `GamepadAdded` / `Removed`,
  `GamepadButtonDown` / `Up`, `GamepadAxis`, `DisplayScaleChanged`; key
  and wheel events carry `modifiers` (`KeyModifiers`); `GamepadButton`
  and `GamepadAxis` are numbered as SDL's; `windowDisplayScale`. Gamepads
  are opened when they arrive (SDL sends only open pads' events);
  without gamepad support the platform starts anyway.
- **DPI:** a root attached to a window takes the window's display scale
  and follows `DisplayScaleChanged`. Pointer positions are logical units
  (physical pixels divided by the scale).
- **Pointer:**
  - `Hover` is the element under the pointer and all its ancestors, as
    CSS's `:hover`; enter and leave events don't bubble;
  - a press captures the pointer to the pressed element (and sets
    `Pressed`) until that button's release, so a button gets its release
    when the pointer slides off; a left release over the pressed element
    is a click, which raises `Activate`;
  - a press focuses the nearest focusable element under it, or clears
    focus;
  - disabled elements (`UIState.Disabled`, on them or an ancestor) pass
    hits to their parent and don't receive events.
- **Focus:** `IsFocusable`, `Focus()`, `UIRoot.FocusedElement`,
  `ClearFocus`; the focused element has `UIState.Focus`. An element
  leaving the tree loses focus, hover, capture, and pressed state.
- **Tab order:** focusable, shown, enabled elements in the focus's scope,
  by `TabIndex` then tree order, wrapping. The scope is the innermost
  ancestor with `IsFocusScope`, or the whole tree; a nested scope is one
  stop (focus it to go inside).
- **Directional navigation:** an explicit neighbor (`NavigateUp` …)
  first; else the candidate ahead in that direction with the lowest
  score, the gap along the direction plus twice the sideways offset of
  their centers (zero where they overlap sideways), so the element
  straight ahead beats a nearer one off to the side. Bounds are layout
  bounds (render transforms ignored).
- **Actions:** Enter, keypad Enter, and Space activate the focused
  element (pressed while held); Escape cancels, bubbling from the focused
  element (or the root's content). Gamepad actions do the same.
- **Repeat:** keyboard repeat comes from the OS; a held D-pad direction
  or stick repeats after `RepeatDelay` (0.4 s) every `RepeatInterval`
  (0.1 s), on the root's real-time clock. The stick navigates past
  `StickThreshold` (0.5) and lets go below 80% of it, so it doesn't
  flicker at the edge.
- **Order with game systems:** `UIInputSystem` runs after `InputSystem`
  by insertion (games may replace `InputSystem`); game systems in later
  phases see what the UI used, and ones in Input order themselves after
  `UIInputSystem`.
- **Events reuse one object per kind** per root, so routing allocates
  nothing; handlers must not keep them.
- **Hit testing uses the last layout.** The UI step lays out every
  frame; a game that changes the tree and then feeds input in the same
  frame calls `UpdateLayout` first.

## Why

An engine system means a UI works with no wiring, in the order games
need. `On…` overrides are what controls are written with; C# events let
game code react to a click without a subclass. Giving the focused element
the first look at arrows is what lets lists, sliders and text fields use
them, while plain menus navigate for free. Remappable gamepad buttons
cover regions where confirm is on East.

Given up: text input and IME (M7's text editing); touch and pen; several
pointers; drag and drop; per-gamepad focus (every pad drives the one
focus); and navigation that follows render transforms.

## As built (M6 step 6)

- Native: `crucible/Platform.h`'s new events, `KeyModifiers`,
  `GamepadButton`, `GamepadAxis`, `InputEvent::modifiers`,
  `windowDisplayScale`; `Platform.cpp` opens gamepads.
- C#: `Crucible.UI`'s `UIEvent`, `PointerEvent`, `PointerButton`,
  `KeyEvent`, `NavigationEvent`, `NavigationDirection`, `UIAction`,
  `UIInputMap`, `UIInputSystem` (in the default schedule); `UIElement`'s
  input members (`UIElement.Input.cs`); `UIRoot`'s (`UIRoot.Input.cs`).
- Tests:
  - native: wheels, modifiers, gamepad buttons and axes, and display
    scales from synthetic SDL events;
  - C# (`UIInputTests.cs`, JIT and NativeAOT): bubbling and handling,
    capture and clicks, hover over ancestors and disabled elements,
    focus by click, Tab order with `TabIndex` and wrapping, focus scopes,
    spatial and explicit navigation, a control keeping its arrows,
    Enter, Space, Escape and gamepad actions with remapping, the stick's
    threshold, margin and repeat, removed elements losing focus, logical
    pointer positions;
  - the default schedule's systems, and that frames still allocate
    nothing.
