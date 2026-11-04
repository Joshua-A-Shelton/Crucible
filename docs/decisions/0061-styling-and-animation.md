# 0061 — Styling and animation

**Status:** Accepted (2026-11-03); built. Names settled by the owner
(docs/naming.md).

## Context

ADR 0020 settles styling's shape: themes made of style classes with a
small selector set (element type, class, state: hover, focus, pressed,
disabled) setting properties such as colors, borders, padding, fonts, and
transitions, with no full CSS cascade; animation in v1 is transitions and
tweens. ADR 0060 built the element tree. M6 step 5 decides the style
format.

## Options

**How styles set properties:** registered properties whose values come in
layers (animation, code, style, default); or styles assigning the C#
properties directly.

**Selectors:** type, classes, and states; or also descendant parts
("Toolbar Button").

**Theme files:** a JSON asset like `.crmaterial`; a CSS-like text format;
or C# only.

**The animation clock:** real time; or game time.

## Decision

1. **`StyledProperty<T>`:** stylable properties are registered once per
   owner type (`UIElement.BackgroundProperty`, …), with a default, what a
   change invalidates (`Invalidates`: measure, visual), a JSON reader, and a blend for
   animation. An element's value comes from layers, highest first: an
   animation, a value set in code, the best-matching style, the default.
2. **Selectors are a type, classes, and states:** `Button.primary:hover`.
   A type matches its subclasses; `*` or no type matches any element. The
   more specific selector wins (classes and states count alike, ahead of
   the type), then the later style. No parent or descendant parts.
3. **Themes are `.crstyle` JSON assets** (GUID inside, as `$guid`),
   imported to `.crsty`; C# can build them too. Changing a theme (or
   loading a changed file into it) restyles every root using it.
4. **Animation runs on real time,** each root's clock, so menus animate
   over a paused game. Transitions animate a style's change of a property;
   `Animate(property, to, seconds, easing)` tweens one from code.

## Details

- **Element properties now styled:** `Width`, `Height`, the minimums and
  maximums, `Margin`, `Padding`, both alignments, `Visibility`,
  `Background`, `CornerRadii`, and new: `BorderWidth`, `BorderColor`
  (drawn inside the bounds over the background), `Opacity` (fading the
  element and its children: the canvas gained an opacity stack),
  `Foreground`, `Font` and `FontSize` (for step 7's text elements), and
  the render transform's parts `RenderScale`, `RenderRotation` (degrees),
  `RenderOffset`, applied before `RenderTransform`.
- **Classes** (`Classes.Add`, `Remove`, `Set`) and **states** (`State`, a
  `UIState` flags set: `Hover`, `Pressed`, `Focus`, `Disabled`) restyle the
  element on the root's next update. Step 6's input sets the first three.
- **Styling** runs in `UIRoot.UpdateStyles`, first thing in each update:
  changed themes restyle the whole tree, changed classes and states their
  element; then animations advance. Matching is per element (no
  descendant parts), so a change touches only its element.
- **Transitions:**
  - a style names a property, a duration, and an easing; the winning
    transition among the styles matching *now* is used, as in CSS;
  - a transition starts from the value showing, so reversing midway
    turns back smoothly;
  - an element's first styling doesn't animate;
  - a value set in code stops an animation of it; a running tween isn't
    interrupted by a style change.
- **Tweens** end by setting the value in code (so it stays). Without a
  root, `Animate` sets the value at once.
- **Blending:** numbers, vectors, thicknesses, and radii linearly; colors
  in linear light, premultiplied (as the canvas blends); other types (an
  alignment, `Visibility`) switch at the end.
- **Easing** is CSS's cubic Bézier: `Linear`, `Ease`, `EaseIn`,
  `EaseOut`, `EaseInOut`, or any four control values.
- **JSON values:** numbers; `[x, y]`; corner radii as one number or four;
  thicknesses as one number, `[horizontal, vertical]`, or four; colors as
  `"#rgb"`, `"#rgba"`, `"#rrggbb"`, `"#rrggbbaa"`, or `[r, g, b(, a)]`
  (sRGB); enums by name; fonts by asset ID. Mistakes are refused with
  where they are (`Styles[2] (Button:hover).Set.Background: expected a
  color …`), at import and at load.
- **The imported `.crsty`** holds the validated JSON in a `JSON` section;
  a compact binary form can come later if parsing ever shows up in
  profiles.
- **Custom properties** registered by a game's element types become
  stylable once their type's static fields run; themes name properties
  globally, so names must be unique.

## Why

Value layers are what make styles safe: removing a class or a state falls
back cleanly, code always beats the theme, and animations sit on top
without either side knowing. Per-element selectors keep matching cheap
and predictable. JSON matches the engine's other authored assets and
needs no parser of our own. Real time is what menus over a paused game
need.

Given up: descendant and child selectors; a cascade or inheritance of
values down the tree (fonts and colors are set per element by styles);
keyframe animations; and animating properties that aren't styled.

## As built (M6 step 5)

- C#: `Crucible.UI`'s `StyledProperty` / `StyledProperty<T>`,
  `Invalidates`, `StyleValues`, `Theme`, `Style`, `Selector`,
  `Transition`, `Classes`, `UIState`, `Easing`; `UIElement`'s styled
  properties, `GetValue`, `SetValue`, `ClearValue`, `IsSet`, `Animate`;
  `UIRoot.Theme`, `Now`, `IsAnimating`, `UpdateStyles`;
  `Canvas.PushOpacity` / `PopOpacity`. `Crucible.Assets`'s
  `StyleImporter`.
- Tests:
  - C# (`StyleTests.cs`, JIT and NativeAOT, on a clock the tests move):
    the layers, specificity and order, classes and states, transitions
    (none on a first look, reversing midway, stopped by code), tweens,
    theme changes, easing curves, color blending, JSON values and errors;
  - `StyleImporterTests.cs`: a theme imports and loads; a mistake fails
    the import saying where;
  - `launcher.jit.styles`: a themed tree on the GPU, a border, opacity, a
    value set in code, and a hover transition with scale waited out in
    real time, checked at pixels and saved as `styles.png`.
