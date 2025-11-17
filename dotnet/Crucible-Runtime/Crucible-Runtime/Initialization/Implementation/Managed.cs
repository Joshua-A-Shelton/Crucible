using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using Crucible.Exceptions;

namespace Crucible.Initialization.Implementation;

[StructLayout(LayoutKind.Sequential)]
internal struct ManagedType
{
    public IntPtr TypePointer = IntPtr.Zero;
    public ManagedType()
    {
        TypePointer = IntPtr.Zero;
    }

    public Type Value()
    {
        return Type.GetTypeFromHandle(RuntimeTypeHandle.FromIntPtr(TypePointer))!;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct ManagedFunctionInternals
{
    public IntPtr DelegateHandle = IntPtr.Zero;
    public IntPtr DelegateInstance =  IntPtr.Zero;

    public ManagedFunctionInternals()
    {
        DelegateHandle = IntPtr.Zero;
        DelegateInstance = IntPtr.Zero;
    }
}

internal unsafe static class Managed
{
#region HelperMethods

    public static bool IsUnmanaged(this Type type)
    {
        // primitive, pointer or enum -> true
        if (type.IsPrimitive || type.IsPointer || type.IsEnum)
            return true;

        // not a struct -> false
        if (!type.IsValueType)
            return false;

        // otherwise check recursively
        return type
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .All(f => IsUnmanaged(f.FieldType));
    }
    
#pragma warning disable CS8602, CS8604 
    private static readonly Func<Type[],Type> MakeNewCustomDelegate = (Func<Type[],Type>)Delegate.CreateDelegate(typeof(Func<Type[],Type>), typeof(Expression).Assembly.GetType("System.Linq.Expressions.Compiler.DelegateHelpers").GetMethod("MakeNewCustomDelegate", BindingFlags.NonPublic | BindingFlags.Static));
#pragma warning restore CS8602, CS8604 
    
    private static Type NewDelegateType(Type ret, params Type[] parameters)
    {
        Type[] args = new Type[parameters.Length+1];
        parameters.CopyTo(args, 0);
        args[args.Length-1] = ret;
        
        return MakeNewCustomDelegate(args);
    }

    private class ParameterData
    {
        public List<Type> paramTypes = new List<Type>();
        public List<object?> parametersInstances = new List<object?>();
    }
    private static ParameterData ExtractParameterData(Int32 count, ManagedType* types, IntPtr* parameters)
    {
        ParameterData pd = new ParameterData();
        for (int i = 0; i < count; i++)
        {
            var paramType = types[i].Value();
            pd.paramTypes.Add(paramType);
            if (paramType.IsValueType)
            {
                object? valInst = Marshal.PtrToStructure(parameters[i],paramType);
                pd.parametersInstances.Add(valInst);
            }
            else
            {
                var refInst =  GCHandle.FromIntPtr(parameters[i]).Target;
                pd.parametersInstances.Add(refInst);
            }
        }
        return pd;
    }

#endregion
    
    public static void GetManagedType(string typeName, ref ManagedType managedType)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            throw new ArgumentException("typeName is null or empty");
        }
        var type = Type.GetType(typeName, (name) =>
        {
            return AppDomain.CurrentDomain.GetAssemblies().Where(z => z.FullName == name.FullName || z.GetName().Name == name.Name).FirstOrDefault();
        },null,false);
        if (type != null)
        {
            managedType.TypePointer = type.TypeHandle.Value;
        }
        else
        {
            throw new TypeNotFoundException(typeName);
        }
    }
    
    private static object? InvokeInstanceMethodShared(IntPtr instance, string methodName, int parameterCount, ManagedType* parameterTypes, IntPtr* parameters)
    {
        var inst = GCHandle.FromIntPtr(instance).Target;
        if (inst == null)
        {
            return null;
        }
        var realType = inst.GetType();
        ParameterData pc = ExtractParameterData(parameterCount, parameterTypes, parameters);
        var methodInfo = realType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,pc.paramTypes.ToArray());
        if (methodInfo == null)
        {
            throw new ArgumentException("No method '" + methodName + "' exists for on type " + realType);
        }
        return methodInfo.Invoke(inst,pc.parametersInstances.ToArray());
    }

    public static void GetManagedFunctionDelegate(ref ManagedType onType, string functionName, BindingFlags flags, ManagedType* parameterTypeArray, Int32 parameterTypeCount, ref ManagedFunctionInternals managedFunctionInternals)
    {
        var type = onType.Value();
        Type[] parameterTypes = new Type[parameterTypeCount];
        for (int i = 0; i < parameterTypeCount; i++)
        {
            parameterTypes[i] = parameterTypeArray[i].Value();
        }
        var methodData = type.GetMethod(functionName,flags,parameterTypes);
        if (methodData == null)
        {
            throw new FunctionNotFoundException(type,functionName,parameterTypes,flags);
        }
        if (!methodData.IsStatic)
        {
            throw new UnsuitableForUnmanagedCallerException(methodData,"function is not static");
        }

        if (methodData.ReturnType != typeof(void) && methodData.ReturnType != typeof(string) && !IsUnmanaged(methodData.ReturnType))
        {
            throw new UnsuitableForUnmanagedCallerException(methodData,"function does not have blittable return type");
        }
        
        List<Type> paramTypes = new List<Type>();
        
        foreach(ParameterInfo pinfo in methodData.GetParameters())
        {
            if (pinfo.ParameterType != typeof(void) && pinfo.ParameterType != typeof(string) && !IsUnmanaged(pinfo.ParameterType))
            {
                throw new UnsuitableForUnmanagedCallerException(methodData,"function has non blittable parameter types");
            }
            paramTypes.Add(pinfo.ParameterType);
        }
        var del = NewDelegateType(methodData.ReturnType, paramTypes.ToArray());
        var func = Delegate.CreateDelegate(del,null, methodData);
        var handle = GCHandle.Alloc(func, GCHandleType.Normal);
        managedFunctionInternals.DelegateHandle = Marshal.GetFunctionPointerForDelegate(func);
        managedFunctionInternals.DelegateInstance = GCHandle.ToIntPtr(handle);
    }

    public static void NewInstance(ref ManagedType type, Int32 parameterCount, ManagedType* parameterTypes, IntPtr* parameters, ref IntPtr instance)
    {
        var realType = type.Value();
        ParameterData pc = ExtractParameterData(parameterCount, parameterTypes, parameters);
        var inst = Activator.CreateInstance(realType,pc.parametersInstances.ToArray());
        var gcHandle = GCHandle.Alloc(inst,GCHandleType.Normal);
        instance = GCHandle.ToIntPtr(gcHandle);
    }

    public static void FreeInstance(IntPtr handle)
    {
        GCHandle.FromIntPtr(handle).Free();
    }
    
    public static void InvokeInstanceMethod(IntPtr instance, string methodName, int parameterCount, ManagedType* types, IntPtr* parameters)
    {
        InvokeInstanceMethodShared(instance, methodName, parameterCount, types, parameters);
    }
    
    public static void InvokeInstanceMethodWithReturnValueByReference(IntPtr instance, string methodName, int parameterCount, ManagedType* types, IntPtr* parameters,ref IntPtr returnValue)
    {
        var reference = InvokeInstanceMethodShared(instance, methodName, parameterCount, types, parameters);
        if (reference == null)
        {
            returnValue = IntPtr.Zero;
            return;
        }

        if (reference.GetType().IsValueType)
        {
            throw new InvalidOperationException("Cannot return reference types by value");
        }
        var handle = GCHandle.Alloc(reference, GCHandleType.Normal);
        returnValue = GCHandle.ToIntPtr(handle);
    }
    
    public static void InvokeInstanceMethodWithReturnValueByValue(IntPtr instance, string methodName, int parameterCount, ManagedType* types, IntPtr* parameters, IntPtr returnValue)
    {
        var reference = InvokeInstanceMethodShared(instance, methodName, parameterCount, types, parameters);
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
}