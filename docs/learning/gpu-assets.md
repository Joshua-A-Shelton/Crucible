# GPU assets

A learning note for M5 step 3 (ADR 0049): getting imported models and
textures into GPU memory, and getting them out again safely.

## Two kinds of memory, and the bridge between them

A discrete GPU has its own memory (VRAM), fast for the GPU and, for most
of it, invisible to the CPU. The CPU writes into memory it *can* see
(system memory the GPU reads over PCIe, "upload" memory in Slag), and then
asks the GPU to copy from there into VRAM. That copy is the classic
**staging upload**: write into a staging buffer, record a copy, submit,
and free the staging buffer once the copy is done.

**Resizable BAR** changes that: with it, the CPU can map all of VRAM and
write into it directly. Slag calls this memory `GpuUpload` and reports
whether the device has it. Crucible writes a model's buffer straight into
it when available and uses staging otherwise. The data ends up in VRAM
either way, but the direct path saves a copy and a staging allocation.
Because both paths must keep working, the tests force the staging path
too.

Textures always go through staging: how a texture's texels are arranged in
VRAM is up to the driver (swizzled into tiles for cache efficiency), so
only the GPU's copy knows how to place them. The staging buffer has its own
rules: each row must start at a multiple of 256 bytes, so rows are copied
one by one from the file's tightly packed ones. For block-compressed
formats like BC7, a "row" is a row of 4x4 blocks.

## One buffer per model

A `.crmesh` file holds every mesh of one source: positions, normals, UVs,
and indices, each as its own stream. Crucible puts all of a model's streams
in **one GPU buffer**, each at an offset rounded up to 256 bytes, which is
safe for any way a shader or the vertex fetcher reads it. The tables a
draw needs on the CPU side (which meshes, LODs, index ranges, materials)
stay in system memory. Unloading a model frees exactly one buffer.

## Handles that know when they're stale

Code that draws keeps a `MeshHandle`, not a pointer. A handle is an index
into a table plus a **generation** number. When an entry is freed, its
generation goes up; if the slot is reused, the new handle has the new
number. An old handle kept somewhere (in a component, say) no longer
matches, so looking it up returns nothing instead of a different model or
freed memory. The ECS's `Entity` works the same way.

## Counting uses by asset ID

Ten crates in a level use one model. The first `acquireMesh(crateId)`
loads it; the next nine find it already loaded and only count. Each
`releaseMesh` takes one away, and the last frees it. The loader keeps the
asset-ID-to-handle map in C++. When it needs a file's bytes, it calls a
function C# registered, which asks the asset database (in the editor) and
hands the bytes back during the call, pinned so the garbage collector
can't move them meanwhile.

## Never free what the GPU is still using

The CPU runs ahead of the GPU: when a game releases a texture, frames that
sample it may still be queued. Freeing it right away would make those
frames read garbage, or crash. So every frame and every upload signals a
**timeline semaphore** with a rising number when the GPU finishes it. A
released buffer or texture is handed to Slag's `DeferredDestroyer` along
with the last number signalled, and destroyed only once the GPU has
reached it. Each frame, the destroyer frees whatever has become safe.

The texture's descriptor slot (its index in the bindless resource heap)
gets the same treatment from Slag's `DescriptorAllocator`: the slot isn't
handed to another texture until queued frames are done with it, so a shader
in flight never samples the wrong texture.
