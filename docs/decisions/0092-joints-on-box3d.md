# 0092 — Joints on Box3D

**Status:** Accepted (2026-11-11); M9 step 5 built, then tried by hand by
the owner in the Walker's Joints scene. Names, including those added while
building, settled by the owner as drafted (docs/naming.md).

## Context

The contract (ADR 0075) has a `Joint` component: a kind (Fixed, Hinge,
BallSocket, SwingTwist, Distance), the entity it joins (`other`), an anchor
in each entity's space, an axis, a lower and upper limit, and swing and
twist limits. The plan had Fixed, Hinge, and Distance on Box3D, and
ball-and-socket and swing-twist unsupported (from Box3D's README); but
Box3D at the pinned commit has a spherical joint with a cone and a twist
limit (ADR 0085's note).

## Decision

Decided by the owner (2026-11-11):

1. **All five kinds:** Fixed on Box3D's weld joint, Hinge on its revolute
   joint, Distance on its distance joint, BallSocket on its spherical joint
   without limits, SwingTwist with its cone (Swing Limit) and twist
   (±Twist Limit) limits.
2. **Other None joins to the world**, held where the anchor is when the
   joint is made (a hanging lamp, a door), on one static body per world.
   Other Anchor isn't used then.
3. **Limits are off unless set:** a hinge turns freely while Lower Limit ≥
   Upper Limit; a swing or twist limit of 0 is free; a distance joint
   without a range (Lower ≥ Upper) holds the length it starts with, and
   with one is a rope or rod free between them.
4. **View > Colliders draws joints:** each anchor, the line between them,
   the axis, and a hinge's limits as an arc.

## How it works

- **Frames.** Frame A is at the anchor (scaled by the entity's world
  scale) with its z along the axis (Box3D's hinge, cone, and twist axis;
  +Z with no axis). Frame B is at the other anchor, turned so the two
  frames line up as the bodies sit when the joint is made: angles count
  from there, and anchors that are apart are pulled together. A distance
  joint's length is the anchors' distance then (at least 1 cm: Box3D needs
  one, and one to the world starts at 0).
- **Lifetime.** Physics.Sync makes each joint once both bodies exist (an
  entity with a Joint needs a Collider for its body), and makes it again
  when the component changes, or either body is a new one; it destroys
  joints whose component or entity is gone. Box3D destroys a joint with
  its body, so a joint whose other body went is noticed as no longer valid.
  A joint to itself makes nothing.
- **Jointed bodies don't collide with each other** (`collideConnected`
  off), the usual default.
- **Left out:** motors, springs, and breaking (not in the contract).

## As built

- Box3dPhysics.cpp: `JointState`*, `PhysicsWorld::joints`*, `ground`*,
  `sameJoint`*, `frameTurn`*, `b3Frame`*, `groundOf`*, `destroyJoint`*,
  `makeJoint`*, `syncJoint`*, `minimumLength`*; `drawJoint`*,
  `drawCross`*.
- Tests: `FixedJointTests`, `HingeJointTests`, `DistanceJointTests`,
  `SphericalJointTests`, `JointLifetimeTests` (native), `JointTests` (C#).
- For trying it by hand: the Walker's Joints.crscene: a lamp swinging on a
  ball-and-socket to the world, a door on a limited hinge, a chain of four
  links on distance joints from a hook, a welded pair tumbling as one, and
  a swing-twist arm under a beam.
