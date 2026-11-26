namespace Crucible.Scenes;

/// <summary>
/// A component's icon in the editor (ADR 0131): a Lucide icon's name
/// (lucide.dev), shown on the Hierarchy's rows, the Inspector's headers, and
/// in Add Component. A C++ component names it with CRUCIBLE_HINT_ICON on its
/// struct. Components without one show none.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ComponentIconAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
