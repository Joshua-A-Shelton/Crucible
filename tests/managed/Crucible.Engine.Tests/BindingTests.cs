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
        BindingProbe probe = Core.MakeBindingProbe(-7, true, 2.5f, 200, 1e10);

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
        await Assert.That(Core.SumBindingProbe(probe)).IsEqualTo(1143.5);
    }

    [Test]
    public async Task FalseFlagCrossesAsFalse()
    {
        BindingProbe probe = Core.MakeBindingProbe(0, false, 0, 0, 0);

        await Assert.That(probe.Flag).IsFalse();
        await Assert.That(Core.SumBindingProbe(probe)).IsEqualTo(0.0);
    }
}
