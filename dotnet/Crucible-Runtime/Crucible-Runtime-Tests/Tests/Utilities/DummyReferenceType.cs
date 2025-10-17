using Crucible.Core.Math;

namespace Crucible.Tests.Utilities;

public class DummyReferenceType
{
    public int thing1;
    public int thing2;

    public DummyReferenceType(int thing1, int thing2)
    {
        this.thing1 = thing1;
        this.thing2 = thing2;
    }

    public int Sum()
    {
        return thing1 + thing2;
    }

    public DummyReferenceType? SpanNew(Int32 asNull)
    {
        if (asNull == 0)
        {
            return null;
        }
        return new DummyReferenceType(1, 1);
    }

    public void WriteToConsole()
    {
        Console.Write("Dummy write to console");
    }

    public float SumAsFloat()
    {
        return thing1 + thing2;
    }

    public Vector3 AsVector()
    {
        return new Vector3(thing1, thing2, 0);
    }
}