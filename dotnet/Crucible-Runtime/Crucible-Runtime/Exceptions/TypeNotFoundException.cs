namespace Crucible.Exceptions;

public class TypeNotFoundException: Exception
{
    public TypeNotFoundException(string className) : base($" Class {className} not found in loaded assemblies")
    {
        
    }
}