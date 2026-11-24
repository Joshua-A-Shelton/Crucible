# Viewport tools

How a 3D view decides what a click means, and what a tool needs to know
about the scene under the pointer (ADR 0121).

## One tool at a time

Image editors made the toolbox familiar: pick the brush, and clicks paint;
pick the eraser, and they erase. Scene editors work the same way. Each
view has one *active tool*, and the pointer's input goes to it. The
alternative, tools that guess from what's selected, saves a click but
makes the same click mean different things at different times, and needs
a rule for when two tools both think it's theirs.

## Who gets the input

A tool sees input first and says whether it took it. What it doesn't take
falls through to the view: the camera's look, pan, orbit, and zoom. That
lets most tools ignore the camera entirely, and lets the few that need a
button (map mode picks tiles up with the right one) have it. A press a
tool took stays the tool's until it's let go, so a drag that leaves the
handle it started on still reaches the tool. That's *pointer capture*.

## Drawing on top

A tool shows what it's doing: handles, points, a brush, a box. The
simplest way is an *overlay*: flat drawing over the rendered image, after
the scene. A point in the world is *projected* through the camera to find
where it appears on screen, so an overlay can still follow the world. It
can't be hidden behind a wall, which for editing is usually what you want:
a hidden handle can't be grabbed.

## What's under the pointer

A view is a camera looking through a rectangle. Each point of the
rectangle is a *ray* from the camera into the world. Where that ray meets
a plane is simple geometry, right away: enough for 2D tools, or placing
things on a flat ground.

What's actually drawn there is a different question: the GPU knows, the
CPU doesn't. *Picking* asks the GPU to draw each entity's ID into a hidden
image along with its depth, and reads back the pixel under the pointer.
That takes a frame or two, so the answer comes later, as a callback. The
depth turns back into a distance along the ray, which gives the surface
point: where on the model or terrain the pointer is.
