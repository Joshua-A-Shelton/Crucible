using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Crucible.Core;
using Crucible.Initialization.Implementation;

namespace Crucible.Initialization;




internal static unsafe class Interop
{
    public delegate void VoidDelegate();
    public delegate void IntPtrDelegate(IntPtr pointer);
    public delegate void StringDelegate(string str);
    public delegate void StringStringBoolDelegate(string str1, string str2, bool boolean);
    public delegate void StringManagedTypeDelegate(string str, ref ManagedType managedType);
    public delegate void GetManagedFunctionDelegate(ref ManagedType onType, string functionName, BindingFlags flags, ManagedType* parameterTypeArray, Int32 parameterTypeCount, ref ManagedFunctionInternals managedFunctionInternals);
    public delegate void NewInstanceDelegate(ref ManagedType type, Int32 parameterCount, ManagedType* parameterTypes, IntPtr* parameters, ref IntPtr instance);
    public delegate void InvokeInstanceMethodDelegate(IntPtr instance, string methodName, int parameterCount, ManagedType* types, IntPtr* parameters);
    public delegate void InvokeInstanceMethodReferenceReturnDelegate(IntPtr instance, string methodName, int parameterCount, ManagedType* types, IntPtr* parameters,ref IntPtr returnValue);
    public delegate void InvokeInstanceMethodValueReturnDelegate(IntPtr instance, string methodName, int parameterCount, ManagedType* types, IntPtr* parameters, IntPtr returnValue);
    
    
    public static StringStringBoolDelegate LoadAssemblyPtr = Assemblies.LoadAssembly;
    public static StringDelegate UnloadContextPtr = Assemblies.UnloadContext;
    public static VoidDelegate UnloadAllContextsPtr = Assemblies.UnloadAllContexts;
    
    public static StringManagedTypeDelegate GetManagedTypePtr = Managed.GetManagedType;
    public static GetManagedFunctionDelegate GetManagedFunctionDelegatePtr = Managed.GetManagedFunctionDelegate;
    public static NewInstanceDelegate NewInstancePtr = Managed.NewInstance;
    public static IntPtrDelegate FreeInstancePtr = Managed.FreeInstance;
    public static InvokeInstanceMethodDelegate InvokeInstanceMethodPtr = Managed.InvokeInstanceMethod;
    public static InvokeInstanceMethodReferenceReturnDelegate InvokeInstanceMethodReferenceReturnPtr = Managed.InvokeInstanceMethodWithReturnValueByReference;
    public static InvokeInstanceMethodValueReturnDelegate InvokeInstanceMethodValueReturnPtr = Managed.InvokeInstanceMethodWithReturnValueByValue;

    public static VoidDelegate ManagedInitializePtr = GameManager.Initialize;
    public static VoidDelegate ManagedCleanUpPtr = GameManager.CleanUp;



}