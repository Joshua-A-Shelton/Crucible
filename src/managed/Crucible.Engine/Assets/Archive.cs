namespace Crucible.Assets;

/// <summary>
/// A game's cooked assets (ADR 0099): an archive the editor's cooker wrote
/// (Game.crpak), read by the core's ArchiveReader. Installed, it is the
/// asset source (ADR 0076) every loader reads through; it must stay open
/// while it is.
/// </summary>
public sealed class Archive : IDisposable
{
    private ulong _reader;

    private Archive(ulong reader) => _reader = reader;

    /// <summary>
    /// Opens an archive. Throws <see cref="CrucibleException"/>: NotFound or
    /// IoError for the file, InvalidData if it's damaged, Unsupported if it's
    /// another version (cook it again).
    /// </summary>
    public static Archive Open(string path) => new(Api.OpenArchive(Path.GetFullPath(path)));

    /// <summary>The scene the game starts with (none if the archive names none).</summary>
    public AssetId StartScene => Api.ArchiveStartScene(Reader);

    /// <summary>How many assets it holds.</summary>
    public int Count => (int)Api.ArchiveAssetCount(Reader);

    /// <summary>What a scene needs, to load it ahead; empty if the archive hasn't the scene.</summary>
    public AssetId[] SceneAssets(AssetId scene)
    {
        uint count = Api.ArchiveSceneAssets(Reader, scene, []);
        AssetId[] assets = new AssetId[count];
        Api.ArchiveSceneAssets(Reader, scene, assets);
        return assets;
    }

    /// <summary>Makes it the asset source, replacing whatever was installed.</summary>
    public void Install() => Api.InstallArchive(Reader);

    public void Dispose()
    {
        Api.CloseArchive(_reader);
        _reader = 0;
    }

    private ulong Reader => _reader != 0 ? _reader : throw new ObjectDisposedException(nameof(Archive));
}
