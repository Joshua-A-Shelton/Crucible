using System.Numerics;
using Crucible.Assets;
using Crucible.Render;

namespace Crucible.Tests;

// RenderAssets (ADR 0049) from C#, under JIT and NativeAOT. The renderer
// isn't started in these tests (the launcher owns it), so acquiring reports
// Unsupported; the native tests load, read back, and count uses on the GPU.
public class RenderAssetsTests
{
    [Test]
    public async Task AcquiringNeedsTheRenderer()
    {
        RenderAssets.SetReader(_ => [1, 2, 3]);
        CrucibleException mesh = Assert.Throws<CrucibleException>(() => RenderAssets.AcquireMesh(AssetId.New()));
        await Assert.That(mesh.Status).IsEqualTo(Status.Unsupported);
        CrucibleException texture = Assert.Throws<CrucibleException>(() => RenderAssets.AcquireTexture(AssetId.New()));
        await Assert.That(texture.Status).IsEqualTo(Status.Unsupported);
        RenderAssets.SetReader(null);
        await Assert.That(RenderAssets.TextureDescriptor(default)).IsEqualTo(0u);
        await Assert.That(RenderAssets.SamplerDescriptor(Sampler.LinearClamp))
            .IsNotEqualTo(RenderAssets.SamplerDescriptor(Sampler.PointClamp));
    }

    private struct Swatch : IMaterialData
    {
        public Vector4 Color;

        public static AssetId ShaderId { get; } = AssetId.Parse("7e0f1d2c-3b4a-4596-8778-695a4b3c2d1e");

        public static string TypeName => "Swatch";
    }

    [Test]
    public async Task MaterialsNeedTheRendererAndTheirOwnType()
    {
        CrucibleException material = Assert.Throws<CrucibleException>(() => RenderAssets.AcquireMaterial(AssetId.New()));
        await Assert.That(material.Status).IsEqualTo(Status.Unsupported);
        // A stale handle is no Swatch: refused before anything is written.
        ArgumentException wrongType =
            Assert.Throws<ArgumentException>(() => RenderAssets.SetMaterial(default, new Swatch { Color = Vector4.One }));
        await Assert.That(wrongType.Message).Contains("stale handle");
    }
}
