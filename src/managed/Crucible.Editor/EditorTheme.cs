using Crucible.UI;

namespace Crucible.Editor;

/// <summary>
/// The editor's look (ADR 0129): Editor.crstyle beside the editor, layered
/// over the engine's default theme, read at start and again whenever the
/// file changes. A file that doesn't read leaves the look as it was and
/// says why in the Log.
/// </summary>
internal sealed class EditorTheme : IDisposable
{
    public const string FileName = "Editor.crstyle";

    // Saves often come as several writes; read once they've stopped this long.
    private const double SettleSeconds = 0.2;

    private readonly FileSystemWatcher? _watcher;
    private long _changedAt; // Environment.TickCount64 of the last change, 0 for none pending

    public EditorTheme(string path)
    {
        Path = path;
        string? folder = System.IO.Path.GetDirectoryName(path);
        if (folder is not null && Directory.Exists(folder))
        {
            _watcher = new FileSystemWatcher(folder, FileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            _watcher.Changed += (_, _) => Changed();
            _watcher.Created += (_, _) => Changed();
            _watcher.Renamed += (_, _) => Changed();
            _watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>The theme every editor window uses; reloading changes it in place.</summary>
    public Theme Theme { get; } = Theme.CreateDefault();

    public string Path { get; }

    /// <summary>Reads the file; on a mistake, the error (and the theme stays as it was).</summary>
    public string? Load()
    {
        Theme read;
        try
        {
            read = Theme.Parse(File.ReadAllText(Path));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return $"{FileName}: {exception.Message}";
        }
        Theme combined = Theme.CreateDefault();
        foreach ((string name, object value) in read.Tokens)
        {
            combined.SetToken(name, value);
        }
        foreach (Style style in read.Styles)
        {
            combined.Add(style);
        }
        Theme.ReplaceWith(combined);
        return null;
    }

    /// <summary>Each frame: true once a change has settled and been read (or failed, with the error).</summary>
    public bool Poll(out string? error)
    {
        error = null;
        long changed = Interlocked.Read(ref _changedAt);
        if (changed == 0 || Environment.TickCount64 - changed < SettleSeconds * 1000)
        {
            return false;
        }
        Interlocked.CompareExchange(ref _changedAt, 0, changed);
        error = Load();
        return true;
    }

    public void Dispose() => _watcher?.Dispose();

    private void Changed() => Interlocked.Exchange(ref _changedAt, Math.Max(1, Environment.TickCount64));
}
