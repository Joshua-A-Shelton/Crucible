namespace Crucible;

[System.AttributeUsage(System.AttributeTargets.Field | AttributeTargets.Property)]
public class ExposeAttribute: System.Attribute
{
    public string? Name = null;
    public ExposeAttribute(){}

    public ExposeAttribute(string name)
    {
        Name = name;
    }
}