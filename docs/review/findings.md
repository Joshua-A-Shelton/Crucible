# Review findings

What the engine review (milestones, "After editor extensions: an engine
review") decided to change, system by system, with the owner. Changes are
made after the walk-through, or between systems when the owner says so,
each as its own step.

| System | Finding | Decision | Status |
|---|---|---|---|
| Core | Running a world outside the loop (a preview's) is hand work: animation steps, missing world transforms, the world's version. A way to run chosen systems on a world with its own clock. | Come back to (owner, 2026-11-25) | Noted |
| Core | `World.UpdateWorldTransforms` updates existing world transforms but doesn't add missing ones, as the propagation system does. | Come back to (owner, 2026-11-25) | Noted |
| Core | The editor doesn't show the schedule (phases, systems, order, timing) that ADR 0013 says it would, with gameplay and other systems running at different times. | Come back to (owner, 2026-11-25) | Noted |
| Core | Per-process state (component registration, registries) is a likely cause of two engine tests that fail once in a while in parallel runs. | Come back to (owner, 2026-11-25) | Noted |
| Core | Systems run one at a time; revisit if a game's frame is CPU-bound on the main thread. | Come back to (owner, 2026-11-25) | Noted |
