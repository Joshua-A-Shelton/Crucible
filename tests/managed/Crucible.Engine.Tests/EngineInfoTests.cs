namespace Crucible.Tests;

public class EngineInfoTests
{
    [Test]
    public async Task NameIsCrucible()
    {
        await Assert.That(EngineInfo.Name).IsEqualTo("Crucible");
    }

    [Test]
    public async Task CoreReportsARenderBackend()
    {
        // Crosses the boundary: C# to the C++ core through LibraryImport.
        await Assert.That(EngineInfo.HasRenderBackend).IsTrue();
    }
}
