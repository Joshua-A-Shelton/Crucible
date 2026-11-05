using System.Numerics;
using System.Text.Json.Nodes;
using Crucible.Serialization;

namespace Crucible.Editor.Tests;

// The inspector's pieces that need no window (ADR 0069): field names shown,
// rotations as angles, new values for each kind, and edits at JSON paths.
// The panel itself runs in launcher.jit.inspector.
public class InspectorTests
{
    [Test]
    [Arguments("FieldOfViewY", "Field Of View Y")]
    [Arguments("castsShadows", "Casts Shadows")]
    [Arguments("_secret", "Secret")]
    [Arguments("HDRColor", "HDR Color")]
    [Arguments("Layer2", "Layer 2")]
    public async Task NamesAreShownAsWords(string name, string shown) =>
        await Assert.That(InspectorPanel.Pretty(name)).IsEqualTo(shown);

    [Test]
    public async Task RotationsReadBackAsTheAnglesTheyWereMadeFrom()
    {
        Vector3 angles = new(20, -35, 10);
        Vector3 radians = angles * (MathF.PI / 180);
        Quaternion rotation = Quaternion.CreateFromYawPitchRoll(radians.Y, radians.X, radians.Z);
        Vector3 back = FieldEditor.EulerDegrees(rotation);
        await Assert.That(Vector3.Distance(back, angles)).IsLessThan(1e-3f);
        await Assert.That(Vector3.Distance(FieldEditor.EulerDegrees(Quaternion.Identity), Vector3.Zero)).IsLessThan(1e-5f);
    }

    [Test]
    public async Task NewValuesFollowTheirKind()
    {
        await Assert.That(FieldEditor.DefaultJson(new SerializedField("", FieldKind.Quaternion))!.ToJsonString()).IsEqualTo("[0,0,0,1]");
        await Assert.That(FieldEditor.DefaultJson(new SerializedField("", FieldKind.Enum, enumNames: ["Calm", "Angry"]))!.ToJsonString())
            .IsEqualTo("\"Calm\"");
        await Assert.That(FieldEditor.DefaultJson(new SerializedField("", FieldKind.AssetId))).IsNull();
        SerializedField nested = new("", FieldKind.Serialized, fields: () =>
            [new SerializedField("Count", FieldKind.Int32), new SerializedField("Tags", FieldKind.List)]);
        await Assert.That(FieldEditor.DefaultJson(nested)!.ToJsonString()).IsEqualTo("{\"Count\":0,\"Tags\":[]}");
    }

    [Test]
    public async Task EditsGoToTheirPathInsideTheComponent()
    {
        JsonNode component = JsonNode.Parse("""{"Position":[1,2,3],"Items":[{"Count":1},{"Count":2}]}""")!;
        await Assert.That(ComponentSection.Json.Set(component, ["Items", 1, "Count"], JsonValue.Create(7))).IsTrue();
        await Assert.That(ComponentSection.Json.Get(component, ["Items", 1, "Count"])!.ToJsonString()).IsEqualTo("7");
        await Assert.That(ComponentSection.Json.Set(component, ["Items", 5, "Count"], JsonValue.Create(7))).IsFalse();
        await Assert.That(ComponentSection.Json.Set(component, ["Position"], null)).IsTrue();
        await Assert.That(component.ToJsonString()).IsEqualTo("""{"Position":null,"Items":[{"Count":1},{"Count":7}]}""");
    }
}
