# Root motion and IK: letting animation move the character, and fitting poses to the world

A learning note for M8 step 4 (ADR 0081).

## Two ways to move a character

**Code-driven:** gameplay moves the entity at some speed, and the walk
animation plays in place. It's simple and responsive, but the speeds must
match by hand. If the code moves faster than the legs, the feet slide
along the ground ("skating").

**Root motion:** the animator moves the character forward in the clip, as
a real walk does, and the engine reads that movement back out. The feet
plant exactly, because the body moves just as far as the animation says.
Lunges, dodges and attacks with steps are hard to match by hand, and root
motion gets them right for free.

## Extracting it

The **root joint** (the hips here) is the one everything hangs from. Each
frame:

1. Measure how far the root travelled along the ground during this
   update, and how far it turned about up.
2. Move the entity by that much, through the physics contract's
   `moveCharacter`, so walls and slopes will stop it once real physics
   arrives (M9).
3. Take the same travel out of the pose, so the body stays over the entity
   instead of running ahead of it.

Only the travel along the ground is taken. The up-and-down bob of the hips
stays in the pose: that's the body's motion, not the character's.

The travel is turned back by how far the root had already turned. If the
clip walks in a curve, its second step is "forward" relative to where the
character faces after the first, not relative to where the clip began.
Then the engine turns it by the entity's own rotation, so a character
facing east walks east.

Loops need a little care. When WalkForward wraps from its end back to its
start, the root jumps back 1.7 m in the clip. The update adds a whole lap's
travel each time it wraps, so the entity never jumps backward.

## IK: fitting the pose to the world

Animations are authored on flat ground, reaching for nothing. **Inverse
kinematics** bends the pose so a hand reaches a door handle, a foot stands
on a step, or a head turns toward whoever is talking. It works in reverse
of the usual direction: from where the end should be to how the joints
must turn.

### Two-bone IK

A leg (thigh, shin, foot) or an arm (upper arm, forearm, hand) is a
triangle. Two sides are the bones, whose lengths never change, and the
third is the distance from the hip to the target. Three sides fix the
triangle's angles. The **law of cosines** gives the angle at the hip:

    cos A = (upper² + distance² − lower²) / (2 · upper · distance)

That says how far the knee sits from the line between hip and target, but
not which way. A knee could bend forward, backward or sideways and still
reach. The **pole** decides: the knee bends toward it, so pass a point in
front of the character for knees and behind for elbows.

Then the thigh turns so the knee lands where the triangle says, and the
shin turns so the foot lands on the target. Each turn carries everything
below it along. A target out of reach just straightens the leg toward it.

### Look-at

Look-at is one joint turning so an axis of it (the head's forward) points
at a target. A **maximum angle** keeps it human: past it the head turns as
far as a neck can and no further.

### Weight

Every IK slot has a weight that fades in and out. A foot fades into its
planted position as it lands, and a head stops looking when the speaker
leaves. At half weight, the target is halfway between where the animation
put the foot and where the IK wants it. A blend in target space looks more
natural than blending two finished poses.
