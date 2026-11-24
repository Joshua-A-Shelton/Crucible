namespace Crucible.Hosting;

/// <summary>
/// The plugins a game uses (ADR 0118), by assembly name, in the order they
/// load: each after those it uses. The editor writes it into the game's
/// build, so the game loader finds them in the editor, in a package, and
/// compiled in with NativeAOT alike, and registers their systems,
/// components, and services before the game's.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class GamePluginsAttribute(params string[] names) : Attribute
{
    public IReadOnlyList<string> Names { get; } = names;
}
