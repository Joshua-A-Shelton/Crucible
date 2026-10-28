using System.Text;
using System.Text.Json;
using Crucible.Ecs;
using Crucible.Loop;
using Crucible.Serialization;

namespace Crucible.Tests;

// Components and services registered by the generator (ADR 0041), under
// JIT and NativeAOT.

[Component, Serialized]
public partial struct RegisteredHealth
{
    public int Value;
}

[Component(Sparse = true)]
public struct RegisteredStun
{
    public float Seconds;
}

[Component, Serialized]
public partial struct RegisteredTag;

[Component, Serialized]
public partial class RegisteredInventory
{
    public List<string> Items = [];
}

[Service, Serialized]
public sealed partial class QuestLog
{
    public int Completed;
    public List<string> Active = [];
}

[Service]
public sealed class DiceRoller
{
    public int Rolls;
}

public class RegistrationTests
{
    [Test]
    public async Task ComponentsAreRegisteredByStorageAndSavedWhenSerialized()
    {
        new Crucible.Generated.ComponentRegistration().Register();
        // The sparse marker bit (ADR 0023) is in the sparse one's ID only.
        const uint sparseBit = 0x8000_0000;
        await Assert.That(Components.Of<RegisteredStun>().Value & sparseBit).IsEqualTo(sparseBit);
        await Assert.That(Components.Of<RegisteredHealth>().Value & sparseBit).IsEqualTo(0u);

        using World world = new();
        Entity entity = world.CreateEntity(Components.Of<RegisteredHealth>(), Components.Of<RegisteredTag>());
        world.Set(entity, new RegisteredHealth { Value = 7 });
        world.SetManaged(entity, new RegisteredInventory { Items = ["key"] });
        await Assert.That(SavedComponents.Find(Components.Of<RegisteredStun>())).IsNull();
        await Assert.That(Write(world, entity, Components.Of<RegisteredHealth>())).IsEqualTo("""{"Value":7}""");
        await Assert.That(Write(world, entity, Components.Of<RegisteredTag>())).IsEqualTo("{}");
        await Assert.That(Write(world, entity, Components.Of<RegisteredInventory>())).IsEqualTo("""{"Items":["key"]}""");

        // And read back onto another entity.
        Entity other = world.CreateEntity();
        Read(world, other, Components.Of<RegisteredHealth>(), """{"Value":9}""");
        Read(world, other, Components.Of<RegisteredInventory>(), """{"Items":["map"]}""");
        Read(world, other, Components.Of<RegisteredTag>(), "{}");
        await Assert.That(world.Read<RegisteredHealth>(other).Value).IsEqualTo(9);
        await Assert.That(world.GetManaged<RegisteredInventory>(other).Items.Single()).IsEqualTo("map");
        await Assert.That(world.Has(other, Components.Of<RegisteredTag>())).IsTrue();
    }

    private static string Write(World world, Entity entity, ComponentId component)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            SavedComponents.Find(component)!.Write(writer, world, entity);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Read(World world, Entity entity, ComponentId component, string json)
    {
        Utf8JsonReader reader = new(Encoding.UTF8.GetBytes(json));
        reader.Read();
        SavedComponents.Find(component)!.Read(ref reader, world, entity);
    }

    [Test]
    public async Task ServicesAreFoundByTypeAndSavedTogether()
    {
        Services services = new();
        new Crucible.Generated.ServiceRegistration().Register(services);
        services.Get<QuestLog>().Completed = 3;
        services.Get<QuestLog>().Active.Add("Find the key");
        services.Get<DiceRoller>().Rolls = 12;

        string saved = Encoding.UTF8.GetString(services.Save());
        await Assert.That(saved).IsEqualTo(
            "{\n  \"Crucible.Tests.QuestLog\": {\n    \"Completed\": 3,\n    \"Active\": [\n      \"Find the key\"\n    ]\n  }\n}\n");

        Services fresh = new();
        new Crucible.Generated.ServiceRegistration().Register(fresh);
        IReadOnlyList<string> unknown = fresh.Load(Encoding.UTF8.GetBytes(
            saved.Replace("{\n  \"Crucible", "{\n  \"Game.Removed\": { \"x\": 1 },\n  \"Crucible")));
        await Assert.That(unknown.Single()).IsEqualTo("Game.Removed");
        await Assert.That(fresh.Get<QuestLog>().Completed).IsEqualTo(3);
        await Assert.That(fresh.Get<QuestLog>().Active.Single()).IsEqualTo("Find the key");
        await Assert.That(fresh.Get<DiceRoller>().Rolls).IsEqualTo(0); // not [Serialized]: not saved

        await Assert.That(new Services().TryGet(out QuestLog? none)).IsFalse();
        await Assert.That(none).IsNull();
        await Assert.That(Assert.Throws<InvalidOperationException>(() => new Services().Get<QuestLog>()).Message)
            .Contains("There is no QuestLog service");
    }
}
