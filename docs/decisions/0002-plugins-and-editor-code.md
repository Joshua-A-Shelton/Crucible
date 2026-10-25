# 0002 — Plugins and editor code

**Status:** Accepted (2026-10-24).

## Context

Goal 3: a visual editor that supports plugins, where a plugin can contain
both game code and editor code, and editor code is removed from final
builds. ADR 0001 already fixed how C# runs: per-assembly hot reload in the
editor, JIT or NativeAOT when shipping.

## Options

How editor code is kept out of shipping builds:

- **(i) Separate assemblies.** Each plugin has a runtime assembly and an
  editor assembly; the editor one may reference the runtime one, never the
  reverse. Shipping builds leave editor assemblies out. (Unity Editor
  asmdefs, Unreal `Type = Editor` modules.) The compiler enforces the
  split, but small editor touches end up in a different project from the
  code they're for.
- **(ii) A conditional-compilation symbol** in one assembly
  (`#if CRUCIBLE_EDITOR`, placeholder; Unity's `UNITY_EDITOR`). Convenient,
  but the shipping configuration can break without anyone noticing.
- **(iii) Both,** with a build check that compiles the shipping
  configuration.

Separately, **attributes** in runtime code can mark data for the editor at
almost no cost in shipping builds.

## Decision

- **Option (iii).** Editor assemblies are the main mechanism; the editor
  symbol is allowed for small cases inside runtime code. A build check
  compiles the shipping configuration so code under the symbol can't break
  it unnoticed.
- **Attributes** mark that data should be exposed to the editor, and to
  some degree how (e.g. as a slider with a range). They can't build a
  custom editor: anything beyond exposing data, up to whole tools such as
  a non-linear video editor, lives in editor assemblies.
- **Plugins are C#.** C++ never extends the editor, so there is no
  runtime/editor split to enforce on the C++ side. C++ may get its own,
  different extension mechanism for the runtime (open item).
- **Plugins are source-only in v1,** compiled with the project. Prebuilt
  plugins are revisited later.
- **The game is the root plugin.** A game is structured like any other
  plugin; its assembly is `<GameName>.dll`. A shipped game may have a
  native executable named after the game, but the only real difference
  from any other plugin is that it's the one loaded as the root.

## Why

Separate assemblies make the split a compiler guarantee and fit ADR 0001
(hot reload and AOT trimming work per assembly). The symbol keeps small
editor-only touches next to their code, and the shipping build check
removes its main risk. Attributes cover the common case, exposing a field,
without a second project.

C#-only editor extension keeps one way to extend the editor, and the
native core stays free of editor plumbing.

Treating the game as the root plugin means one model to learn and build:
everything a plugin can do, a game can do, and vice versa.

Given up: C++ editor extensions, and (for now) binary plugin distribution.

## Consequences (to be designed later)

- Under NativeAOT there is no `<GameName>.dll` to load: the root plugin
  and its dependencies are compiled into the native binary. "Loading the
  root plugin" has to mean the same thing in both shipping modes.
- The native executable embedding .NET (JIT) or linking the AOT output is
  the hosting design, still to be decided.
