# Fixed-step physics: simulating at one rate, drawing at another

A learning note for M9 step 1 (ADR 0085).

## Why a fixed step

A physics engine moves bodies forward in time in small steps. The size of
the step changes the answer: a bigger step lets things sink into each
other, bounce differently, or pass through thin walls. If physics stepped
with the frame, the same jump would land differently at 30 and 144 fps.

So physics steps at a **fixed rate**, 60 Hz in Crucible. Each frame, the
loop works out how much time has passed and runs as many 1/60 s ticks as
fit (zero, one, or several), carrying the remainder to the next frame.
Inside each tick Box3D takes four smaller **substeps**, which keeps stacks
and joints stable.

## The cost: frames between ticks

At 144 fps most frames run no tick at all. Drawing bodies where the last
tick left them makes them move in jumps: still, still, still, jump. That
looks like stutter even though the physics is smooth.

The fix is **interpolation**:
- Keep each body's pose at the last two ticks.
- Each frame, the loop knows how far it is between those two ticks: the
  **fixed alpha**, 0 at the last tick and nearly 1 just before the next.
- Draw the body that far between the two poses.

Motion becomes as smooth as the frame rate. The price is that what's drawn
is up to one tick (17 ms) behind the simulation. That's why `bodyPose`
exists: gameplay that needs the exact simulated pose, such as aiming or
hit checks, asks for it instead of reading the transform.

## Two copies of the truth

The ECS holds each body's transform and velocity, and Box3D holds its own.
Each tick they're reconciled:

- **Sync (before the step):** anything the *game* changed goes into Box3D.
  That includes new bodies, removed ones, a moved crate, and a velocity set
  by a jump.
- **Write-back (after it):** what *Box3D* computed comes back out.

The tricky part is knowing who changed a transform. Interpolation writes
transforms every frame too, and those mustn't be mistaken for the game
teleporting a body. So each body remembers exactly what the physics last
wrote. A transform that still equals it is the physics' own; anything else
is the game's.

## Moving bodies the right way

- **Dynamic** bodies are the simulation's. If the game moves one, that's a
  teleport: it appears there, keeping its speed.
- **Kinematic** bodies are the game's: moving platforms, doors. Teleporting
  them would make them pass through whatever is in the way. Instead, each
  new position becomes a **target** that Box3D reaches over the tick at
  whatever velocity that takes, so the platform really moves and really
  pushes.
- **Static** bodies are the level. They shouldn't move, but if they do,
  they're simply moved.

## Layers, either way round

The contract says two bodies collide when *either* one's mask includes a
layer the other is on. Box3D's built-in test asks that *both* agree. Box3D
lets a shape carry 64 bits of category and calls back to decide pairs, so
each shape packs its layers and its mask into those bits, and a small
callback applies the contract's rule. It's a reminder that a contract is
the behaviour promised, not the behaviour of whichever library is
underneath.
