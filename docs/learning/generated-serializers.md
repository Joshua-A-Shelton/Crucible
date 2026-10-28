# Generated serializers

A learning note for M4 step 1 (ADR 0038): how `[Serialized]` types become
JSON and back, and what to know when changing a saved type.

## Using it

```csharp
[Serialized]
public partial class Character
{
    public string Name { get; set; } = "Unnamed";  // saved: public get and set
    public Vector3 Position;                        // saved: public field
    [Serialized] private int _secret;               // saved: marked
    [NotSerialized] public int Cache;               // not saved
    public int Score => _secret * 2;                // not saved: computed
}

byte[] json = Json.Serialize(character);
Character copy = Json.Deserialize<Character>(json);
```

The type must be `partial` because the generator adds two methods to it.
That also lets the generated code reach private members, which code in
another class couldn't.

## What gets generated

For each type, a `WriteJson` that writes each member in order through
`Utf8JsonWriter`, and a `ReadJson` that loops over the object's properties
through `Utf8JsonReader`, matching names. Look at one: build with
`EmitCompilerGeneratedFiles=true`, and the files appear under `obj/`.

`Utf8JsonReader` is a *streaming* reader: it walks the bytes token by
token without building a tree first, which is why it's fast and allocates
little. The generated code is the same thing you would write by hand with
it; the generator just writes it for every type.

Static interface members (`static abstract` in `IJsonSerialized<T>`) let
`Json.Serialize<T>` call `T.WriteJson` without reflection or virtual calls.
The compiler sees the exact type, which is what NativeAOT needs.

## Changing a saved type

| Change | What to do | Old files |
|---|---|---|
| Add a field | Nothing | It keeps its default (the initializer) |
| Remove a field | Nothing | Its value is skipped |
| Rename a field | `[FormerlyNamed("Old")]` on the new name | Read under the old name, written under the new |
| Change a field's meaning or type | Raise the version, write `Migrate` | Upgraded before reading |

A migration edits the old data as a tree, one version step at a time:

```csharp
[Serialized(Version = 2)]
public partial class GameSettings
{
    public float Volume = 1;  // version 1 stored "VolumePercent" (0-100)

    static void Migrate(JsonObject data, int fromVersion)
    {
        if (fromVersion < 2)
        {
            data["Volume"] = data["VolumePercent"]!.GetValue<int>() / 100f;
            data.Remove("VolumePercent");
        }
    }
}
```

Only files older than the current version build the tree; current files
go straight through the streaming reader.

## Why every field is written

A file holds every value, defaults included. If a default changes in code
later, files that were saved keep the value they were saved with. Omitting
defaults would make files smaller but let a code change silently change
saved data.

## Floats

`0.1f` is written as `0.1`, not `0.100000001490116`: .NET writes the
shortest text that reads back to exactly the same float. The tests check
2,000 random floats bit for bit. JSON numbers can't hold NaN or the
infinities, so those are written as the strings `"NaN"`, `"Infinity"`,
`"-Infinity"`.
