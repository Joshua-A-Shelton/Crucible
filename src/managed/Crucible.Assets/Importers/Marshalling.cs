using System.Runtime.CompilerServices;

// As in the engine: the importers' native calls pass raw memory only, which
// the binding generator handles itself (ADR 0017).
[assembly: DisableRuntimeMarshalling]
