namespace Crucible;

/// <summary>
/// Crucible's version (ADR 0118), generated from the CMake project's, and
/// which versions a compiled plugin built with another one works with.
/// </summary>
public static partial class CrucibleVersion
{
    /// <summary>
    /// The part of a version that must match for a compiled plugin to load
    /// (semantic versioning): the major and minor before 1.0, when any minor
    /// may break the API ("0.1"); the major from 1.0 ("2").
    /// </summary>
    public static string Line => LineOf(Major, Minor);

    /// <summary>
    /// <paramref name="builtFor"/>'s line ("0.4" for "0.4.2"), or null if it
    /// isn't a version: "major.minor.patch", or "major.minor".
    /// </summary>
    public static string? LineOf(string builtFor)
    {
        string[] parts = builtFor.Split('.');
        if (parts.Length is < 2 or > 3 || !parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit)) ||
            !int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out int minor))
        {
            return null;
        }
        return LineOf(major, minor);
    }

    /// <summary>Whether a plugin built with Crucible <paramref name="builtFor"/> works with this one.</summary>
    public static bool Accepts(string builtFor) => LineOf(builtFor) == Line;

    private static string LineOf(int major, int minor) =>
        major == 0 ? $"0.{minor.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : major.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
