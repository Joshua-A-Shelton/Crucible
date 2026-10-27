using System.Buffers;
using System.Text;

namespace Crucible.Interop;

/// <summary>
/// A C# string converted to UTF-8 for a <c>std::string_view</c> parameter
/// (ADR 0017), without garbage: short strings use the caller's stack buffer,
/// long ones a pooled array returned on dispose. The same approach as .NET's
/// own LibraryImport string marshaller.
/// </summary>
internal ref struct Utf8Argument
{
    /// <summary>Stack bytes the generated code reserves per string argument.</summary>
    public const int StackSize = 256;

    private byte[]? _rented;

    public Utf8Argument(string text, Span<byte> stackBuffer)
    {
        Span<byte> buffer = stackBuffer;
        // The exact count only when the worst case doesn't fit, to keep the
        // common short-string path to one pass.
        if (Encoding.UTF8.GetMaxByteCount(text.Length) > stackBuffer.Length)
        {
            int count = Encoding.UTF8.GetByteCount(text);
            if (count > stackBuffer.Length)
            {
                _rented = ArrayPool<byte>.Shared.Rent(count);
                buffer = _rented;
            }
        }
        Bytes = buffer[..Encoding.UTF8.GetBytes(text, buffer)];
    }

    /// <summary>The UTF-8 bytes, with no terminator.</summary>
    public Span<byte> Bytes { get; }

    public void Dispose()
    {
        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
            _rented = null;
        }
    }
}
