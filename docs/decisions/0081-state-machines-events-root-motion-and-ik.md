# 0081 — State machines, events, root motion, and IK

**Status:** Accepted (2026-11-10); M8 step 4 built, then tried by hand by
the owner (the Walker's state machine taking turns between Idle and a walk
that moves it by root motion). Names, including those added while
building, settled by the owner as drafted (docs/naming.md).

## Context

Step 2 (ADR 0079) plays clips with crossfades and layers, step 3 (ADR 0080)
skins meshes with the poses. Step 4 makes characters behave: ADR 0026 asks
for state machines as data assets with a C# API, events at exact clip times
and windows as time ranges (for parries compared against timestamped input,
ADR 0010), root motion handed to the character controller, and two-bone and
look-at IK.

## Decision

Decided by the owner (2026-11-10):

1. **State machines are JSON, imported.** A hand-edited `.crstates` (its
   GUID inside, like materials) is imported into a binary `.crstatemachine`
   (StateMachineFile.h); a graph editor (ADR 0026, the action RPG) will
   write the same JSON.
2. **Events and windows live in the model's sidecar,** per clip, and are
   baked into the `.crclip` (EVNT, WNDW). They belong to the clip's
   timeline, so a clip carries them whether a state machine or `play`
   plays it.
3. **Root motion goes through `moveCharacter`.** With `Animator.rootMotion`
   on, the engine moves the entity through the physics contract (ADR 0075;
   the null physics just moves it, M9's slides and climbs) and takes the
   travel out of the pose. Gameplay can read what moved it.
4. **IK by calls, like layers.** Slots in the native pose, set from
   gameplay each frame; not saved, and seen only while playing. Saved
   components can wrap the calls later.
5. **1D blend spaces now, 2D later** (owner: build 1D so that adding 2D
   doesn't mean reworking it). Positions and blend parameters have room for
   two dimensions in the file, and everything after the weighting function
   works on a list of weighted clips, so 2D is a new weighting function
   (docs/open-items.md).
6. **Windows are events and a query:** open and close arrive as events, and
   `openWindows` says what's open, when it opened, and when it will close.

## State machines

- **Parameters:** Float, Bool, Trigger, each with a default. A Trigger
  stays set until a transition that tests it is taken.
- **States** play one clip, or blend their clips by a Float (a 1D blend
  space: the two clips either side of the value, weighted by how near each
  is; past an end, the end clip). A state has a speed and loops or not.
- **Transitions** go from a state, or from any state but their target, with
  a blend time; all their conditions must hold (a Float `>` or `<` a value,
  a Bool `==` or `!=`, a Trigger set). One with an exit time is taken only
  when the state passes that share of itself: once a loop, or, for a state
  that doesn't loop, any time after. They're checked in order, at most one a
  frame, in "Animation.Update" (so, like clips, only while playing).
  Amended (ADR 0084): a transition taken at an exit time carries the
  frame's time past the exit into the next state, played for that long in
  the same update, so where it falls doesn't depend on the frame rate.
- **Phase.** A state's clips share one phase, 0 to 1; its length is their
  lengths weighted, so a 2 s idle and a 1 s walk blended half and half take
  1.5 s, and both reach their ends together. This keeps a walk and a run in
  step: both plant their feet at the same phase.
- **Crossfades** are between states, each sampled with all its clips, as
  step 2's are between clips. With a state machine, `play` returns
  Unsupported: the machine owns the base. Layers work as before.
- **The C# API:** `Animator.SetFloat`, `SetBool`, `Trigger`, `CurrentState`,
  `FindState`. The runtime reads the machine through the asset source, like
  clips; `reloadAnimationAsset` makes characters start it over.

## Events and windows

- **Exact times.** Each update covers the time from the last frame's start
  to this one's (the system context now carries `frameStartNs`, on the input
  clock). A clip passing an event a share of the way through its advance is
  timestamped that share of the way between the two frame starts. So
  footsteps half a second apart in the clip are half a second apart in
  timestamps at 30 fps or 144 fps (`AnimationEventTests` checks within
  50 µs), and the audio can schedule them sample-accurately (step 7).
- **Which clip.** In the base and in each layer, the clip with the most
  weight passes events, so a crossfade or a blend space doesn't double the
  footsteps. A clip that just started passes its first moment too.
- **Windows** open when their clip's time enters them and close when it
  leaves, or when their clip stops being the one with the most weight (then
  at the frame's start). `openWindows` gives each open window's opening time
  and when it will close at the clip's current rate, so a press timestamp
  can be compared with both while the window is still open.
- **Names are hashes:** `eventName` (32-bit FNV-1a), computed by the
  importer from the sidecar's names; C# `AnimationEvent.NameOf`*.
  `AnimationEvent.Name`, as drafted, would clash with the struct's own
  `Name` field.

## Root motion

- **What's extracted:** the skeleton's root joint's travel along the ground
  (x and z of its model position) and its turn about up (from the axis of
  its matrix that lies flattest at the clip's start). Its height stays in
  the pose, so the hips still bob.
- **Per frame:** each base clip's travel over the update (with a lap added
  each time it loops), turned back by how far the root had turned, weighted
  like the clips; then turned by the entity's rotation and scaled by its
  scale into its parent's space, and handed to `moveCharacter`; the turn is
  applied to the entity's rotation. `animation::rootMotion` (C#
  `Animator.LastRootMotion`*) returns it.
- **Out of the pose:** after the hierarchy pass, every joint is moved back
  by the base's travel so far and turned back about the root's starting
  point, so the root stays over the entity as it was at its clip's start.
  Layers don't contribute root motion.
- The Walker gained a "WalkForward" clip (the hips moving at 1.7 m/s), and
  `RootMotionTests` check it travels as far at 30 and 144 fps.

## IK

- **Slots.** Up to `maxIkSlots` (4) per character, applied in slot order
  after layers, the hierarchy pass, and root motion, in model space. Targets
  and poles are in world space, turned into the character's space with its
  transform as it is now (root motion included; propagation comes later in
  the frame). A slot's weight fades like a layer's (set again with the same
  weight, the fade goes on); `clearIk` fades it out.
- **Two-bone IK** (`setTwoBoneIk`, `TwoBoneIkDesc`): the end joint, its
  parent, and theirs. The law of cosines gives the angle at the root joint;
  the pole (or the chain's current bend) gives the plane. The root joint
  turns, then the middle joint, each turning everything below it. Weight
  moves the target from where the pose has the end joint. A target out of
  reach straightens the chain toward it. The end joint keeps its turn from
  the chain (a foot isn't flattened to the ground yet).
- **Look-at** (`setLookAt`, `LookAtDesc`): the joint turns so its aim axis
  (in its own space) points at the target, at most `maxAngle` (radians)
  from where the pose points it, scaled by the weight.

## As built

- crucible/StateMachineFile.h, src/native/core/StateMachine.h* and .cpp*
  (reading, blend space weights, choosing transitions); Animation.cpp (the
  rest); crucible/ClipFile.h (`ClipEvent`, `ClipWindow`);
  `loop::SystemContext::frameStartNs`*.
- C#: `StateMachineImporter` (Crucible.Assets), `MeshImportSettings.Clips`
  with `ClipImportSettings`*, `ClipEventSettings`*, `ClipWindowSettings`*;
  the Animator's new calls; the editor imports `.crstates`.
- The Walker: "WalkForward", footsteps on Walk and WalkForward at 0 and
  0.5 s, and `Walker.crstates`: Idle and Walk (a blend space by Speed from
  Walk in place to WalkForward, Speed 1.7 by default) taking turns by exit
  time, so it shows itself off without code.
- Tests: `StateMachineTests`, `BlendSpaceTests`, `AnimationEventTests`,
  `RootMotionTests`, `IkTests` (native, against the scalar reference where
  poses are compared); `StateMachineImporterTests` and the Walker's import in
  `SubAssetTests` (managed).
- Not covered by a test: root motion from a clip that turns (the Walker
  walks straight).
