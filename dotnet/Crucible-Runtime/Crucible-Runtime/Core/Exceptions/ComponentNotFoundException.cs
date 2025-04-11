namespace Crucible.Core.Exceptions;

/// <summary>
/// Indicates that a reference component was not found attached to a node
/// </summary>
public class ComponentNotFoundException: Exception
{
    public ComponentNotFoundException(string message) : base(message) { }
}