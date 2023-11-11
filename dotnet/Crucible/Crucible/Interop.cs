using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Crucible;

[StructLayout(LayoutKind.Sequential)]
internal struct FunctionMap
{
    public IntPtr FunctionName;
    public IntPtr FunctionPointer;
}
internal static unsafe class Interop
{
    public delegate void RegisterUnmanagedFunctionDelegate(ref FunctionMap map);

    public static RegisterUnmanagedFunctionDelegate RegisterUnmanagedFunction_ptr = RegisterUnmanagedFunction;
    public static void RegisterUnmanagedFunction(ref FunctionMap mapping)
    {
        string? mapTo = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Marshal.PtrToStringUni(mapping.FunctionName) : Marshal.PtrToStringUTF8(mapping.FunctionName);

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

    public delegate void FreeUnmanagedGCHandleDelegate(GCHandle handle);

    public static FreeUnmanagedGCHandleDelegate FreeUnmanagedGcHandle_ptr = FreeUnmanagedGCHandle;
    public static void FreeUnmanagedGCHandle(GCHandle handle)
    {
        handle.Free();
    }

    public delegate void GetComponentTypesFunctionDelegate(IntPtr unmanagedList);

    public static GetComponentTypesFunctionDelegate GetComponentTypes_ptr = GetComponentTypes;

    public static void GetComponentTypes(IntPtr unmanagedList)
    {
        var typesWithMyAttribute =
            from a in AppDomain.CurrentDomain.GetAssemblies()
            from t in a.GetTypes()
            let attributes = t.GetCustomAttributes(typeof(ComponentAttribute), true)
            where attributes != null && attributes.Length > 0
            select new { Type = t, Attributes = attributes.Cast<ComponentAttribute>() };
        

        foreach (var type in typesWithMyAttribute)
        {
            var typeName = type.Type.ToString();
            UnmanagedPushStringToList(unmanagedList, typeName);
        }
    }
    
    public static delegate* unmanaged<Vector3, Vector3, out Vector3, void*> UnmanagedVector3Cross = null;
    public static delegate* unmanaged<Vector3, Vector3, out float, void*> UnmanagedVector3Dot = null;
    public static delegate* unmanaged<IntPtr, string, void*> UnmanagedPushStringToList = null;

}