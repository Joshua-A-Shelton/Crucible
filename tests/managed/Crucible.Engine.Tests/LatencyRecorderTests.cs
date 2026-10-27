using Crucible.Loop;
using Crucible.Platform;

namespace Crucible.Tests;

// The latency harness's bookkeeping (ADR 0003), driven by hand: no window,
// so GPU times are never collected, and the stages come from the timestamps
// given here.
public class LatencyRecorderTests
{
    private static void Deliver(FrameContext context, params InputEvent[] events)
    {
        events.CopyTo(context.EventBuffer);
        context.SetEventCount(events.Length);
    }

    private static InputEvent Marker(int sequence, ulong arrivalNs) =>
        new() { Type = InputEventType.LatencyMarker, Code = sequence, TimestampNs = arrivalNs };

    [Test]
    public async Task StagesAreMeasuredFromEachInputsArrival()
    {
        LatencyRecorder recorder = new(frameCapacity: 8, inputCapacity: 8) { RecordsPresents = true };
        FrameContext context = new() { Latency = recorder };

        // Frame 0 starts at 1 ms; a marker that arrived at 0.5 ms is read at
        // 2 ms and submitted at 3 ms. Frame 1's pacing wait returns at 9 ms,
        // which is when frame 0's present completed.
        recorder.FrameStarted(context, 1_000_000);
        Deliver(context, Marker(0, 500_000), new InputEvent { Type = InputEventType.MouseMotion });
        recorder.InputRead(context, 2_000_000);
        recorder.Submitted(context, 3_000_000, submitIndex: 1, submitted: true);
        context.FrameNumber = 1;
        recorder.FrameStarted(context, 9_000_000);

        string directory = Directory.CreateTempSubdirectory("crucible-latency").FullName;
        string path = Path.Combine(directory, "run.csv");
        string summary = recorder.Finish(default, path);

        string[] rows = File.ReadAllLines(path);
        await Assert.That(rows.Length).IsEqualTo(2); // the header and the marker; motion isn't recorded
        await Assert.That(rows[1]).IsEqualTo("Marker,0,500000,0,1.500,2.500,,8.500");
        await Assert.That(File.Exists(Path.Combine(directory, "run.frames.csv"))).IsTrue();
        await Assert.That(summary).Contains("2 frames, 1 inputs (1 synthetic)");
        Directory.Delete(directory, recursive: true);
    }

    [Test]
    public async Task SkippedFramesHaveNoPresentTime()
    {
        LatencyRecorder recorder = new(frameCapacity: 8, inputCapacity: 8) { RecordsPresents = true };
        FrameContext context = new() { Latency = recorder };

        recorder.FrameStarted(context, 1_000_000);
        Deliver(context, Marker(0, 500_000));
        recorder.InputRead(context, 2_000_000);
        recorder.Submitted(context, 3_000_000, submitIndex: 0, submitted: false); // minimized
        context.FrameNumber = 1;
        recorder.FrameStarted(context, 9_000_000);

        string path = Path.Combine(Directory.CreateTempSubdirectory("crucible-latency").FullName, "run.csv");
        recorder.Finish(default, path);

        await Assert.That(File.ReadAllLines(path)[1]).EndsWith(",,");
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Test]
    public async Task PercentilesUseNearestRank()
    {
        List<double> sorted = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

        await Assert.That(LatencyRecorder.Percentile(sorted, 0.5)).IsEqualTo(5.0);
        await Assert.That(LatencyRecorder.Percentile(sorted, 0.95)).IsEqualTo(10.0);
        await Assert.That(LatencyRecorder.Percentile([7], 0.95)).IsEqualTo(7.0);
    }

    [Test]
    public async Task RecordingAllocatesNothingPerFrame()
    {
        LatencyRecorder recorder = new();
        FrameLoop loop = FrameLoop.CreateDefault();
        loop.Run(new FrameContext { Latency = recorder }, frameLimit: 10);

        FrameContext context = new() { Latency = new LatencyRecorder() };
        long before = GC.GetAllocatedBytesForCurrentThread();
        loop.Run(context, frameLimit: 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
    }
}
