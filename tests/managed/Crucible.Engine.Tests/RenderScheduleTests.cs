using Crucible.Render;

namespace Crucible.Tests;

// The render schedule from C# (ADR 0058), under JIT and NativeAOT. The
// renderer isn't started here (the launcher owns it), so steps aren't
// prepared; launcher.jit.render-steps draws through a C# step on the GPU.
[NotInParallel("render-schedule")]
public class RenderScheduleTests
{
    private sealed class CountingStep : IRenderStep
    {
        public int Prepares;
        public int Releases;

        public void Prepare() => Prepares++;

        public void AddPasses(RenderStepContext context)
        {
        }

        public void Release() => Releases++;
    }

    [Test]
    public async Task CSharpStepsAreEditedByNameAmongTheEnginesSteps()
    {
        IReadOnlyList<string> before = RenderSchedule.Default.StepNames;
        await Assert.That(before).Contains("Opaque");
        CountingStep step = new();
        RenderSchedule.Default.InsertBefore("Opaque", "Test.Before", step);
        RenderSchedule.Default.InsertAfter("Opaque", "Test.After", step);
        RenderSchedule.Default.Add("Test.Last", step);
        RenderSchedule.Default.Replace("Test.Last", "Test.Replacement", step);

        List<string> names = [.. RenderSchedule.Default.StepNames];
        await Assert.That(names.IndexOf("Test.Before") + 1).IsEqualTo(names.IndexOf("Opaque"));
        await Assert.That(names.IndexOf("Test.After")).IsEqualTo(names.IndexOf("Opaque") + 1);
        await Assert.That(names[^1]).IsEqualTo("Test.Replacement");
        // No renderer: prepared when it starts.
        await Assert.That(step.Prepares).IsEqualTo(0);

        CrucibleException duplicate = Assert.Throws<CrucibleException>(() => RenderSchedule.Default.Add("Opaque", step));
        await Assert.That(duplicate.Status).IsEqualTo(Status.InvalidData);
        CrucibleException missing =
            Assert.Throws<CrucibleException>(() => RenderSchedule.Default.InsertAfter("Nowhere", "Test.Lost", step));
        await Assert.That(missing.Status).IsEqualTo(Status.NotFound);

        foreach (string name in new[] { "Test.Before", "Test.After", "Test.Replacement" })
        {
            RenderSchedule.Default.Remove(name);
        }
        await Assert.That(step.Releases).IsEqualTo(0);
        CrucibleException gone = Assert.Throws<CrucibleException>(() => RenderSchedule.Default.Remove("Test.Before"));
        await Assert.That(gone.Status).IsEqualTo(Status.NotFound);
        await Assert.That(string.Join(",", RenderSchedule.Default.StepNames)).IsEqualTo(string.Join(",", before));
    }

    [Test]
    public async Task RemovingEveryCSharpStepLeavesTheEnginesSteps()
    {
        IReadOnlyList<string> before = RenderSchedule.Default.StepNames;
        RenderSchedule.Default.Add("Test.One", new CountingStep());
        RenderSchedule.Default.Add("Test.Two", new CountingStep());
        RenderSchedule.RemoveAll();
        await Assert.That(string.Join(",", RenderSchedule.Default.StepNames)).IsEqualTo(string.Join(",", before));
    }

    [Test]
    public async Task BlackboardKeysAreSharedWithNativeSteps()
    {
        BlackboardKey key = BlackboardKey.Register("Test.CSharpKey");
        await Assert.That(BlackboardKey.Register("Test.CSharpKey").Value).IsEqualTo(key.Value);
        await Assert.That(key.Name).IsEqualTo("Test.CSharpKey");
        await Assert.That(BlackboardKey.Backbuffer.Name).IsEqualTo("Backbuffer");
        await Assert.That(BlackboardKey.SceneColor.Name).IsEqualTo("SceneColor");
    }

    [Test]
    public async Task PipelinesNeedTheRenderer()
    {
        CrucibleException failed = Assert.Throws<CrucibleException>(() =>
            Render.Api.CreateComputePipeline(Assets.AssetId.New(), "main"));
        await Assert.That(failed.Status).IsEqualTo(Status.Unsupported);
        Render.Api.DestroyPipeline(default); // nothing to destroy
    }
}
