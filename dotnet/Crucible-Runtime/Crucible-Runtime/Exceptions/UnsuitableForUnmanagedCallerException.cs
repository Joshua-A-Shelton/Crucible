using System.Reflection;

namespace Crucible.Exceptions;

public class UnsuitableForUnmanagedCallerException: Exception
{
    public UnsuitableForUnmanagedCallerException(MethodInfo methodInfo, string reason):base($"{methodInfo.Name} is unsuitable for calling by unmanaged code: "+reason)
    { }
}