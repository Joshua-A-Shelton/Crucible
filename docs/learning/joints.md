# Joints: connecting bodies

A learning note for M9 step 5 (ADR 0092).

## What a joint does

A joint is a rule the physics solver keeps between two bodies (or a body
and the world): "these two points stay together", "these two stay this far
apart", "this one only turns about that axis". Each tick the solver nudges
the bodies' velocities so the rules hold, along with collisions.

Each body gets a **frame**: a point (the anchor) and a set of axes. The
joint's rule is about how the two frames relate:

- **Fixed (weld):** the frames stay together, position and turn: the two
  bodies move as one.
- **Hinge:** the points stay together and the frames can turn only about
  one shared axis, like a door. Limits stop it turning past an angle.
- **Ball-and-socket:** the points stay together; any turn is allowed, like a
  shoulder or a hanging lamp.
- **Swing-twist:** a ball-and-socket with two limits: how far the axis may
  swing away from where it started (a cone), and how far it may twist
  about itself. Ragdolls are made of these.
- **Distance:** the points stay a set distance apart (a rod), or within a
  range (a rope: slack when closer, taut at the end).

## Where angles start

A hinge's "angle 0" has to mean something. Crucible measures from how the
bodies sit when the joint is made: lay a door out closed, give it a hinge
with limits −1.2 to 1.2, and it swings 1.2 radians either way from closed.

## Joints to the world

Often one side isn't a body at all: a lamp hangs from the ceiling, a door
from a wall that never moves. Leaving the joint's other entity empty holds
it to the world, at the place the anchor is when the joint is made.

## Why jointed bodies don't collide

Two bodies joined at a point usually overlap there a little. If they also
collided, the solver would push them apart while the joint pulled them
together, and they'd jitter. So, as most engines do by default, bodies
joined to each other don't collide with each other.
