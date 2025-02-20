using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Crucible.Initialization;

internal static class DelegateCreator
{
    #pragma warning disable CS8602, CS8604 
    private static readonly Func<Type[],Type> MakeNewCustomDelegate = (Func<Type[],Type>)Delegate.CreateDelegate(typeof(Func<Type[],Type>), typeof(Expression).Assembly.GetType("System.Linq.Expressions.Compiler.DelegateHelpers").GetMethod("MakeNewCustomDelegate", BindingFlags.NonPublic | BindingFlags.Static));
    #pragma warning restore CS8602, CS8604 
    public static Type NewDelegateType(Type ret, params Type[] parameters)
    {
        Type[] args = new Type[parameters.Length+1];
        parameters.CopyTo(args, 0);
        args[args.Length-1] = ret;
        return MakeNewCustomDelegate(args);
    }
}