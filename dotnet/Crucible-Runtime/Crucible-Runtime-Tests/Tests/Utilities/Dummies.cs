namespace Crucible.Tests.Utilities;

public static class Dummies
{
    public static DummyReferenceType GetDummyReference(int thing1, int thing2)
    {
        return new DummyReferenceType(thing1, thing2);
    }

    public static int GetDummyReferenceThing1(DummyReferenceType dummyReference)
    {
        return dummyReference.thing1;
    }

    public static int LoadedDLLStaticFunction(int thing1, int thing2)
    {
        return thing1 + thing2;
    }
}