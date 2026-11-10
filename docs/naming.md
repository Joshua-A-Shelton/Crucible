# Naming

Final names chosen by the project owner. Anything not listed here is still
a placeholder (CLAUDE.md §7).

## Code organization (2026-10-26)

| Name | What it is |
|---|---|
| `crucible` | C++ namespace |
| `crucible_core` (`crucible::core`) | The C++ core library target |
| `Crucible` | C# root namespace |
| `Crucible.Engine` | The engine's C# assembly |
| `src/native/`, `src/managed/` | C++ and C# source folders, mirrored under `tests/` |
| `CRUCIBLE_` | Prefix for macros and CMake options |
| `crucible_core_static` | The static form of the core library, for AOT builds |
| `crucible_launcher` | The launcher executable target (shipped games are named after the game) |
| `Crucible.Hosting` | Namespace of the engine's managed entry point |
| `Crucible.BindGen` | The binding generator tool |
| `Crucible.TestGame` | Small test game assembly for hot-reload tests |

## Files and tools (2026-10-28)

Settled in a review of M4's and M5's placeholders (ADR 0047's amendment).

| Name | What it is |
|---|---|
| `.crproject` | The project file |
| `.crmeta` | A source file's sidecar: GUID, importer, settings (`hero.png.crmeta`) |
| `.crscene` | A scene (JSON) |
| `.crprefab` | A prefab (JSON) |
| `.crmesh` | An imported model (binary, kind MESH) |
| `.crtexture` | An imported texture (binary, kind TXTR) |
| `.crshader` | A compiled shader (binary, kind SHDR) |
| `shaderfax` | The build's shader compiler (C++ executable and CMake target) |
| `crucible_add_shader` | The CMake function that runs it |
| `[GpuShared]` | Slang attribute: a struct C++ and C# mirror |
| `CRBN`, `TXTR`, `MESH`, `SHDR`, and section tags | Binary-file magic and tags (ADRs 0039, 0042, 0043, 0047) |

## Rendering (2026-10-28)

| Name | What it is |
|---|---|
| `RenderSchedule` | The frame's ordered render steps (ADR 0048) |
| `RenderStep` | One entry: a named step adding 1..n graph passes |
| `RenderStepContext` | What a step sees while adding its passes |
| `Blackboard`, `BlackboardKey` | Resources steps share by name |
| `Backbuffer` | The built-in blackboard key for the surface's image |
| `renderFrame` / `Render.Api.RenderFrame` | Renders and presents one frame |
| `RenderSystem` | The C# system calling it, in RenderSubmit |
| `Clear`, `Triangle` | The first render steps (temporary, until M5 step 5) |

## GPU assets (2026-10-29)

| Name | What it is |
|---|---|
| `MeshHandle`, `TextureHandle` | A loaded model or texture (ADR 0049); Slag's render graph handles became `RenderGraphTexture` and `RenderGraphBuffer` to keep them apart (Slag 1.1.0) |
| `acquireMesh` / `releaseMesh`, `acquireTexture` / `releaseTexture` | Counted use by asset ID |
| `loadMesh` / `unloadMesh`, `loadTexture` / `unloadTexture` | From bytes, outside the asset system |
| `meshData(MeshHandle)` → `MeshData` | What render steps read to draw a model |
| `textureDescriptor`, `samplerDescriptor` | Resource- and sampler-heap slots for shaders |
| `Sampler`: `LinearRepeat`, `LinearClamp`, `PointClamp` | The fixed samplers |
| `crucible/RenderAssets.h`, `setAssetReader` | The header, and C#'s byte reader |
| `RenderAssets` | The C# class |
| `crucible/MeshFile.h`, `crucible/TextureFile.h` | The imported files' layouts, in the core |
| `AssetId` (C++) | The same 16 bytes as C#'s |

## Transforms and cameras (2026-10-29)

| Name | What it is |
|---|---|
| `crucible/Transform.h`: `LocalTransform`, `WorldTransform` (`matrix`) | Transforms, in `crucible::scenes` / `Crucible.Scenes` (ADR 0050) |
| `updateWorldTransforms`, C# `World.UpdateWorldTransforms` | Propagation on demand |
| `computeWorldTransform`, C# `World.ComputeWorldTransform` | One entity's world matrix now, not stored |
| `SystemContext::lastRunVersion`, C# `FrameContext.LastRunVersion` | The version of a system's previous run (ADR 0034 amendment) |
| `changedSince(world, entity, ...)`, C# `World.ChangedSince` | Per-entity form of `changedSince` |
| `crucible.TransformPropagation` | The propagation system |
| `crucible/Camera.h`: `Camera`, `CameraProjection` (`Perspective`, `Orthographic`) | The camera component, in `crucible::render` / `Crucible.Render` |
| `Camera` fields: `projection`, `fieldOfViewY`, `orthographicHeight`, `nearPlane`, `farPlane`, `priority` | Full vertical angle, full height |
| `ViewConstants`: `view`, `projection`, `viewProjection`, `inverseView`, `inverseProjection`, `inverseViewProjection`, `cameraPosition`, `time`, `viewportSize` | The per-frame view for shaders |
| Blackboard key `View`, `viewKey()` | Where steps find the frame's `ViewConstants` |
| `Matrix4x4::perspectiveReverseZInfinite`, `orthographicReverseZ` | The renderer's projections |

## Drawing meshes (2026-10-29)

| Name | What it is |
|---|---|
| `crucible/MeshInstance.h`: `MeshInstance` (`model`, `mesh`), `LoadedMesh` (`handle`) | The saved instance, and the renderer's resolved handle (ADR 0051) |
| `crucible/DrawList.h`: `DrawList` (`batches`, `instances`, `transforms`), `DrawBatch` (`model`, `mesh`, `lod`, `firstInstance`, `instanceCount`, `baseColorTextures`) | What render steps draw |
| `RenderStepContext`: `world`, `view`, `draws` | What steps get for drawing |
| `DepthPrepass`, `Opaque`, `Resolve` | The default render steps |
| Blackboard keys `SceneDepth`, `SceneColor`; `sceneDepthKey()`, `sceneColorKey()` | The MSAA targets |
| `MeshData::descriptor` | The model buffer's raw-buffer slot |
| `setMsaaSamples`, `msaaSamples`, launcher `--msaa` | The MSAA sample count |
| `renderFrame(..., lastRunVersion, ...)` | The render system's last-run version |
| `shaders/crucible/mesh.slang` (`depthVertex`, `opaqueVertex`, `opaquePixel`), `resolve.slang` (`fullscreenVertex`, `resolvePixel`) | The default steps' shaders |
| `InstanceTransform` (`x`, `y`, `z`), `DrawConstants` | GPU structs: world matrix columns, per-draw push constants |

## Materials (2026-11-02)

| Name | What it is |
|---|---|
| `.crmaterial`, `.crmat` | Authored material (JSON) and its imported binary (ADR 0052; M5 step 6b) |
| `Shader` importer | Material shaders, `.slang` to `.crshader` (6b) |
| Module `crucible.material`: `IMaterial` (`surface`, `shading`, `Shading`), `IShadingModel` (`Data`, `Accumulator`, `begin`, `light`, `ambient`, `finish`) | The interfaces |
| `SurfaceInput` (`toWorld`), `SurfaceData` (`defaults`), `LightSample`, `AmbientSample`, `TextureSlot` | What materials and models see |
| `ToonShading`, `ToonData` (`bands`, `softness`, `shadowTint`, `rimWidth`, `rimColor`), `PbrShading`, `PbrData` | The shading models |
| `sampleTexture`, `linearRepeatSampler`, `linearClampSampler`, `pointClampSampler` | Helpers |
| `MaterialVertex`, `materialVertexMain`, `surfaceInput`, `loadMaterial`, `shadeMaterial`, `clipMaterial` | Engine-side library functions |
| `shaders/crucible/default.slang`: `DefaultMaterial`, `DefaultToon`, `DefaultPbr`; entries `materialVertex`, `toonPixel`, `pbrPixel`, `maskPixel` | The built-in materials |
| `crucible/MaterialFile.h`: `MaterialInfo`, `MaterialTexture`, `AlphaMode`, `DefaultMaterialType`; section `MTLS` | Material records |
| `MeshImportSettings.Shading`, `ShadingModel` (`Toon`, `Pbr`) | Generated materials' shading |
| `MaterialHandle`, `DrawBatch::materials` | Runtime |
| C# per-shader structs named after the Slang type (`Brick`), in `Crucible.Materials`; `RenderAssets.SetMaterial`; C++ `acquireMaterial` / `releaseMaterial`, `setMaterialParameters` | Runtime changes from C# (6b) |

## Game materials (2026-11-02, M5 step 6b)

| Name | What it is |
|---|---|
| `TextureSlot` (Slang `{ index; sample(uv, fallback) }`, C# `{ Index }`) | A texture parameter |
| Field initializers | A material's defaults (no attribute) |
| `IMaterialData` (`ShaderId`, `TypeName`); generated `Defaults`, `<Type><Field>Array` | The generated C# structs, in `Code/Generated/Materials/<Type>.g.cs` |
| `.crmaterial` keys: `$guid`, `Shader`, `Type`, `AlphaMode`, `DoubleSided`, `AlphaCutoff`, `Values`, `Textures` | The authored material |
| `MeshImportSettings.Materials` | Slots remapped to authored materials |
| `MaterialTypeInfo`, `MaterialParameter`, `MaterialParameterKind` (`Float`, `Int`, `Uint`, `Texture`); `shaderMaterialTypesTag` (MTYP), `meshRemappedMaterialsTag` (MRMP), `materialFileKind` (MATR) | Records |
| Library `materialForward<M>`, `materialMask<M>`; entries `<Type>_forward`, `<Type>_mask`, `materialVertex`; `DrawConstants.alphaCutoff` | Specialized entry points |
| `MaterialImporter` (`"Material"`), `ShaderImportSettings`, `MaterialImportSettings`, `ShaderImporter.LibraryFolder`; `ImportContext.ReadImported`, `WriteGeneratedSource`, `AssetsFolder` | Importers |
| `shaderfax --materials`, `crucible_add_shader(... MATERIALS)` | Building material shaders |
| `acquireMaterial`, `releaseMaterial`, `setMaterialParameters`; internal `materialShader`, `materialTypeName`, `createMaterialFromRecords`, `readAsset` | Runtime |
| `compileMaterialShader`, `compiledSectionCount` / `compiledSection` / `copyCompiledSection`, `compiledDependencyCount` / `compiledDependency`, `ShaderSectionInfo` | The importers' compiler API |

## Lighting (2026-11-02)

| Name | What it is |
|---|---|
| `crucible/Lights.h`: `DirectionalLight` (`color`, `intensity`), `PointLight` (+ `range`), `SpotLight` (+ `innerConeAngle`, `outerConeAngle`, full angles), `AmbientLight` (`skyColor`, `groundColor`, `intensity`) | Light components, C# `Crucible.Render.*` (ADR 0053) |
| `maxDirectionalLights` (4), `maxVisibleLights` (1024), `maxLightsPerCluster` (64) | Limits |
| Step `LightClustering`; blackboard keys `Lights`, `LightClusters`; `lightsKey()`, `lightClustersKey()` | The render step |
| `[GpuShared] LightData` (`position`, `range`, `direction`, `spotScale`, `color`, `spotOffset`) | A light as shaders read it |
| `ViewConstants`: `clusterTileSize`, `ambientSky`, `clusterSliceScale`, `ambientGround`, `clusterSliceBias`, `directionalLightCount`, `lightCount`, `lights`, `clusterRanges`, `clusterLights` | The frame's lighting |
| `shaders/crucible/clusters.slang` (`buildClusters`); library `clusterOf`, `pointSample` | Shaders |

## Shadows (2026-11-02)

| Name | What it is |
|---|---|
| `DirectionalLight` `castsShadows`, `shadowDistance`; `PointLight` / `SpotLight` `castsShadows`; `MeshInstance::noShadows` | What casts (ADR 0054); zeroed components cast from meshes, not lights |
| `shadowCascadeCount` (4), `maxShadowedLights` (16) | Limits, in `crucible/Lights.h` |
| `setShadowMapSize` / `shadowMapSize`, `setShadowAtlasSize` / `shadowAtlasSize`; launcher `--shadow-map-size`, `--shadow-atlas-size` | Quality settings |
| Step `Shadows`; graph passes `ShadowCascade`, `ShadowAtlas`; blackboard keys `ShadowCascades`, `ShadowAtlas`; `shadowCascadesKey()`, `shadowAtlasKey()` | The render step |
| `[GpuShared] ShadowView` (`viewProjection`, `rect`, `texelSize`, `normalOffset`, `normalOffsetPerMeter`, `layer`); `LightData::shadowView`, `noShadowView` | Shadow views as shaders read them |
| `ViewConstants`: `shadowViews`, `cascadeMap`, `shadowAtlas`, `cascadeSplits` | The frame's shadows |
| `LightSample::shadow` | What shading models receive |
| `shaders/crucible/shadows.slang`: `sunShadow`, `localShadow`, `shadowComparisonSampler`; mesh.slang `clearTileVertex` | Shaders |

## Sky and transparency (2026-11-02, M5 step 9a)

| Name | What it is |
|---|---|
| `crucible/Sky.h`: `Sky` (`zenithColor`, `horizonColor`, `groundColor`, `intensity`, `sunSize`, `sunIntensity`); registered by `registerLights` | The sky component, C# `Crucible.Render.Sky` (ADR 0055) |
| Steps `Sky`, `Transparent` | Render steps |
| `shaders/crucible/sky.slang` (`skyVertex`, `skyPixel`); `ViewConstants`: `skyZenith`, `skyHorizon`, `skyGround`, `skySunCosine`, `skySunIntensity`, `sky` | The sky's shader and constants |
| Library `materialTransparent<M>`, `materialCoverage<M>`; entries `<Type>_transparent`, `<Type>_coverage`; default.slang `toonTransparentPixel`, `pbrTransparentPixel`, `coveragePixel`; `MaterialPipelines::transparent` | Transparency and alpha-to-coverage |
| `specularAntialiasing(normal, roughness)` | Specular AA helper |

## Finishing and outlines (2026-11-03, M5 step 9b)

| Name | What it is |
|---|---|
| `crucible/PostProcess.h`: `PostProcess` (`exposure`, `tonemapper`, `bloomStrength`, `bloomThreshold`, `contrast`, `saturation`, `temperature`, `tint`, `lift`, `gamma`, `gain`), `Tonemapper` (`PbrNeutral`, `AgX`, `Aces`); registered by `registerCamera` | The camera's finishing, C# `Crucible.Render.PostProcess` (ADR 0055) |
| Steps `Bloom`, `Tonemap` (was `Resolve`), `Outlines`; blackboard key `Bloom` (`bloomKey()`); passes `BloomPrefilter`, `BloomDownsample`, `BloomUpsample` | Render steps |
| `ViewConstants`: `whiteBalance`, `lift`, `contrast`, `gamma`, `saturation`, `gain`, `tonemapper`, `exposureScale`, `bloomStrength`, `bloomThreshold`, `bloomLevels` | The frame's finishing |
| `bloom.slang` (`bloomVertex`, `bloomPrefilterPixel`, `bloomDownsamplePixel`, `bloomUpsamplePixel`); `tonemap.slang` (`fullscreenVertex`, `tonemapPixel`) | Shaders |
| `MaterialInfo::outlineWidth`, `outlineColor`, `outlineTint`; `.crmaterial` and `MeshImportSettings` `Outline { Width, Color, Tint }`, C# `OutlineSettings` | A material's outline |
| `MeshAttribute::OutlineNormals`, stream `ONRM` | Outline normals |
| Library `outlineVertexMain`, `materialOutline<M>`, `worldNormal`, `outlineFadeDistance`; entries `outlineVertex`, `<Type>_outline`, default.slang `toonOutlinePixel`, `pbrOutlinePixel`; `MaterialPipelines::outline`; `DrawConstants` `outlineNormals`, `outlineWidth`, `outlineColor`, `outlineTint` | Hull rendering |

## The M5 demonstration (2026-11-03, M5 step 10)

| Name | What it is |
|---|---|
| `samples/Diorama` (`Crucible.Samples.Diorama`), `make_scene.py`, `run_diorama.cmake`; CTest `samples.diorama` | The demonstration (ADR 0056) |
| `--game-arg` `project`, `frames`, `capture`, `compare`, `measure` | Its options |

## Fonts and text (2026-11-03, M6 step 1)

| Name | What it is |
|---|---|
| `FontImporter` (`"Font"`), `.crfont`, kind `FONT` | The font asset (ADR 0057) |
| `crucible::text` (C# `Crucible.Text`): `FontHandle`, `acquireFont` / `releaseFont`, `loadFont` / `unloadFont`; C# `Fonts.Acquire` / `Fonts.Release` | Fonts |
| `TextLayout`, `TextLayout.Shape(font, style, text, wrapWidth)`; `TextStyle` (`Size`, `Scale`, `LineHeight`, `Rendering`) | A shaped, broken text |
| `GlyphRendering` (`Exact`, `DistanceField`) | Which glyph path a style draws with |
| `GlyphCache` (`GlyphCache.cpp`); `Font.cpp`, `Shaping.cpp` | Native text |
| `FontImportSettings`; section `DATA` | The importer's settings and the font file's one section |
| `shapeText`, `destroyTextLayout`, `TextLayoutHandle` | Native layouts |
| `textMetrics` → `TextMetrics` (`width`, `height`, `lineCount`); `textLine` → `TextLine` (`start`, `end`, `width`, `top`, `baseline`, `height`); `hitTestText`; `textCaret` → `TextCaret` (`x`, `top`, `height`, `line`) | Layout queries |
| `textGlyphs` → `PositionedGlyph` (`glyph`, `x`, `y`, `offset`) | Glyphs for drawing (C++ only) |
| `text::shutdown`, `MemoryTag::Text` | Lifetime and memory accounting |
| C# `Fonts.Load` / `Fonts.Unload`; `TextLayout.Size`, `LineCount`, `Line(index)`, `HitTest(point)`, `Caret(index)`, `Handle` | C# API |

## C# render steps (2026-11-03, M6 step 2)

| Name | What it is |
|---|---|
| `IRenderStep` (`Prepare`, `AddPasses`, `Release`) | A C# render step (ADR 0058) |
| C# `RenderSchedule` (`Add`, `InsertBefore`, `InsertAfter`, `Replace`, `Remove`, `StepNames`) | Placing C# steps among the C++ ones |
| `RenderStepContext` (`Width`, `Height`, `Frame`, `FrameDataDescriptor`, `MsaaSamples`, `Texture`, `Buffer`, `Publish`, `CreateTexture`, `CreateBuffer`, `Upload<T>`, `AddPass`) | What a step sees in `AddPasses` |
| `FrameData<T>` (`Data`, `Offset`, `Size`); the frame data | Per-frame data for shaders |
| `RenderPass` (`Use`, `RenderTarget`, `DepthTarget`, `Commands`); `RenderCommands` (`SetPipeline`, `PushConstants`, `SetConstantBuffer`, `SetViewport`, `SetScissor`, `Draw`, `Dispatch`, `BeginLabel`, `EndLabel`) | A pass and its commands |
| `BlackboardKey.Register`, `Name`, `Backbuffer`, `View`, `SceneDepth`, `SceneColor`, `Bloom` | Blackboard keys from C# |
| `crucible/RenderCommands.h`: `PipelineHandle`, `GraphTexture`, `GraphBuffer`, `RenderFormat`, `BlendMode` (`Opaque`, `Alpha`, `Premultiplied`, `Additive`), `CullMode`, `CompareOp`, `Topology`, `GraphicsPipelineState`, `PassQueue`, `ResourceUsage`, `TextureUsage`, `TransientTextureDesc`, `LoadAction`, `StoreAction` | Exposed types |
| `createGraphicsPipeline`, `createComputePipeline`, `destroyPipeline`, `pipelineCount` | Pipelines from shader assets |
| `StepFrame`, `StepFrameInfo`, `StepMemory`, `StepPlacement`, `StepCall`; `setStepTrampoline`, `insertManagedStep`, `removeStep`, `stepCount`, `stepName`, `stepFrameInfo`, `stepTexture`, `stepBuffer`, `publishTexture`, `publishBuffer`, `createStepTexture`, `createStepBuffer`, `addStepPass`, `useTexture`, `useBuffer`, `passRenderTarget`, `passDepthTarget`, `passCommands`, `uploadFrameData` | The engine's internal step functions |
| `RenderCommandType`, `RenderCommandHeader`; `SetPipelineCommand`, `PushConstantsCommand`, `SetConstantBufferCommand`, `SetViewportCommand`, `SetScissorCommand`, `DrawCommand`, `DispatchCommand`, `BeginLabelCommand` | Command records |
| `Crucible.RenderStepGame`; CTest `launcher.jit.render-steps` | The C# GPU test |

## UI drawing (2026-11-03, M6 step 3)

| Name | What it is |
|---|---|
| `Crucible.UI.Canvas` (`Scale`, `Count`, `Clear`, `FillRect`, `StrokeRect`, `DrawImage`, `DrawNineSlice`, `DrawText`, `DrawLine`, `DrawPolyline`, `DrawShadow`, `PushClip` / `PopClip`, `PushTransform` / `PopTransform`) | The 2D drawing API (ADR 0059), and the step that draws it |
| Step `UI`; pass `GlyphUpload` | The UI's render step, and the glyph pages' copy |
| `text::writeTextQuads` | A layout's glyphs as UI instances |
| `UiInstance` (`rect`, `transform`, `translation`, `kind`, `texture`, `radii`, `color`, `color2`, `uv`, `extra`, `extra2`, `clip`), `uiRect` … `uiShadow`, `uiGradientNone` / `Linear` / `Radial`; `shaders/crucible/ui.slang` | The UI shader's instance |
| `Gradient` (`Linear`, `Radial`; `Kind`, `Start`, `End`, `Radius`, `StartColor`, `EndColor`), `GradientKind` | Gradient fills |
| `GlyphUpload.cpp`: `glyphPageDescriptor`, `glyphPageDescriptors`, `beginGlyphUpload`, `finishGlyphUpload`, `stopGlyphUpload`; `destroyAfterFrame` | The glyph pages on the GPU |
| `createEngineGraphicsPipeline`, `textGlyphCount` | Engine pipelines; a layout's glyph count |
| `CanvasChecks`, CTest `launcher.jit.canvas`, `canvas.png`; `docs/learning/drawing-2d.md` | The canvas test and its note |

## Elements and layout (2026-11-03, M6 step 4)

| Name | What it is |
|---|---|
| `UIElement` (`Measure`, `Arrange`, `MeasureCore`, `ArrangeCore`, `OnDraw`) | An element of the UI tree (ADR 0060) |
| `UIRoot` | A surface's UI, and the `UI` step that draws it |
| `StackPanel`, `Grid`, `DockPanel`, `AnchorPanel` | The panels |
| `UIElement` members: `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight`, `Margin`, `Padding`, `HorizontalAlignment`, `VerticalAlignment`, `Visibility`, `IsHitTestVisible`, `ClipToBounds`, `RenderTransform`, `TransformOrigin`, `Background`, `CornerRadii`, `DesiredSize`, `Bounds`, `ActualSize`, `InvalidateMeasure`, `InvalidateArrange`, `InvalidateVisual`; `Panel` (`Add`, `Insert`, `Remove`, `Clear`) | Element settings and children |
| `Thickness`, `HorizontalAlignment` / `VerticalAlignment` (`Stretch`, `Start`, `Center`, `End`), `Visibility` (`Visible`, `Hidden`, `Collapsed`), `Orientation` | Layout types |
| `GridLength` (`Auto`, `Pixels`, `Star`), `GridUnit`; `Grid.Columns`, `Rows`, `ColumnSpacing`, `RowSpacing`, `DefinitionsChanged`, `SetRow`, `SetColumn`, `SetRowSpan`, `SetColumnSpan`; `StackPanel.Spacing` | Stacks and grids |
| `Dock`, `DockPanel.SetDock`, `LastChildFill`; `Anchors` (`Fill`, `TopLeft` … `BottomRight`, `Center`, `At`, `Inset`), `AnchorPanel.SetAnchors` | Docking and anchors |
| `UIRoot.Content`, `Scale`, `Size`, `UpdateLayout`, `HitTest` | The root |
| `LayoutChecks`, CTest `launcher.jit.layout`, `layout.png`; `docs/learning/ui-layout.md` | The layout test and its note |

## Styling and animation (2026-11-03, M6 step 5)

| Name | What it is |
|---|---|
| `StyledProperty<T>` | A stylable, animatable property (ADR 0061) |
| `Theme`, `Style`, `Selector` | Styles for a UI, one style, and what it matches |
| `.crstyle` | The authored theme file (JSON) |
| `UIState` (`Hover`, `Pressed`, `Focus`, `Disabled`); `UIElement.Animate` | States selectors match; tweens |
| `Invalidates` (`None`, `Measure`, `Visual`), `StyledProperty.Invalidates` | What a property's change invalidates (renamed from `PropertyAffects` / `Affects`) |
| `StyledProperty` members: `Register`, `Find`, `Default`, `Interpolate`, `Owner`; `StyleValues` (`ReadFloat`, `ReadVector2`, `ReadVector4`, `ReadThickness`, `ReadColor`, `ReadAssetId`, `ReadEnum`, `Lerp`, `LerpColor`) | Registering properties; JSON readers and blends |
| `Classes` (`Add`, `Remove`, `Set`, `Contains`; renamed from `ClassList`), `Transition`, `Easing` (`Linear`, `Ease`, `EaseIn`, `EaseOut`, `EaseInOut`, `Evaluate`) | An element's classes; transitions and easing |
| `UIElement.GetValue`, `SetValue`, `ClearValue`, `IsSet`, `Classes`, `State`; `BorderWidth`, `BorderColor`, `Opacity`, `Foreground`, `Font`, `FontSize`, `RenderScale`, `RenderRotation`, `RenderOffset`; `…Property` fields | Element styling members |
| `Theme.Add`, `Clear`, `ReplaceWith`, `Parse`, `Load`, `Version`; `Style.Set`, `Transition`, `Values`, `Transitions`; `Selector.Parse`, `Matches`, `Specificity`; `UIRoot.Theme`, `Now`, `IsAnimating`, `UpdateStyles`; `Canvas.PushOpacity` / `PopOpacity` | Themes, styles, and the root |
| `.crsty` (kind `STYL`, section `JSON`), `StyleImporter` (`"Style"`), `StyleImportSettings`; JSON keys `Styles`, `Selector`, `Set`, `Transitions`, `Duration`, `Easing`; states `:hover`, `:pressed`, `:focus`, `:disabled` | The theme files |
| `StyleChecks`, CTest `launcher.jit.styles`, `styles.png`; `docs/learning/styling-and-animation.md` | The styles test and its note |

## UI input (2026-11-03, M6 step 6)

| Name | What it is |
|---|---|
| `PointerEvent`, `KeyEvent`, `NavigationEvent` | UI event arguments (ADR 0062) |
| `Activate` / `Cancel` (`OnActivate` / `Activated`, `OnCancel` / `Canceled`) | Device-neutral actions |
| `IsFocusScope`, `NavigateUp` / `NavigateDown` / `NavigateLeft` / `NavigateRight`, `UIRoot.FocusedElement`, `UIElement.Focus()` | Focus |
| `UIInputMap`, `UIInputSystem` | Gamepad mapping; the engine system feeding roots |
| Platform events `MouseWheel`, `GamepadAdded` / `GamepadRemoved`, `GamepadButtonDown` / `GamepadButtonUp`, `GamepadAxis`, `DisplayScaleChanged`; `KeyModifiers` (`Shift`, `Ctrl`, `Alt`, `Gui`), `GamepadButton` (`South` … `DpadRight`), `GamepadAxis`, `InputEvent::modifiers`, `windowDisplayScale` | Platform input |
| `UIEvent` (`Handled`, `Source`), `PointerButton`, `PointerEvent` (`Position`, `Button`, `Wheel`, `Modifiers`), `KeyEvent` (`Scancode`, `Modifiers`, `IsRepeat`), `NavigationDirection` (`Up`, `Down`, `Left`, `Right`, `Next`, `Previous`), `UIAction`; `UIInputMap` (`Buttons`, `StickThreshold`, `RepeatDelay`, `RepeatInterval`) | Event types and the gamepad map |
| `UIElement` input: `IsFocusable`, `TabIndex`, `IsFocused`, `IsEnabled`; `OnPointerEnter` / `Leave` / `Move` / `Down` / `Up` / `Wheel`, `OnKeyDown` / `Up`, `OnNavigate`, `OnGotFocus` / `OnLostFocus`; events `PointerEnter` … `PointerWheel`, `KeyDown`, `KeyUp`, `Navigate`, `GotFocus`, `LostFocus` | Element handlers |
| `UIRoot.Window`, `Attached`, `InputMap`, `HandledPointer`, `HandledKeyboard`, `ProcessInput`, `ClearFocus`, `MoveOrNavigate`, `MoveFocus`; `docs/learning/ui-input.md` | Root input; the note |

## Controls (2026-11-03, M6 step 7)

| Name | What it is |
|---|---|
| `Label`, `Button`, `Toggle`, `Slider`, `Image`, `ScrollView`, `ListBox`, `Spinner` (renamed from `Selector`, which the style's `Selector` uses), `ComboBox`, `Popup` | The controls (ADR 0063) |
| `SliderTrack`, `SliderThumb`, `ToggleBox`, `ToggleMark`, `ScrollBar`, `ScrollThumb`, `ListItem`, `SpinnerArrow`, `ComboBoxArrow` | Their parts |
| `ButtonBase` (`Content`, `Text`, `Clicked`, `OnClicked`); `Label` (`Text`, `Wrap`, `TextRendering`, `Layout`); `Toggle` (`IsOn`, `Changed`, `Box`, `Mark`; class `on`) | Text and buttons |
| `Slider` (`Minimum`, `Maximum`, `Value`, `Step`, `ValueChanged`, `Track`, `Thumb`); `Image` (`Texture`, `Stretch`, `SliceInsets`, `SourceSlice`, `Tint`, `NaturalSize`, `Fit`); `Stretch` (`None`, `Fill`, `Uniform`, `UniformToFill`) | Sliders and images |
| `ScrollView` (`Content`, `ScrollsHorizontally`, `ScrollsVertically`, `WheelStep`, `Offset`, `Extent`, `Viewport`, `ScrollTo`, `ScrollBy`, `ScrollIntoView`, `VerticalBar`, `HorizontalBar`; bar classes `vertical`, `horizontal`) | Scrolling |
| `UIRoot.DefaultFont`, `UIRoot.Popups`, `Theme.CreateDefault`, `UIElement.OnAttached` / `OnDetached`; native `textureSize`, `TextureSize` | Support |
| `Popup` (`Content`, `Anchor`, `Open`, `Close`, `Closed`, `IsOpen`); `ListBox` (`ItemCount`, `CreateItem`, `BindItem`, `EstimatedItemHeight`, `SelectedIndex`, `SelectionChanged`, `ItemActivated`, `ItemsChanged`, `ScrollIntoView`, `FocusItem`, `RealizedCount`); `ListItem` (`Index`, `Content`, `List`; class `selected`) | Popups and lists |
| `Spinner` (`Options`, `SelectedIndex`, `SelectedOption`, `Wraps`, `Step`, `SelectionChanged`, `ValueLabel`; arrow classes `previous`, `next`); `ComboBox` (`Options`, `SelectedIndex`, `SelectedOption`, `SelectionChanged`, `Popup`, `List`, `IsOpen`; list class `dropdown`) | Choices |
| `ControlsChecks`, CTest `launcher.jit.controls`, `controls.png`; `docs/learning/controls.md` | The controls test and its note |

## The M6 demonstration (2026-11-03, M6 step 8)

| Name | What it is |
|---|---|
| `samples/UiShowcase` (`Crucible.Samples.UiShowcase`), CTest `samples.ui` | The demonstration (ADR 0064) |
| `--game-arg` `project`, `input`, `capture`, `compare` | Its options |
| `input=none`; `Menu`, `ShowcaseSystem`, `ShowcaseSettings`, `run_ui.cmake`; captures `scale-<s>.png`, `options-<s>.png`; class `probe`, `Showcase.crstyle` | The sample's parts |
| `UIElement.Root` (public) | The root an element is in |
| `docs/learning/testing-a-ui.md` | The demonstration's note |

## Text editing (2026-11-04, M7 step 1)

All settled by the owner (2026-11-04): the fields and their events first,
the rest as drafted.

| Name | What it is |
|---|---|
| `TextField`, `NumberField` | The fields (ADR 0065) |
| `TextChanged`, `Committed` | Every change; an edit done |
| Platform event `TextInput`; `polledText`, `startTextInput` / `stopTextInput`, `clipboardText` / `setClipboardText` | Platform text input and the clipboard |
| `FrameContext.PolledText`; `UIRoot.ProcessInput(events, text)`, `TextTyped`, `DoubleClickTime`, `DoubleClickDistance` | The frame's typed text; the root |
| `TextInputEvent` (`Text`); `UIElement.OnTextInput` / `TextInput`, `AcceptsTextInput`, `OnFocusedTick`; `PointerEvent.ClickCount` | Elements and events |
| `Clipboard` (`Text`) | The UI's clipboard |
| `TextField` (`Text`, `Multiline`, `CaretColor`, `SelectionColor`, `CaretIndex`, `SelectionStart`, `SelectionLength`, `SelectedText`, `Select`, `SelectAll`, `Undo`, `Redo`, `Layout`, `Commit`, `IsEdited`, `OnTextChanged`) | The text field's members |
| `NumberField` (`Value`, `Minimum`, `Maximum`, `Step`, `ValueChanged`, `Format`) | The number field's members |
| `TextFieldChecks`, CTest `launcher.jit.textfields`, `textfields.png`; `docs/learning/text-editing.md` | The test and the note |

## The editor shell (2026-11-04, M7 step 2)

Settled by the owner, the alternatives where offered (`DockSpace`,
`DockGroup`, `EditorPanel`, `Shortcut`); the rest as drafted. Names added
while building (marked *) settled as drafted too.

| Name | What it is |
|---|---|
| `crucible_editor`, `Crucible.Editor`, `--project` | The editor launcher and assembly (ADR 0066) |
| `EditorSettings` (`RecentProjects`, `Layout`, `Load`, `Save`, `AddRecentProject`*, `DefaultPath`*, `MaxRecentProjects`*), `editor.json` | The user's settings |
| `DockSpace` (`Node`*, `Groups`*, `Panels`*, `LargestGroup`*, `Dock`*, `Take`*, `TargetAt`*, `ShowPreview`*), `DockSplit` (`Orientation`, `Sizes`, `Nodes`*, `Insert`*, `Add`*, `Remove`*, `SetSize`*), `DockGroup` (`Panels`*, `ActivePanel`*, `Add`*, `Activate`*, `Space`*), `DockTab` (`Panel`*), `Splitter` (`Thickness`*), `DockTarget` (`Left`, `Right`, `Top`, `Bottom`, `Center`) | Docking |
| `EditorPanel` (`Id`, `Title`, `Group`*), `PanelRegistry` (`Register`*, `Create`*, `Contains`*, `Panels`*), `HierarchyPanel`, `InspectorPanel`, `ViewportPanel`, `AssetsPanel`, `LogPanel` (`Add`*, `LineCount`*), `PlaceholderPanel`* | Panels |
| `EditorWindow` (`Window`*, `Surface`*, `IsMain`*, `Root`*, `DockSpace`*, `MenuBar`*, `StatusBar`*), `StatusBar` (`Text`*), the tab menu's "Dock to Main Window" | Windows |
| `MenuBar` (`Add`*, `Menus`*), `Menu` (`Title`*, `Items`*, `Add`*, `AddSeparator`*, `Close`*, `IsOpen`*), `MenuItem` (`IsChecked`, `Shortcut`, `Title`*, `Command`*, `Submenu`*, `Clicked`*; classes `bar`*, `open`*), `MenuSeparator`, `ContextMenu` (`Open`*), `Popup.Position`* | Menus |
| `Command` (`Id`, `Title`, `Execute`, `CanExecute`, `Shortcut`*, `IsChecked`*), `CommandRegistry` (`Add`*, `Remove`*, `Find`*, `HandleShortcut`*, `Commands`*), `Shortcut` (`Modifiers`*, `Keycode`*, `Matches`*); command IDs `File.OpenProject`*, `File.Quit`*, `Edit.Undo`*, `Edit.Redo`*, `Window.<panel>`*, `Window.ResetLayout`*, `Help.About`* | Commands |
| Platform `InputEvent::keycode`, C# `Keycode` (`A` … `Z`, `D0` … `D9`, `F1` … `F12`, `Return`*, `Escape`*, `Backspace`*, `Tab`*, `Space`*, `Delete`*, `Name`*), `KeyEvent.Keycode` | Keycodes |
| Events `DragMoved`, `DragDropped`, `DragCanceled`, `FileDialogResult`; `WindowDragSupport` (`None`, `DropOnly`, `Full`); `windowDragSupport`, `startWindowDrag`, `stopWindowDrag`, `windowPosition`, `setWindowPosition`, `pointerScreenPosition`, `showOpenFileDialog`, `preferencesPath` | The platform |
| `RenderSchedule` (an object: `Create`, `Default`, `Add`, `InsertBefore`, `InsertAfter`, `Replace`, `Remove`, `SetSurfaceSchedule`, `Dispose`), `setSurfaceSchedule`, `createRenderSchedule`*, `destroyRenderSchedule`*, the step `Clear`* | Render schedules |
| `EditorChecks`, CTest `launcher.jit.editor`, `editor.png`; `DockingTests`, `MenuTests`; `Crucible.Editor.Tests`*; `docs/learning/docking-and-windows.md` | Tests and the note |

## The editor's own window frames (2026-11-04, M7 step 2, ADR 0066 amended)

Settled as drafted: `TitleBar`, `WindowButton` (classes `minimize`,
`maximize`, `close`), `setWindowBordered`, `setWindowResizeBorder`,
`startWindowMove`, `minimizeWindow`, `maximizeWindow`, `restoreWindow`,
`windowMaximized`, `showWindowMenu`. Names added while building (marked *)
settled as drafted too:

| Name | What it is |
|---|---|
| `TitleBar` (`Title`*, `Leading`*, `MinimizeButton`*, `MaximizeButton`*, `CloseButton`*, `OnMoveStarted`*; label class `title`*) | The title bar's members |
| `WindowButton` (`Kind`*), `WindowButtonKind`* (`Minimize`*, `Maximize`*, `Close`*) | Window buttons |
| `EditorWindow.TitleBar`*, the editor's main title "<project> — Crucible" | The editor's frames |
| `TitleBarTests`* | Its tests |
| `setWindowParent`* | A window as another's child (floating windows) |

## The scene viewport (2026-11-04, M7 step 3)

Settled as drafted: `SceneView`, `EditorCamera`, `Selection` (`Selected`,
`Changed`), `TransformHandles`, `HandleMode` (`Move`, `Rotate`, `Scale`),
`HandleSpace` (`Local`, `World`), `setSurfaceCamera`, `clearSurfaceCamera`,
`surfaceTexture`, `resizeOffscreenSurface`, `requestPick`, `pickResult`,
`PickResult` (`frame`, `entity`, `hit`), `setSurfaceSelection`,
`addEditorSteps`, the steps `Grid`, `Pick`, `SelectionOutline`, the command
`File.OpenScene`, `--scene`, `Village.crscene`, `ViewportTests`, CTest
`launcher.jit.viewport`, `viewport.png`, `docs/learning/viewports-and-picking.md`.
Names added while building (marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `setSurfaceGrid`*, `maxOutlined`*, the blackboard key `EntityIds`*, `entityAtIndex`*, `RenderStepContext::surface`* | Native additions |
| `RenderSchedule.AddEditorSteps`*, `World.Handle`* (public) | Engine C# |
| `EditorCamera` (`Pivot`*, `Distance`*, `Yaw`*, `Pitch`*, `FieldOfViewY`*, `NearPlane`*, `FlySpeed`*, `Orbit`*, `Look`*, `Pan`*, `Dolly`*, `Fly`*, `Frame`*, `View`*, `Projection`*, `Project`*, `Ray`*, `WorldSize`*) | The camera's members |
| `SceneView` (`Camera`*, `Handles`*, `ShowsGrid`*, `Surface`*, `LastFrameMilliseconds`*), `ViewportPanel.View`*, `EditorViewportSystem`*, `ViewportChecks`*, `--game-arg check-viewport`*, the shell's `Database`*, `Prefabs`*, `OpenScene`*, `ScenePath`*; the Diorama's `save-scene`* | The editor's parts |

## Undo and the hierarchy (2026-11-04, M7 step 4a)

Settled as drafted: `TreeView`, `TreeRow`, `TreeExpander`, `Dialog`,
`Popup.IsModal`, `EntityName` (`Value`), `UndoHistory`, `UndoStep`,
`BeginGroup`, `EndGroup`, the commands `File.NewScene`, `File.SaveScene`,
`File.SaveSceneAs`, `Edit.Delete`, `Edit.Duplicate`, `Edit.Rename`,
`Hierarchy.CreateEmpty`, `showSaveFileDialog`, `TreeViewTests`,
`UndoTests`, `HierarchyTests`, CTest `launcher.jit.hierarchy`,
`hierarchy.png`, `docs/learning/undo-and-hierarchies.md`. Names added
while building (marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `TreeDrop`* (`Node`*, `Target`*, `Position`*), `TreeDropPosition`* (`Before`*, `After`*, `Inside`*) | A drag's drop |
| `TreeView` (`ChildrenOf`*, `BindRow`*, `Indent`*, `CanDrag`*, `RowCount`*, `RowNode`*, `RowDepth`*, `IndexOf`*, `ScrollView`*, `SelectedNode`*, `SelectionChanged`*, `Renamed`*, `Dropped`*, `ContextRequested`*, `IsExpanded`*, `SetExpanded`*, `Refresh`*, `ScrollIntoView`*, `FocusNode`*, `RowOf`*, `BeginRename`*) | The tree's members |
| `TreeRow` (`Tree`*, `Node`*, `Depth`*, `Text`*, `IsRenaming`*); classes `tree`* (its list rows), `open`* (an expanded expander), `tree-drop`* (the drop indicator), `dialog`*, `dialog-message`* | Rows and style classes |
| `Dialog` (`Message`*, `Chosen`*) | The dialog's members |
| `UndoHistory` (`Limit`*, `CanUndo`*, `CanRedo`*, `NextUndo`*, `NextRedo`*, `UndoCount`*, `RedoCount`*, `IsDirty`*, `Changed`*, `Push`*, `Do`*, `Undo`*, `Redo`*, `MarkSaved`*, `Clear`*), `UndoStep` (`Title`*, `Undo`*, `Redo`*) | Undo's members |
| `HierarchyPanel.Tree`*, `HierarchyChecks`*, `--game-arg check-hierarchy`*; the shell's `History`*, `SceneId`*, `IsDirty`*, `SceneName`*, `SceneChanged`*, `NewScene`*, `SaveScene`*, `SaveSceneAs`*, `WriteScene`*, `ConfirmDiscard`*, `RequestQuit`*, `Rename`*, `CreateEmpty`*, `Duplicate`*, `Delete`*, `Reparent`*, `LabelOf`* | The editor's parts |
| Internal: `SceneIds`*, `ComponentEdit`*, `SubtreeEdit`*, `ParentEdit`*, `Shell.Scene.cs`*, `SceneEdits.cs`* | Undo's insides |
| The menu and dialog text: "New Scene", "Save Scene", "Save Scene As…", "Rename", "Duplicate", "Delete", "Create Empty", the default name "Empty", "Save changes to X before quitting?" with "Save", "Don't Save", "Cancel"; the step titles "Move", "Rotate", "Scale", "Rename", "Create Empty", "Duplicate", "Delete", "Move in Hierarchy"; the title "Village.crscene* — Diorama — Crucible", "Untitled" | Text users see |

## The inspector (2026-11-05, M7 step 4b)

Settled as drafted: `SerializedFields`, `SerializedField` (`Name`,
`Kind`, `Element`, `EnumNames`, `Fields`, `Min`, `Max`, `Tooltip`,
`Header`, `IsColor`), `FieldKind` (the generator's kinds), `[Range]`,
`[Tooltip]`, `[Color]`, `[Header]`, `ColorField`, `ColorPicker`,
`InspectorPanel`, `ComponentSection`, `FieldEditor`, "Add Component",
"Remove Component", "Revert", "Revert All", the class `overridden`,
`InspectorTests`, `FieldTableTests`, `ColorPickerTests`, CTest
`launcher.jit.inspector`, `inspector.png`, ADR 0069,
`docs/learning/inspectors-and-metadata.md`. Names added while building
(marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `SerializedField.HasRange`*; the attributes' `Min`*, `Max`*, `Text`* | Field table extras |
| `ColorField` (`Color`*, `HasAlpha`*, `Picker`*, `ColorChanged`*), `ColorPicker` (`Color`*, `HasAlpha`*, `Hsv`*, `ColorChanged`*, `ToLinear`*, `ToSrgb`*, `Hex`*, `TryParseHex`*); classes `color-picker`*, `color-r`*, `color-g`*, `color-b`*, `color-a`* | The color controls |
| `InspectorPanel` (`Entity`*, `Sections`*, `Pretty`*), `ComponentSection` (`Component`*, `Editors`*), `FieldEditor` (`Field`*, `IsOverridden`*, `Value`*, `Controls`*, `Nested`*, `EulerDegrees`*); classes `inspector-header`*, `inspector-title`*, `component`*, `component-header`*, `component-title`*, `field-name`*, `field-header`* | The inspector's members |
| The shell's `SetComponent`*, `AddComponent`*, `RemoveComponent`*; `InspectorChecks`*, `--game-arg check-inspector`* | The editor's parts |
| Internal: `IconButton`*, `PrefabValues`*, `ComponentSection.Json`* | Insides |
| "Nothing selected", "No fields", "Hex", "Value", "Matrix", "N items", "N entries (read-only)", "None"; step titles "Edit <field>", "Add to <list>", "Remove from <list>", "Add Component", "Remove Component", "Revert", "Revert All" | Text users see |

## Editor hints for C++ components (2026-11-09, ADR 0070)

Settled as drafted: `CRUCIBLE_HINT_COLOR`, `CRUCIBLE_HINT_RANGE(min, max)`,
`CRUCIBLE_HINT_TOOLTIP("…")`, `CRUCIBLE_HINT_HEADER("…")`, ADR 0070
"Editor hints for C++ components", the tests in `FieldTableTests`, the
section "Hints from C++ headers" in `docs/learning/inspectors-and-metadata.md`.
Added while building (placeholders): the binding generator's `FieldHints`*
(internal), the annotations `crucible_hint_color`*, `crucible_hint_range:`*,
`crucible_hint_tooltip:`*, `crucible_hint_header:`*, and the header "Grading"*.

## The asset browser (2026-11-09, M7 step 5)

Settled as drafted: `UIRoot.BeginDrag(payload, preview)`, `CancelDrag`,
`IsDragging`, `DragEvent` (`Payload`, `Position`, `Accepted`),
`UIElement.AllowDrop`, `DragEnter`, `DragOver`, `DragLeave`, `Drop`,
`PickResult.depth`, `setSurfacePickExclusion`, `AssetsPanel`, `AssetTile`,
`AssetWatcher`, `AssetDrag` (`Id`, `Path`), the commands `Assets.Refresh`
("Refresh", Ctrl+R) and `Assets.Reimport` ("Reimport"), the step "Place
<name>", `DragDropTests`, `AssetBrowserTests`, `AssetWatcherTests`, CTest
`launcher.jit.assets`, `assets.png`, ADR 0071,
`docs/learning/drag-drop-and-file-watching.md`. Names added while building
(marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `UIRoot.DragPayload`*, `DragOutside`* (internal), `ContinueDrag`* (internal), `DropAt`* (internal); the On… overrides `OnDragEnter`*, `OnDragOver`*, `OnDragLeave`*, `OnDrop`* | Drag extras |
| `InputEventType.WindowFocusGained`*; `DrawList::entities`*; the pick target "PickDepths"* | Native additions |
| `AssetDatabase.Import(id, force)`* | Forced reimport |
| `AssetsPanel` (`Folder`*, `SelectedAsset`*, `Tiles`*, `Folders`*, `ShowFolder`*), `AssetTile` (`Path`*, `Asset`*, `IsFolder`*, `IsSelected`*, `Kind`*, `CanPlace`*); the kinds "Folder", "Model", "Prefab", "Texture", "Scene", "Shader", "Font", "Style", "Material", "File"*; `AssetWatcher` (`Folder`*, `QuietSeconds`*, `MarkAll`*, `TakeSettled`*) | Browser members |
| The shell's `AssetsChanged`*, `RefreshAssets`*, `Reimport`*, `CanPlace`*, `Placing`*, `Place`*, `PlacementPoint`*; `AssetChecks`*, `--game-arg check-assets`*; classes `asset-crumb`*, `asset-name`*, `drag-preview`* | The editor's parts |
| "Open", "No project open", "(not an asset)", "no importer", "not imported", "Assets / …" | Text users see |

## Play mode and a project's code (2026-11-09, M7 step 6a)

Settled as drafted: `GameBuilder`, the generated project
`Cache/Build/<Project>.Game.csproj` building `<Project>.Game.dll`, the
shell's `Play()`, `Stop()`, `IsPlaying`, `PlayStateChanged`, the command
`Play.Toggle` ("Play", Ctrl+P), `GamePanel` ("Game"), the title's "—
Playing", `GameBuildTests`, `PlayModeTests`, CTest `launcher.jit.play`,
`play.png`, ADR 0072, `docs/learning/play-mode-and-reloading.md` (and, for
6b, `RenderAssets.Reload(AssetId)`, `reloadRenderAsset`). Names added while
building (marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `GameBuilder` (`AssemblyName`*, `ProjectFile`*, `OutputPath`*, `HasCode`*, `IsBuilding`*, `Start`*, `TakeFinished`*, `BuildNow`*, `ProjectText`*), `BuildError`* (`File`*, `Line`*, `Column`*, `Message`*), `GameBuild`* (`Succeeded`*, `Assembly`*, `Errors`*, `Output`*) | Building |
| The shell's `Game`*, `Builder`*, `BuildCode`*, `Shutdown`* (was `Stop`) | The shell |
| `GameView`* (`Surface`*, `HasInput`*, `TakeInput`*), `GamePanel.View`*; `PlayPhaseSystem`* and `PlayBeginFrame`*, `PlayInput`*, `PlayPrePhysics`*, `PlayPhysics`*, `PlayPostPhysics`*, `PlayUpdate`*, `PlayAnimation`*, `PlayLateUpdate`*, `PlayRenderPrepare`*, `PlayRenderSubmit`*, `PlayEndFrame`* | Play's parts |
| `Diorama.Spin`* (`Start`*, `PerFrame`*), `Diorama.SpinSystem`*; `PlayChecks`*, `--game-arg check-play`* | Sample and checks |
| "Building <name>…", "Build error: …", "The game didn't build", "Reloaded while playing", "Playing (no game code: nothing runs)", "Stopped: the scene is as it was" | Text users see |

### Part b: GPU assets reloaded in place (2026-11-09, M7 step 6b)

Settled as drafted: `RenderAssets.Reload(AssetId)`, `reloadRenderAsset`,
`RenderAssetReloadTests`, `GpuReloadTests`, the section "Reloading GPU
assets" in `docs/learning/play-mode-and-reloading.md`, the checks in
`launcher.jit.play`, "Reloaded <path> on the GPU", "Can't reload <path>:
<reason>; the last version stays". Names added while building (marked *)
settled as drafted too:

| Name | What it is |
|---|---|
| `reloadMaterialAsset`*, `reloadGameShader`*, `waitForReload`*, `renderAssetReloads`* (RenderInternal.h) | Internal hooks |
| `LoadedMaterial::mismatched`* | A material whose reloaded shader's type changed size |

## Running a project standalone (2026-11-09, M7 step 7a)

Settled as drafted: the launcher's `--project` and `--scene` in the game
launcher (the game arguments `project=` and `scene=`), `ProjectStartup`
(Crucible.Hosting), `StandaloneRun`, `Cache/Run/<Scene>.crscene`, the
command `Play.RunStandalone` ("Run Standalone", Ctrl+Shift+P; "Stop
Standalone" while running), "Running <scene> standalone", "[game] <line>",
"The standalone run ended (exit code N)", ADR 0073 "Running a project
standalone", `docs/learning/running-standalone.md`, `ProjectStartupTests`,
`StandaloneRunTests`, CTest `launcher.jit.standalone` ("StandaloneChecks:
all N checks passed"). `AssetCache` (Crucible.Assets, in the engine)
replaces the drafted `AssetDatabase.OpenImported`, chosen by the owner.
Names added while building (marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `AssetCache` (`Open`*, `Read`*, `FileOf`*, `Prefabs`*, `Count`*, `Root`*, `CacheFolder`*) | The engine's read-only cache |
| `ProjectStartup.Open`*; `LaunchOptions.editor`* | Opening a project in a game |
| `Command.CurrentTitle`* | A command's title that follows its state |
| `StandaloneRun` (`Arguments`*, `LauncherPath`*, `Start`*, `TakeLines`*, `TakeExit`*, `Stop`*, `IsRunning`*); the shell's `RunStandalone`*, `StopStandalone`*, `Standalone`* | The editor's parts |
| `StandaloneChecks`*, `--game-arg check-standalone`* | The check |
| "opened <scene>: N entities at the top"*, "<scene> has <component>, which nothing registered"*, "The standalone run was stopped"*, "Can't run standalone: …"* | Text users see |

## Native modules (2026-11-09, M7 step 7b)

Settled as drafted: the folder `Project/Native` (`Project.NativeFolder`),
the library `<Project>.Native` (in Cache/Build/Native), crucible/Module.h
with `crucibleModuleStart()`, `crucibleModuleStop()`, and
`CRUCIBLE_MODULE_EXPORT`, the core's `loadModule(path)`, the launcher's
`--module <file>`, `NativeBuilder`, "Building Diorama.Native…", "Built
Diorama.Native: restart the editor to load it", `Diorama::Bob` (`height`,
`speed`) and the system "Diorama.Bob", ADR 0074 "Native modules",
`docs/learning/native-modules.md`, `NativeBuildTests`, `ModuleTests`,
CTest `launcher.jit.native` ("NativeChecks: all N checks passed"); and,
asked while building, `linkEngineComponents()` and marking each
declaration `CRUCIBLE_EXPORT`. Names added while building (marked *)
settled as drafted too:

| Name | What it is |
|---|---|
| `stopModules`*, `moduleCount`*, `modulePath`*, `nativeSystemFromModule`* (renamed `nativeSystemIsGameplay` in 7c) | The core |
| `ScheduleBuilder.IncludeModuleSystems`* (renamed `IncludeGameplaySystems` in 7c) | Module systems in the editor's play schedule |
| `CrucibleModule.json`* (`Cmake`*, `Ninja`*, `CxxCompiler`*, `Environment`*, `BuildType`*, `IncludeDirectories`*, `CompileDefinitions`*, `CompileOptions`*, `CoreLibrary`*, `CoreHeaders`*, `Dotnet`*, `Bindgen`*, `Libclang`*, `Clang`*), `ModuleToolkit`* | The engine's build, for module builds |
| The binding generator's `--root-namespace`* | A module's own namespace |
| `NativeBuilder` (`LibraryName`*, `Folder`*, `ProjectFile`*, `OutputPath`*, `BindingsPath`*, `LibraryExtension`*, `ToolkitBeside`*, `HasCode`*, `IsBuilding`*, `Start`*, `TakeFinished`*, `BuildNow`*, `ReadToolkit`*, `ProjectText`*); the shell's `NativeBuilder`*, `BuildNative`*, `ModuleLoaded`*; Cache/Build/Native/Loaded/<process>/* | The editor's parts |
| `diorama::bobbedLastFrame()`* (C#: `Diorama.Api.BobbedLastFrame()`), `Diorama.Interop`* | The sample |
| `NativeChecks`*, `--game-arg check-native`*, `exports.public`*, `crucible_module_tests`*, `crucible_test_module`*, `crucible_not_a_module`* | Tests |
| "Loaded Diorama.Native"*, "Can't load Diorama.Native: …"*, "Diorama.Native didn't build"* | Text users see |
| The shell's `GameReloading`* | A new game build is about to replace the old one |

## Physics and audio contracts (2026-11-09, M7 step 7c)

Settled as drafted: crucible/Physics.h (`crucible::physics`, C#
`Crucible.Physics`): `RigidBody` (`kind`, `mass`, `linearVelocity`,
`angularVelocity`, `gravityScale`, `layer`, then `layers` and `mask`), `BodyKind` (`Static`,
`Kinematic`, `Dynamic`), `Collider` (`shape`, `size`, `offset`, `friction`,
`restitution`, `isTrigger`, `mesh`), `ShapeKind` (`Box`, `Sphere`, `Capsule`,
`Mesh`, `Convex`), `Joint` (`kind`, `other`, anchors, limits), `JointKind`
(`Fixed`, `Hinge`, `BallSocket`, `SwingTwist`, `Distance`),
`CharacterController` (`radius`, `height`, `stepHeight`, `slopeLimit`);
`raycast`, `shapeCast`, `overlap`, `contacts`, `ContactEvent`,
`moveCharacter`, `setLayersCollide` (removed when layers became masks); the systems "Physics.Sync",
"Physics.Step", "Physics.WriteBack"; `installPhysics`, `PhysicsService`.
crucible/Audio.h (`crucible::audio`, C# `Crucible.Audio`): `AudioSource`
(`clip`, `volume`, `pitch`, `loop`, `playOnStart`, `bus`, `spatial`,
`minDistance`, `maxDistance`, `rolloff`, `occlusion`), `AudioListener`;
`playOneShot`, `stop`, `setBusVolume`, `setBusEffects`, `BusEffects`,
`blendToSnapshot`, `playMusic`, `MusicTransition`; the system
"Audio.Update"; `installAudio`, `AudioService`. `SystemDesc::gameplay`,
`nativeSystemIsGameplay`, `ScheduleBuilder.IncludeGameplaySystems`; ADR
0075 "Physics and audio contracts", `docs/learning/subsystem-contracts.md`,
`PhysicsContractTests`, `AudioContractTests`, `SubsystemReplacementTests`.
Names added while building (marked *) settled as drafted too:

| Name | What it is |
|---|---|
| `Joint` (`anchor`*, `otherAnchor`*, `axis`*, `lowerLimit`*, `upperLimit`*, `swingLimit`*, `twistLimit`*); `Collider.meshIndex`* | Fields |
| `QueryShape`* (`kind`*, `size`*), `RayHit`* (`hit`*, `entity`*, `point`*, `normal`*, `distance`*), `ContactKind`* (`Begin`*, `End`*), `ContactEvent` (`kind`*, `trigger`*, `a`*, `b`*, `point`*, `normal`*), `CharacterMove`* (`position`*, `grounded`*, `groundNormal`*), `physicsService`*, `registerPhysics`*; the constants `syncSystemName`*, `stepSystemName`*, `writeBackSystemName`*, `layerCount`*, `allLayers`* | Physics |
| `Rolloff`* (`Linear`*, `Inverse`*, `None`*), `BusEffects` (`lowPassHz`*, `reverbSend`*, `duckedBy`*, `duckAmount`*), `MusicAlign`* (`Now`*, `Beat`*, `Bar`*), `MusicTransition` (`crossfadeSeconds`*, `align`*), `defineSnapshot`*, `audioService`*, `registerAudio`*, `NullAudioCounts`* (`plays`*, `stops`*, `busChanges`*, `snapshots`*, `musicChanges`*, `sourcesStarted`*), `nullAudioCounts`*; the constants `updateSystemName`*, `masterBus`*, `noBus`* | Audio |
| `registerDefaultSystem`* | An engine default a replacement registered first keeps out |
| `crucible_test_subsystems`*, tests/native/SubsystemTestSupport.h* | Tests |

Layers as masks (2026-11-09, after trying 7c by hand), settled as drafted:
`RigidBody.layers` and `mask`, `CharacterController.layers` and `mask`
(replacing `layer`), `CRUCIBLE_HINT_LAYERS` and C# `[Layers]`
(`LayersAttribute`), `LayerMaskField` (Crucible.UI), the project file's
`"PhysicsLayers"`. Names added while building (marked *) settled as
drafted too:

| Name | What it is |
|---|---|
| `LayerMaskField` (`LayerNames`*, `Mask`*, `MaskChanged`*, `IsOpen`*, `List`*, `NameOf`*), `LayerMaskRow`* (`Text`*, `IsChecked`*); the rows "Nothing"*, "Everything"*, "Default"*, "Layer N"* | The control |
| `SerializedField.IsLayers`*, `Project.PhysicsLayers`*, `ProjectFile.PhysicsLayers`* | Carrying it |

## Input, asset, renderer, and loop contracts (2026-11-09, M7 step 7d)

Settled as drafted: `InputSource` (`poll`, `text`), `installInputSource`,
`inputSource`, `platformInputSource`; crucible/AssetSource.h, `AssetSource`
(`read`), `installAssetSource`, `assetSource`; C# `AssetSource.Install`
(replacing `RenderAssets.SetReader`), `PrefabLibrary.FromAssetSource()`;
`render::steps::clear`, `shadows`, … and C# `RenderSteps.Clear`, …;
`ILoopDriver` (`Run`), `[assembly: LoopDriver(typeof(...))]`; ADR 0076
"Input, asset, renderer, and loop contracts", the section in
`docs/learning/subsystem-contracts.md`, `InputSourceTests`,
`AssetSourceTests`, `LoopDriverTests`. Names added while building (marked
*) settled as drafted too:

| Name | What it is |
|---|---|
| `AssetDelivery`*, `assetSourceInstalled`*, `assetSize`*, `copyAsset`* | The asset source's C++ side |
| C# `AssetSource.InstallDefault`*, `IsInstalled`*, `Read`* | Its C# side |
| `render::steps` (`depthPrepass`*, `lightClustering`*, `opaque`*, `outlines`*, `sky`*, `transparent`*, `bloom`*, `tonemap`*, `grid`*, `pick`*, `selectionOutline`*) and the matching C# constants | Step names |
| `ILoopDriver.Schedule`*, `LoopDriverAttribute.DriverType`*, `LoadedGame.CreateLoopDriver`* | The loop driver |

## The M7 demonstration (2026-11-09, M7 step 8)

Settled as drafted: `DemonstrationChecks`, `--game-arg check-demonstration`,
CTest `launcher.jit.demonstration` ("DemonstrationChecks: all N checks
passed"), tests/demonstration/compare_demonstration.cmake, the project "Demo"
with Code/Wobble.cs (`Wobble`, `WobbleSystem`) and Native/Pulse.cpp
(`demo::Pulse`, "Demo.Pulse"), `placed.png`, `playing.png`, ADR 0077 "The M7
demonstration", `docs/learning/end-to-end-checks.md`. Names added while
building are placeholders (marked *):

| Name | What it is |
|---|---|
| `--game-arg demonstration-repo`*, `--game-arg compare`* | The check's arguments |
| `Wobble` (`Amount`*, `Frames`*), `Pulse.rate`*, `demo::pulsesLastFrame`*, Native/Pulse.h*, Code/Pulse.cs* | The Demo project's code |
| The scene `Main.crscene`*, the entities "Sun"* and "Camera"* | Made by the session |

