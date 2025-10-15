namespace Crucible.Exceptions;

public class NonBlittableReturnTypeException: Exception
{
    public Type InvalidType { get; private set; }

    public NonBlittableReturnTypeException(Type returnType) : base(
        $"Function has return type '{returnType.FullName}', which is not a blittable type")
    {
        InvalidType = returnType;
    }
}