# The shader toolchain

A learning note for M5 step 1 (ADR 0047): from Slang source to code each
graphics API runs, and why the hard part is agreeing on where every byte
goes.

## One source, two machine languages

GPUs don't run shader source. Each API takes its own intermediate form,
which the driver compiles again for the actual GPU:

- **D3D12** takes **DXIL**, an LLVM-based bytecode. Microsoft's DXC is the
  only producer, and the runtime only accepts DXIL that DXC's validator
  signed.
- **Vulkan** takes **SPIR-V**, Khronos' binary format, which any compiler
  may produce and `spirv-val` checks.

**Slang** compiles one source to both (and more), so Crucible's shaders are
written once. For DXIL it hands its output to DXC, which is why Crucible
builds DXIL only on Windows, where D3D12 runs anyway.

A compiled shader is a `.crshader` file with one section per format, in
order of preference. Slag takes a list of binaries per stage and uses the
first one its backend accepts, so the engine never asks "which backend am
I on?".

Which formats go in the file is decided by the platform being built for,
not by the shader: Windows can run D3D12 and Vulkan, so it gets both;
Linux runs Vulkan only, so it gets SPIR-V; a console would get only its
own format. Sections are numbered by format (`CD00` DXIL, `CD01` SPIR-V),
so a console backend kept in a private repository can add its own section
without public code ever naming it.

## Why layouts are the hard part

The CPU fills buffers the GPU reads: camera matrices, lights, material
parameters. Both sides must agree on every field's offset, or the GPU
reads garbage with no error at all. The trouble is that there are several
rule sets for laying out the same struct:

| Rules | Used for | `float a; float3 b;` |
|---|---|---|
| D3D constant buffer packing | D3D12 `cbuffer`s | `b` at 4 (a vector may not straddle a 16-byte boundary, and this one doesn't) |
| D3D structured buffers | D3D12 `StructuredBuffer` | `b` at 4, stride 16 |
| std140 | Vulkan uniform buffers by default | `b` at 16 (vec3 aligned to 16) |
| std430 | Vulkan storage buffers by default | `b` at 16, stride 32 |
| scalar | Vulkan with `scalarBlockLayout` | `b` at 4, stride 16 |

Crucible makes both APIs use D3D12's rules (Slag's reference API): Slang's
`-fvk-use-dx-layout` makes the SPIR-V follow them, and Vulkan's
`scalarBlockLayout` feature (mandatory in Vulkan 1.4) lets the driver
accept packed storage buffers. That needed a change in Slag, which now
enables the feature on every device.

Even within D3D12 the two rule sets disagree sometimes: `float2 uv; float3
tint;` puts `tint` at 8 in a structured buffer but at 16 in a constant
buffer, because at 8 it would straddle 16. So a struct shared with the CPU
must be laid out the same way under every rule set, which the build checks.
Where they disagree, the author adds explicit padding
(`float2 uv; float2 padding; float3 tint;`).

## One source of truth, generated mirrors

A struct the CPU and GPU share is written once, in Slang, and marked
`[GpuShared]`. The build tool asks Slang where each field lands and
generates the C++ and C# structs, with checks (`static_assert(offsetof(...)
== 16)`) that fail to compile if anything drifts. Hand-written copies on
three sides would drift eventually; generated ones can't.

A lesson from building it: Slang's reflection API has a call to lay out a
type under given rules, and it **ignores** `-fvk-use-dx-layout`. The real
code generator honours the flag; that query doesn't. The fix was to ask the
same question the way a shader would: declare each struct as a constant
buffer and a structured buffer in a tiny generated module, and read
*those* layouts. When a tool's answer and the real behaviour can differ,
check the real behaviour.

Another: Slang's command-line options attach to the most recent `-target`.
Given once at the end, `-fvk-use-dx-layout` silently applied only to the
last target, not to SPIR-V.

## Testing on the GPU

The only proof that layouts agree is the GPU writing a struct and the CPU
reading it back through the generated mirror, on every backend. The test
does exactly that, and it found a real bug on its first run on Windows: on
NVIDIA's Vulkan driver (but not on D3D12 with the same GPU, nor on AMD's
RADV), a struct inside a struct was written at the wrong place. A
shader writing only `output[i].tint.strength` put the value at byte 12
instead of 92: the inner field's offset was used, the outer one dropped.

Narrowing it down went in steps:

1. The Windows-built SPIR-V passed on Linux's AMD driver, so the compiler
   wasn't at fault.
2. `spirv-val` accepted it, and its disassembly showed the right offsets
   and a correct chain of member accesses.
3. A shader touching only one nested member reproduced it alone.

So it's a driver bug. Crucible refuses nested structs in `[GpuShared]`
types until NVIDIA fixes it, instead of letting engine shaders hit it
silently. This is why every feature runs on every backend before it's
called done.
