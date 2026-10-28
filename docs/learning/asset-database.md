# The asset database and the import cache

A learning note for M4 step 3 (ADR 0040): how Crucible knows which assets
exist, and how it avoids importing anything twice.

## A project on disk

```
MyGame/
  MyGame.crproject      the project file
  .gitignore            keeps Cache/ out of source control
  Assets/
    hero.png            a source file
    hero.png.meta       its sidecar: GUID, importer, settings
  Code/                 the game's C# project
  Plugins/
  Cache/                rebuildable: delete it any time
    stamps.json         size, time, and hash of every file
    imports.json        what each asset's last import depended on
    Imported/4f/4f3a….bin
```

## Identity: GUIDs, not paths

A scene that refers to `Assets/hero.png` by path breaks when the file is
renamed. So references store the asset's GUID instead, kept in its sidecar:

```json
{
  "Guid": "8f14e45f-ceea-467a-9a36-dedd4bea2543",
  "Importer": "Texture",
  "Settings": { ... }
}
```

Move or rename a file **with its sidecar** and everything still finds it.
Move it without the sidecar and it gets a new GUID (the old sidecar is
reported as orphaned). Copy both and the copy gets a new GUID, since two
assets can't share one.

## Sidecars are the source of truth

Every time the database opens, it walks `Assets/` and reads the sidecars.
There is no separate index that could drift out of step with the files:
if a teammate's commit adds, moves, or deletes files, the next open simply
sees the new state.

Walking is cheap; *hashing* every file's contents isn't. So
`Cache/stamps.json` remembers each file's size and modification time with
its hash, and only files whose size or time changed are hashed again.

## The import cache: name outputs by their inputs

An import's output depends on:

- the source file's contents,
- the import settings,
- the importer and its version,
- any other files it read (a glTF's buffers, say).

Hash all of those together and you get a **key**. The output is stored
under that key: `Cache/Imported/4f/4f3a….bin`. To check whether an asset
needs importing, recompute the key from the current files: if a file with
that name exists, it's exactly what an import would produce now.

This is *content addressing*, the same idea as git's objects or a build
cache. Some consequences:

- Nothing is reimported unless an input really changed. Touching a file
  without changing it (a newer time) costs a rehash, not an import.
- Reformatting a sidecar by hand changes nothing, because the settings are
  re-serialized before hashing.
- Raising an importer's `Version` changes every key it made, so exactly
  its assets reimport.
- Deleting `Cache/` is always safe: everything is rebuilt from source.

Dependencies are recorded during the import (each `ReadDependency` call),
because they aren't known beforehand: a glTF lists its buffers inside
itself. The next check hashes those recorded files again.

## XXH128

The hash is XXH128 (from Microsoft's `System.IO.Hashing`): 128 bits, so
two different inputs essentially never share a key, and fast enough
(over 10 GB/s) that hashing never dominates. It isn't cryptographic,
which doesn't matter here: nobody is trying to forge a texture.
