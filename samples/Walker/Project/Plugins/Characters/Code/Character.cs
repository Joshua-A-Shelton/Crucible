using System.ComponentModel;
using Crucible.Ecs;
using Crucible.Serialization;

namespace Characters;

/// <summary>
/// A character's stats (ADR 0128): the editor extensions demonstration's
/// component, edited in the Inspector by its drawer and in its workspace.
/// Pace drives how fast its Animator plays while the game runs.
/// </summary>
[Component, Serialized]
[Category("Characters"), Description("A character's stats; Pace sets how fast it animates.")]
[Crucible.Scenes.ComponentIcon("user")]
public partial struct Character
{
    public Character()
    {
        Strength = 10;
        Agility = 10;
        Pace = 1;
    }

    [Range(1, 100)] public int Strength;

    [Range(1, 100)] public int Agility;

    [Range(0.5f, 2), Tooltip("How fast it moves: its Animator's speed while playing")]
    public float Pace;
}
