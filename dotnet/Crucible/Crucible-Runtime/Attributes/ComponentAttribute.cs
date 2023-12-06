namespace Crucible;

[System.AttributeUsage(System.AttributeTargets.Struct)]
public class ComponentAttribute: System.Attribute
{
    public string? ExposedName=null;

    public ComponentAttribute()
    {
    }

    public ComponentAttribute(string exposedName)
    {
        ExposedName = exposedName;
    }
}