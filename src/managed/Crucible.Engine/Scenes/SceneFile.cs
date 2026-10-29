using System.Globalization;
using System.Text.Json;
using Crucible.Assets;
using Crucible.Serialization;

namespace Crucible.Scenes;

// A scene or prefab file, parsed once (ADR 0041, 0045): the entity tree,
// with each component's JSON kept as a slice of the file, read when the
// entities exist (so references can name any of them).
internal sealed class SceneFile
{
    public AssetId Id = AssetId.None;
    public readonly List<EntityNode> Roots = [];

    public static SceneFile Parse(ReadOnlySpan<byte> json)
    {
        SceneFile file = new();
        byte[] bytes = json.ToArray();
        Utf8JsonReader reader = new(bytes);
        reader.Read();
        JsonValues.Expect(reader, JsonTokenType.StartObject, "a scene object");
        bool sawEntities = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals(Scene.GuidProperty))
            {
                reader.Read();
                file.Id = JsonValues.ReadAssetId(ref reader);
            }
            else if (reader.ValueTextEquals("Entities"u8))
            {
                reader.Read();
                ReadEntityList(ref reader, bytes, file.Roots);
                sawEntities = true;
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
        if (!sawEntities)
        {
            throw new JsonException("Scene: no Entities list");
        }
        return file;
    }

    private static void ReadEntityList(ref Utf8JsonReader reader, byte[] bytes, List<EntityNode> into)
    {
        JsonValues.Expect(reader, JsonTokenType.StartArray, "a list of entities");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            into.Add(ReadEntity(ref reader, bytes));
        }
    }

    private static EntityNode ReadEntity(ref Utf8JsonReader reader, byte[] bytes)
    {
        JsonValues.Expect(reader, JsonTokenType.StartObject, "an entity object");
        Utf8JsonReader start = reader;
        EntityNode node = new();
        bool sawId = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("Id"u8))
            {
                reader.Read();
                node.Id = ReadId(reader);
                sawId = true;
            }
            else if (reader.ValueTextEquals("Prefab"u8))
            {
                reader.Read();
                node.Prefab = JsonValues.ReadAssetId(ref reader);
            }
            else if (reader.ValueTextEquals("Components"u8))
            {
                reader.Read();
                ReadComponents(ref reader, bytes, node.Components);
            }
            else if (reader.ValueTextEquals("Children"u8))
            {
                reader.Read();
                ReadEntityList(ref reader, bytes, node.Children);
            }
            else if (reader.ValueTextEquals("Overrides"u8))
            {
                reader.Read();
                JsonValues.Expect(reader, JsonTokenType.StartObject, "an object of overrides");
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    ulong target = ReadId(reader);
                    reader.Read();
                    node.Overrides.Add((target, ReadOverride(ref reader, bytes)));
                }
            }
            else if (reader.ValueTextEquals("AddedChildren"u8))
            {
                reader.Read();
                JsonValues.Expect(reader, JsonTokenType.StartObject, "an object of added children");
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    ulong target = ReadId(reader);
                    reader.Read();
                    List<EntityNode> children = [];
                    ReadEntityList(ref reader, bytes, children);
                    node.AddedChildren.Add((target, children));
                }
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
        return sawId ? node : throw JsonValues.Error(start, "an entity has no Id");
    }

    private static OverrideNode ReadOverride(ref Utf8JsonReader reader, byte[] bytes)
    {
        JsonValues.Expect(reader, JsonTokenType.StartObject, "an override object");
        OverrideNode node = new();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.ValueTextEquals("Components"u8))
            {
                reader.Read();
                ReadComponents(ref reader, bytes, node.Components);
            }
            else if (reader.ValueTextEquals("Removed"u8))
            {
                reader.Read();
                JsonValues.Expect(reader, JsonTokenType.StartArray, "a list of component names");
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    JsonValues.Expect(reader, JsonTokenType.String, "a component name");
                    node.Removed.Add(reader.GetString()!);
                }
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }
        return node;
    }

    private static void ReadComponents(ref Utf8JsonReader reader, byte[] bytes, List<ComponentJson> into)
    {
        JsonValues.Expect(reader, JsonTokenType.StartObject, "an object of components");
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString()!;
            reader.Read();
            int start = (int)reader.TokenStartIndex;
            reader.Skip();
            into.Add(new ComponentJson(name, bytes.AsMemory(start, (int)reader.BytesConsumed - start)));
        }
    }

    // An entity's ID (16 hex digits, not zero), at a property name or a string value.
    public static ulong ReadId(in Utf8JsonReader reader) =>
        reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName &&
        ulong.TryParse(reader.GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong id) &&
        id != 0
            ? id
            : throw JsonValues.Error(reader, "an entity's Id is 16 hex digits");
}

// One entity in a file: plain (components and children), or an instance of
// a prefab (its overrides and added children).
internal sealed class EntityNode
{
    public ulong Id;
    public AssetId Prefab = AssetId.None;
    public readonly List<ComponentJson> Components = [];
    public readonly List<EntityNode> Children = [];
    public readonly List<(ulong Target, OverrideNode Override)> Overrides = [];
    public readonly List<(ulong Target, List<EntityNode> Children)> AddedChildren = [];

    public bool IsInstance => !Prefab.IsNone;
}

internal sealed class OverrideNode
{
    public readonly List<ComponentJson> Components = [];
    public readonly List<string> Removed = [];
}

internal readonly record struct ComponentJson(string Name, ReadOnlyMemory<byte> Json);
