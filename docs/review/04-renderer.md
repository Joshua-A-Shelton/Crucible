# Review 4: the renderer

How the world becomes an image: the frame's render steps on Slag's render
graph, meshes and materials, lights and shadows, the sky and transparency,
and the finishing (tonemapping, bloom, grading, outlines). The 2D path's
rendering (sprites, the pixel camera, 2D lights) is page 8's. Decisions:
ADRs 0004, 0015, 0025, 0047, 0048, 0049, 0050, 0051, 0052, 0053, 0054,
0055, 0058.

## What it does

Each frame, for each surface (the game's window, an editor view, a
preview), the renderer runs an ordered list of render steps that add
passes to Slag's render graph, which works out the barriers and runs them
on the GPU. It's a clustered forward renderer with MSAA, built for a
clear image in a single frame: nothing smears across frames by default.

```
 RenderSchedule (named steps, editable)             Slag render graph       GPU
 ─────────────────────────────────────              ─────────────────       ───
 depth prepass → light clustering → shadows    ──►  passes recorded    ──►  D3D12 /
 → opaque (MSAA) → sky → transparent → resolve      in parallel on          Vulkan
 → post (tonemap, bloom, grading, outlines)         the job system
 → 2D and UI → present
         ▲  steps share textures through the Blackboard; C# steps add passes
            through the batched command API
```

## How it works

### Steps and the render graph (ADR 0015, 0048, 0058)

- **A `RenderSchedule` of named `RenderStep`s,** edited by name like frame
  phases: add, insert before or after, replace, remove. Each step adds one
  or more passes to Slag's render graph each frame.
- **Steps share resources through a `Blackboard`:** a step publishes a
  texture under a key (`SceneColor`, `SceneDepth`), later steps read it;
  reading one nobody published is reported.
- **Passes are recorded in parallel** on the job system.
- **C# steps** write compact commands into native memory during
  `AddPasses`; native code replays them on the job threads when the graph
  records the pass. C# never runs during recording, and the boundary is
  crossed once per pass, not per draw.

### Meshes, assets, and cameras (ADR 0049, 0050, 0051)

- **`MeshInstance`** (a model's asset ID and a mesh index) draws a mesh.
  The renderer loads the model on first use and frees it two seconds after
  the last instance stops using it.
- **World matrices live on the GPU** and are updated only from chunks
  whose transforms changed (change detection, page 1). CPU culling on jobs
  writes the visible instances.
- **GPU assets** (meshes, textures) have generational handles: a stale one
  is detected.
- **Reverse-Z with an infinite far plane**, for even depth precision. The
  camera with the highest priority renders the game.
- **HDR with MSAA:** 16-bit float color, 4x by default.

### Shaders and materials (ADR 0025, 0047, 0052)

- **Shaders are Slang,** compiled to DXIL and SPIR-V at import or build
  time; shipped games never compile shaders.
- **A material shader is a surface function** (albedo, normal, roughness,
  emissive, …); the engine provides vertex processing and the lighting
  loop. **Shading models** are Slang interfaces the loop is specialised
  for at compile time: Crucible ships toon (soft bands) and PBR, and games
  add their own.
- **Materials are data:** each material's parameters are a struct in one
  big GPU buffer, indexed per draw (bindless). Their C# types are
  generated from the shader's reflection.
- **Variants only where unavoidable** (skinned or not, shading model, pass);
  everything else is material data.

### Lights and shadows (ADR 0053, 0054)

- **Directional, point, spot, and ambient light components,** placed by
  their entities. Point and spot lights fall off to zero at their range.
- **3D light clusters** (16 × 9 screen tiles × 24 depth slices), built on
  the GPU, list the lights per cell.
- **Shadows:** four cascades for the sun, snapped to texels so they don't
  shimmer, a 5×5 tent filter (soft, no noise); spot and point lights in an
  atlas whose tiles are kept until something in them changes. Lights cast
  shadows only when asked (`castsShadows`).

### The rest of the frame (ADR 0004, 0055)

- **A gradient sky** with a sun disk; it doesn't light the scene (the
  ambient light does).
- **Sorted transparency:** premultiplied alpha, back to front, one draw per
  instance.
- **Clarity first:** MSAA, specular antialiasing, alpha-to-coverage, and
  antialiased toon band edges are on; anything temporal (TAA, motion blur,
  auto exposure) is opt-in.
- **Finishing on the camera** (`PostProcess`): tonemapping (PBR Neutral by
  default, AgX, ACES), bloom, parameter grading, and hull outlines.

## Why it was built this way

- **Clustered forward + MSAA** because MSAA needs forward shading, and a
  single-frame-clear image matters for an anime-esque toon style where
  edges are the art.
- **Slag's render graph** handles barriers and resource lifetimes, and its
  limitations are fixed in Slag rather than worked around.
- **Surface functions and shading models** let games write materials and
  their own lighting looks without touching the lighting loop, at no
  run-time cost.
- **Everything compiled ahead** so a shipped game never stalls compiling a
  shader mid-play.

## What I'd question

1. **Nothing's measured on the target.** The first game's bar is 60 fps on
   Steam Deck at 1280×800 with MSAA (ADR 0010); shadows, bloom, and
   outlines are waiting on a Deck to measure (open items). It would be
   good to know the headroom before the first game commits to a look.
2. **One camera, one image.** The highest-priority camera renders the
   game; there's no rendering to a texture a game can use (a character
   portrait, a minimap, a mirror) or split screen. The editor's views and
   previews get their own surfaces, but a game can't.
3. **Limits are silent.** Past 64 lights in a cluster, the rest are
   dropped; when the shadow atlas is full, a light goes without shadows
   that frame; the 1,024 visible lights are kept in entity order, not by
   importance. None of it is reported.
4. **Materials have gaps a game will hit:** no per-instance overrides
   (tinting one enemy), samplers fixed to linear repeat (pixel-art
   textures on 3D models), shader hot reload not built, and material type
   names share one C# namespace.
5. **Outlines are only hulls.** The art direction leans on anime-style
   lines; edge-detected outlines (creases, intersections) and painted line
   maps were prototyped and set aside to come back to.
6. **Late latching is ready but not built** (ADR 0050): the camera data
   sits where it could be rewritten at the last moment. Worth revisiting
   with the D3D12 timing priority from page 2, since both are about how
   fresh input is when the frame is shown.
