using System.Numerics;
using Crucible.Assets;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Crucible.Tests;

public enum TableMood
{
    Calm,
    Angry = 4,
    Sleepy,
}

[Serialized]
public partial struct TableInner
{
    public int Count;
}

[Serialized]
public partial class TableSample
{
    [Header("Looks"), Color, Tooltip("What it's tinted")]
    public Vector3 Tint;

    [Range(0, 10)]
    public float Speed;

    public TableMood Mood;
    public List<TableInner> Items = [];
    public TableInner[]? Fixed;
    public Dictionary<string, float> Scores = [];
    public int? Maybe;
    public AssetId Model;
    public Entity Target;
    public string Name { get; set; } = "";

    [NotSerialized]
    public int Skipped;
}

// The generated field tables (ADR 0069): one entry per saved member, in
// order, with its kind, its items, enum names, nested tables, and hints.
public class FieldTableTests
{
    [Test]
    public async Task TablesListSavedMembersInOrderWithTheirKinds()
    {
        IReadOnlyList<SerializedField> fields = TableSample.SerializedFields;
        await Assert.That(string.Join(" ", fields.Select(field => $"{field.Name}:{field.Kind}")))
            .IsEqualTo("Tint:Vector3 Speed:Single Mood:Enum Items:List Fixed:Array Scores:Dictionary Maybe:Nullable " +
                       "Model:AssetId Target:Entity Name:String");
        await Assert.That(string.Join(",", fields[2].EnumNames)).IsEqualTo("Calm,Angry,Sleepy");
        SerializedField items = fields[3];
        await Assert.That(items.Element!.Kind).IsEqualTo(FieldKind.Serialized);
        await Assert.That(items.Element.Fields.Single().Name).IsEqualTo("Count");
        await Assert.That(fields[5].Element!.Kind).IsEqualTo(FieldKind.Single);
        await Assert.That(fields[6].Element!.Kind).IsEqualTo(FieldKind.Int32);
    }

    [Test]
    public async Task HintsComeFromTheAttributes()
    {
        IReadOnlyList<SerializedField> fields = TableSample.SerializedFields;
        SerializedField tint = fields[0], speed = fields[1], mood = fields[2];
        await Assert.That(tint.IsColor).IsTrue();
        await Assert.That(tint.Header).IsEqualTo("Looks");
        await Assert.That(tint.Tooltip).IsEqualTo("What it's tinted");
        await Assert.That(tint.HasRange).IsFalse();
        await Assert.That(speed.HasRange).IsTrue();
        await Assert.That(speed.Min).IsEqualTo(0f);
        await Assert.That(speed.Max).IsEqualTo(10f);
        await Assert.That(mood.IsColor || mood.Header is not null || mood.Tooltip is not null).IsFalse();
    }

    [Test]
    public async Task EngineComponentsHaveTablesToo()
    {
        // C++ components' C# halves come from the binding generator; their tables from this one.
        IReadOnlyList<SerializedField> transform = Crucible.Scenes.LocalTransform.SerializedFields;
        await Assert.That(string.Join(" ", transform.Select(field => $"{field.Name}:{field.Kind}")))
            .IsEqualTo("Position:Vector3 Rotation:Quaternion Scale:Vector3");
        await Assert.That(Crucible.Render.Camera.SerializedFields.Count).IsGreaterThan(2);
    }

    // C++ components' hints (ADR 0070): CRUCIBLE_HINT_* annotations, and comments as tooltips.
    [Test]
    public async Task EngineComponentsCarryTheirHeadersHints()
    {
        SerializedField Find(IReadOnlyList<SerializedField> fields, string name) => fields.Single(field => field.Name == name);
        IReadOnlyList<SerializedField> point = Crucible.Render.PointLight.SerializedFields;
        await Assert.That(Find(point, "Color").IsColor).IsTrue();
        await Assert.That(Find(point, "Color").Tooltip).IsEqualTo("Linear");
        await Assert.That(Find(point, "Range").Tooltip).IsEqualTo("Meters: no light past it");
        await Assert.That(Find(point, "Intensity").Tooltip).IsNull();
        SerializedField cone = Find(Crucible.Render.SpotLight.SerializedFields, "OuterConeAngle");
        await Assert.That(cone.HasRange && cone.Min == 0 && Math.Abs(cone.Max - 3.14159f) < 1e-5f).IsTrue();
        SerializedField contrast = Find(Crucible.Render.PostProcess.SerializedFields, "Contrast");
        await Assert.That(contrast.Header).IsEqualTo("Grading");
        await Assert.That(contrast.Min).IsEqualTo(-1f);
        // The comment lines above a field are its tooltip when it has none on its line.
        await Assert.That(Find(Crucible.Render.Camera.SerializedFields, "Priority").Tooltip)
            .IsEqualTo("The renderer draws from the camera with the highest priority; among equals, the one with the lowest entity index.");
        await Assert.That(Find(Crucible.Render.AmbientLight.SerializedFields, "SkyColor").IsColor).IsTrue();
    }
}
