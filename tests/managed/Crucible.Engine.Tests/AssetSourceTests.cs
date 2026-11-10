using Crucible.Assets;
using Crucible.Scenes;

namespace Crucible.Tests;

// The runtime asset contract from C# (ADR 0076), under JIT and NativeAOT: an
// installed source answers reads through the native side (as the renderer
// reads), a default doesn't displace one, and prefabs read through it.
[NotInParallel("asset-source")]
public class AssetSourceTests
{
    private static readonly AssetId _known = AssetId.Parse("2b3c4d5e-0000-4000-8000-000000000001");

    [Test]
    public async Task ReadsGoThroughTheInstalledSource()
    {
        AssetSource.Install(id => id == _known ? [1, 2, 3, 4] : null);
        try
        {
            await Assert.That(AssetSource.IsInstalled).IsTrue();
            await Assert.That(Convert.ToHexString(AssetSource.Read(_known) ?? [])).IsEqualTo("01020304");
            await Assert.That(AssetSource.Read(AssetId.New())).IsNull();
            // A default (a game's imported files) leaves an installed source alone.
            await Assert.That(AssetSource.InstallDefault(_ => [9])).IsFalse();
            await Assert.That(Convert.ToHexString(AssetSource.Read(_known) ?? [])).IsEqualTo("01020304");
        }
        finally
        {
            AssetSource.Install(null);
        }
        await Assert.That(AssetSource.IsInstalled).IsFalse();
        await Assert.That(AssetSource.Read(_known)).IsNull();
    }

    [Test]
    public async Task ADefaultIsInstalledWhenNothingElseIs()
    {
        AssetSource.Install(null);
        try
        {
            await Assert.That(AssetSource.InstallDefault(id => id == _known ? [7] : null)).IsTrue();
            await Assert.That(Convert.ToHexString(AssetSource.Read(_known) ?? [])).IsEqualTo("07");
        }
        finally
        {
            AssetSource.Install(null);
        }
    }

    [Test]
    public async Task PrefabsReadThroughTheSource()
    {
        bool asked = false;
        AssetSource.Install(_ =>
        {
            asked = true;
            return null;
        });
        try
        {
            PrefabLibrary prefabs = PrefabLibrary.FromAssetSource();
            // No such prefab in this source: asked, and refused.
            Assert.Throws<System.Text.Json.JsonException>(() => prefabs.Get(_known));
            await Assert.That(asked).IsTrue();
        }
        finally
        {
            AssetSource.Install(null);
        }
    }
}
