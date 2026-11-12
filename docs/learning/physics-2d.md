# 2D physics: a plane in a 3D engine

A learning note for M9 step 6a (ADR 0093).

## Why a separate 2D physics

A 2D game could use 3D physics with everything flattened onto a plane, but
it's wasteful and fragile: bodies drift off the plane, turn about axes they
shouldn't, and pay for a third dimension they never use. A dedicated 2D
engine (Box2D) works in two coordinates and one angle, and is faster and
steadier for it. Crucible keeps the two side by side, each behind its own
contract of the same shape, so a game can use either, or both in one scene.

## A 2D transform

A 3D entity's place is a position, a rotation (a quaternion), and a scale.
A 2D one needs less: a position in the plane, one angle, a scale in X and Y.
Crucible gives 2D entities their own `Transform2D` with exactly that, plus a
**depth**: where along Z the plane sits, which decides what's drawn in front
of what.

Both kinds of transform end up as the same thing: a world matrix. That
matrix is what the renderer and everything else reads, so a 2D entity is
drawn, picked, and parented like any other, with no 2D-specific drawing
code.

## Angles

In 2D a rotation is a single angle. Crucible counts it in radians, turning
+X toward +Y: counterclockwise, as the default camera (looking along +Z)
sees it. Box2D uses the same convention. When blending between two angles
(interpolating a body between physics ticks), the difference is taken the
short way round: from 3.1 to −3.1 radians is a small step across π, not
almost a whole turn back.

## Polygons

A polygon collider is a list of points. Box2D only collides **convex**
polygons (no dents), so it takes the convex hull of the points: the tightest
convex shape around them, like a rubber band stretched round pins. Box2D
caps polygons at 8 points; larger outlines are made of several shapes, or of
chains (a later step).
