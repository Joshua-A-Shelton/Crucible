namespace Crucible.Ecs;

/// <summary>
/// A registered component type (ADR 0023). For sparse components the native
/// ID carries a marker bit. The name is a placeholder (docs/naming.md).
/// </summary>
public readonly record struct ComponentId(uint Value);

/// <summary>An entity's index and generation (ADR 0023): see <c>crucible::ecs::Entity</c>.</summary>
public partial struct Entity
{
    public static Entity Null => default;

    public readonly uint Index => (uint)Value;

    public readonly uint Generation => (uint)(Value >> 32);

    public readonly bool IsNull => Value == 0;

    public override readonly string ToString() => IsNull ? "Entity(null)" : $"Entity({Index}v{Generation})";
}
