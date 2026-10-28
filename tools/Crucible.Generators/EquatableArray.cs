using System.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Crucible.Generators;

// An incremental generator's stages are cached by comparing their outputs, so
// those outputs must compare by value. Arrays compare by reference; this
// wrapper compares element by element.
internal readonly struct EquatableArray<T>(T[] items) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _items = items;

    public int Length => _items?.Length ?? 0;

    public bool Equals(EquatableArray<T> other) =>
        (_items ?? []).AsSpan().SequenceEqual((other._items ?? []).AsSpan());

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in _items ?? [])
        {
            hash = hash * 31 + item.GetHashCode();
        }
        return hash;
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Where a diagnostic goes, without holding on to the syntax tree (which a
// Location does), so cached stages don't keep old trees alive.
internal readonly record struct LocationInfo(string Path, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(Location? location) =>
        location?.SourceTree is null
            ? null
            : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);

    public Location ToLocation() => Location.Create(Path, Span, LineSpan);
}

internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public Diagnostic ToDiagnostic() =>
        Diagnostic.Create(Descriptor, Location?.ToLocation(), Arguments.Cast<object>().ToArray());
}
