# Running standalone: one game, two hosts

A learning note for M7 step 7a (ADR 0073).

## Why run outside the editor at all?

Play runs your game inside the editor's process, which is what makes it
instant: no new process, no loading, and hot reload of your code while it
runs. But it also means your game shares everything with the editor. A
crash in native code takes the editor down with it. Memory the editor
uses is counted as yours. And the editor's systems run around your game
every frame.

Running standalone starts the game the way a player would: the plain game
launcher, its own window, and only the engine and your game inside it.
If it behaves differently from Play, something in your game depends on
the editor, and you want to find that out early.

## What the game needs to start

A shipped game and a game run from the editor both need the same three
things:

- **The engine and the game's code.** The launcher starts .NET, loads the
  engine, then loads the game's assembly (`--game`), registering its
  components and systems.
- **The assets.** The renderer asks for an asset by ID and gets its bytes
  back from a *reader*. In the editor, the reader is the asset database,
  which can import things on demand. A game can't import: importers are
  editor tools (and native libraries the game shouldn't ship). It can
  only read what was already imported.
- **A scene to start in.**

## The cache is the contract

The editor's asset database writes every import into `Cache/Imported`,
named by a hash of everything that went into it, and records each asset's
current hash in `Cache/imports.json`. That makes a simple contract
between the two sides: the editor guarantees the cache is up to date
(it imports what changed before running), and the game only has to look
up an ID in `imports.json` and open one file. That's `AssetCache`: a few
dozen lines, no importers, nothing written.

The same split shows up in bigger engines as *cooking*: turning editor
assets into the exact files a game reads. Crucible's cache is a cook in
miniature. A real packaging step will copy only what a game uses, and
pack it into fewer files, but the game's side stays a reader of finished
data.

## Default behavior beats boilerplate

The Diorama sample opens its project itself, with a setup system that
imports and loads everything. Every project could do the same, but then
every project would carry the same code. Instead, the engine does it
when it's given a project and a scene, before your systems start: the
scene's entities are already there in your first `Start`.

## Talking to a child process

The editor starts the game with .NET's `Process`, reads its output on
background threads, and hands the lines to the Log on the main thread
through a queue, since UI may only be touched from the main thread. The
end of the process is noticed by polling once per frame. Before it reports
the exit code, it waits for the output readers to reach the end, so the
last lines aren't lost after "ended".
