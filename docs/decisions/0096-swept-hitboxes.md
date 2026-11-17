# 0096 — Swept hitboxes

**Status:** Accepted (2026-11-16); M9 step 7 built, awaiting the owner's
try by hand in the Walker's Hitboxes scene. The design's names were settled
before building; names marked * were added while building and are
placeholders until the try.

## Context

ADR 0027 chose combat hit detection by swept, query-based hitboxes: during
an animation window (ADR 0081), a shape on a joint is swept from where it
was to where it is, and a hit's place along the sweep gives an exact time
to compare with input and animation timestamps. Hitboxes are queries, not
bodies. M9's done-when asks for a hit that registers identically at 30 and
240 fps. Two things were missing: nothing made an entity follow a joint,
and `shapeCast` returns only its first hit.

The owner chose (2026-11-16): a separate socket component and a hitbox on
its own entity; activity by a window, or always when no window is named;
interpolated sweeps split into pieces; every target reported once per
activation, triggers included, never the owner's family; 2D hitboxes too;
the Walker kicking crates as the sample. Names that native components can't
hold as strings (a joint's, a window's) are stored as their 32-bit hash
(`eventName`) and picked by name in the inspector.

## Decision

- **Sockets.** `Socket` (`joint`, `position`, `rotation`): its entity
  follows a joint of the nearest Animator above it, at the offset in the
  joint's space. "Animation.Sockets" runs after "Animation.Pose" every
  frame, while editing too, and sets the entity's LocalTransform position
  and rotation relative to its parent; its own scale is kept. Sockets are
  placed parents first, so one under another follows both. Without an
  Animator above it, a joint of that name, or a pose yet, it's left alone.
  Swords, lights, and colliders can ride joints the same way.
- **Hitboxes.** `Hitbox` (`shape` Box, Sphere, or Capsule; `size` as a
  Collider's, scaled with the entity; `mask`; `window`; `enabled`) and
  `Hitbox2D` (Box, Circle, Capsule): a shape on its own entity, wherever
  that is. It's active while enabled and, with a window, while the
  nearest Animator at or above it has that window open. Its owner is that
  Animator's entity or, without one, the top of its hierarchy.
- **When in the frame.** Each run covers the time from the last frame's
  start to this one's, as animation events do. A windowed hitbox is
  active from its window's open event to its close event, so a window
  opening and closing within one frame still sweeps the stretch between.
  A hitbox switched on (enabled, or seen first) tests only where it is at
  the frame's start; one switched on while touching something hits it.
- **The sweep.** "Physics.Hitboxes" (and "Physics2D.Hitboxes") run in the
  Animation phase after "Animation.Sockets", while the game plays. The
  hitbox's motion over the frame is taken as a screw: a steady turn about
  an axis, with a steady slide along it, carrying last frame's place to
  this one's. A swing round a pivot (a foot round a hip) follows its
  circle exactly, and a move without a turn is a straight line. The sweep
  is split so no piece turns more than 15 degrees (at most 24 pieces),
  each piece a straight `shapeCastAll` at the piece's middle turn. The
  design had said position moves in a straight line; building found that
  a hitbox carried round a pivot then cuts across its arc (a 170 degree
  swing in one frame missed a target on the arc), so the screw replaced
  it, and splitting by distance moved went with it, since a straight move
  is swept exactly by one cast.
- **Hits.** Every collider a sweep touches, triggers included, filtered
  by the hitbox's mask; each entity once per activation (a new window
  opening, or being enabled again, is a new one); never the owner or
  anything under it. `HitboxHit` (`hitbox`, `owner`, `target`, `point`,
  `normal`, `timestamp`) from `physics::hitboxHits`, `HitboxHit2D` from
  `physics2d::hitboxHits`, the last run's hits by timestamp; C#
  `Physics.HitboxHits` and `Physics2D.HitboxHits`. A hit's share of the way
  along its piece is that share of the way between the piece's times.
  Hitboxes push nothing: the game reacts to hits.
- **`shapeCastAll`** (both services and both namespaces, C#
  `Physics.ShapeCastAll`* and `Physics2D.ShapeCastAll`*): every hit along
  a cast, nearest first, each entity once at its nearest. It's an
  optional service entry: a service without it finds nothing. The sweeps
  call it through the installed service, so a replacement physics gets
  hitboxes too.
- **Picked by name.** `CRUCIBLE_HINT_JOINT` and `CRUCIBLE_HINT_WINDOW`
  (C# `[JointName]`, `[WindowName]`) mark a `uint32_t` name hash. The
  inspector shows a drop-down: None (0), the joints of the nearest
  Animator's skeleton above the entity, or the windows named on the
  project's clips, and a hash none of them has as "#hash (not found)".
  Scene files hold the numbers.
- **Drawing.** View > Colliders draws hitboxes dimmer than colliders, and
  brighter while active.

At 30 and 240 fps a ball swung through a target registers its hit 15 µs
apart (`HitboxTests` checks within 50 µs, as footsteps are).

## Known limitations

- Between frames the motion is one screw, not the animation itself: a
  foot whose path bends within a frame (a kick's snap) is followed by the
  screw through its two ends. At 30 fps and up the difference is small;
  sampling the pose in between would be exact and costs pose evaluations.
- 2D has no skeletal animation, so a 2D hitbox's window needs an Animator
  above it; otherwise the game switches it with `enabled`.
- Queries see the last tick's bodies (ADR 0090), so a target moving fast
  is hit where it was at the last tick.
- A hitbox switched on mid-frame by the game (not by a window) starts at
  the frame's start, not the moment it was switched on.

## As built

- crucible/Animation.h: `Socket`, `socketSystemName`, `placeSockets`*
  (the system's body, for tests). crucible/Physics.h: `Hitbox`,
  `HitboxHit`, `hitboxSystemName`, `hitboxHits`, `sweepHitboxes`*,
  `shapeCastAll` (also on `PhysicsService`). crucible/Physics2D.h: the
  same for 2D. crucible/Expose.h: the two hints.
- Hitboxes.cpp (with Hitboxes.h*: `registerHitboxes`*,
  `registerHitboxes2D`*, `hitboxActive`*, `forgetHitboxes`*), on the
  installed services; TransformParts.h* (`TransformParts`*, `decompose`,
  `compose`, `rotationOf`), shared with Box3dPhysics.cpp, whose own copy
  it replaces. Animation.cpp's skeletons keep their joints' name hashes
  (`nameHashes`*).
- C#: `Socket`, `Hitbox`, `Hitbox2D` halves with defaults; BindGen and the
  serializer generator carry the hints; `AssetDatabase.JointNamesOf`* and
  `WindowNames`*; the inspector's name picker.
- Tests: `SocketTests`, `HitboxTests` (with the importers' tests, for the
  Walker), `Hitbox2DTests`; `PickerChecks` (CTest launcher.jit.pickers)
  picks a socket's joint and a hitbox's window.
- For trying it by hand: the Walker's Hitboxes.crscene. The Walker walks
  its circle with a "Foot Socket" on its right foot and a "Kick Hitbox"
  under it, active in the walk's "Kick" window (0.6 to 0.95 s of Walk and
  WalkForward, in the model's sidecar), masked to layer 2, where "Crate
  1" to "Crate 4" are. Code/Kicks.cs's `KickSystem`* logs each hit's time
  and knocks the crate away.
