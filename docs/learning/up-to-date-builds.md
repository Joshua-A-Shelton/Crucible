# Knowing when a build is up to date

A learning note from ADR 0110. Read this before adding a cache or skip
of your own.

## The question every build tool answers

A build turns inputs into outputs. Running it again with the same inputs
makes the same outputs, so a build tool tries to skip work whose inputs
haven't changed. The hard part is deciding "haven't changed" cheaply and
correctly.

## File times

`make`, MSBuild, and Ninja compare modification times: an output older
than any of its inputs is rebuilt. It's cheap (one `stat` per file) and
works well while files stay where they were built. It goes wrong when
times move without contents changing, or the reverse:

- **Copying** a project gives every file a new time on some systems and
  keeps the old one on others. A copy may then look entirely out of date,
  or a stale output may look fresh.
- **Checking out** a branch writes files with the checkout's time.
- **Saving a file as it was** makes it newer, so its outputs rebuild.

## Content hashes

Hashing each input's contents asks the real question: are the bytes the
same? A hash (XxHash128 here) runs at gigabytes a second, so hashing a
game's sources and the engine assembly takes milliseconds. Copies, checkouts,
and no-op saves all hash the same. Bazel and ccache work this way.

The catch is the **list of inputs**. A hash covers only what it's given:
forget an input, and changing it won't rebuild, which is worse than never
skipping. That's why Crucible's stamp covers the C# build, whose inputs
are few and inside the project, and not the C++ module, whose inputs
reach into engine headers, the toolkit, and the compiler.

## What makes a stamp trustworthy

- **Hash the output too.** If something replaces the built assembly, the
  stamp no longer matches it, so the editor builds again.
- **Write the stamp only after a build succeeds,** and delete it before
  starting one. A build that fails or is interrupted leaves no stamp
  claiming it worked.
- **Leave the project's folder out of the hash** so a copy elsewhere is
  still up to date. Then make sure the tools themselves cope with the move:
  dotnet's `obj` folder and CMake's build folder record absolute paths, so
  they are cleared when the project has moved.
- **Test every input.** Change each one and check that the build is no
  longer up to date (`GameBuildTests`).

## Why it helped the tests

Each editor check opens its own copy of a sample so the checks can run at
once (ADR 0109). With file times, or with no stamp at all, every copy built
from nothing. With a content stamp, a copy of a project that was warmed up
once is already up to date: a setup test pays for the cold build, and the
checks that copy it pay nothing.
