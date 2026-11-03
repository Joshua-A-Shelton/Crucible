# Lighting

A learning note for M5 step 7 (ADR 0053): many lights, cheaply.

## The problem with many lights

Forward rendering shades each pixel once, with every light that reaches
it. Loop over all 1,000 lights at every pixel, and almost all the work
goes to lights that are far away and contribute nothing. The fix is to
know, before shading, which few lights each part of the screen can see.

## Clusters

Cut the view into a 3D grid: 16 × 9 tiles across the screen, each split
into 24 slices of depth. Each cell, a **cluster**, is a small box in front
of the camera. A compute pass tests every light's sphere of reach against
every box and writes each box's short list of lights. A pixel works out
its box from where it is on screen and how far away it is, and loops over
only that list.

The depth slices grow with distance (each one is a fixed ratio deeper
than the last), because things far away are smaller on screen: a slice
1 m thick right in front of the camera and 30 m thick far off cover about
the same amount of screen.

## Why lights have a range

Physically, light falls off with the square of the distance and never
quite reaches zero, so a light would belong to every cluster. Multiplying
the falloff by a smooth window, `(1 − (d / range)⁴)²`, brings it to exactly
zero at the range. Close in, the window is nearly 1, so the light still
behaves physically where it matters. The range is what lets the clusters
leave a light out.

## Culling on the CPU first

Before the GPU sorts lights into clusters, the CPU throws away lights
that can't be on screen at all: their sphere is outside the view. That
runs on the job system, chunk by chunk, and keeps the GPU's list short.
The farthest surviving light also decides how deep the clusters go, since
the camera itself has no far plane.

## Ambient light from a hemisphere

Light bounces: even a surface in shadow is lit by the sky and the ground.
A **hemisphere** ambient is the simplest believable version. Faces
pointing up get the sky's color, faces pointing down the ground's, and
everything else a blend. A character's head is lit a little blue from
above, the underside of its chin a little brown from below.
