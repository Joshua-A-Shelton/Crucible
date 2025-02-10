using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

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

[StructLayout(LayoutKind.Sequential)]
internal struct StringWrapper
{
    public IntPtr RawString = IntPtr.Zero;

    public StringWrapper()
    {
        RawString = IntPtr.Zero;
    }
}
internal static class Interop
{

    private static Dictionary<string, CrucibleAssemblyLoadContext> _loadedContexts =
        new Dictionary<string, CrucibleAssemblyLoadContext>();

    private static HashSet<Delegate> _delegates = new HashSet<Delegate>();
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
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var assembly in assemblies)
                    {
                        Console.WriteLine(assembly.GetName());
                    }
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
    
    public delegate void FreeUnmanagedGCHandleDelegate(IntPtr handle);

    public static FreeUnmanagedGCHandleDelegate FreeUnmanagedGcHandle_ptr = FreeUnmanagedGCHandle;
    public static void FreeUnmanagedGCHandle(IntPtr handle)
    {
        GCHandle.FromIntPtr(handle).Free();
    }

    public delegate void GetFunctionDelegate(ref ManagedType type, string name, ref IntPtr delegateHandle);

    public static GetFunctionDelegate GetFunction_ptr = GetFunctionHandle;
    public static void GetFunctionHandle(ref ManagedType type, string name, ref IntPtr delegateHandle)
    {
        var handle = RuntimeTypeHandle.FromIntPtr(type.TypePointer);
        Type? realType = Type.GetTypeFromHandle(handle);
        if (realType != null)
        {
            var methodData = realType.GetMethod(name, BindingFlags.Public| BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
            if (methodData == null)
            {
                throw new ArgumentException("No method '" + name + "' exists for on type "+realType);
            }
            //Non-blitable types (minus string) cause a garbage collection leak. The object gets pinned, but we have no way to release it
            if (!BlittableHelper.IsBlittable(methodData.ReturnType) && methodData.ReturnType != typeof(string) && methodData.ReturnType != typeof(void))
            {
                throw new ArgumentException("Cannot have non-blittable return types!");
            }
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
            delegateHandle = Marshal.GetFunctionPointerForDelegate(func);
        }
        
    }

    public delegate void GetTypeHandleDelegate(string delegateName, ref ManagedType returnType);

    public static GetTypeHandleDelegate GetTypeHandle_ptr = GetTypeHandle;
    public static void GetTypeHandle(string delegateName, ref ManagedType returnType)
    {
        string tname = delegateName;
        if (!string.IsNullOrEmpty(tname))
        {
            var type = Type.GetType(tname, (name) =>
            {
                return AppDomain.CurrentDomain.GetAssemblies().Where(z => z.FullName == name.FullName || z.GetName().Name == name.Name).FirstOrDefault();
            },null,true);
            if (type != null)
            {
                returnType.TypePointer = type.TypeHandle.Value;
            }
            else
            {
                returnType.TypePointer = IntPtr.Zero;
            }
            
        }
        else
        {
            throw new Exception("Unable to decode name of type");
        }
       
    }


    public delegate void LoadLibraryDelegate(string contextName,string path);

    public static LoadLibraryDelegate LoadLibrary_ptr=LoadAssembly;

    public static void LoadAssembly(string contextName,string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            AssemblyLoadContext context = null;
            if (string.IsNullOrEmpty(contextName))
            {
                context = AssemblyLoadContext.Default;
            }
            else if (_loadedContexts.ContainsKey(contextName))
            {
                context = _loadedContexts[contextName];
            }
            else
            {
                context = new CrucibleAssemblyLoadContext(full);
                _loadedContexts[contextName] = (CrucibleAssemblyLoadContext)context;
            }

            context.LoadFromAssemblyPath(full);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
        
    }

    public delegate void UnloadLibraryDelegate(string path);

    public static UnloadLibraryDelegate UnloadLibrary_ptr = UnloadAssembly;
    public static void UnloadAssembly(string path)
    {
        var full = Path.GetFullPath(path);
        CrucibleAssemblyLoadContext? context;
        if (_loadedContexts.TryGetValue(full, out context))
        {
            context.Unload();
            _loadedContexts.Remove(full);
        }
    }

    public delegate void UnloadAllContextsDelegate();

    public static UnloadAllContextsDelegate UnloadAllContexts_ptr = UnloadAllContexts;

    public static void UnloadAllContexts()
    {
        foreach (var context in _loadedContexts)
        {
            context.Value.Unload();
        }
        _loadedContexts.Clear();
    }
}