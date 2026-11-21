# 0109 — Tests in parallel

**Status:** Accepted (2026-11-19). Names settled by the owner as drafted
(2026-11-20; docs/naming.md).

## Context

Waiting on the test suite broke the owner's flow. A full Linux release run
took 101 s, though its tests add up to 253 s and so need about 32 s on 8
jobs: 20 of them ran one after another. The editor's checks and the sample
tests opened the Walker's and the Diorama's projects themselves, importing
(and, for the Diorama, building its C++ module) into each sample's own
Cache, so two at once would have raced; the RESOURCE_LOCKs
`walker-project-cache` and `diorama-project-cache` kept them apart. Almost
every check then copied the project, Cache left out, and worked on the
copy: the import into the sample was wasted, and was the only reason for
the locks.

## Decision

Decided by the owner (2026-11-19): keep every test, and remove the locks.

1. **Checks start with no project open.** CTest passes the sample as
   `--game-arg source-project=<.crproject>` instead of `--project`; a
   check copies it into its own folder (`AssetChecks.SourceProjectFile`)
   and opens the copy. The four checks that worked on the sample in place
   (viewport, hierarchy, inspector, scene loading) copy it too
   (`AssetChecks.OpenCopy`); the first three run until they finish
   (`--frames 100000`, as the others) since the copy imports cold.
2. **The locks are gone.** Nothing writes into the samples during a run,
   so the package tests copying a sample with its Cache need no lock
   either.
3. **More jobs.** One preset at a time: `-j16` on the 24-core Linux machine
   (about 4 GB more memory at its peak), `-j8` on the 8-core Windows one.
   The test presets don't set a count, since the machines differ.

## Results (Linux, 497 tests, all passing)

| | Before | Locks gone, `-j8` | `-j12` | `-j16` |
|---|---|---|---|---|
| Release | 101 s | 32 s | 26 s | 23 s |
| Debug | | 40 s | | 28 s |

On Windows (8 logical processors, `-j8`, all 497 passing) debug takes
165 s and release 133 s. There the packaging tests dominate: the longest
(`samples.walker-packaged`) takes 90 s alone, so more jobs won't help.

Running debug and release at once (`-j16` each) ran the machine out of
memory, with 23 GB already in use by other programs; they run one after
the other.

## Consequences

- Tests no longer import into the samples' Caches, so a run never changes
  the projects the owner opens by hand, and a sample edited by hand is
  what the tests copy.
- Each check imports its copy cold; that cost was already paid by the
  checks that copied, and the three in-place checks now pay it too (a few
  seconds each, in parallel).
- A new check that changes a sample must copy it, through the same
  helpers.
