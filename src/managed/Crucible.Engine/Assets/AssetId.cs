namespace Crucible.Assets;

/// <summary>
/// An asset's identity (ADR 0021, 0040): a random GUID kept in the asset's
/// sidecar, or inside Crucible's own files, so references survive renames
/// and moves. Saved as the GUID's string, or null for <see cref="None"/>.
/// The name is a placeholder (docs/naming.md).
/// </summary>
public readonly record struct AssetId(Guid Value) : IComparable<AssetId>
{
    /// <summary>No asset: what an unset reference holds.</summary>
    public static AssetId None => default;

    public bool IsNone => Value == Guid.Empty;

    public static AssetId New() => new(Guid.NewGuid());

    public static AssetId Parse(string text) => new(Guid.Parse(text));

    public static bool TryParse(string? text, out AssetId id)
    {
        bool parsed = Guid.TryParse(text, out Guid value);
        id = new AssetId(value);
        return parsed;
    }

    public int CompareTo(AssetId other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString("D");
}
