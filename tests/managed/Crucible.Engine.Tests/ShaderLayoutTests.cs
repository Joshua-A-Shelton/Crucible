using System.Runtime.CompilerServices;
using Crucible.Shaders;

namespace Crucible.Tests;

// The C# mirrors shaderfax generates from [GpuShared] structs
// (ADR 0047): the shader's offsets and sizes, under JIT and NativeAOT. The
// native ShaderTests check the same layout against the GPU.
public class ShaderLayoutTests
{
    [Test]
    public async Task GeneratedMirrorsHaveTheShadersLayout()
    {
        Probe probe = default;
        await Assert.That(Unsafe.SizeOf<PackedPair>()).IsEqualTo(16);
        await Assert.That(Unsafe.SizeOf<Probe>()).IsEqualTo(112);
        await Assert.That(Offset(ref probe, ref probe.Radius)).IsEqualTo(12);
        await Assert.That(Offset(ref probe, ref probe.Transform)).IsEqualTo(16);
        await Assert.That(Offset(ref probe, ref probe.TintColor)).IsEqualTo(80);
        await Assert.That(Offset(ref probe, ref probe.TintStrength)).IsEqualTo(92);
        await Assert.That(Offset(ref probe, ref probe.Uv)).IsEqualTo(96);
        await Assert.That(Offset(ref probe, ref probe.Flags)).IsEqualTo(108);
    }

    private static int Offset<T>(ref Probe probe, ref T field) =>
        (int)Unsafe.ByteOffset(ref Unsafe.As<Probe, byte>(ref probe), ref Unsafe.As<T, byte>(ref field));
}
