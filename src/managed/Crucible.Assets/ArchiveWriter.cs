using System.Buffers.Binary;
using System.Text;
using Crucible.Binary;

namespace Crucible.Assets;

/// <summary>
/// Writes a PACK archive (ADR 0098; the layout is crucible/PackFile.h's): a
/// header, each asset's bytes on a 4 KiB boundary, and an index sorted by
/// asset ID. Imported files are recompressed for shipping (zstd's high
/// setting); files read as they are (scenes, prefabs) are stored as they are.
/// </summary>
public sealed class ArchiveWriter
{
    public static readonly uint Kind = Tag.Of("PACK");
    public const uint Version = 1; // as packFileVersion
    public const int Alignment = 4096; // as packAlignment
    private const int HeaderSize = 48;
    private const int EntrySize = 48;
    private const int ExtensionSize = 16;

    private readonly SortedDictionary<AssetId, (byte[] Bytes, string Extension)> _entries = new(ByBytes.Instance);

    /// <summary>How many assets it holds.</summary>
    public int Count => _entries.Count;

    /// <summary>The scene the game starts with (none for an archive that isn't a game's own).</summary>
    public AssetId StartScene { get; set; }

    /// <summary>
    /// Adds an asset's file, as the Cache holds it, under its kind
    /// (<paramref name="extension"/>, as <see cref="AssetDatabase.KindOf"/>
    /// names it). An imported (CRBN) file is recompressed.
    /// </summary>
    public void Add(AssetId id, ReadOnlySpan<byte> file, string extension)
    {
        if (Encoding.UTF8.GetByteCount(extension) > ExtensionSize)
        {
            throw new ArgumentException($"{extension} is longer than {ExtensionSize} bytes", nameof(extension));
        }
        _entries[id] = (IsImported(file) ? Recompress(file) : file.ToArray(), extension);
    }

    /// <summary>Writes the archive to <paramref name="path"/>, atomically.</summary>
    public void Write(string path)
    {
        using MemoryStream stream = new();
        stream.Write(new byte[HeaderSize]);
        List<(AssetId Id, long Offset, long Size, string Extension)> placed = [];
        foreach ((AssetId id, (byte[] bytes, string extension)) in _entries)
        {
            Pad(stream);
            placed.Add((id, stream.Position, bytes.Length, extension));
            stream.Write(bytes);
        }
        Pad(stream);
        long indexOffset = stream.Position;
        byte[] entry = new byte[EntrySize];
        foreach ((AssetId id, long offset, long size, string extension) in placed)
        {
            Array.Clear(entry);
            id.Value.TryWriteBytes(entry.AsSpan(0, 16));
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(16), (ulong)offset);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(24), (ulong)size);
            Encoding.UTF8.GetBytes(extension, entry.AsSpan(32, ExtensionSize));
            stream.Write(entry);
        }
        byte[] file = stream.ToArray();
        Span<byte> header = file.AsSpan(0, HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header, Kind);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)placed.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(header[16..], (ulong)indexOffset);
        BinaryPrimitives.WriteUInt64LittleEndian(header[24..], (ulong)file.Length);
        StartScene.Value.TryWriteBytes(header[32..]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        Files.WriteAtomically(path, file);
    }

    /// <summary>An archive's start scene: for tests and tools.</summary>
    public static AssetId ReadStartScene(ReadOnlySpan<byte> archive)
    {
        ReadIndex(archive);
        return new AssetId(new Guid(archive.Slice(32, 16)));
    }

    /// <summary>An archive's entries, in the index's order: for tests and tools.</summary>
    public static IReadOnlyList<(AssetId Id, long Offset, long Size, string Extension)> ReadIndex(ReadOnlySpan<byte> archive)
    {
        if (archive.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(archive) != Kind ||
            BinaryPrimitives.ReadUInt32LittleEndian(archive[4..]) != Version)
        {
            throw new InvalidDataException("not a PACK archive of this version");
        }
        int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(archive[8..]);
        long indexOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(archive[16..]);
        List<(AssetId, long, long, string)> entries = [];
        for (int i = 0; i < count; ++i)
        {
            ReadOnlySpan<byte> entry = archive.Slice((int)indexOffset + i * EntrySize, EntrySize);
            ReadOnlySpan<byte> name = entry.Slice(32, ExtensionSize);
            int length = name.IndexOf((byte)0);
            entries.Add((new AssetId(new Guid(entry[..16])),
                (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]),
                (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[24..]),
                Encoding.UTF8.GetString(length < 0 ? name : name[..length])));
        }
        return entries;
    }

    private static void Pad(MemoryStream stream)
    {
        long padding = (Alignment - stream.Position % Alignment) % Alignment;
        stream.Write(new byte[padding]);
    }

    private static bool IsImported(ReadOnlySpan<byte> file) =>
        file.Length >= 32 && BinaryPrimitives.ReadUInt32LittleEndian(file) == Tag.Of("CRBN");

    // The same sections, compressed for shipping.
    private static byte[] Recompress(ReadOnlySpan<byte> file)
    {
        uint kind = BinaryPrimitives.ReadUInt32LittleEndian(file[8..]);
        uint kindVersion = BinaryPrimitives.ReadUInt32LittleEndian(file[12..]);
        string temporary = Path.Combine(Path.GetTempPath(), $"crucible-cook-{Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            using (ImportedFile source = ImportedFile.Parse(file, kind, kindVersion))
            using (ImportedFileWriter writer = new(kind, kindVersion))
            {
                for (int i = 0; i < source.SectionCount; ++i)
                {
                    SectionInfo info = source.Section(i);
                    writer.Add(info.Tag, info.Version, source.Read(i), info.Alignment);
                }
                writer.Write(temporary, Compression.Small);
            }
            return File.ReadAllBytes(temporary);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    // Asset IDs by their 16 bytes, as a reader's binary search compares them.
    private sealed class ByBytes : IComparer<AssetId>
    {
        public static readonly ByBytes Instance = new();

        public int Compare(AssetId x, AssetId y)
        {
            Span<byte> a = stackalloc byte[16], b = stackalloc byte[16];
            x.Value.TryWriteBytes(a);
            y.Value.TryWriteBytes(b);
            return a.SequenceCompareTo(b);
        }
    }
}
