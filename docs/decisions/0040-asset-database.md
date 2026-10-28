# 0040 — Projects, the asset database, and the import cache

**Status:** Accepted (2026-10-28). Names are placeholders (docs/naming.md).

## Context

ADR 0021 gives every asset a GUID (inside Crucible's own files, in a
sidecar for foreign ones), an asset database indexing assets, settings, and
dependencies, and a local cache of imported data keyed by a hash of source
contents, import settings, and importer version. ADR 0039 fixed the cached
files' format. M4 decision L settles the project layout, the sidecar
format, the hash, where the pipeline lives, and how the index persists.

## Options

**Project layout:** separate folders for content, code, plugins, and the
cache (Unity), or the project root as the content folder with a hidden
cache folder (Godot).

**Hash:** XXH128 from Microsoft's `System.IO.Hashing` package (MIT, ~10+
GB/s); SHA-256, built into .NET (~1.5–2 GB/s with the CPU's SHA
instructions; cryptographic strength not needed); or our own xxHash3 port.

**Where the pipeline lives:** a new editor-side assembly (ADR 0002), or
inside `Crucible.Engine` behind the editor symbol.

**Index between runs:** rescan the content folder on every open, with a
stamp file so unchanged files skip hashing; or a persistent database
(SQLite would be a new dependency) updated incrementally.

## Decision

1. **Separate folders:** a project is a folder with a JSON project file
   (`<Name>.crproject`), `Assets/` (content and sidecars), `Code/` (the
   game's C# project), `Plugins/`, and `Cache/` (imported data and the
   database's own files, never in source control).
2. **XXH128** through `System.IO.Hashing` 10.0.14 (MIT), pinned like every
   package (lock files). Used by the editor only; never shipped.
3. **A new editor assembly, `Crucible.Assets`:** projects, sidecars,
   scanning, importers, and the cache. It references `Crucible.Engine` and
   is never part of a shipped game. The engine keeps what shipped games
   need: `AssetId`, and later loading imported data by ID.
4. **Every open rescans `Assets/`:** sidecars are the source of truth, so
   the index can't drift from the files. A stamp file in `Cache/` (each
   file's size, modification time, and hash) lets unchanged files skip
   hashing.

## Why

Separate folders make it obvious where things go and what to ignore in
source control. XXH128 is built for exactly this, from the .NET team, and
fast enough that hashing never dominates an import. The pipeline is
editor code (ADR 0002), so keeping it out of the engine keeps shipped games
small. Rescanning with stamps is nearly as fast as a database for the sizes
Crucible targets, and there is nothing to repair when files change behind
the editor's back.

Given up: a persistent index for very large projects, and opening a project
without walking its folder.

## As built (M4 step 3)

- **`AssetId`** (`Crucible.Engine`, namespace `Crucible.Assets`): a GUID,
  `None` when empty. The serializer writes it as the GUID's string, or
  null for `None`.
- **Projects** (`Crucible.Assets.Project`): `Create` makes the folders, the
  project file (`{ "Name": ... }`), and a `.gitignore` holding `/Cache/`;
  `Open` reads one.
- **Sidecars** (`<file>.meta`, written by hand because the settings' type
  depends on the importer): `Guid`, `Importer`, and the importer's
  `Settings`, written by its generated serializer. Every file in `Assets/`
  gets one, even with no importer (its `Importer` is empty until one
  arrives, which it then takes).
- **Scanning** (`AssetDatabase.Open` and `Refresh`): every file under
  `Assets/` except sidecars, hidden files and folders, and writes in
  progress, in ordinal path order; asset paths are relative to `Assets/`
  with '/' on every platform.
  - A file without a sidecar gets one with a new GUID.
  - Two files with one GUID (copied with the sidecar): the later one in
    path order gets a new GUID, saved, with a warning.
  - A sidecar whose file is gone, an importer that isn't installed, or
    settings an importer can't read: a warning.
  - A sidecar that can't be read: a warning, the asset is skipped, and the
    file is left untouched for whoever is editing it.
  - `Cache/stamps.json` keeps each file's size, modification time (UTC
    ticks), and XXH128; files whose size and time are unchanged aren't
    rehashed.
- **Importers:** `Importer<TSettings>` with a `Name` (stored in sidecars),
  a `Version`, its `Extensions`, and `Import(context, settings)`. The
  context reads the source, reads dependencies (recording their hashes),
  and creates the one output, in ADR 0039's format. A throwing importer
  fails that asset only.
- **The cache:** an import's key is the XXH128 of a key-format number, the
  importer's name and version, the source's hash, each dependency's path
  and hash, and the settings re-serialized (so reformatting a sidecar by
  hand changes nothing). The output is `Cache/Imported/<first two hex
  digits>/<key>.bin`, compressed at the fast level. `Cache/imports.json`
  records each asset's key and dependencies; an asset is up to date when
  its key, recomputed from the current hashes, matches and the output
  exists. A missing dependency makes it stale.
- **Not yet:** Crucible's own files keeping their GUID inside (with scenes
  in step 4), watching files while the editor runs, cleaning up outputs no
  key points at any more, and importing in parallel.
- **Tests** (`Crucible.Assets.Tests`, JIT: the editor runs on a JIT):
  project folders; sidecars and stable GUIDs across opens (with no
  rehashing the second time); moves with and without the sidecar; copies;
  importing once then reusing the cache across sessions; reimports for a
  changed source, settings, importer version, dependency, or a deleted
  cache, and none for a newer time alone or a reformatted sidecar; failing,
  unknown, and late-arriving importers; unreadable sidecars.
