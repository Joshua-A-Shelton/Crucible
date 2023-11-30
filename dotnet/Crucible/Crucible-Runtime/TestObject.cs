using System.Runtime.InteropServices;

namespace Crucible;

public class TestObject
{
    private static int currentId = 0;
    public int id = 0;

    public TestObject()
    {
        
        id = ++currentId;
        Console.WriteLine("created "+id);
    }

    ~TestObject()
    {
        Console.WriteLine("destroying "+id);
    }

    public static TestObject CreateTest()
    {
        TestObject t = new TestObject();
        TestObject t2 = new TestObject();
        return t;
    }

    public static void DisplayInfo(IntPtr obj)
    {
        var handle = GCHandle.FromIntPtr(obj);
        Console.WriteLine(handle.Target?.GetType());
    }
}