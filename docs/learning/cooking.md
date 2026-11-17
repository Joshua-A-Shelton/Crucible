# Cooking: packing a game for shipping

A learning note for M10 step 1 (ADR 0098).

## Source, cache, and cooked data

An asset lives in three forms over its life:

1. **Source**, in the project: a glTF, a PNG, a scene's JSON. People edit
   it and source control keeps it.
2. **Imported**, in the project's Cache: the engine's own binary form,
   ready to load (asset-database.md). The editor makes it whenever the
   source changes, and the editor and a standalone run read it.
3. **Cooked**, in an archive: what ships. Only what the game uses, packed
   into one file, compressed as hard as is worth it.

Cooking turns form 2 into form 3. It doesn't convert anything: the bytes a
loader sees are the same either way, only where they come from changes.

## What the game uses: a walk

A game shouldn't ship every asset in the project: test scenes, unused
models, the sample you copied in once. So the cooker starts from the
scenes the game can start in and follows everything they name, and what
those name, and so on: a graph walk, each asset visited once.

Two kinds of links have to be followed:

- **In authored files** (scenes, prefabs): a component's asset field, a
  prefab instance. These are JSON, so the walker can look at every string
  and ask "is this an asset's ID?" without knowing any component's layout.
- **In imported files**: a material names its shader and textures, a state
  machine its clips. These are binary, in formats the walker doesn't know.
  Rather than teach the cooker every format, each importer says what it
  wrote ("I referenced these IDs") when it runs, and the database keeps
  that list beside the import.

Some links no walk can see: game code that loads a sound by an ID written
in the code. Those go in an include list that's always packed. Missing
one is the classic "works in the editor, crashes in the shipped game" bug,
which is why a cook fails loudly, with every problem listed, rather than
writing an archive with holes.

## The archive

One file, laid out for reading quickly:

- A **header** says what it is and where the index is.
- Each asset's **bytes**, one after another, each starting on a 4 KiB
  boundary. Disks and the operating system work in pages of that size, so
  one asset can be read, or mapped into memory, on its own without
  touching its neighbours.
- An **index** of every asset's ID, offset, and size, sorted by ID, so
  finding one is a binary search: about 17 comparisons for 100,000 assets.

Compression is per section of each imported file, as in the Cache, but
harder: zstd at level 19 instead of 3. It takes much longer to write, but
a cook happens once per release, and decompressing is just as fast at
every level, so the player only gains: a smaller download and less to
read from disk.
