using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crucible.Assets;
using Crucible.Serialization;

namespace Crucible.Tests;

// Generated JSON serialization (ADR 0038), under JIT and NativeAOT: the
// types below get their readers and writers from Crucible.Generators.

public enum Faction
{
    Neutral,
    Player,
    Enemy,
}

[Flags]
public enum Traits
{
    None = 0,
    Fast = 1,
    Strong = 2,
}

[Serialized]
public partial struct Stats
{
    public int Health;
    public float Speed;

    [FormerlyNamed("Armour")]
    public int Armor;
}

[Serialized]
public partial class Character
{
    public string Name { get; set; } = "Unnamed";
    public Stats Stats;
    public Faction Faction = Faction.Neutral;
    public Traits Traits;
    public bool Alive = true;
    public byte Byte;
    public sbyte SByte;
    public short Short;
    public ushort UShort;
    public uint UInt;
    public long Long;
    public ulong ULong;
    public double Mass;
    public Vector2 Facing;
    public Vector3 Position;
    public Vector4 Color;
    public Quaternion Rotation = Quaternion.Identity;
    public Matrix4x4 Transform = Matrix4x4.Identity;
    public Guid Id;
    public AssetId Portrait;
    public AssetId Missing;
    public int? Level;
    public List<string> Tags = [];
    public float[]? Weights;
    public int[][]? Grid;
    public Dictionary<string, Stats> Buffs = [];
    public Character? Pet;

    [Serialized]
    private int _secret;

    [NotSerialized]
    public int Cache;

    // Computed: not saved.
    public int TagCount => Tags.Count;

    [NotSerialized]
    public int Secret
    {
        get => _secret;
        init => _secret = value;
    }
}

// Version 3. Version 1 stored the volume as a percentage, "VolumePercent";
// version 2 stored languages by name ("english") instead of code.
[Serialized(Version = 3)]
public partial class GameSettings
{
    public float Volume = 1;
    public string Language = "en";

    private static void Migrate(JsonObject data, int fromVersion)
    {
        if (fromVersion < 2)
        {
            data["Volume"] = data["VolumePercent"]!.GetValue<int>() / 100f;
            data.Remove("VolumePercent");
        }
        if (fromVersion < 3 && data["Language"]?.GetValue<string>() == "english")
        {
            data["Language"] = "en";
        }
    }
}

// Nested: the generator repeats the containing declarations.
public static partial class Outer
{
    [Serialized]
    public partial struct Nested
    {
        public int Value;
    }
}

public class SerializationTests
{
    private static Character Sample() => new()
    {
        Name = "Ada \"the\" Brave\n",
        Stats = new Stats { Health = 120, Speed = 0.1f, Armor = -3 },
        Faction = Faction.Enemy,
        Traits = Traits.Fast | Traits.Strong,
        Alive = false,
        Byte = 255,
        SByte = -128,
        Short = short.MinValue,
        UShort = ushort.MaxValue,
        UInt = uint.MaxValue,
        Long = long.MinValue,
        ULong = ulong.MaxValue,
        Mass = double.NaN,
        Facing = new Vector2(float.PositiveInfinity, float.NegativeInfinity),
        Position = new Vector3(1.5f, -2, 1e-7f),
        Color = new Vector4(0.1f, 0.2f, 0.3f, 1),
        Rotation = Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f),
        Transform = Matrix4x4.CreateTranslation(1, 2, 3) * Matrix4x4.CreateRotationY(0.7f),
        Id = Guid.Parse("8f14e45f-ceea-467a-9a36-dedd4bea2543"),
        Portrait = AssetId.Parse("0b6b8e1c-3f0e-4c55-a4d6-2d7c1b2f9e10"),
        Level = 7,
        Tags = ["a", "b"],
        Weights = [0.25f, float.Epsilon],
        Grid = [[1, 2], [], [3]],
        Buffs = new Dictionary<string, Stats> { ["zeal"] = new() { Health = 1 }, ["anger"] = new() { Speed = 2 } },
        Pet = new Character { Name = "Rex", Level = null, Weights = null },
        Secret = 42,
        Cache = 99,
    };

    [Test]
    public async Task EverySupportedMemberRoundTrips()
    {
        Character original = Sample();
        byte[] json = Json.Serialize(original);
        Character copy = Json.Deserialize<Character>(json);

        await Assert.That(copy.Name).IsEqualTo(original.Name);
        await Assert.That(copy.Stats).IsEqualTo(original.Stats);
        await Assert.That(copy.Faction).IsEqualTo(Faction.Enemy);
        await Assert.That(copy.Traits).IsEqualTo(Traits.Fast | Traits.Strong);
        await Assert.That(copy.Alive).IsFalse();
        await Assert.That((copy.Byte, copy.SByte, copy.Short, copy.UShort, copy.UInt, copy.Long, copy.ULong))
            .IsEqualTo((original.Byte, original.SByte, original.Short, original.UShort, original.UInt, original.Long,
                original.ULong));
        await Assert.That(double.IsNaN(copy.Mass)).IsTrue();
        await Assert.That(copy.Facing).IsEqualTo(original.Facing);
        await Assert.That(copy.Position).IsEqualTo(original.Position);
        await Assert.That(copy.Color).IsEqualTo(original.Color);
        await Assert.That(copy.Rotation).IsEqualTo(original.Rotation);
        await Assert.That(copy.Transform).IsEqualTo(original.Transform);
        await Assert.That(copy.Id).IsEqualTo(original.Id);
        await Assert.That(copy.Portrait).IsEqualTo(original.Portrait);
        await Assert.That(copy.Missing.IsNone).IsTrue();
        await Assert.That(copy.Level).IsEqualTo(7);
        await Assert.That(copy.Tags.SequenceEqual(original.Tags)).IsTrue();
        await Assert.That(copy.Weights!.SequenceEqual(original.Weights!)).IsTrue();
        await Assert.That(string.Join("|", copy.Grid!.Select(row => string.Join(",", row)))).IsEqualTo("1,2||3");
        await Assert.That(copy.Buffs["zeal"].Health).IsEqualTo(1);
        await Assert.That(copy.Buffs["anger"].Speed).IsEqualTo(2);
        await Assert.That(copy.Pet!.Name).IsEqualTo("Rex");
        await Assert.That(copy.Pet.Level).IsNull();
        await Assert.That(copy.Pet.Weights).IsNull();
        await Assert.That(copy.Pet.Pet).IsNull();
        await Assert.That(copy.Secret).IsEqualTo(42); // private, marked
        await Assert.That(copy.Cache).IsEqualTo(0);   // [NotSerialized]

        // Writing the copy gives the same bytes: nothing drifts on a resave.
        await Assert.That(Encoding.UTF8.GetString(Json.Serialize(copy))).IsEqualTo(Encoding.UTF8.GetString(json));
    }

    [Test]
    public async Task FilesAreIndentedOneFieldPerLineWithVectorsOnOneLine()
    {
        string json = Json.SerializeToString(new Stats { Health = 3, Speed = 0.1f });
        await Assert.That(json).IsEqualTo("{\n  \"Health\": 3,\n  \"Speed\": 0.1,\n  \"Armor\": 0\n}\n");

        string character = Json.SerializeToString(new Character { Position = new Vector3(1, 2.5f, -0.1f) });
        await Assert.That(character).Contains("\n  \"Position\": [1, 2.5, -0.1],\n");
        await Assert.That(character).Contains("\n  \"Traits\": \"None\",\n");
        await Assert.That(character).Contains("\n  \"Portrait\": null,\n");
        await Assert.That(character).DoesNotContain("\r");
        await Assert.That(character).DoesNotContain("TagCount");
        await Assert.That(character).DoesNotContain("Cache");
        // Buffs' keys are written in ordinal order, whatever the insertion order.
        string buffs = Json.SerializeToString(Sample());
        await Assert.That(buffs.IndexOf("\"anger\"", StringComparison.Ordinal))
            .IsLessThan(buffs.IndexOf("\"zeal\"", StringComparison.Ordinal));
    }

    [Test]
    public async Task FloatsReadBackBitForBit()
    {
        Random random = new(1234);
        float[] values = [.. Enumerable.Range(0, 2000).Select(_ => BitConverter.Int32BitsToSingle(random.Next()))];
        values = [.. values.Where(float.IsFinite), float.MaxValue, float.MinValue, float.Epsilon, -0.0f, 1 / 3f];
        Character copy = Json.Deserialize<Character>(Json.Serialize(new Character { Weights = values }));
        await Assert.That(copy.Weights!.Select(BitConverter.SingleToInt32Bits)
            .SequenceEqual(values.Select(BitConverter.SingleToInt32Bits))).IsTrue();
    }

    [Test]
    public async Task MissingMembersKeepDefaultsAndUnknownOnesAreSkipped()
    {
        Character character = Json.Deserialize<Character>("""
            {
              "Name": "Lin",
              "Removed": { "deeply": [1, { "nested": true }] },
              "Stats": { "Health": 5, "Armour": 2 }
            }
            """);
        await Assert.That(character.Name).IsEqualTo("Lin");
        await Assert.That(character.Stats.Armor).IsEqualTo(2); // its former name
        await Assert.That(character.Alive).IsTrue();          // the initializer's default
        await Assert.That(character.Rotation).IsEqualTo(Quaternion.Identity);
        await Assert.That(character.Tags).IsEmpty();
    }

    [Test]
    public async Task OlderVersionsAreMigrated()
    {
        // Version 1 had no "$version" at all.
        GameSettings v1 = Json.Deserialize<GameSettings>("""{ "VolumePercent": 40, "Language": "english" }""");
        await Assert.That(v1.Volume).IsEqualTo(0.4f);
        await Assert.That(v1.Language).IsEqualTo("en");

        GameSettings v2 = Json.Deserialize<GameSettings>("""{ "$version": 2, "Volume": 0.5, "Language": "english" }""");
        await Assert.That((v2.Volume, v2.Language)).IsEqualTo((0.5f, "en"));

        // The current version is written first, and read without migrating.
        string current = Json.SerializeToString(new GameSettings { Volume = 0.75f, Language = "english" });
        await Assert.That(current).StartsWith("{\n  \"$version\": 3,\n");
        await Assert.That(Json.Deserialize<GameSettings>(current).Language).IsEqualTo("english");

        JsonException newer = Assert.Throws<JsonException>(() => Json.Deserialize<GameSettings>("""{ "$version": 4 }"""));
        await Assert.That(newer.Message).Contains("newer than this build's 3");
    }

    [Test]
    public async Task ErrorsNameTheMemberThatFailed()
    {
        JsonException error = Assert.Throws<JsonException>(() =>
            Json.Deserialize<Character>("""{ "Stats": { "Health": "lots" } }"""));
        await Assert.That(error.Message).StartsWith("Character.Stats: Stats.Health: expected a number, found a string");

        error = Assert.Throws<JsonException>(() => Json.Deserialize<Character>("""{ "Faction": "Pirate" }"""));
        await Assert.That(error.Message).Contains("Pirate isn't a Faction");

        error = Assert.Throws<JsonException>(() => Json.Deserialize<Character>("""{ "Byte": 256 }"""));
        await Assert.That(error.Message).Contains("256 doesn't fit in a byte");

        error = Assert.Throws<JsonException>(() => Json.Deserialize<Character>("""{ "Position": [1, 2] }"""));
        await Assert.That(error.Message).Contains("a Vector3 needs 3 numbers");
    }

    [Test]
    public async Task NestedTypesGetTheirSerializersToo()
    {
        await Assert.That(Json.Deserialize<Outer.Nested>(Json.Serialize(new Outer.Nested { Value = 3 })).Value)
            .IsEqualTo(3);
    }
}
