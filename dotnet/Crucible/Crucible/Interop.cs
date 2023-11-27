using System.Linq.Expressions;
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

    private static HashSet<Delegate> _delegates;
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

    public delegate void FreeUnmanagedGCHandleDelegate(IntPtr handle);

    public static FreeUnmanagedGCHandleDelegate FreeUnmanagedGcHandle_ptr = FreeUnmanagedGCHandle;
    public static void FreeUnmanagedGCHandle(IntPtr handle)
    {
        var mhandle = GCHandle.FromIntPtr(handle);
        mhandle.Free();
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

    public delegate IntPtr GetFunctionDelegate(ref ManagedType type, string name);

    public static GetFunctionDelegate GetFunction_ptr = GetFunctionHandle;
    public static IntPtr GetFunctionHandle(ref ManagedType type, string name)
    {

        IntPtr method = IntPtr.Zero;
        var handle = RuntimeTypeHandle.FromIntPtr(type.TypePointer);
        Type? realType = Type.GetTypeFromHandle(handle);
        if (realType != null)
        {
            var methodData = realType.GetMethod(name);
            List<Type> paramTypes = new List<Type>();
            if (!methodData.IsStatic)
            {
                paramTypes.Add(realType);
            }
            foreach(ParameterInfo pinfo in methodData.GetParameters())
            {
                paramTypes.Add(pinfo.ParameterType);
            }
            var del = DelegateCreator.NewDelegateType(methodData.ReturnType, paramTypes.ToArray());
            var func = Delegate.CreateDelegate(del, null, methodData);
            //prevent GC from collecting TODO: possibly leave this to the native side
            _delegates.Add(func);
            return Marshal.GetFunctionPointerForDelegate(func);
        }
        return method;
    }

    public delegate ManagedType GetTypeHandleDelegate(string typeName);

    public static GetTypeHandleDelegate GetTypeHandle_ptr = GetTypeHandle;
    public static ManagedType GetTypeHandle(string typeName)
    {
        ManagedType mtype = new ManagedType();
        var type = Type.GetType(typeName);
        if (type != null)
        {
            mtype.TypePointer = type.TypeHandle.Value;
        }
        return mtype;
    }


    public static delegate* unmanaged[Cdecl]<Vector3, Vector3, out Vector3, void*> UnmanagedVector3Cross = null;
    public static delegate* unmanaged<Vector3, Vector3, out float, void*> UnmanagedVector3Dot = null;
    public static delegate* unmanaged<IntPtr, string, void*> UnmanagedPushStringToList = null;

}