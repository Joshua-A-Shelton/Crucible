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
        return Type.GetTypeFromHandle(RuntimeTypeHandle.FromIntPtr(TypePointer));
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

    public static void GetManagedFunction(ref ManagedType onType, string functionName, BindingFlags flags, ManagedType* parameterTypeArray, Int32 parameterTypeCount, ref ManagedFunctionInternals managedFunctionInternals)
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

        if (methodData.ReturnType != typeof(void) && !IsUnmanaged(methodData.ReturnType))
        {
            throw new NonBlittableReturnTypeException(methodData.ReturnType);
        }
        
        List<Type> paramTypes = new List<Type>();
        if (!methodData.IsStatic)
        {
            paramTypes.Add(type);
        }
        foreach(ParameterInfo pinfo in methodData.GetParameters())
        {
            paramTypes.Add(pinfo.ParameterType);
        }
        
        var del = NewDelegateType(methodData.ReturnType, paramTypes.ToArray());
        var func = Delegate.CreateDelegate(del, null, methodData);
        var handle = GCHandle.Alloc(func, GCHandleType.Normal);
        managedFunctionInternals.DelegateHandle = Marshal.GetFunctionPointerForDelegate(func);
        managedFunctionInternals.DelegateInstance = GCHandle.ToIntPtr(handle);
    }

    public static void NewInstance(ref ManagedType type, ref IntPtr instance)
    {
        var realType = type.Value();
        var inst = Activator.CreateInstance(realType);
        var gcHandle = GCHandle.Alloc(inst,GCHandleType.Normal);
        instance = GCHandle.ToIntPtr(gcHandle);
    }

    public static void FreeInstance(IntPtr handle)
    {
        GCHandle.FromIntPtr(handle).Free();
    }
}