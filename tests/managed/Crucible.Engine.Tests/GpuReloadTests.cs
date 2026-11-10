using Crucible.Assets;
using Crucible.Render;

namespace Crucible.Tests;

// RenderAssets.Reload (ADR 0072) from C#, under JIT and NativeAOT. The
// renderer isn't started here, so nothing is loaded and a reload has nothing
// to do; the native tests reload each kind on the GPU.
[NotInParallel("asset-source")]
public class GpuReloadTests
{
    [Test]
    public async Task NothingLoadedIsNothingToReload()
    {
        AssetSource.Install(_ => [1, 2, 3]);
        await Assert.That(RenderAssets.Reload(AssetId.New())).IsFalse();
        AssetSource.Install(null);
    }
}
