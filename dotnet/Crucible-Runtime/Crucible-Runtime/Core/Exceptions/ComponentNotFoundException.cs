namespace Crucible.Core.Exceptions;

public class ComponentNotFoundException: Exception
{
    public ComponentNotFoundException(string message) : base(message) { }
}