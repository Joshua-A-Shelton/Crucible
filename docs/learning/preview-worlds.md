# Preview worlds

How an editor shows something apart from the scene, and keeps it moving
(ADR 0125).

## Several worlds

An ECS world is just a container of entities and components. Nothing
stops a program having more than one: the scene being edited is one, and a
character shown in a panel can be another, with its own lights and camera.
Keeping them apart means a preview can't change the level, and the level's
lights don't light the preview. The renderer draws whichever world it's
given into whichever image it's given.

## Systems and clocks

Entities don't move by themselves: systems move them, once a frame,
working out where everything is now from where it was and how much time
passed. The scene has the engine's schedule. A preview world needs the same
work done (animations advanced, skeletons posed, transforms worked out), so
the editor runs those steps on it too, with a clock of its own. Switching
the clock off stops time there, but still poses the world, so whatever
it's set to shows.

## Scrubbing

Scrubbing is dragging through time by hand, as on a video's timeline.
Instead of advancing an animation by the frame's time, it's put directly
at a moment, and the pose there is worked out. A jump isn't playback, so
it shouldn't fire the animation's events (a footstep sound for every frame
dragged past), and it should land exactly where asked rather than easing
in from where it was.

## Change tracking

Renderers avoid redoing work by asking "what changed since I last drew?".
Worlds count changes with a version number that the frame loop advances
after each system. A world nobody advances never looks changed, so a
model that finished loading after the first draw would never appear. A
world run by hand has to advance its own version too.
