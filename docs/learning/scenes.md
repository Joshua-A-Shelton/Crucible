# Scenes

A learning note for M4 step 4 (ADR 0041): how a world becomes a scene file
and back, and why the file looks the way it does.

## What gets saved

Mark a component type for saving with both attributes:

```csharp
[Component, Serialized]
public partial struct Health
{
    public int Value;
}

[Component]                 // registered, but never saved: a runtime cache
public struct PathCache { ... }
```

`[Component]` registers the type (the generator writes the call);
`[Serialized]` gives it a JSON reader and writer. Both together put it in
scenes. Anything recomputed at runtime should stay unsaved.

## The file

```json
{
  "$guid": "5e1f0c2a-9b3d-4e6f-8a7b-0c1d2e3f4a5b",
  "Entities": [
    {
      "Id": "720e61166ec44974",
      "Components": {
        "Game.Health": { "Value": 10 }
      },
      "Children": [
        {
          "Id": "1a0ba0d4c9bea5fe",
          "Components": {
            "Game.Follow": { "Target": "720e61166ec44974", "Distance": 1.5 }
          }
        }
      ]
    }
  ]
}
```

- **`$guid`** is the scene's own asset ID. Crucible's own files carry it
  inside, so they need no sidecar.
- **`Id`** is the entity's scene ID: random, 64 bits, and kept across
  saves (in a `SceneEntityId` component). Random rather than numbered so
  that adding an entity doesn't renumber the rest of the file, and so two
  people adding entities on different branches don't pick the same one.
- **Components** are keyed by type name, in name order, so the file only
  changes where the scene did.
- **Children** are nested inside their parent, in order, like the editor's
  scene tree.

## Entity references

A component can point at another entity:

```csharp
[Component, Serialized]
public partial struct Follow { public Entity Target; public float Distance; }
```

An `Entity` value is an index and a generation, which mean nothing in
another session. So while a scene is saved, the generated serializer writes
the target's *scene ID* instead; while it loads, it maps that ID to the
newly created entity. A reference to an entity outside the scene (or
destroyed) is written as null.

Loading reads the file twice. The first pass creates every entity and the
hierarchy; the second reads components. By then every scene ID has an
entity, so a reference can point anywhere in the file, even at an entity
that appears later.

## Loading adds, it doesn't replace

`Scene.Load` creates the scene's entities beside whatever the world holds.
Load the same file twice with `newIds: true` and you get two independent
copies, each with its own scene IDs and with references kept inside each
copy. Prefabs (step 8) build on this.

## Round trips

The milestone's test is that saving, loading, and saving again gives the
same bytes. That checks everything at once: every value survives, IDs are
kept, order is stable, and nothing drifts.
