# 0007 — License

**Status:** Accepted (2026-10-24).

## Context

An engine's license reaches every game made with it: whether a game must
show credits, publish its changes, or worry about patents.

## Options

| License | What it asks of a shipped game |
|---|---|
| zlib | Nothing; only source copies keep the notice, altered source is marked |
| MIT | The notice in copies, usually read as covering binaries (license screens) |
| BSD-3 | As MIT, plus no endorsement using the author's name |
| Apache 2.0 | Notice, NOTICE file, marked changes; explicit patent grant |
| MPL 2.0 | Changes to Crucible's own files must be published |

## Decision

Crucible is licensed under the **zlib license** (`LICENSE`, copyright
Josh Shelton), matching Slag (Slag ADR 0064).

- Commercial use, modification, and closed-source games are permitted.
- Shipped games need no attribution for Crucible; source copies keep the
  notice, and altered source must be marked as modified.
- No explicit patent grant.

## Why

Nothing is asked of shipped games, which suits games better than MIT.
Game teams that modify the engine source (ADR 0006) can keep their
changes private, which MPL wouldn't allow. One license across Crucible and
Slag means one set of terms.

Given up: Apache 2.0's patent grant.

## Dependencies

Dependencies must stay compatible (CLAUDE.md §3). Their own notices still
apply to games: a game built with Crucible carries the notices of MIT
dependencies such as the .NET runtime. Needing no license screen at all
would only hold if every shipped dependency were zlib-like; that's nice
to have, but not a goal.
