namespace Crucible.Tests;

// "Allocates nothing" checks, measured in several windows: under NativeAOT
// a thread's allocation counter now and then picks up a few stray KB that
// aren't the code under test's (docs/open-items.md), so a check passes if
// any window allocated nothing. Code that allocates every frame or call
// allocates in every window.
internal sealed class AllocationWindows(int first, int length, int count)
{
    private readonly long[] _marks = new long[count + 1];

    // The fewest bytes any window saw.
    public long Fewest { get; private set; } = long.MaxValue;

    // Called once per frame (or iteration) with its number.
    public void At(ulong frame)
    {
        long offset = (long)frame - first;
        if (offset < 0 || offset % length != 0 || offset / length > count)
        {
            return;
        }
        int mark = (int)(offset / length);
        _marks[mark] = GC.GetAllocatedBytesForCurrentThread();
        if (mark > 0)
        {
            Fewest = Math.Min(Fewest, _marks[mark] - _marks[mark - 1]);
        }
    }

    // The fewest bytes any of `count` runs of `run` allocated.
    public static long FewestOf(int count, Action run)
    {
        long fewest = long.MaxValue;
        for (int i = 0; i < count && fewest != 0; ++i)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            run();
            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        return fewest;
    }
}

public class AllocationWindowsTests
{
    private static object? _kept;

    [Test]
    public async Task CodeThatAllocatesEveryTimeStillFails()
    {
        await Assert.That(AllocationWindows.FewestOf(4, () => _kept = new byte[64])).IsGreaterThan(0L);
        AllocationWindows windows = new(0, 2, 3);
        for (ulong frame = 0; frame <= 6; ++frame)
        {
            _kept = new object();
            windows.At(frame);
        }
        await Assert.That(windows.Fewest).IsGreaterThan(0L);
        GC.KeepAlive(_kept);
    }
}
