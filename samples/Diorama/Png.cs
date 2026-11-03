using System.Buffers.Binary;

namespace Crucible.Samples.Diorama;

/// <summary>
/// The sample's images: 8-bit RGB PNGs, written uncompressed (stored deflate
/// blocks) so no compression library is needed, and read back only in that
/// form (what <see cref="Write"/> makes).
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>Rows top to bottom, 3 bytes (R, G, B) per pixel.</summary>
    public static void Write(string path, int width, int height, ReadOnlySpan<byte> rgb)
    {
        // Each row starts with filter type 0 (none).
        byte[] raw = new byte[height * (width * 3 + 1)];
        for (int y = 0; y < height; ++y)
        {
            rgb.Slice(y * width * 3, width * 3).CopyTo(raw.AsSpan(y * (width * 3 + 1) + 1));
        }
        using MemoryStream zlib = new();
        zlib.WriteByte(0x78); // deflate, 32 KB window
        zlib.WriteByte(0x01); // no preset dictionary, fastest; 0x7801 % 31 == 0
        for (int at = 0; at < raw.Length || at == 0; at += 65535)
        {
            int length = Math.Min(65535, raw.Length - at);
            zlib.WriteByte(at + length >= raw.Length ? (byte)1 : (byte)0); // stored, final or not
            zlib.WriteByte((byte)length);
            zlib.WriteByte((byte)(length >> 8));
            zlib.WriteByte((byte)~length);
            zlib.WriteByte((byte)(~length >> 8));
            zlib.Write(raw, at, length);
        }
        Span<byte> adler = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(adler, Adler32(raw));
        zlib.Write(adler);

        using FileStream file = File.Create(path);
        file.Write(Signature);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bits per channel
        header[9] = 2; // RGB
        Chunk(file, "IHDR", header);
        Chunk(file, "IDAT", zlib.ToArray());
        Chunk(file, "IEND", []);
    }

    /// <summary>An image <see cref="Write"/> made: its size and RGB rows.</summary>
    public static (int Width, int Height, byte[] Rgb) Read(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        if (!file.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new InvalidDataException($"{path} isn't a PNG.");
        }
        int width = 0, height = 0;
        using MemoryStream data = new();
        for (int at = 8; at < file.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(at));
            string kind = System.Text.Encoding.ASCII.GetString(file, at + 4, 4);
            ReadOnlySpan<byte> body = file.AsSpan(at + 8, length);
            if (kind == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(body);
                height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                if (body[8] != 8 || body[9] != 2)
                {
                    throw new InvalidDataException($"{path} isn't 8-bit RGB.");
                }
            }
            else if (kind == "IDAT")
            {
                data.Write(body);
            }
            at += 12 + length;
        }
        // Stored deflate blocks only, after the 2-byte zlib header.
        byte[] stream = data.ToArray();
        using MemoryStream raw = new();
        for (int at = 2; ;)
        {
            byte flags = stream[at];
            if ((flags & 0x6) != 0)
            {
                throw new InvalidDataException($"{path} is compressed; only uncompressed PNGs are read.");
            }
            int length = stream[at + 1] | (stream[at + 2] << 8);
            raw.Write(stream, at + 5, length);
            at += 5 + length;
            if ((flags & 1) != 0)
            {
                break;
            }
        }
        byte[] rows = raw.ToArray();
        byte[] rgb = new byte[width * height * 3];
        for (int y = 0; y < height; ++y)
        {
            if (rows[y * (width * 3 + 1)] != 0)
            {
                throw new InvalidDataException($"{path} uses PNG row filters; only unfiltered rows are read.");
            }
            rows.AsSpan(y * (width * 3 + 1) + 1, width * 3).CopyTo(rgb.AsSpan(y * width * 3));
        }
        return (width, height, rgb);
    }

    private static void Chunk(Stream file, string kind, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        byte[] kindBytes = System.Text.Encoding.ASCII.GetBytes(kind);
        file.Write(kindBytes);
        file.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, kindBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        file.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] MakeCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; ++n)
        {
            uint c = n;
            for (int k = 0; k < 8; ++k)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}
