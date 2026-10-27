using System.Runtime.CompilerServices;

// The engine's native calls pass raw memory only (ADR 0017): blittable values,
// structs, and pointers, never automatic conversions of bool, strings, or
// arrays, which the binding generator does itself. Saying so lets the
// LibraryImport source generator pass types from other assemblies by value,
// such as System.Numerics.Vector3 (ADR 0024's amendment), and makes any
// signature that would need conversion a compile error.
[assembly: DisableRuntimeMarshalling]
