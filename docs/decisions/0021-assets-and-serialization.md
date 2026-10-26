# 0021 — Assets and serialization

**Status:** Accepted (2026-10-25). Importer libraries are decided
separately.

## Context

Engines keep two forms of each asset: **source assets** (model files,
images, audio, and Crucible's own authored files: scenes, prefabs,
materials, settings), kept in source control; and **imported assets**,
engine-ready (GPU-compressed textures with mips, GPU-ready mesh buffers),
fast to load and never hand-edited. The editor converts one to the other;
a shipped game contains only imported data. Hot reload already relies on
serializing managed components (ADR 0012).

## Options considered

- **Identity:** paths (break on rename or move; a long-standing Godot pain
  point) or GUIDs (Unity's `.meta` sidecars).
- **Authored text format:** JSON via `System.Text.Json` (part of .NET, AOT-
  safe source generation; no comments, somewhat noisy), YAML via
  YamlDotNet (MIT; reflection-heavy, against ADR 0001), or our own format.
  Binary authored files (Unreal's `.uasset`) can't be merged.
- **Prefabs:** no nesting; nesting with outermost-level overrides only;
  full nesting with overrides at every level (Unity: powerful, notoriously
  complex).

## Decision

1. **Every asset has a GUID; references store GUIDs.** Crucible's own files
   store their GUID inside themselves; foreign files get a sidecar with the
   GUID and import settings. The editor tracks renames and moves.
2. **An asset database** indexes every asset, its import settings, and its
   dependencies. Imported results live in a **local cache keyed by a hash
   of source contents, import settings, and importer version,** out of
   source control and rebuildable at any time.
3. **Authored files are text; imported data is binary.** The text format
   is **JSON** (via `System.Text.Json`), with stable key order and one
   field per line so diffs stay clean; revisited later. The binary format
   is Crucible's own, built for fast loading; blittable components are
   copied straight into native memory.
4. **Serializers are source-generated** by the same generators as editor
   metadata (ADR 0006), AOT-safe and reflection-free; C++-backed components
   come through the binding generator (ADR 0017). Text writes fields by
   name; binary uses the exact layout. Hot reload's serialize-and-recreate
   (ADR 0012) uses the same serializers.
5. **Versioning:** new fields get defaults, removed fields are skipped,
   renamed fields use a "formerly named" attribute, bigger changes use a
   per-type version number and a C# migration function.
6. **Prefabs:** property overrides plus added components or children.
   Prefabs may nest, but overrides apply only at the outermost level.
7. **Cooking:** a shipping build walks dependencies from the root scenes
   and packs only reachable assets into archives. **Loading is
   asynchronous,** on background-priority jobs (ADR 0014).
8. **Save data** uses the same serializer machinery in its own versioned
   format, captures plain C# game state (ADR 0012) plus the world
   components a game marks as saved, and must load across game updates.
   Revisited later.

## Why

GUIDs make renames and moves safe. Content-hash caching means nothing is
reimported unless it changed, and an importer update reimports exactly
what it affects. Text authored files diff and merge; binary imported data
loads fast. One generated serializer stack serves the editor, the cooker,
saves, and hot reload. Outermost-only prefab overrides cover common
nesting (a house prefab containing door prefabs) without multi-level
override resolution.

Given up: comments in authored files (JSON), and full nested-prefab
overrides.
