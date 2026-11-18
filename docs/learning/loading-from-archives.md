# Loading from archives

A learning note for M10 step 2 (ADR 0099): reading a game's assets from
its cooked archive (2a), and loading them on background jobs (2b).

## One file, many readers

A shipped game reads its assets from one big file (cooking.md). Opening it
once and keeping its index in memory makes finding an asset a binary
search over a few kilobytes; reading it is one read at a known offset.

The usual way to read a file, "seek, then read", keeps a position inside
the open file. Two threads doing that at once trample each other's
position: one seeks, the other seeks, the first reads the second's bytes.
A *positional read* takes the offset with the call instead (`pread` on
Linux; on Windows, `ReadFile` given an offset), so the file has no shared
position to fight over and any number of threads can read at once. That's
what lets step 2b load assets on background jobs.

## Loading on jobs, publishing on the main thread

Reading a file, decompressing it, and building GPU buffers takes time a
frame can't spare, so it runs on background jobs (step 2b). But most of
the engine's tables (the renderer's slots, the mixer's sounds, the
physics world) belong to the main thread, and touching them from a worker
would need locks everywhere.

So each load comes in two halves. The *prepare* half runs on a job: it
reads the bytes and makes everything it can on its own (a GPU buffer, a
decoded sound, a collision mesh). The *publish* half runs on the main
thread at the start of a frame: it puts the result where the rest of the
engine can see it. Between the two, the thing asked for exists only as a
placeholder: a handle that answers "not ready yet".

What a system does with a placeholder is a design choice. Crucible waits:
a model isn't drawn until its textures are there too, so it never shows
grey first; a body isn't made until its shape is, so nothing falls
through a level that's still loading. A game that wants something on
screen sooner can preload (step 2c) or show a loading screen.

## Knowing a scene's needs ahead

To load a scene without stalls, a loader wants its whole list of assets up
front, so it can fetch them all before the scene appears. Finding that out
at run time means parsing the scene and following every reference, and
each of those needs its file read first.

The cooker already did that walk. So it writes the answer down: for each
scene, every asset it reaches. Other scenes are left out, and so is what
only they reach, because a scene that names another (a door's destination,
say) doesn't load it. It only needs to know the other's ID.

## Checking it plays the same

A good test of a new asset source is that nothing changes. The Walker's
level runs once from the project's Cache and once from its archive, and
the two runs must agree to the microsecond: the same frame-rate-independent
moments (walking-a-level.md) from the same bytes. To be sure the second
run really reads the archive, the Cache's imported files are moved aside
while it runs.
