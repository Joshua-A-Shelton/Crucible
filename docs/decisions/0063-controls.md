# 0063 — Controls

**Status:** Accepted (2026-11-03); built (7a and 7b). Names settled by
the owner (docs/naming.md): `Label`, `Button`, `Toggle`, `Slider`,
`Image`, `ScrollView`, `ListBox`, `Spinner` (first named `Selector`,
which clashed with the style's `Selector`), `ComboBox`, `Popup`, and the
parts `SliderTrack`, `SliderThumb`, `ToggleBox`, `ToggleMark`,
`ScrollBar`, `ScrollThumb`, `ListItem`, `SpinnerArrow`, `ComboBoxArrow`,
and the rest as drafted.

## Context

M6 step 7 builds the controls a menu needs on ADR 0060's tree, ADR
0061's styles, and ADR 0062's input: label, button, toggle, slider,
list or selector, image, scroll view. Built in two parts at the owner's
choice: 7a the controls below, 7b the popup layer, the virtualized
`ListBox`, `Spinner`, and `ComboBox`.

## Options

**Styling parts** (a slider's thumb, a toggle's box): parts as typed
child elements; or styled properties per part.

**List-like controls:** a `ListBox` and a menu cycler (built as `Spinner`);
the `ListBox` alone; or also a dropdown (`ComboBox`), which needs popups.

**A default theme:** a plain one built in; or none.

**Long lists:** virtualized now; or an element per item.

## Decision

1. **Parts are typed child elements,** styled by type like any element
   (`SliderThumb:hover`), with their own hover and pressed states. A
   control and its parts have the class `on` (and the like) for states
   the selector set lacks.
2. **`ListBox`, `Spinner` (the cycler), and `ComboBox`,** the dropdown's list in a
   **popup layer** on the root: drawn and hit-tested above the content,
   placed by an anchor element (below it, flipping above when there's no
   room), a focus scope, closed by a click outside or Cancel, giving focus
   back.
3. **A plain default theme** (`Theme.CreateDefault`), which a root uses
   until given another.
4. **`ListBox` is virtualized with measured rows:** elements exist only
   for visible rows, recycled while scrolling; rows are measured as they
   come into view, others estimated.

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

## Details (7b)

- **The popup layer:** `UIRoot.Popups`, laid out after the content (each
  measured against the root, placed by `Popup.Place`: below its
  `Anchor`, flipping above when it doesn't fit below but does above, at
  least as wide as the anchor, kept on the root), drawn after it, and hit
  tested first, newest on top. `Popup.Open` / `Close` / `Closed`;
  opening lays it out at once and focuses its first focusable element;
  closing gives focus back to what had it and closes popups opened after
  it. A press outside the newest popup closes it and is used up
  (`HandledPointer`); Cancel inside closes it.
- **`ListBox`:**
  - `ItemCount`, `CreateItem`, `BindItem`, `EstimatedItemHeight`,
    `SelectedIndex`, `SelectionChanged`, `ItemActivated`,
    `ItemsChanged`, `ScrollIntoView`, `FocusItem`, `RealizedCount`;
  - a `ScrollView` of an internal row panel; rows (`ListItem`s, wrapping
    what `CreateItem` made) exist for the items in view and one each
    side, are pooled when they scroll out (except the focused one), and
    are bound again only when they show a different item;
  - each row is measured as it's realized; unmeasured items count as the
    average measured height, so the extent settles as rows are seen;
    when a row above the view turns out a different height, the offset
    moves with it so the view stays still;
  - rows are added during the panel's measure without invalidating it,
    and styled at once (`StyleNow`), so they're measured with their
    style; the root runs up to three layout passes in an update, so a
    list learning its viewport settles in the same frame;
  - Up and Down move focus row by row, scrolling and realizing as needed;
    past either end they're left unhandled, so focus leaves the list;
  - positions are prefix sums over the heights, linear in the item count:
    fine for thousands of items; a tree of sums would serve millions.
- **`Spinner`:** `Options`, `SelectedIndex`, `SelectedOption`, `Wraps`,
  `SelectionChanged`, `Step`; two `SpinnerArrow`s (classes `previous`,
  `next`) around a label; left and right (keys, D-pad, stick) step it.
- **`ComboBox`:** a button showing the selected option and a
  `ComboBoxArrow`; activated, it opens a `Popup` holding a `ListBox`
  (class `dropdown`) of its `Options`, focused on the selection;
  activating an option selects it and closes the list, Cancel or a press
  outside closes it unchanged.
- **The arrows** are custom-drawn chevrons in their `Foreground`.
- **The default theme** styles the list, its rows (hover, `selected`,
  focus), popups, spinners and their arrows, and combo boxes; dropdown
  lists are at most 240 units high.

## As built (7b)

- C#: `Crucible.UI`'s `Popup`, `ListBox`, `ListItem`, `Spinner`,
  `SpinnerArrow`, `ComboBox`, `ComboBoxArrow`; `UIRoot.Popups`; the
  scroll view's internal hooks for virtualizing.
- Tests:
  - C# (`ControlsTests.cs`, JIT and NativeAOT): a 1000-item list making
    fewer than 20 rows and reusing them to the end, its extent settling
    as rows are measured; row-by-row navigation that scrolls, selection
    on activate, and leaving past the top; popups placed below an anchor
    or flipped above, focused inside, closed by Cancel or a press outside
    with focus given back; spinners stepping and wrapping; combo boxes
    picking from their list by pointer and keys, and closing unchanged;
  - `launcher.jit.controls`: a spinner, a dropdown opened by a synthetic
    click (placed below its box, at its width), and a 500-item list,
    added to the check and the capture.
- Found by the capture: a list measured as wide as its space, so a
  dropdown's popup filled the window; it now measures as wide as its
  widest row in view.
