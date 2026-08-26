using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Crucible.Initialization;

/// <summary>
/// The layout of this struct should match that in ScriptingModule.hs
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ScriptingModule
{
    Initializer.CrucibleModule module;
    private IntPtr* managedLoadAssembly;
    private IntPtr* managedUnloadAssembly;
    private IntPtr* managedUnloadAllContexts;
    private IntPtr* managedGetType;
    private IntPtr* managedCreateInstance;
    private IntPtr* managedCreateInstanceWithParams;
    private IntPtr* managedFreeInstance;
    
    public void Initialize()
    {
        *managedLoadAssembly = Marshal.GetFunctionPointerForDelegate(LoadAssembly_ptr);
        *managedUnloadAssembly = Marshal.GetFunctionPointerForDelegate(UnloadAssembly_ptr);
        *managedUnloadAllContexts = Marshal.GetFunctionPointerForDelegate(UnloadAllContexts_ptr);
        *managedGetType = Marshal.GetFunctionPointerForDelegate(GetManagedType_ptr);
        *managedCreateInstance = Marshal.GetFunctionPointerForDelegate(CreateInstance_ptr);
        *managedCreateInstanceWithParams = Marshal.GetFunctionPointerForDelegate(CreateInstanceWithParams_ptr);
        *managedFreeInstance = Marshal.GetFunctionPointerForDelegate(FreeInstance_ptr);
    }

   
    
    private static Dictionary<string, AssemblyLoadContext> _loadedContexts = new Dictionary<string, AssemblyLoadContext>();
    private static Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();

    enum AssemblyLoadResult
    {
        Success = 0,
        AssemblyNotFound = 1,
        UnknownError
    }
    
    //Methods and delegates

    private delegate AssemblyLoadResult LoadAssemblyDelegate(string contextName, string assemblyPath, bool collectable);
    private static LoadAssemblyDelegate LoadAssembly_ptr = ManagedLoadAssembly;
    private static AssemblyLoadResult ManagedLoadAssembly(string contextName, string assemblyPath, bool collectible)
    {
        try
        {
            var full = Path.GetFullPath(assemblyPath);
            if (!File.Exists(full))
            {
                return AssemblyLoadResult.AssemblyNotFound;
            }
            AssemblyLoadContext? context = null;
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
                context = new AssemblyLoadContext(full,collectible);
                context.Resolving += ResolveAssembly;
                context.Unloading += loadContext =>
                {
                    foreach (var assembly in loadContext.Assemblies)
                    {
                        _loadedAssemblies.Remove(assembly.GetName().FullName);
                    }
                };
                
                _loadedContexts[contextName] = context;
            }

            var assembly = context.LoadFromAssemblyPath(full);
            _loadedAssemblies.Add(assembly.GetName().FullName,assembly);
            //this... isn't perfect, but add all assemblies referenced in the loaded assembly... I should probably set up
            //a reference count to only unload an assembly when all it's dependents are also unloaded
            foreach (var asmbly in context.Assemblies)
            {
                if (!_loadedAssemblies.ContainsKey(assembly.GetName().FullName))
                {
                    _loadedAssemblies.Add(assembly.GetName().FullName,asmbly);
                }
            }

            return AssemblyLoadResult.Success;
        }
        catch (Exception e)
        {
            return AssemblyLoadResult.UnknownError;
        }
    }
    
    private delegate void UnloadAssemblyDelegate(string assemblyPath);
    private static UnloadAssemblyDelegate UnloadAssembly_ptr = ManagedUnloadAssembly;
    private static void ManagedUnloadAssembly(string assemblyPath)
    {
        var full = Path.GetFullPath(assemblyPath);
        AssemblyLoadContext? context;
        if (_loadedContexts.TryGetValue(full, out context))
        {
            context.Unload();
            _loadedContexts.Remove(full);
        }
    }
    
    private delegate void UnloadAllContextsDelegate();
    private static UnloadAllContextsDelegate UnloadAllContexts_ptr = ManagedUnloadAllContexts;
    private static void ManagedUnloadAllContexts()
    {
        foreach (var context in _loadedContexts)
        {
            context.Value.Unload();
        }
        _loadedContexts.Clear();
    }

    private delegate IntPtr GetManagedTypeDelegate(string typeName);
    private static GetManagedTypeDelegate GetManagedType_ptr = ManagedGetType;
    private static IntPtr ManagedGetType(string typeName)
    {
        if (!string.IsNullOrEmpty(typeName))
        {
            var type = Type.GetType(typeName,
                (name) =>
                {
                    return AppDomain.CurrentDomain
                        .GetAssemblies().FirstOrDefault(z => z.FullName == name.FullName || z.GetName().Name == name.Name);
                }, null, true);
            if (type != null)
            {
                return type.TypeHandle.Value;
            }
        }
        return IntPtr.Zero;
    }

    private delegate IntPtr CreateInstanceDelegate(IntPtr typeHandle);
    private static CreateInstanceDelegate CreateInstance_ptr = ManagedCreateInstance;
    private static IntPtr ManagedCreateInstance(IntPtr typeHandle)
    {
        var handle = RuntimeTypeHandle.FromIntPtr(typeHandle);
        var type = Type.GetTypeFromHandle(handle);
        if (type != null)
        {
            var instance = Activator.CreateInstance(type);
            if (instance != null)
            {
                var gcHandle = GCHandle.Alloc(instance, GCHandleType.Normal);
                return GCHandle.ToIntPtr(gcHandle);
            }
        }
        return IntPtr.Zero;
    }
    
    private delegate IntPtr CreateInstanceWithParamsDelegate(IntPtr typeHandle, int parameterCount, IntPtr* types, IntPtr* parameters);
    private static CreateInstanceWithParamsDelegate CreateInstanceWithParams_ptr = ManagedCreateInstanceWithParams;
    private static IntPtr ManagedCreateInstanceWithParams(IntPtr typeHandle, int parameterCount, IntPtr* types, IntPtr* parameters)
    {
        var handle = RuntimeTypeHandle.FromIntPtr(typeHandle);
        var type = Type.GetTypeFromHandle(handle);
        if (type != null)
        {
            
            List<Type> paramTypes = new List<Type>();
            List<object?> parametersInstances = new List<object?>();
            for (int i = 0; i < parameterCount; i++)
            {
                var runtimeType = RuntimeTypeHandle.FromIntPtr(types[i]);
                Type? paramType = Type.GetTypeFromHandle(runtimeType);
                if (paramType == null)
                {
                    throw new ArgumentException("Parameter " + i + "'s type is not a valid type");
                }
                paramTypes.Add(paramType);
                if (paramType.IsValueType)
                {
                    object? valInst = Marshal.PtrToStructure(parameters[i],paramType);
                    parametersInstances.Add(valInst);
                }
                else
                {
                    var refInst =  GCHandle.FromIntPtr(parameters[i]).Target;
                    parametersInstances.Add(refInst);
                }
            }
            var instance = Activator.CreateInstance(type, parametersInstances.ToArray());
            if (instance != null)
            {
                var gcHandle = GCHandle.Alloc(instance, GCHandleType.Normal);
                return GCHandle.ToIntPtr(gcHandle);
            }
        }
        return IntPtr.Zero;   
    }

    private delegate void FreeInstanceDelegate(IntPtr handle);
    private static FreeInstanceDelegate FreeInstance_ptr = ManagedFreeInstance;
    private static void ManagedFreeInstance(IntPtr handle)
    {
        GCHandle.FromIntPtr(handle).Free();
    }
    
    
    
    
    //Helper functions
    private static Assembly? ResolveAssembly(AssemblyLoadContext? context, AssemblyName assemblyName)
    {
        //if we've already loaded an assembly with the given name, re-use it instead of loading it again
        if (_loadedAssemblies.TryGetValue(assemblyName.FullName, out var loadedAssembly))
        {
            return loadedAssembly;
        }

        return null;
    }

}