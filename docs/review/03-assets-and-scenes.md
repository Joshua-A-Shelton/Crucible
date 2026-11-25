# Review 3: assets and scenes

How files on disk become what a game uses: projects and the asset
database, importers and the import cache, the binary format, scenes and
prefabs, cooking a game's archive, and loading. Decisions: ADRs 0021,
0022, 0038, 0039, 0040, 0041, 0045, 0049, 0076, 0087, 0098, 0099, 0115.

## What it does

Artists' files (glTF, FBX, PNG, WAV, …) go in a project's `Assets/`
folder. The editor imports each into a compact binary file in `Cache/`,
keyed by what went into it, so nothing is imported twice. Scenes and
prefabs are JSON, readable and diffable. Shipping a game cooks everything
it reaches into one archive, which the game loads from on background jobs.

```
 Assets/ (source)        editor (Crucible.Assets)            Cache/            shipped game
 ────────────────        ────────────────────────            ──────            ────────────
 Walker.gltf      ──►    asset database: GUIDs,       ──►    imported files ─► cook ─► Game.crpak
 Walker.gltf.crmeta      sidecars, importers, hashes         (binary, zstd)            (one archive)
 Walk.crscene (JSON)                                                                    loaded on jobs
```

## How it works

### Projects and the asset database (ADR 0021, 0040)

- **A project is a folder:** `<Name>.crproject`, `Assets/`, `Code/`,
  `Plugins/`, and `Cache/` (never in source control).
- **Every asset has a GUID.** Crucible's own files carry it inside;
  anything else gets a sidecar (`.crmeta`) with the GUID and its import
  settings. References between assets are GUIDs, so renaming or moving a
  file breaks nothing.
- **The database rescans `Assets/` on every open:** the sidecars are the
  truth, so the index can't drift from the files. A stamp file lets
  unchanged files skip hashing.
- **The database is editor-only** (`Crucible.Assets`); a shipped game only
  knows asset IDs and reads imported data through an asset source.

### Importers and the cache (ADR 0022, 0039, 0087)

- **Importers turn a source into an imported file:** models through cgltf
  and ufbx, optimized with meshoptimizer; textures decoded by stb_image,
  mipmapped with Crucible's own filter, and compressed to BC formats;
  sounds, shaders, materials, fonts, styles, state machines, tilesets,
  tilemaps, chains.
- **Cached by a hash** of the source, its import settings, and the
  importer's version: change any of them and it imports again; change
  nothing and it's skipped.
- **One binary container for everything imported:** a header, a table of
  sections, each section compressed with zstd and checked with a CRC.
  Payloads are plain data with offsets, used in place once loaded. Old
  versions are rejected and reimported, never migrated: it's a cache.
- **Import settings** are edited in the Inspector, applied with Apply,
  and stored in the sidecar.

### Serialization (ADR 0021, 0038)

- **Authored files are JSON,** with stable key order, one field per line,
  so diffs are clean. **Imported data is binary.**
- **Serializers are generated** at compile time (no reflection, safe for
  NativeAOT) for any `[Serialized]` type. New fields get defaults, removed
  ones are skipped, renamed ones are matched by a "formerly named"
  attribute.

### Scenes and prefabs (ADR 0041, 0045)

- **A scene is a JSON tree of entities**, children nested in their parent,
  each entity with a random 64-bit scene ID kept across saves (so branches
  can't collide) and its saved components by name.
- **`[Component, Serialized]`** makes a component saved; components that
  aren't serialized (caches, per-frame state) aren't.
- **Prefabs** are reusable entity trees. An instance stores only what it
  overrides, field by field, so later edits to the prefab still arrive.
  Entities inside an instance get IDs derived from the instance's, so
  overrides find them. **An imported model is a prefab** directly, one
  entity per node.
- **Services** (`[Service]` classes) hold game state that isn't entities;
  serialized ones are saved together as one document.

### Cooking and loading (ADR 0049, 0076, 0098, 0099)

- **Cooking** walks references from the build's scenes (and an include
  list for what code loads by ID) and packs every reachable asset into one
  archive, its sections recompressed hard.
- **One asset source at a time** serves bytes by ID: the editor's
  database, a project's imported files, or an archive. The renderer,
  prefabs, and C# all read through it.
- **Loading is asynchronous:** acquiring a mesh, texture, sound, shape, or
  clip returns at once; a background job reads, decompresses, and creates
  it; `Assets.Publish` hands finished ones over on the main thread. Things
  appear when they're ready (a game's start scene waits for all its loads
  before the first frame).

## Why it was built this way

- **GUIDs and sidecars** are how Unity keeps references stable through
  renames; the cost is a sidecar per foreign file.
- **A content-hashed cache** makes imports predictable and shareable: the
  same inputs always give the same output, and a clean checkout rebuilds
  it.
- **JSON for authored files** because people diff and merge them in
  source control; binary only where speed matters.
- **Generated serializers** because the same game code must run under
  NativeAOT, where reflection-based serialization doesn't work.

## What I'd question

1. **A scene can lose data silently.** A component nothing registers (a
   plugin removed, an official plugin left out, game code renamed) is left
   out when the scene loads, with a warning in the log; saving the scene
   then writes it without that data. Keeping unknown components as JSON,
   untouched, until something registers them again would make removing a
   plugin safe.
2. **Editor code can't ask an asset about itself.** The preview read mesh
   bounds by opening imported files itself, and the character editor
   measured a clip's length by setting its time past the end. A small
   "asset info" API (a clip's length, a mesh's bounds, a texture's size)
   would serve editors and games alike.
3. **Save games aren't built.** ADR 0021 sketched them (the same
   serializers, versioned, loading across game updates) and left them for
   later; the first game needs them, and their versioning rules deserve a
   decision before there are saves to keep working.
4. **Prefab overrides only at the outermost level.** A prefab inside a
   prefab can't keep its own overrides of what's nested in it; fine now,
   limiting once games build prefabs of prefabs.
5. **File watching is timing-sensitive.** Changes are found by watchers
   that wait for quiet; on Windows one settled late during a package run
   (the fix to the packaged check in step 10). Fine for people, fragile
   for automation that moves files around.
