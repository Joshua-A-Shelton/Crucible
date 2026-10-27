# 0031 — Windows C runtime and .NET SDK pinning

**Status:** Accepted (2026-10-26). Found while bringing M1 to Windows
(milestone M1, step 8).

## Context

**The C runtime.** On Windows the C/C++ runtime comes static (`/MT`, compiled
into each program) or as a DLL (`/MD`, needing the Visual C++
Redistributable), each with a debug variant, and one executable can't mix
them. NativeAOT's runtime libraries are built with the static release
runtime, and there is no DLL alternative. Anything statically linked into a
NativeAOT program, the core and Slag included (ADR 0011), must therefore use
`/MT`. CMake's default is `/MD` (`/MDd` in debug).

**The SDK.** With NativeAOT enabled, NuGet lock files record the AOT
compiler (`Microsoft.DotNet.ILCompiler`) and trimming tools
(`Microsoft.NET.ILLink.Tasks`), whose versions the SDK picks: the latest
patch it knows. Two machines a month apart in .NET updates write different
lock files (Linux had 10.0.13, Windows 10.0.14), and CI's locked restores
would break each time .NET ships a patch.

## Options

**C runtime:** (a) `/MT` everywhere on Windows; (b) `/MD` for JIT and
editor builds and `/MT` only for AOT, which needs Slag built twice with
different runtimes in one build and makes JIT and AOT run differently
compiled C++.

**SDK:** pin the exact SDK in `global.json`; drop the lock files (losing
package hash checks); or keep them without locked restores in CI (noisy
diffs).

## Decision

1. **The static release C runtime (`/MT`) everywhere on Windows,** in every
   configuration and for Slag too (`CMAKE_MSVC_RUNTIME_LIBRARY`
   `MultiThreaded`, set before Slag is fetched).
2. **The exact .NET SDK is pinned in `global.json`** (10.0.403, roll-forward
   disabled). Moving to a new .NET patch is a deliberate commit that updates
   `global.json` and regenerates the lock files.

## Why

One runtime model means JIT and AOT builds can't diverge, and shipped games
need no Visual C++ Redistributable: the Windows AOT launcher depends only on
Windows' own DLLs (checked with `dumpbin`). A pinned SDK makes every machine
and CI resolve the same packages, as Slag pins Slang and clang-format.

Given up: MSVC's debug runtime in Windows debug builds (iterator checking,
the debug heap); Slag's validation and Linux debug builds still check.
Every machine needs the exact SDK installed, not just any .NET 10 SDK.

## Related Windows fixes (same step)

- Visual Studio's environment sets `Platform=x64`, which MSBuild reads as a
  property; CMake clears it for `dotnet` commands.
- `libnethost.lib` is built `/MT` too, but `nethost.dll` works with either
  runtime and is what the launcher uses on Windows.
- MSVC drops a DLL import nothing calls, so the engine resolves the core
  next to the executable itself (`CoreLibraryResolver`).
- The launcher uses `wmain` on Windows: `main`'s arguments use the legacy
  code page and garble non-ASCII paths (a CTest test covers it).
