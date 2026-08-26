using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NativeExample;

public class SomeObject
{
    public int SomeInt;
    public float SomeFloat;
}

public static class NativeMethods
{
    [UnmanagedCallersOnly(EntryPoint = "AddNumbers")]
    public static int AddNumbers(int a, int b)
    {
        return a + b;
    }
}