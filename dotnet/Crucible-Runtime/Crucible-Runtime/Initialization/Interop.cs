using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Crucible.Core;
using Crucible.Core.Animation;

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

    private static Dictionary<string, AssemblyLoadContext> _loadedContexts = new Dictionary<string, AssemblyLoadContext>();
    private static Dictionary<string, Assembly> _loadedAssemblies = new Dictionary<string, Assembly>();

    static Interop()
    {
        var me = Assembly.GetAssembly(typeof(ManagedType));
        if (me != null)
        {
            _loadedAssemblies.Add(me.GetName().FullName,me);
        }
    }
    
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

    public delegate void NewInstanceDelegate(ref ManagedType type, ref IntPtr instance);

    public static NewInstanceDelegate NewInstance_ptr = NewInstance;

    public static void NewInstance(ref ManagedType type,ref IntPtr instance)
    {
        var handle = RuntimeTypeHandle.FromIntPtr(type.TypePointer);
        Type? realType = Type.GetTypeFromHandle(handle);
        if (realType != null)
        {
            var inst = Activator.CreateInstance(realType);
            var gcHandle = GCHandle.Alloc(inst,GCHandleType.Normal);
            instance = GCHandle.ToIntPtr(gcHandle);
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


    private static object? InvokeInstanceMethodShared(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters)
    {
        var handle = RuntimeTypeHandle.FromIntPtr(type.TypePointer);
        Type? realType = Type.GetTypeFromHandle(handle);
        if (realType != null)
        {
            var inst = GCHandle.FromIntPtr(instance).Target;
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
            var methodInfo = realType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,paramTypes.ToArray());
            if (methodInfo == null)
            {
                throw new ArgumentException("No method '" + methodName + "' exists for on type " + realType);
            }
            return methodInfo.Invoke(inst,parametersInstances.ToArray());
        }
        else
        {
            throw new ArgumentException("Type is not a valid type");
        }
    }
    public delegate void InvokeInstanceMethodDelegate(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters);

    public static InvokeInstanceMethodDelegate InvokeInstanceMethod_ptr = InvokeInstanceMethod;

    public static void InvokeInstanceMethod(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters)
    {
        InvokeInstanceMethodShared(ref type,instance, methodName, parameterCount, types, parameters);
    }
    
    public delegate void InvokeInstanceMethodWithReturnValueDelegate(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters, ref IntPtr returnValue, ref ManagedType returnType);

    public static InvokeInstanceMethodWithReturnValueDelegate InvokeInstanceMethodWithReturnValueByReferenceDelegate_ptr = InvokeInstanceMethodWithReturnValueByReference;
    public static void InvokeInstanceMethodWithReturnValueByReference(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters,ref IntPtr returnValue, ref ManagedType returnType)
    {
        var reference = InvokeInstanceMethodShared(ref type, instance, methodName, parameterCount, types, parameters);
        if (reference == null)
        {
            returnValue = IntPtr.Zero;
            returnType.TypePointer = IntPtr.Zero;
            return;
        }

        if (reference.GetType().IsValueType)
        {
            throw new InvalidOperationException("Cannot return reference types by value");
        }
        var handle = GCHandle.Alloc(reference, GCHandleType.Normal);
        returnValue = GCHandle.ToIntPtr(handle);
        returnType.TypePointer = reference.GetType().TypeHandle.Value;
    }
    
    public delegate void InvokeInstanceMethodWithReturnValueByValueDelegate(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters, IntPtr returnValue);

    public static InvokeInstanceMethodWithReturnValueByValueDelegate InvokeInstanceMethodWithReturnValueByValueDelegate_ptr = InvokeInstanceMethodWithReturnValueByValue;

    public static void InvokeInstanceMethodWithReturnValueByValue(ref ManagedType type, IntPtr instance, string methodName, int parameterCount, IntPtr* types, IntPtr* parameters, IntPtr returnValue)
    {
        var reference = InvokeInstanceMethodShared(ref type, instance, methodName, parameterCount, types, parameters);
        if (reference == null)
        {
            throw new InvalidDataException("Cannot have null return value when returning value types!");
        }

        if (!reference.GetType().IsValueType)
        {
            throw new InvalidOperationException("Cannot return value types by reference");
        }
        
        Marshal.StructureToPtr(reference, returnValue, false);
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
    
    public delegate void GetMeshRenderDataDelegate(IntPtr meshRendererManagedReference, ref IntPtr mesh, ref IntPtr material, ref byte priority);

    public static GetMeshRenderDataDelegate GetMeshRenderData_ptr = GetMeshRenderData;

    public static void GetMeshRenderData(IntPtr meshRendererManagedReference, ref IntPtr mesh, ref IntPtr material, ref byte priority)
    {
        var inst = GCHandle.FromIntPtr(meshRendererManagedReference);
        MeshRenderer? meshRenderer = (MeshRenderer?)inst.Target;
        if (meshRenderer != null)
        {
            mesh = meshRenderer.Mesh._pointer;
            material = meshRenderer.Material._materialPointer;
            priority = meshRenderer.Priority;
        }
        else
        {
            mesh = IntPtr.Zero;
            material = IntPtr.Zero;
            priority = 0;
        }
        
    }
    
    public delegate void GetSkeletonDelegate(IntPtr skeletonManagedReference, ref IntPtr skeleton);

    public static GetSkeletonDelegate GetSkeleton_ptr = GetSkeleton;

    public static void GetSkeleton(IntPtr skeletonManagedReference, ref IntPtr skeleton)
    {
        var inst = GCHandle.FromIntPtr(skeletonManagedReference);
        Skeleton? skel = (Skeleton?)inst.Target;
        if (skel != null)
        {
            skeleton = skel._pointer;
        }
        else
        {
            skeleton = IntPtr.Zero;
        }
    }

    public delegate void GetCameraDelegate(IntPtr cameraManagedReference, ref IntPtr camera);

    public static GetCameraDelegate GetCamera_ptr = GetCamera;

    public static void GetCamera(IntPtr cameraManagedReference, ref IntPtr camera)
    {
        var inst = GCHandle.FromIntPtr(cameraManagedReference);
        Camera? managedCamera = (Camera?)inst.Target;
        if (managedCamera != null)
        {
            camera = managedCamera._cameraPtr;
        }
        else
        {
            camera = IntPtr.Zero;
        }
        
    }
    
    
    internal static Assembly? ResolveAssembly(AssemblyLoadContext? context, AssemblyName assemblyName)
    {
        //if we've already loaded an assembly with the given name, re-use it instead of loading it again
        if (_loadedAssemblies.TryGetValue(assemblyName.FullName, out var loadedAssembly))
        {
            return loadedAssembly;
        }

        return null;
    }


    public delegate void LoadLibraryDelegate(string contextName,string path,bool collectible);

    public static LoadLibraryDelegate LoadLibrary_ptr=LoadAssembly;

    public static void LoadAssembly(string contextName,string path,bool collectible)
    {
        try
        {
            var full = Path.GetFullPath(path);
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
        AssemblyLoadContext? context;
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