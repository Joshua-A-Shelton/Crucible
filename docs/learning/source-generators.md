# Source generators

A learning note for M3 step 8b (ADR 0036): how `[System]` turns into
registration code, and what to know when changing or adding a generator.

## What a generator is

A Roslyn source generator is a plugin that runs inside the C# compiler.
While a project compiles, it can read the project's code (syntax trees, and
the symbols the compiler resolved from them) and add new `.cs` files. Those
files compile into the same assembly as if you'd written them.

So work that would otherwise need reflection at runtime ("find every class
with this attribute") happens once, at build time, and leaves plain code
behind. Plain code is what NativeAOT needs: it compiles the whole program
ahead of time and can't discover types at runtime. It also reloads with
the game, because it is part of the game's assembly.

A game project uses a generator by referencing it as an analyzer:

```xml
<ProjectReference Include="...Crucible.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

The generator then runs in every build of that project, and in the IDE
while you type. It is never copied into the game's output.

## Why netstandard2.0

The compiler loads generators into itself, and the compiler runs on
several runtimes (.NET in the SDK, .NET Framework inside older Visual
Studio). netstandard2.0 is what all of them can load, so generators must
target it, whatever the game targets. That means older C# defaults (we set
`LangVersion` to `latest`) and a few missing types (`Polyfills.cs`).

The `Microsoft.CodeAnalysis.CSharp` package version is the compiler API
the generator is written against. The compiler that loads it must be at
least that new, so it's pinned low (4.14) rather than to the newest SDK.

## Incremental generators, and why everything compares by value

Generators run constantly in an IDE: on every keystroke, potentially. The
current API (`IIncrementalGenerator`) makes that cheap by describing the
work as a pipeline of stages, which Roslyn caches:

```
classes with [System]  →  one SystemModel per class  →  collect  →  write the file
```

On each run, Roslyn compares each stage's new output with the cached one.
If it's equal, everything after that stage is skipped. That only works if
the outputs compare **by value**:

- Roslyn's symbols (`INamedTypeSymbol`) are tied to one compilation, so a
  new compilation never equals the old one. The per-class stage turns
  symbols into strings at once (`SystemModel`).
- Arrays compare by reference, so `EquatableArray<T>` compares elements.
- A `Location` holds the whole syntax tree. Caching it would keep old trees
  alive, so `LocationInfo` keeps only the file, span, and line numbers.

`UnrelatedEditsReuseTheCachedResult` tests this: after adding a file with
no `[System]` class, every tracked stage reports `Cached` or `Unchanged`.
If someone adds a symbol to `SystemModel`, that test fails.

## The fast path: ForAttributeWithMetadataName

The first stage could look at every class in the project. Instead,
`ForAttributeWithMetadataName("Crucible.Loop.SystemAttribute")` uses the
compiler's index of attribute names, so only classes that carry the
attribute ever reach the generator. It matters in large projects.

## Diagnostics: errors at the right place

A generator can report its own compile errors, which show up in the IDE
and the build like any other error. Ours (`CRU0001`–`CRU0007`) catch
misuse the compiler alone wouldn't: a `[System]` class with no
parameterless constructor would otherwise fail inside generated code,
with an error pointing at a file the game developer never wrote.

Errors are carried through the pipeline as data (`DiagnosticInfo`) and
reported in the last stage, because only that stage can report. A class
with errors gets no `AddSystem` call, so the developer sees our error and
nothing else.

## Things that bit us

- **Build properties leak into the generator.** `dotnet publish
  -p:PublishAot=true` sets that property for every project in the build,
  including the netstandard2.0 generator, where it's an error.
  `TreatAsLocalProperty="PublishAot"` on the generator's `<Project>` lets
  its own setting win.
- **RS2008:** the analyzer rules that come with the Roslyn package want a
  release-notes file of diagnostic IDs. That's for analyzers shipped as
  NuGet packages; ours is off.
- **Diagnostic locations rebuilt from a path** are "external file"
  locations (`IsInSource` is false). They still show at the right file and
  line; tests check the line instead.

## Testing a generator

Roslyn's own `CSharpGeneratorDriver` runs a generator on a compilation made
from source strings, and returns the generated files and diagnostics. The
tests compile against the running .NET's own assemblies
(`TRUSTED_PLATFORM_ASSEMBLIES`) plus `Crucible.Engine.dll`, so they need no
reference-assembly package. Running the compiler needs a JIT, so these
tests have no NativeAOT variant; the generated code's AOT run is in
`Crucible.Engine.Tests`, which uses the generator like a game does.

## Where to look

- `tools/Crucible.Generators/SystemRegistrationGenerator.cs`: the pipeline.
- `tools/Crucible.Generators/Diagnostics.cs`: the errors.
- `tests/managed/Crucible.Generators.Tests/`: what it writes and reports.
- To see generated code in a build, set `EmitCompilerGeneratedFiles` to
  `true` in the game's project; the files land under `obj/`.
