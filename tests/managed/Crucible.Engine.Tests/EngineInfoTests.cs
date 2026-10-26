namespace Crucible.Tests;

public class EngineInfoTests
{
    [Test]
    public async Task NameIsCrucible()
    {
        await Assert.That(EngineInfo.Name).IsEqualTo("Crucible");
    }
}
