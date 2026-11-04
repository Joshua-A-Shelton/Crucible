# 0060 — Elements and layout

**Status:** Accepted (2026-11-03); built. Names settled by the owner
(docs/naming.md).

## Context

ADR 0019 makes the UI a retained element tree in C#, laid out by
Crucible's own panels (stack, grid, dock, overlay/anchor), with a
custom-draw element; ADR 0020 lays out in logical units scaled by the DPI
factor and snaps every element's final rectangle to whole pixels. ADR 0059
built the `Canvas` the tree draws with. M6 step 4 decides Z: the element
and panel API.

## Options

**What elements are:** a C# class tree with virtual measure, arrange, and
draw; or ECS entities laid out by systems.

**The layout protocol:** measure then arrange (WPF): desired sizes up for
the space offered, then final rectangles down, with dirty flags; or
constraints down and sizes up in one pass (Flutter).

**Overlay children's placement:** Godot's anchors (each edge at a
fraction of the parent) plus offsets; or alignment and margin only.

**Render transforms:** now; or with step 5's animation.

## Decision

1. **Elements are a C# class tree:** `UIElement`, subclassed by panels
   (`Panel`), and by controls in step 7.
2. **Layout is measure then arrange:** `Measure(available)` sets
   `DesiredSize` (margin included), `Arrange(slot)` sets `Bounds`;
   subclasses override `MeasureCore` and `ArrangeCore`. A change marks
   measure (or arrange) dirty up to the root, so only what changed is
   laid out again.
3. **Panels:** `StackPanel` (orientation, spacing), `Grid` (rows and
   columns `Auto`, `Pixels`, or `Star`, with spacing and spans),
   `DockPanel` (left, top, right, bottom; the last child fills), and
   `AnchorPanel` (anchors and offsets).
4. **Render transforms now:** `RenderTransform` around `TransformOrigin`,
   applied to drawing and hit testing, not to layout.
5. **`UIRoot`** holds a surface's top element and DPI scale, and is the
   `UI` render step: each frame it lays out to the surface's size in
   logical units if anything changed, and redraws its `Canvas` only if
   anything changed.
6. **Drawing:** an element draws its background, then `OnDraw(Canvas)` in
   its own coordinates (0, 0 to `ActualSize`), then its children. A
   custom-draw element is any element overriding `OnDraw`.

## Details

- **Every element has:** `Width` / `Height` (NaN: sized by content or
  slot), `MinWidth` … `MaxHeight`, `Margin` and `Padding` (`Thickness`),
  `HorizontalAlignment` / `VerticalAlignment` (`Stretch`, `Start`,
  `Center`, `End`), `Visibility` (`Visible`, `Hidden`, `Collapsed`),
  `IsHitTestVisible`, `ClipToBounds`, `Background` and `CornerRadii`
  (until step 5's styles draw backgrounds).
- **Sizes:** a fixed size wins; then minimum over maximum; a minimum wins
  over the space available (the element overflows, as in WPF).
- **Snapping:** layout works in unsnapped logical floats; each element's
  four edges are rounded to whole physical pixels in absolute
  coordinates, so neighbors share edges with no gap or overlap at any
  scale. Changing the scale lays everything out again.
- **Grid:** fixed tracks first; auto tracks grow to their single-track
  children (measured unbounded); star tracks share what's left by weight,
  then their children are measured in it. A child spanning a star track
  doesn't size auto tracks. Unbounded, star tracks size to content. Edit
  `Columns` and `Rows`, then call `DefinitionsChanged`.
- **Anchors:** each edge at a fraction of the panel (`Min` for left and
  top, `Max` for right and bottom) plus an offset. Along an axis whose
  anchors and offsets coincide, the child keeps its desired size and the
  anchor is its pivot (anchored at 1, its right edge sits there). Presets:
  `Fill`, the eight edges and corners, and `Center`; `At(point, offset)`;
  `Inset`.
- **Hit testing** (`UIRoot.HitTest`, logical units): children back to
  front, then the element itself if it's visible, hit-testable, and the
  point is in its bounds; points go through inverse render transforms;
  clipping elements hide their children's hits outside them. The layout
  hit is the last one done: call `UpdateLayout` after changing the tree.
- **Per-child panel settings** are static setters (`Grid.SetRow`,
  `DockPanel.SetDock`, `AnchorPanel.SetAnchors`), stored on the element.
- **The DPI scale** is set on the root; asking the window for it waits for
  step 6's platform work.
- **Antialiasing fix:** while checking snapped edges, square corners came
  out soft; ADR 0059's coverage now uses the pixel's size in local units
  (see its Details).

## Why

C# classes are what UI frameworks people know are made of, and they let
games and plugins add element types by subclassing. Measure then arrange
handles content-sized grids and docks in one pass each, and dirty flags
keep a still UI free. Anchors give game HUDs proportional placement that
alignment can't. Render transforms now give step 5's tweens a target.

Given up: wrapping panels, `Auto` sizing from children spanning several
tracks, layout transforms (rotating what layout sees), and per-property
change notification beyond the dirty flags.

## As built (M6 step 4)

- C#: `Crucible.UI`'s `UIElement`, `Panel`, `StackPanel`, `Grid`
  (`GridLength`, `GridUnit`), `DockPanel` (`Dock`), `AnchorPanel`
  (`Anchors`), `UIRoot`, `Thickness`, `HorizontalAlignment`,
  `VerticalAlignment`, `Visibility`, `Orientation`.
- Tests:
  - C# (`UILayoutTests.cs`, JIT and NativeAOT): alignment, margin,
    padding, sizes; each panel; snapping at 1.25 and 1.5 with shared
    edges; only changed elements measured again; hit testing with
    visibility, render transforms and clipping; one parent per element;
  - `launcher.jit.layout`: every panel kind at a scale of 1.25 on the GPU,
    custom-drawn elements and a turned badge, checked at chosen pixels and
    across a cell edge for blending, saved as `layout.png`.
