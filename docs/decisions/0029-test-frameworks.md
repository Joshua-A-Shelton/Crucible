# 0029 — Test frameworks

**Status:** Accepted (2026-10-26).

## Context

M0 needs a C# and a C++ test framework, both run from CTest. Every C#
test suite runs under both JIT and NativeAOT (ADR 0001; M1's "same test
passes under JIT and AOT"), so the C# framework must support NativeAOT.
Test code never ships, so licenses only need to be permissive.

Modern .NET test frameworks run on Microsoft.Testing.Platform, which makes
each test project a standalone executable that CTest can run directly,
including an AOT-compiled one.

## Options

**C#** (checked 2026-10-26):

| Framework | License | NativeAOT |
|---|---|---|
| xUnit v3 | Apache 2.0 | Since 4.0 (2026-08), through separate AOT package variants; some features reflection-only |
| MSTest | MIT | Since 4.4 (2026-09) |
| TUnit | MIT | Built source-generated and AOT-first |
| NUnit | MIT | Not supported; excluded |

**C++:** doctest (MIT), as Slag uses.

## Decision

- **C#: TUnit,** with **MSTest** as the fallback.
- **C++: doctest,** matching Slag, registered with CTest through
  `doctest_discover_tests`.

## Why

TUnit discovers tests with a source generator and never uses reflection,
so AOT is its normal path rather than a separate mode added in 2026, and
Crucible runs every C# suite under AOT. It runs on Microsoft.Testing.Platform,
so test projects are executables CTest runs directly.

Risks: TUnit is younger, with a smaller ecosystem and one main maintainer.
Switching is cheap while the test count is small, and MSTest (Microsoft-
maintained, MIT, AOT supported) is the fallback.

doctest keeps one set of C++ testing habits across Crucible and Slag.
