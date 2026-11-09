namespace Crucible.Editor;

/// <summary>
/// Notices files changing under a project's Assets folder (ADR 0071), through
/// .NET's <see cref="FileSystemWatcher"/> (inotify on Linux,
/// ReadDirectoryChangesW on Windows). Its events come on other threads;
/// <see cref="TakeSettled"/>, polled each frame, reports once changes have
/// stopped for a moment, since an editor saves a file in several writes. A
/// watcher can drop events when many come at once (its buffer overflows):
/// then everything counts as changed, as it does after <see cref="MarkAll"/>.
/// </summary>
public sealed class AssetWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Lock _lock = new();
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);
    private bool _everything;
    private long _lastChange; // Environment.TickCount64 of the newest change

    public AssetWatcher(string folder, double quietSeconds = 0.3)
    {
        Folder = Path.GetFullPath(folder);
        QuietSeconds = quietSeconds;
        _watcher = new FileSystemWatcher(Folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += (_, e) => Note(e.FullPath);
        _watcher.Created += (_, e) => Note(e.FullPath);
        _watcher.Deleted += (_, e) => Note(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Note(e.OldFullPath);
            Note(e.FullPath);
        };
        _watcher.Error += (_, _) => MarkAll();
        _watcher.EnableRaisingEvents = true;
    }

    public string Folder { get; }

    /// <summary>How long changes must stop before they're reported.</summary>
    public double QuietSeconds { get; }

    /// <summary>Counts everything as changed (the window regained focus; the watcher overflowed).</summary>
    public void MarkAll()
    {
        lock (_lock)
        {
            _everything = true;
            _lastChange = Environment.TickCount64;
        }
    }

    /// <summary>
    /// The files changed (full paths), once nothing has changed for
    /// <see cref="QuietSeconds"/>; false while changes are still coming, or
    /// none came. <paramref name="everything"/> is true when any file may
    /// have changed.
    /// </summary>
    public bool TakeSettled(out IReadOnlyList<string> changed, out bool everything)
    {
        lock (_lock)
        {
            changed = [];
            everything = false;
            if ((_changed.Count == 0 && !_everything) || Environment.TickCount64 - _lastChange < QuietSeconds * 1000)
            {
                return false;
            }
            changed = [.. _changed.Order(StringComparer.Ordinal)];
            everything = _everything;
            _changed.Clear();
            _everything = false;
            return true;
        }
    }

    public void Dispose() => _watcher.Dispose();

    private void Note(string path)
    {
        // The database's own writes in progress, and hidden files, aren't changes to assets.
        string name = Path.GetFileName(path);
        if (name.StartsWith('.') || name.Contains(".tmp-", StringComparison.Ordinal))
        {
            return;
        }
        lock (_lock)
        {
            _changed.Add(path);
            _lastChange = Environment.TickCount64;
        }
    }
}
