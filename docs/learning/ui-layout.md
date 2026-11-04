# UI layout: measure, arrange, and snapping

A learning note for M6 step 4 (ADR 0060).

## Two questions, two passes

Laying out a UI means answering two questions for every element: how big
does it want to be, and where does it actually go? They can't be answered
in one walk down the tree, because a parent's size can depend on its
children (a stack is as tall as its items) while the children's places
depend on the parent's final size.

WPF's answer, which Crucible uses, is two passes:

1. **Measure**, bottom up: each parent offers its children some space
   (possibly infinite: "take what you need"), and each child reports a
   *desired size*. A vertical stack offers unlimited height and adds up
   the heights it gets back.
2. **Arrange**, top down: each parent, now knowing its own final
   rectangle, gives each child a slot. The child places itself in the
   slot by its alignment: stretched to fill, or at its desired size at
   the start, center, or end.

## Star sizing

A grid column can be fixed, *auto* (as wide as its widest child), or
*star* (a share of what's left). Star tracks can only be sized once the
fixed and auto ones are known, so the grid measures in order: fixed
tracks, then children in auto tracks, then splits the remainder among the
star tracks by weight, then measures the children in star tracks with
their share.

## Doing less

Layout runs every frame, but most frames nothing changed. Each element
remembers whether it needs measuring or arranging again. A change marks
the element and all its ancestors (a child's size can change its
parent's), and measure skips any element that isn't marked and is offered
the same space as last time. A still UI costs a check at the root.

## Snapping to pixels

At a DPI scale of 1.25, a 10-unit gap is 12.5 physical pixels: an edge
landing mid-pixel is drawn half covered, a soft blurry line. So every
element's edges are rounded to whole physical pixels. Rounding each edge
on its own, in absolute coordinates, matters: rounding a position and a
size separately could leave neighbors a pixel apart or overlapping, while
rounding the shared edge gives both neighbors exactly the same pixel.

## Anchors

Game HUDs often want proportional placement: a health bar across the
bottom, a minimap in the top right corner, a panel filling the screen
minus a margin. Godot's anchors express all of these the same way: each
edge sits at a fraction of the parent (0 is the left or top, 1 the right
or bottom) plus an offset in units. When both edges of an axis share an
anchor and offset, the child keeps its own size and the anchor becomes
its pivot.
