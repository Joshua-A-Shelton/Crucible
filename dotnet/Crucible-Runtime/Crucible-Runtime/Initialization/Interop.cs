using System.Reflection;
using System.Runtime.InteropServices;

namespace Crucible.Initialization;

[StructLayout(LayoutKind.Sequential)]
internal struct FunctionMap
{
    public IntPtr ClassName;
    public IntPtr DelegateName;
    public IntPtr FunctionPointer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ManagedType
{
    public IntPtr TypePointer = IntPtr.Zero;
    public ManagedType()
    {
        TypePointer = IntPtr.Zero;
    }
}
internal static unsafe class Interop
{
    public delegate void RegisterUnmanagedFunctionDelegate(ref FunctionMap map);

    public static RegisterUnmanagedFunctionDelegate RegisterUnmanagedFunction_ptr = RegisterUnmanagedFunction;

    public static void RegisterUnmanagedFunction(ref FunctionMap mapping)
    {
        string? mapTo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Marshal.PtrToStringUni(mapping.DelegateName) : Marshal.PtrToStringUTF8(mapping.DelegateName);
        if (mapTo != null)
        {
            Type source = typeof(Interop);
            string? className = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Marshal.PtrToStringUni(mapping.ClassName) : Marshal.PtrToStringUTF8(mapping.ClassName);
            if (!string.IsNullOrEmpty(className))
            {
                var t = Type.GetType(className, (name) =>
                {
                    return AppDomain.CurrentDomain.GetAssemblies().Where(z => z.FullName == name.FullName || z.GetName().Name == name.Name).FirstOrDefault();
                },null,true);
                if (t == null)
                {
                    throw new ArgumentException("No such type \"" + className + "\" exists");
                }
                source = t;
            }
            var fieldInfo = source.GetField(mapTo,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (fieldInfo == null)
            {
                throw new KeyNotFoundException("No static field \"" + mapTo + "\" exists in "+source+" to map to");
            }

            fieldInfo.SetValue(null, mapping.FunctionPointer);
        }
    }
}