# Component icons

Why the editor shows icons beside entities and components, and how it
picks them (ADR 0131).

## Why icons

A hierarchy of a hundred entities is read by scanning, not reading.
An icon is recognized faster than a word, so a column of icons lets you
find "the lights" or "the camera" without reading every name. The trick
is restraint: one quiet color, so the icons help scanning without
shouting over the names; the accent only where something is selected.

## Where an icon comes from

Each component type can name one, as an attribute on its type:

```csharp
[ComponentIcon("lightbulb")]
public partial struct Lantern { ... }
```

or on a C++ component, which the binding generator copies over:

```cpp
struct CRUCIBLE_EXPOSE CRUCIBLE_HINT_ICON("lightbulb") Lantern { ... };
```

The names are Lucide's (lucide.dev), the icon set the editor ships.

## Which icon a row gets

An entity has many components; its row shows one:

1. The most telling of a short list (a camera, a light, a mesh), the same
   list that names an entity without a name.
2. Else any of its components that has an icon, by name.
3. Else a dot: just a transform.

A prefab instance gets a small package mark after its name, or the
package itself when its root has nothing of its own (a model's meshes are
its children).

## Groups inside groups

Add Component groups components by `[Category]`. A `/` makes a group
inside a group, `"Physics/2D"`, so long lists stay short: fold Physics
and its 2D half folds with it. A search ignores the groups and shows every
match with where it lives.

## Try it

Add `[ComponentIcon("star")]` to one of your project's components and
save: after the reload, entities with it show a star.
