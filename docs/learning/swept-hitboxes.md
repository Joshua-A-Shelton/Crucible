# Swept hitboxes: hits that don't depend on the frame rate

A learning note for M9 step 7 (ADR 0096).

## The problem with checking once a frame

The simplest way to see whether a sword hit someone is to check each frame
whether the sword's shape overlaps them. That breaks in two ways:

- **Tunnelling.** A fast swing at 30 fps moves the blade a long way
  between frames. It can be on one side of an enemy in one frame and past
  them in the next, never overlapping in either. At 240 fps the same swing
  hits. The game plays differently on different machines.
- **Coarse timing.** Even when it hits, "this frame" is all you know. At
  30 fps that's a 33 ms window. A game that compares the hit with a parry
  press (also timestamped exactly, ADR 0010) needs better than that.

## Sweeping

Instead of asking "does it overlap now?", ask "did it touch anything on
its way from where it was last frame to where it is now?" That's a
**shape cast** (physics-queries.md): sweep the shape along the path, and
the physics engine reports what it touches and **how far along** it was.
That fraction, 0 at the path's start and 1 at its end, turns into a time:
the frame covers a known stretch of time, so a hit 35% of the way along
happened 35% of the way through it. That's the same way animation events
get exact timestamps (state-machines-and-events.md).

A hit found this way doesn't depend on the frame rate. At 30 fps the
sweep is long, at 240 fps it's short, but either way it finds the same
moment of contact.

## Paths that curve

A shape cast moves in a straight line, but swings don't. A sword turning
round a shoulder traces an arc, and a straight line from where it was to
where it is cuts across the arc. At a high frame rate the arc is nearly
straight between frames; at a low one the shortcut can miss what the arc
passes through, or touch something the arc doesn't.

So the sweep is split into pieces, each turning only a little (15
degrees), and each piece is a straight cast. The more the hitbox turned
in the frame, the more pieces.

That only helps if the pieces follow the curve. Crucible takes the
hitbox's motion between two frames as a **screw**: every rigid motion
(any change of place and turn) can be made by turning about some axis
while sliding along it. Find that axis, and the in-between places are the
same turn and slide, a bit at a time. A foot swinging round a hip turns
about an axis through the hip, so its in-between places lie on the
circle. A projectile that doesn't turn just slides, in a straight line.

## Activations

A swing should hit each enemy once, not once per frame while the blade
passes through them. So a hitbox remembers what it has hit during an
**activation**: from the moment it becomes active to the moment it stops.
The next swing (the animation window opening again) is a new activation,
and can hit the same enemy again.

## Windows, owners, and bone attachments

- **Windows** (ADR 0081) are named stretches of an animation clip: "the
  blade is dangerous from 0.3 s to 0.45 s of the attack". A hitbox named
  after a window is active while that window is open, from the exact
  moment it opens to the moment it closes.
- **The owner** is the character the hitbox belongs to: the nearest
  Animator above it. A hitbox never hits its owner, or anything under the
  owner (its own hurtboxes, its own shield), however they overlap.
- **Bone attachments** make an entity follow a bone of the skeleton: a
  sword in a hand, a hitbox on a foot. Each frame, after the pose is worked
  out, the attachment's entity is placed at the bone (plus an offset).
  Anything can be attached this way, not just hitboxes. Other engines call
  the same idea a socket.

## Queries, not bodies

A hitbox isn't simulated: it doesn't push anything and nothing pushes it.
It only reports what it touched, and the game decides what a hit means:
damage, a stagger, or (in the Walker's sample) a crate knocked away. This
keeps combat in the game's hands. A physics body as a sword would shove
enemies around whether the game wanted it to or not.

## Names stored as numbers

Components on the native side hold only plain data, never strings, so a
bone attachment can't store "RightFoot". It stores the name's 32-bit hash
instead, the same hash events and windows already use. The editor turns it
back into a name by hashing the skeleton's bone names and matching. Scene
files hold the number, and the inspector shows the name.
