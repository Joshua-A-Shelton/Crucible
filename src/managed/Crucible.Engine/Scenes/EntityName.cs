using Crucible.Serialization;

namespace Crucible.Scenes;

/// <summary>
/// An entity's name (ADR 0068), as the editor's hierarchy shows it and F2
/// sets it; optional, and saved in scenes. Entities without one are labeled
/// by their prefab or their main component.
/// </summary>
[Serialized]
public sealed partial class EntityName
{
    public string Value { get; set; } = "";
}
