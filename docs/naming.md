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

Settled: `TextField`, `NumberField`, `TextChanged`, `Committed`, `TextInput`,
`polledText`, and the clipboard functions. The rest are placeholders.

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
