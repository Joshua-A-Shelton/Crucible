# 0003 — Frame latency

**Status:** Accepted (2026-10-24).

## Context

Goal 4: frames displayed should match the game state as closely as
possible. Latency builds up at each stage of a frame:

1. **Input sampling:** input read at frame start goes stale if the frame
   then waits.
2. **Simulation.**
3. **Fixed-timestep interpolation:** rendering a blend of the previous and
   current physics state shows a state up to one tick old.
4. **Render-thread pipelining:** simulating frame N+1 while frame N is
   submitted adds a frame.
5. **Frames queued for the GPU:** the CPU runs ahead and builds a queue of
   2–3 frames. The largest source in GPU-bound games; NVIDIA Reflex and AMD
   Anti-Lag mainly fix it by delaying the start of each CPU frame
   ("just-in-time" frame start).
6. **GPU work and presentation:** vsync waits, swapchain image count, the
   OS compositor, the display.

Slag already provides the pacing primitive: `Swapchain::setMaxFrameLatency`
and `Swapchain::waitForNextFrame` (Slag ADR 0057; D3D12 frame latency
waitable object, Vulkan `present_id` + `present_wait`). It is an optional
capability. Slag's present modes are `Fifo` (always supported), `Mailbox`
and `Immediate` (both queried), switchable per present (Slag ADR 0056).

## Decision

- **Metrics:** track both input-to-photon latency (ms, the headline
  number) and how far the displayed state lags the simulation.
- **Just-in-time frame pacing is on by default,** built on Slag's frame
  latency capability. Games can opt out, and can expose it as a player
  setting. Deeper pipelining is something a game opts into for
  throughput.
- **Frame generation is never on by default.** It adds latency by
  construction (displayed frames are invented between real ones), so it is
  only available as an opt-in plugin. Upscaling (DLSS, FSR) is a separate
  question, covered under goal 5.
- **Present mode is the player's choice; the default is `Mailbox`,**
  falling back to **double-buffered `Fifo`** (two swapchain images) where
  it isn't supported. Mailbox renders without being capped to the refresh
  rate, shows the newest finished frame, and never tears. `Immediate`
  (tearing) is available to players who want it. Three-image `Fifo` is
  excluded: it adds a frame of queue.
- **Late latching: tentatively yes,** to be revisited once there is a
  prototype (see below).
- **Default game loop** (see below): variable-rate loop, fixed-tick
  physics, physics bodies interpolated by default. The loop is easy for a
  game to change.

## Game loop

Options considered for how simulation and rendering relate:

- **(a) Render the latest simulation state directly:** exact, but judders
  when tick rate and frame rate don't line up.
- **(b) Extrapolate forward from the latest state:** current, but
  overshoots on collisions and snaps back.
- **(c) Variable-rate simulation, fixed tick only for physics.**
- **(d) Interpolate between the last two fixed ticks** (the industry
  default): smooth, but everything is up to a tick behind.

Chosen: (c), which leaves open how physics bodies are drawn between ticks:

1. **The main loop runs at a variable rate.** Gameplay, animation, and the
   **camera and player-input path** update every frame from the latest
   state and never wait for a physics tick.
2. **Physics runs on a fixed tick** (configurable rate) for stability and
   determinism.
3. **Each physics body picks how it's presented between ticks:**
   interpolate (the default), extrapolate, or raw.
4. **The loop is hackable** (goal 7): the frame is built from ordered
   phases that a game can reorder, replace, or add to. The mechanism is
   designed under goal 7.

Why: most perceived latency is the player's own camera and input, so that
path gets the strictest treatment and never sees physics lag. For bodies
the player isn't steering, a lag of up to one tick (8.3 ms at 120 Hz) is
less visible than extrapolation's snapping. Games that need something else
change the default per body, or change the loop.

## Why

Pacing matters more than any other single technique: a GPU-bound game
without it carries 2–3 frames of queue. Building it on Slag avoids a
vendor SDK and its license.

"Triple buffering" means two different things in practice: Vulkan
`MAILBOX` (newest frame wins, not capped to refresh) and D3D-style
three-image `FIFO` (a render-ahead queue, capped to refresh, *more*
latency). The default here is the first.

Mailbox costs GPU power, since frames that never reach the screen are
still rendered. Pacing limits how far ahead the CPU starts frames, which
reduces but doesn't remove that waste.

## Late latching

The CPU records a frame's commands against a GPU buffer holding the camera
transform. Just before the GPU executes them, after the CPU has finished
the frame, the CPU samples the newest input and overwrites the camera
transform in that buffer, so the GPU draws with input that is milliseconds
old instead of a whole frame old. VR uses it for head tracking.

Costs:

- Anything the CPU computed from the old camera (culling, LOD, shadow
  cascades) can mismatch the latched one, so those need small margins.
  Usually only camera rotation is latched.
- What the player sees differs slightly from the simulation's camera,
  which matters for things like hit detection.
- The CPU write can race the GPU read, so the buffer needs a torn-write
  scheme (e.g. two slots and an index written last).

## Consequences

- When Slag's frame latency capability is missing, pacing needs a fallback,
  e.g. waiting on the previous frame's GPU completion before starting the
  next. Less precise (it ignores presentation), but it still stops the
  queue from growing.
- Verify how `waitForNextFrame` pacing behaves under `Mailbox`.
- The swapchain image count is fixed at creation and is only a minimum
  (Slag ADR 0056: drivers may return more). Mailbox wants three images, so
  switching between `Mailbox` and `Fifo` needs a `reconfigure` (Slag ADR
  0058) to change the count, and a driver that returns three images for
  `Fifo` needs a check (max frame latency 1 may cover it).
