namespace Crucible.Binary;

/// <summary>
/// Four-letter tags for kinds and sections (<c>crucible::binary::tag</c>):
/// the letters' bytes in file order.
/// </summary>
public static class Tag
{
    public static uint Of(string fourLetters)
    {
        if (fourLetters.Length != 4 || fourLetters.Any(letter => letter > 0x7F))
        {
            throw new ArgumentException("A tag is four ASCII letters.", nameof(fourLetters));
        }
        return fourLetters[0] | (uint)fourLetters[1] << 8 | (uint)fourLetters[2] << 16 | (uint)fourLetters[3] << 24;
    }
}

/// <summary>
/// One of Crucible's binary files of imported data (ADR 0039), opened and
/// validated by the core. Failures throw <see cref="CrucibleException"/>:
/// <see cref="Status.Unsupported"/> means stale data to reimport,
/// <see cref="Status.InvalidData"/> a damaged file. Names are placeholders
/// (docs/naming.md).
/// </summary>
public sealed class ImportedFile : IDisposable
{
    private FileHandle _handle;

    private ImportedFile(FileHandle handle) => _handle = handle;

    public static ImportedFile Open(string path, uint kind, uint kindVersion) =>
        new(Api.OpenFile(path, kind, kindVersion));

    public static ImportedFile Parse(ReadOnlySpan<byte> bytes, uint kind, uint kindVersion) =>
        new(Api.ParseFile(bytes, kind, kindVersion));

    public void Dispose()
    {
        if (!_handle.IsNull)
        {
            Api.CloseFile(_handle);
            _handle = default;
        }
    }

    public int SectionCount => (int)Api.SectionCount(Live);

    public SectionInfo Section(int index) => Api.SectionInfo(Live, (uint)index);

    /// <summary>The first section with the tag, or -1.</summary>
    public int Find(uint tag) => Api.FindSection(Live, tag);

    /// <summary>Decompresses and checks the section into <paramref name="destination"/>, which must be its size.</summary>
    public void Read(int index, Span<byte> destination) => Api.ReadSection(Live, (uint)index, destination);

    public byte[] Read(int index)
    {
        byte[] bytes = new byte[Section(index).Size];
        Read(index, bytes);
        return bytes;
    }

    private FileHandle Live => _handle.IsNull ? throw new ObjectDisposedException(nameof(ImportedFile)) : _handle;
}

/// <summary>Builds one of Crucible's binary files (ADR 0039).</summary>
public sealed class ImportedFileWriter(uint kind, uint kindVersion) : IDisposable
{
    private WriterHandle _handle = Api.CreateWriter(kind, kindVersion);

    public void Dispose()
    {
        if (!_handle.IsNull)
        {
            Api.DestroyWriter(_handle);
            _handle = default;
        }
    }

    /// <summary>Copies the data in; <paramref name="alignment"/> is a power of two up to 4096.</summary>
    public ImportedFileWriter Add(uint tag, uint version, ReadOnlySpan<byte> data, uint alignment = 16)
    {
        Api.AddSection(Live, tag, version, data, alignment);
        return this;
    }

    /// <summary>Compresses and writes the file atomically.</summary>
    public void Write(string path, Compression compression) => Api.WriteFile(Live, path, compression);

    private WriterHandle Live => _handle.IsNull ? throw new ObjectDisposedException(nameof(ImportedFileWriter)) : _handle;
}
