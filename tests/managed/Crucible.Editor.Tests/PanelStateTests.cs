using System.Text.Json.Nodes;

namespace Crucible.Editor.Tests;

// A panel's kept state (ADR 0117): plain values, read back with defaults.
public class PanelStateTests
{
    [Test]
    public async Task ValuesComeBackAndMissingOnesAreTheDefault()
    {
        PanelState state = new();
        await Assert.That(state.IsEmpty).IsTrue();
        state.Set("Name", "Walker");
        state.Set("Zoom", 1.5);
        state.Set("Open", true);
        // Through JSON, as the layout keeps it.
        PanelState read = new(JsonNode.Parse(state.ToJson().ToJsonString())!.AsObject());
        await Assert.That(read.GetString("Name")).IsEqualTo("Walker");
        await Assert.That(read.GetNumber("Zoom")).IsEqualTo(1.5);
        await Assert.That(read.GetBool("Open")).IsTrue();
        await Assert.That(read.GetNumber("Name", 7)).IsEqualTo(7);
        await Assert.That(read.GetString("Missing", "none")).IsEqualTo("none");
        await Assert.That(read.GetBool("Zoom", true)).IsTrue();
    }

    [Test]
    public async Task ItKeepsACopy()
    {
        JsonObject values = new() { ["Count"] = 1 };
        PanelState state = new(values);
        values["Count"] = 2;
        state.Set("Count", 3);
        await Assert.That(values["Count"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(state.GetNumber("Count")).IsEqualTo(3);
    }
}
