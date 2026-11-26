using Crucible.Serialization;

namespace Crucible.Scenes;

/// <summary>
/// An entity's name (ADR 0068), as the editor's hierarchy shows it and F2
/// sets it; optional, and saved in scenes. Entities without one are labeled
/// by their prefab or their main component.
/// </summary>
[System.ComponentModel.Category("Scene"), System.ComponentModel.Description("The entity's name, as the Hierarchy shows it.")]
[Serialized]
[Crucible.Scenes.ComponentIcon("tag")]
public sealed partial class EntityName
{
    public string Value { get; set; } = "";
}
