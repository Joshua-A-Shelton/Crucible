namespace Crucible.Tests;

// The platform API's C# side (ADR 0018). The platform itself is initialized
// only by the launcher (ADR 0011), so these cover what works without it; the
// C++ tests and the launcher cover windows and events.
//
// Calls name the namespace (Platform.Api): inside namespace Crucible a bare
// Api is the root namespace's Crucible.Platform.Api.
public class PlatformBindingTests
{
    [Test]
    public async Task EventClockMovesForward()
    {
        ulong first = Platform.Api.TimestampNow();
        Thread.Sleep(2);

        await Assert.That(Platform.Api.TimestampNow()).IsGreaterThan(first);
    }

    [Test]
    public async Task PollingIntoAnEmptySpanReadsNothing()
    {
        await Assert.That(Platform.Api.PollEvents([])).IsEqualTo(0u);
    }

    [Test]
    public async Task MissingWindowHasNoSize()
    {
        Platform.WindowSize size = Platform.Api.WindowPixelSize(default);

        await Assert.That(size.Width).IsEqualTo(0u);
        await Assert.That(size.Height).IsEqualTo(0u);
    }
}
