namespace Crucible.Editor.Tests;

// The asset watcher (ADR 0071): changes reported once they settle, and
// everything after MarkAll.
public class AssetWatcherTests
{
    [Test]
    public async Task ChangesAreReportedOnceTheySettle()
    {
        string folder = Path.Combine(Path.GetTempPath(), "crucible-watch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "Textures"));
        try
        {
            using AssetWatcher watcher = new(folder, quietSeconds: 0.2);
            await Assert.That(watcher.TakeSettled(out _, out _)).IsFalse();
            string file = Path.Combine(folder, "Textures", "Stone.png");
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
            IReadOnlyList<string> changed = [];
            bool settled = false, everything = true;
            for (int i = 0; i < 100 && !settled; ++i)
            {
                await Task.Delay(50);
                settled = watcher.TakeSettled(out changed, out everything);
            }
            await Assert.That(settled).IsTrue();
            await Assert.That(everything).IsFalse();
            await Assert.That(changed).Contains(Path.GetFullPath(file));
            // Taken: nothing more until something changes again.
            await Assert.That(watcher.TakeSettled(out _, out _)).IsFalse();
            // Hidden files and writes in progress aren't changes.
            await File.WriteAllBytesAsync(Path.Combine(folder, ".hidden"), [1]);
            await Task.Delay(400);
            await Assert.That(watcher.TakeSettled(out _, out _)).IsFalse();

            watcher.MarkAll();
            await Assert.That(watcher.TakeSettled(out _, out _)).IsFalse(); // not settled yet
            await Task.Delay(300);
            await Assert.That(watcher.TakeSettled(out _, out everything)).IsTrue();
            await Assert.That(everything).IsTrue();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
