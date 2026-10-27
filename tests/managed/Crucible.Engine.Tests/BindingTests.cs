using System.Text;

namespace Crucible.Tests;

// The generated bindings (ADR 0017), checked under JIT and NativeAOT alike.
public class BindingTests
{
    [Test]
    public async Task StructMirrorsMatchTheCppLayout()
    {
        await Assert.That(Interop.LayoutChecks.Verify()).IsNull();
    }

    [Test]
    public async Task StructComesBackFromCppIntact()
    {
        BindingProbe probe = Api.MakeBindingProbe(-7, true, 2.5f, 200, 1e10);

        await Assert.That(probe.Number).IsEqualTo(-7);
        await Assert.That(probe.Flag).IsTrue();
        await Assert.That(probe.Value).IsEqualTo(2.5f);
        await Assert.That(probe.Small).IsEqualTo((byte)200);
        await Assert.That(probe.Wide).IsEqualTo(1e10);
    }

    [Test]
    public async Task StructGoesToCppIntact()
    {
        BindingProbe probe = new() { Number = 3, Flag = true, Value = 0.5f, Small = 40, Wide = 100.0 };

        // 3 + 1000 (flag) + 0.5 + 40 + 100
        await Assert.That(Api.SumBindingProbe(probe)).IsEqualTo(1143.5);
    }

    [Test]
    public async Task FalseFlagCrossesAsFalse()
    {
        BindingProbe probe = Api.MakeBindingProbe(0, false, 0, 0, 0);

        await Assert.That(probe.Flag).IsFalse();
        await Assert.That(Api.SumBindingProbe(probe)).IsEqualTo(0.0);
    }

    [Test]
    public async Task EnumsCrossInBothDirections()
    {
        await Assert.That(Api.NextProbeKind(ProbeKind.Zero)).IsEqualTo(ProbeKind.One);
        await Assert.That(Api.NextProbeKind(ProbeKind.One)).IsEqualTo(ProbeKind.Large);
        await Assert.That(Api.NextProbeKind(ProbeKind.Large)).IsEqualTo(ProbeKind.Zero);
        await Assert.That((byte)Api.NextProbeKind(ProbeKind.One)).IsEqualTo((byte)200);
    }

    [Test]
    public async Task ReadOnlySpanReachesCpp()
    {
        BindingProbe[] probes =
        [
            new() { Number = 1, Value = 0.5f },
            new() { Number = 2, Flag = true },
            new() { Small = 3, Wide = 4.0 },
        ];

        // 1.5 + 1002 + 7
        await Assert.That(Api.SumBindingProbes(probes)).IsEqualTo(1010.5);
        await Assert.That(Api.SumBindingProbes([])).IsEqualTo(0.0);
    }

    [Test]
    public async Task CppWritesIntoTheCallersSpan()
    {
        BindingProbe[] probes = new BindingProbe[5];
        probes[4].Wide = 9.0;

        uint written = Api.FillBindingProbes(probes.AsSpan(1, 4));

        await Assert.That(written).IsEqualTo(4u);
        await Assert.That(string.Join(",", probes.Select(probe => probe.Number))).IsEqualTo("0,0,1,2,3");
        await Assert.That(probes[4].Wide).IsEqualTo(9.0);
    }

    [Test]
    [Arguments("")]
    [Arguments("Crucible")]
    [Arguments("Zoë, 鋳造, 🔥")]
    public async Task StringsArriveAsUtf8(string text)
    {
        await Assert.That(Api.HashText(text)).IsEqualTo(Fnv1a(Encoding.UTF8.GetBytes(text)));
    }

    [Test]
    public async Task StringsLongerThanTheStackBufferUseAPooledOne()
    {
        string text = string.Concat(Enumerable.Repeat("Crucible ✓ ", 100));

        await Assert.That(Encoding.UTF8.GetByteCount(text)).IsGreaterThan(Interop.Utf8Argument.StackSize);
        await Assert.That(Api.HashText(text)).IsEqualTo(Fnv1a(Encoding.UTF8.GetBytes(text)));
    }

    [Test]
    public async Task ResultValueComesBackOnSuccess()
    {
        BindingProbe probe = Api.FindBindingProbe(12);

        await Assert.That(probe.Number).IsEqualTo(12);
        await Assert.That(probe.Flag).IsTrue();
    }

    [Test]
    public async Task ResultFailureThrowsWithItsStatus()
    {
        CrucibleException? exception = Assert.Throws<CrucibleException>(() => Api.FindBindingProbe(-1));

        await Assert.That(exception!.Status).IsEqualTo(Status.NotFound);
        await Assert.That(exception.Function).IsEqualTo("crucible::findBindingProbe");
    }

    [Test]
    public async Task VoidResultThrowsOnlyOnFailure()
    {
        Api.CheckBindingProbe(1);
        CrucibleException? exception = Assert.Throws<CrucibleException>(() => Api.CheckBindingProbe(-1));

        await Assert.That(exception!.Status).IsEqualTo(Status.InvalidData);
    }

    [Test]
    public async Task StatusFormReturnsTheStatusInsteadOfThrowing()
    {
        await Assert.That(Api.IsBindingProbeEven(4, out bool four)).IsEqualTo(Status.Ok);
        await Assert.That(four).IsTrue();
        await Assert.That(Api.IsBindingProbeEven(7, out bool seven)).IsEqualTo(Status.Ok);
        await Assert.That(seven).IsFalse();

        // On failure the value is left at its default.
        await Assert.That(Api.IsBindingProbeEven(-2, out bool missing)).IsEqualTo(Status.NotFound);
        await Assert.That(missing).IsFalse();
    }

    private static uint Fnv1a(byte[] bytes)
    {
        uint hash = 2166136261;
        foreach (byte b in bytes)
        {
            hash = (hash ^ b) * 16777619;
        }
        return hash;
    }
}
