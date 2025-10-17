using System.Reflection;

namespace Crucible.Exceptions;

public class FunctionNotFoundException: Exception
{
    public Type AttemptedType { get; private set; }
    public string FunctionName { get; private set; }
    public List<Type> ParameterTypes { get; private set; }
    public BindingFlags BindingFlags { get; private set; }
    
    public FunctionNotFoundException(Type type, string functionName, IEnumerable<Type> parameterTypes, BindingFlags flags) : base($" Function '{functionName}' not found on Type '{type.FullName}' in loaded assemblies with given binding flags and parameter types")
    {
        AttemptedType = type;
        FunctionName = functionName;
        ParameterTypes = new List<Type>(parameterTypes);
        BindingFlags = flags;
    }
}