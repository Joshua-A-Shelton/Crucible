# 0094 — 2D joints and characters

**Status:** Accepted (2026-11-16); M9 step 6b built, then tried by hand by
the owner in the Walker's Characters2D scene (2026-11-16). Names, including those added
while building (marked *), settled by the owner as drafted (2026-11-16,
docs/naming.md).

## Context

ADR 0093 gave 2D bodies, colliders, queries, and contacts; M9 step 6b adds
what 3D has on top of those: joints (ADR 0092) and a character controller
(ADR 0091). Box2D v3.2.0 has weld, revolute, distance, and prismatic joints
and the same mover as Box3D (`b2World_CollideMover`, `b2SolvePlanes`,
`b2World_CastMover`). The owner chose (2026-11-16) four joint kinds, and
the same rules as 3D for both.

## Decision

- **Joint2D** (crucible/Physics2D.h): `kind` (`JointKind2D`*: `Fixed`,
  `Hinge`, `Distance`, `Slider`), `other` (an entity with a Collider2D, or
  none for the world), `anchor` and `otherAnchor` in each entity's space,
  `axis` (Slider), and `lowerLimit`/`upperLimit`. As in 3D: limits are off
  unless lower < upper; angles and positions count from how the bodies sit
  when the joint is made; a distance joint's length is the anchors'
  distance then (at least 1 cm); Physics2D.Sync makes, remakes, and
  destroys joints as 3D's does; jointed bodies don't collide; no motors,
  springs, or breaking. Fixed is Box2D's weld, Hinge its revolute, Distance
  its distance joint, Slider its prismatic joint (which also keeps the
  bodies from turning).
- **Limits are the entity's own movement.** Box2D (and Box3D) measure a
  joint as body B relative to body A; Crucible's joint belongs to its
  entity and is measured the other way round, as that entity moving
  relative to the other (or the world). So both backends make the joint
  with the other body as A and swap and negate the hinge and slider
  limits. A hinge limited 0..1 turns its own entity up to 1 radian
  counterclockwise. This fixes ADR 0092's 3D hinge, which had the sign the
  other way round (its symmetric limits hid it).
- **CharacterController2D**: `radius`, `height`, `stepHeight`,
  `slopeLimit`, `layers`, `mask`, `gravityScale`, `stepSmoothing`, the same
  as 3D's in the plane; `moveCharacter2D`/`launchCharacter2D` (C#
  `CharacterController2D.Move`, `.Launch`) as 3D's, returning
  `CharacterMove2D`* (`position`, `grounded`, `groundNormal`,
  `fallSpeed`). Its entity's Transform2D is its feet; bodies meet it as a
  kinematic capsule, and it pushes dynamic bodies it walks into.
- **Characters pass through each other.** Box2D's `b2World_CastMover`
  takes no filter callback, so a character can't skip its own capsule by
  entity. Instead every character capsule (and trigger shape) leaves the
  top category bit (`moverBit`*) out of its mask, and moves query with
  that bit as their category: the mover sees the level and bodies, never
  characters or triggers. 3D characters still block each other; a 2D game
  wanting that checks with a shape cast.

## Known limitations

- 2D characters don't block each other (above).
- As in 3D, stepping down off a step drops rather than eases; the owner
  asked to ease 2D down too when 3D's is revisited (docs/open-items.md).

## As built

- crucible/Physics2D.h: `Joint2D`, `JointKind2D`*, `CharacterController2D`,
  `CharacterMove2D`*, `moveCharacter2D`*, `launchCharacter2D`*, the
  service's `moveCharacter`/`launchCharacter`.
- Box2dPhysics.cpp: `JointState`*, `Character`*, `moverBit`*,
  `makeJoint`*, `syncJoint`*, `syncCharacter`*, `characterCapsule`*,
  `Mover`*, `gatherPlanes`*, `pushBodies`*, `moveCharacterBox2D`*,
  `launchCharacterBox2D`*; View > Colliders draws characters' capsules and
  joints. Box3dPhysics.cpp's hinge limits swapped and negated.
- C#: `Joint2D`, `CharacterController2D` (`Move`, `Launch`; defaults as
  3D's).
- Tests: `Joint2DTests`, `CharacterController2DTests` (native),
  `Physics2DCharacterTests` (C#); `HingeJointTests` gains "A hinge's
  limits are the entity's own turn".
- For trying it by hand: the Walker's Characters2D.crscene and its
  `Patroller`* component (Code/Patrol.cs): two characters walking back and
  forth (up steps, over a hill, jumping every few seconds), one pushing a
  ball and a cart on a slider; a gate on a limited hinge; a chain of three
  links on distance joints from a hook.
