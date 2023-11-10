using System.Reflection;
using System.Runtime.InteropServices;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
internal struct FunctionMap
{
    public IntPtr FunctionName;
    public IntPtr FunctionPointer;
}
internal static unsafe class Interop
{
    public delegate void RegisterUnmanagedFunction_ptr(FunctionMap map);
    public static void RegisterUnmanagedFunction(FunctionMap mapping)
    {
        string? mapTo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Marshal.PtrToStringUni(mapping.FunctionName) : Marshal.PtrToStringUTF8(mapping.FunctionPointer);

        if (mapTo != null)
        {
            var fieldInfo = typeof(Interop).GetField(mapTo,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (fieldInfo == null)
            {
                throw new KeyNotFoundException("No static method \"" + mapTo + "\" exists in Interop to map to");
            }

            fieldInfo.SetValue(null, mapping.FunctionPointer);
        }
    }

    public delegate void GetComponentTypesFunction_ptr();

    public static void GetComponentTypes()
    {
        var typesWithMyAttribute =
            from a in AppDomain.CurrentDomain.GetAssemblies()
            from t in a.GetTypes()
            let attributes = t.GetCustomAttributes(typeof(ComponentAttribute), true)
            where attributes != null && attributes.Length > 0
            select new { Type = t, Attributes = attributes.Cast<ComponentAttribute>() };

        foreach (var type in typesWithMyAttribute)
        {
            Console.WriteLine(type.Type);
        }
    }
    
    public static delegate* unmanaged<Vector3, Vector3, out Vector3, void*> UnmanagedVector3Cross = null;
    public static delegate* unmanaged<Vector3, Vector3, out float, void*> UnmanagedVector3Dot = null;

}