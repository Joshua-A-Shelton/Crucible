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

## Whose movement a joint measures

A joint ties two bodies, and its limits say how far one may move relative
to the other. Which one? Box2D (and Box3D) measure body B relative to body
A. Crucible's Joint2D belongs to an entity and names the *other* body, so
the natural reading is "how far this entity moves relative to the other".
Crucible makes the joint with the other body as A and its own as B, which
flips the sign: a hinge whose entity may turn 0 to 1 radian is, seen from
the other side, the other turning −1 to 0. So the limits are swapped and
negated on the way in. A symmetric limit (−0.5 to 0.5) looks the same
either way, which is how a sign mistake like this hides.

## A character that doesn't see itself

A character's move sweeps its capsule through the world and slides along
what it hits. But the character is also in the world, as a capsule other
bodies bump into, and the sweep would hit that first. Box2D's mover cast
can't be told "skip this one shape", so Crucible uses collision bits
instead: every shape has a category (what it is) and a mask (what it
collides with), and two shapes meet only if each one's category is in the
other's mask. Character capsules and triggers leave one reserved bit out of
their masks, and the sweep uses only that bit as its category, so it sees
the level and bodies, and no character, including itself. The cost is that
characters walk through each other.
