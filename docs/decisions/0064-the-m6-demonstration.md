# 0064 — The M6 demonstration

**Status:** Accepted (2026-11-03); built. Names settled by the owner
(docs/naming.md): `samples/UiShowcase` (`Crucible.Samples.UiShowcase`),
CTest `samples.ui`, and the options `project`, `input`, `capture`,
`compare`; the rest are placeholders.

## Context

M6 is done when a sample menu is navigable by mouse, keyboard, and
gamepad, with crisp text at several DPI scales. M5's demonstration (ADR
0056) was a sample game the launcher runs headless, checked by a CTest
script, with its captures compared across D3D12 and Vulkan.

## Options

**Where:** a sample of its own; or a pause menu over the Diorama.

**Crispness:** shift invariance (text placed between pixels must come out
identical, shifted by whole pixels); each glyph compared with its bitmap
in the glyph cache; or both.

**DPI scales:** 1, 1.25, 1.5, 2; 1, 1.5, 2; or 1 to 2 by 0.25.

**Across backends:** compare D3D12's captures with Vulkan's, as M5 did;
or let each check itself.

## Decision

1. **`samples/UiShowcase`,** a sample game with its own project (Noto
   Sans and a `.crstyle` theme, imported through the importers), run
   headless by CTest as `samples.ui` (`run_ui.cmake`).
2. **Shift invariance** checks crisp text, with unblended panel edges and
   text height following the scale.
3. **At 1, 1.25, 1.5, and 2.**
4. **On Windows, D3D12's captures are compared with Vulkan's** within ADR
   0056's tolerance.

## Details

- **The menu:** a main screen (title, Play, Options, Quit), an options
  screen (a toggle, a slider, a spinner, a dropdown in a grid of
  captions; Back; Cancel goes back too), and a quit dialog in a popup
  with No focused first. Screens fade in. The theme file extends the
  default theme: the font for every label, a backdrop, panels, a title.
- **Three journeys,** one per device (`input`), each from a fresh menu:
  Play (mouse only), Options, subtitles on, volume up, difficulty to
  Hard, quality to High through the dropdown, back, Quit asked and
  refused. After every step it checks the focused element, the controls'
  values, the screen, and the dialog. Mouse: hover, clicks, a press on
  the slider's track, the spinner's arrow, the dropdown's row. Keyboard:
  Down, Enter, Space, Right, Escape. Gamepad: the D-pad, the stick (past
  its threshold and back, twice), South, East. Input is fed to the root
  as synthetic platform events in physical pixels.
- **At each scale:** the main screen and three probe labels ("Crisp
  0123") drawn moved by render offsets of 0, 0.3, and 0.7 units (across
  and down). A render offset isn't snapped by layout, so only the text's
  own placement keeps the probes alike: each one's pixels, from where its
  origin rounds to, must equal the first's exactly. Then a panel's edge
  must be backdrop outside and border inside, unblended, and the probe
  text's height in pixels must follow the scale (within 15%). The main
  and options screens are captured (`scale-<s>.png`, `options-<s>.png`)
  and, given `compare`, compared with the other backend's.
- **Checked that the checks bite:** with text placement unrounded and
  glyphs sampled with filtering, the probes differ at every scale (480
  to 1550 pixels); with layout snapping off, the panel-edge check fails.
  A first version compared probes placed by layout, which snaps each one
  to whole pixels before the text is drawn; it stayed green with blurred
  text, so the probes now move by render offsets. Point-sampled exact
  glyphs land on whole pixels even unrounded: what the probes guard is
  text drawn blurred between pixels.
- **Found by the captures:** a grid's star-column cell in an auto row
  didn't grow its row (the controls' text overflowed them); `Grid` now
  grows auto tracks from star-track children and shares the rest again
  (ADR 0060's grid, tested).
- **The by-hand check** (a real window, its own DPI scale, real input):
  `input=none` shows the menu in the launcher's window with no script,
  the root attached to the window (so `UIInputSystem` feeds it and it
  follows the window's display scale); Quit, then Yes, leaves. With the
  debug preset built:

  ```bash
  cd /path/to/Crucible && build/debug/src/native/launcher/crucible_launcher --managed-dir build/debug/artifacts/bin/Crucible.Engine/debug --game build/debug/artifacts/bin/Crucible.Samples.UiShowcase/debug/Crucible.Samples.UiShowcase.dll --game-arg project=samples/UiShowcase/Project/UiShowcase.crproject --game-arg input=none
  ```

  (On Windows, the same paths with backslashes and `.exe`.)

## Why

A sample of its own keeps the captures about the UI alone, easy to check
exactly. Shift invariance tests what crispness means (text between
pixels doesn't blur) without tying the test to the shader's coverage
curve. The four scales are the common ones, with the two where snapping
matters most. Comparing backends caught nothing here, but costs one more
run and guards the UI shader on both.

## As built

- `samples/UiShowcase`: `Menu.cs`, `ShowcaseSystems.cs` (setup, the
  journeys, crispness, captures, comparison), `ShowcaseSettings.cs`,
  `Dependencies.cs`, `run_ui.cmake`; `Project/` with Noto Sans (OFL,
  with its license), its sidecar, and `UI/Showcase.crstyle`.
- `UIElement.Root` is public (games open popups in an element's root).
- Results: all 58 checks pass on Linux (Vulkan, RX 7900 XT) and on
  Windows (Vulkan and D3D12, RTX 2070 SUPER, 82 checks with the
  comparison); D3D12's eight captures equal Vulkan's exactly (largest
  difference 0/255).
