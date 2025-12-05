namespace Crucible.Core.Exceptions;

public class BufferNotLocalException: Exception
{
    public BufferNotLocalException(string message) : base(message) { }
}