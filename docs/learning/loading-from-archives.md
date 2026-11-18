# Loading from archives

A learning note for M10 step 2 (ADR 0099). Step 2a: reading a game's
assets from its cooked archive.

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
