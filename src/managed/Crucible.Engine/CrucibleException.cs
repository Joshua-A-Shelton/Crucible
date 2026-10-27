namespace Crucible;

/// <summary>
/// An unexpected failure reported by the C++ core (ADR 0030). The generated
/// bindings throw it when a function returning <c>crucible::Result</c> fails,
/// unless that function is marked to return its <see cref="Status"/> instead.
/// The name is a placeholder (docs/naming.md).
/// </summary>
public sealed class CrucibleException : Exception
{
    public CrucibleException(Status status, string function)
        : base($"{function} failed: {status}")
    {
        Status = status;
        Function = function;
    }

    /// <summary>The status the core returned.</summary>
    public Status Status { get; }

    /// <summary>The C++ function that failed, such as <c>crucible::platform::createWindow</c>.</summary>
    public string Function { get; }
}
