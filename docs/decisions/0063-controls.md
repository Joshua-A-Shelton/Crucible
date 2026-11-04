# 0063 — Controls

**Status:** Accepted (2026-11-03); part 7a built, part 7b to come. Names
settled by the owner (docs/naming.md): `Label`, `Button`, `Toggle`,
`Slider`, `Image`, `ScrollView`, `ListBox`, `Selector`, `ComboBox`,
`Popup`, and the parts `SliderTrack`, `SliderThumb`, `ToggleBox`,
`ToggleMark`, `ScrollBar`, `ScrollThumb`, `ListItem`, `SelectorArrow`,
`ComboBoxArrow`; the rest are placeholders.

## Context

M6 step 7 builds the controls a menu needs on ADR 0060's tree, ADR
0061's styles, and ADR 0062's input: label, button, toggle, slider,
list or selector, image, scroll view. Built in two parts at the owner's
choice: 7a the controls below, 7b the popup layer, the virtualized
`ListBox`, `Selector`, and `ComboBox`.

## Options

**Styling parts** (a slider's thumb, a toggle's box): parts as typed
child elements; or styled properties per part.

**List-like controls:** a `ListBox` and a `Selector` (a menu cycler);
the `ListBox` alone; or also a dropdown (`ComboBox`), which needs popups.

**A default theme:** a plain one built in; or none.

**Long lists:** virtualized now; or an element per item.

## Decision

1. **Parts are typed child elements,** styled by type like any element
   (`SliderThumb:hover`), with their own hover and pressed states. A
   control and its parts have the class `on` (and the like) for states
   the selector set lacks.
2. **`ListBox`, `Selector`, and `ComboBox`,** the dropdown's list in a
   **popup layer** on the root: drawn and hit-tested above the content,
   placed by an anchor element (below it, flipping above when there's no
   room), a focus scope, closed by a click outside or Cancel, giving focus
   back. (7b.)
3. **A plain default theme** (`Theme.CreateDefault`), which a root uses
   until given another.
4. **`ListBox` is virtualized with measured rows:** elements exist only
   for visible rows, recycled while scrolling; rows are measured as they
   come into view, others estimated. (7b.)

## Details (7a)

- **`Label`:**
  - shapes its `Text` with its `Font` (a font asset, acquired through the
    asset reader and released when it changes or the label leaves the
    tree) or the root's `DefaultFont`, at its `FontSize` and the root's
    scale;
  - draws in its `Foreground`;
  - `Wrap` wraps to the width it's given;
  - `TextRendering` (styled: `Exact` or `DistanceField`);
  - the layout is kept until the text, style, scale, or wrap width
    changes;
  - a missing font draws nothing and is reported once.
- **`ButtonBase`** (focusable, one `Content` element or `Text`, which
  makes a centered label, and `Clicked` on activate) under **`Button`**
  and **`Toggle`**, so a theme's `Button` styles don't reach toggles.
- **`Toggle`:** a `ToggleBox` holding a `ToggleMark`, then its content;
  `IsOn`, `Changed`; the toggle, box, and mark have the class `on` while
  on, so a checkbox or a switch is a matter of style.
- **`Slider`:** `Minimum`, `Maximum`, `Value` (clamped, snapped to `Step`
  above 0), `ValueChanged`; a `SliderTrack` and a `SliderThumb`.
  Pressing or dragging sets the value, the thumb's center under the
  pointer; focused, left and right move it a step (a tenth of the range
  without one) and are handled there, so up and down still navigate.
- **`Image`:** a texture (`TextureSize`, new natively, gives its size) by
  `Stretch` (`None`, `Fill`, `Uniform`, `UniformToFill`, cropped to its
  bounds), with `Tint` and the background's corner radii; with
  `SliceInsets` (and optional `SourceSlice` in texels) it's a nine-slice.
- **`ScrollView`:**
  - one `Content`, vertical by default (`ScrollsHorizontally`,
    `ScrollsVertically`), clipped;
  - `Offset` is clamped to `Extent` minus `Viewport`;
  - the wheel scrolls `WheelStep` (48 units) a notch, and Shift turns a
    vertical wheel horizontal;
  - `ScrollBar`s (classes `vertical`, `horizontal`) show only while
    there's more to see; dragging a `ScrollThumb` scrolls, and a press
    beside it pages;
  - focus moving to an element inside scrolls it into view
    (`ScrollIntoView`), which gamepad menus need.
- **Elements** gained `OnAttached` / `OnDetached`, for what they hold
  for drawing.
- **The default theme:** dark neutral grays with a blue accent; buttons
  with hover, pressed, focus-outline, and disabled styles and quick
  ease-out transitions; a checkbox-style toggle whose mark fades and
  grows in; a slider with a thumb that grows on hover; thin rounded
  scrollbars. Games add styles after it (later styles win ties) or set
  their own theme.
- **No font ships with the engine:** a game sets `UIRoot.DefaultFont` or
  styles `Font`. Shipping Noto Sans (OFL) as a default is a separate
  decision.

## Why

Typed parts reuse styling as it is: no new selector features, and each
part responds to the pointer on its own. Classes stand in for the states
the selector set lacks (`on`), keeping selectors small. A built-in theme
makes controls usable on day one, and doubles as an example for game
themes.

## As built (7a)

- C#: `Crucible.UI`'s `Label`, `ButtonBase`, `Button`, `Toggle`,
  `ToggleBox`, `ToggleMark`, `Slider`, `SliderTrack`, `SliderThumb`,
  `Image`, `Stretch`, `ScrollView`, `ScrollBar`, `ScrollThumb`,
  `Theme.CreateDefault`; `UIRoot.DefaultFont`. Native:
  `render::textureSize`.
- Tests:
  - C# (`ControlsTests.cs`, JIT and NativeAOT, with Noto Sans and the
    default theme): labels measuring and wrapping, buttons clicked by
    pointer and Enter, toggles flipping and marking their parts, sliders
    snapping, dragging and taking left and right, scroll views scrolled
    by wheel, thumb, and focus, image fitting;
  - `launcher.jit.controls`: the controls drawn at 1.25x with synthetic
    hover, click and wheel, checked at pixels once transitions have run,
    saved as `controls.png`.
