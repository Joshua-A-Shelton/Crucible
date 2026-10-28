# 0038 — The serializer generator

**Status:** Accepted (2026-10-27). Names are placeholders (docs/naming.md).

## Context

ADR 0021 makes authored files (scenes, prefabs, settings) JSON, written by
source-generated serializers that are AOT-safe and reflection-free, with
stable key order, one field per line, defaults for new fields, skipped
removed fields, a "formerly named" attribute, and per-type versions with
C# migrations. Scenes (M4 step 4) will also need hooks for entity
references. M4 decision J settles how the serializers are made.

## Options

**Generation:**

- **Crucible's own generator** (`tools/Crucible.Generators`, ADR 0036)
  writing reader and writer code over `Utf8JsonWriter` and
  `Utf8JsonReader`, .NET's low-level JSON API (part of the runtime).
- **`System.Text.Json` source generation** (`[JsonSerializable]`
  contexts): mature and AOT-safe, but renames, versioned migrations, and
  component values need hand-written converters, game types must be listed
  in a context, and it's separate from the binary and editor generators.
- Reflection-based `System.Text.Json`: breaks NativeAOT (ADR 0001).

**Which members are saved:** public members of opted-in types, plus marked
private ones (Unity's model); or only marked members.

**Migrations:** a static function editing the old data as a JSON tree, or
old versions of the type kept with conversions.

**Default values:** written, or omitted.

## Decision

1. **Crucible's own generator,** in `tools/Crucible.Generators`, over
   `Utf8JsonWriter`/`Utf8JsonReader`. No new dependency. The same
   generator grows the binary serializer and editor metadata later.
2. **Types opt in with an attribute.** Their public fields and public
   properties with a getter and setter are saved, in declaration order; an
   attribute skips one, and the same opt-in attribute on a private member
   includes it.
3. **Migrations edit the JSON tree:** a type declares its version, and a
   static `Migrate(JsonObject data, int fromVersion)` upgrades older data
   before the generated reader runs. Only old files pay for it.
4. **Every field is written,** defaults included: a changed default in
   code never silently changes existing files.

## Why

Our own generator makes every ADR 0021 requirement ordinary generated code
instead of converters written per type, and it is the same machine the
binary serializer and editor metadata need. Saving public members matches
what game programmers expect from Unity; opting in at the type keeps
anything from being saved by accident. Migrations on a JSON tree are short
to write and cost nothing for current data. Writing every field keeps files
honest about what they contain.

Given up, for now: inheritance between serialized classes, polymorphic
fields (a base-typed field holding derived types), generic serialized
types, and comments in files (ADR 0021).

## As built (M4 step 1)

- **Attributes** (`Crucible.Serialization`): `[Serialized]` on a type, or
  on a non-public member of one; `[Serialized(Version = n)]`;
  `[NotSerialized]`; `[FormerlyNamed("old")]`, repeatable.
- **The generated code lives inside the type,** so serialized types (and
  the types they're nested in) are `partial`. It implements
  `IJsonSerialized<T>` with static `WriteJson` and `ReadJson`, so
  `Json.Serialize<T>` and `Json.Deserialize<T>` call it directly. One file
  per type, so editing a type regenerates only its file. A
  `JsonValues` class holds the per-value reads and writes the code calls.
- **The file format:**
  - Keys are the C# member names, in declaration order. A versioned type
    writes `"$version"` first; version 1 writes none, so data saved before
    a type was versioned reads as version 1.
  - Two-space indents, '\n' line endings on every platform, a final
    newline.
  - Floats in the shortest form that reads back bit for bit; NaN and the
    infinities, which JSON numbers can't hold, as strings.
  - Vectors, quaternions, and matrices (row by row) on one line:
    `[1, 2.5, -0.1]`.
  - Enums by name (flags comma-separated); GUIDs as strings; null for null
    references and empty nullables; dictionary keys in ordinal order.
- **Supported members:** `bool`, the integer types, `float`, `double`,
  `string`, enums, `Guid`, `Vector2/3/4`, `Quaternion`, `Matrix4x4`,
  nullables, arrays, `List<T>`, `Dictionary<string, T>`, and other
  serialized types (from any assembly). Entity and asset references come in
  steps 4 and 3.
- **Reading:** creates the value with its parameterless constructor (any
  accessibility), so missing members keep their initializers' defaults;
  unknown members are skipped, which is how removed fields are handled; a
  member matches its name or any former one. Older versions are parsed
  into a `JsonObject`, migrated, and read from the result; a newer version
  is an error. Errors name the path through the types ("Character.Stats:
  Stats.Health: expected a number, found a string (at byte 23)").
- **Compile errors** (IDs placeholders): `CRU0101` not partial; `CRU0102`
  generic, abstract, static, a ref struct, or a class with a base class;
  `CRU0103` a class without a parameterless constructor; `CRU0104` a
  member type that can't be serialized; `CRU0105` a member that can't be
  read back (readonly, init-only, no getter or setter); `CRU0106` two
  members sharing a name, former names included; `CRU0107` a version below
  1, or above 1 without a matching `Migrate`; `CRU0108` `[Serialized]` on
  a member of an unserialized type, or with `[NotSerialized]`.
- **Build:** the generator's project also ignores the command line's
  runtime (`TreatAsLocalProperty`), which an AOT publish passes to every
  project: it gave the lock file a platform-specific section, different on
  Linux and Windows.
- **Tests:** `Crucible.Engine.Tests` (JIT and NativeAOT) round-trips every
  supported member type (bytes identical on a resave), checks the exact
  file layout, 2,000 random floats bit for bit, defaults, skipped and
  renamed members, three versions of migration, and error messages.
  `Crucible.Generators.Tests` checks the generated code compiles, member
  order, nesting, every compile error with its location, and caching.
