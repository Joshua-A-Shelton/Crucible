namespace Crucible.Core.Attributes;

[System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Property)]
///Exposes a field or property to the editor
public class Expose: System.Attribute
{
    private string _exposeAs;

    public Expose(string asString)
    {
        _exposeAs = asString;
    }
    
    public string As
    {
        get { return _exposeAs; }
    }
}