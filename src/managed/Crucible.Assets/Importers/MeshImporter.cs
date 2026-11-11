using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Crucible.Binary;
using Crucible.Importers;
using Crucible.Serialization;
using Crucible.Shaders;
using ImportersApi = Crucible.Importers.Api;

namespace Crucible.Assets.Importers;

/// <summary>The shading model of a model's generated materials (ADR 0052).</summary>
public enum ShadingModel
{
    /// <summary>Soft-banded toon, what new projects start with (ADR 0010).</summary>
    Toon,
    Pbr,
}

/// <summary>
/// A material's inverted-hull outline (ADR 0055): <see cref="Width"/> in
/// pixels (0 for none), thinning past 10 meters; the line is
/// <see cref="Color"/> times the surface's color as far as
/// <see cref="Tint"/> says (0 a flat color, 1 fully tinted).
/// </summary>
[Serialized]
public partial struct OutlineSettings
{
    public float Width;
    public System.Numerics.Vector3 Color;
    public float Tint;
}

/// <summary>A model's import settings, kept in its sidecar (ADR 0043).</summary>
[Serialized]
public partial class MeshImportSettings
{
    /// <summary>How the materials generated for the model's slots are lit.</summary>
    public ShadingModel Shading = ShadingModel.Toon;

    /// <summary>The outline of every material generated for the model's slots.</summary>
    public OutlineSettings Outline;

    /// <summary>
    /// Authored materials (.crmaterial) for the model's slots, by slot:
    /// <see cref="AssetId.None"/>, or a slot past the list's end, keeps its
    /// generated material (ADR 0052).
    /// </summary>
    public List<AssetId> Materials = [];

    /// <summary>Order triangles for the vertex cache and overdraw, and vertices for fetching.</summary>
    public bool Optimize = true;

    /// <summary>Make tangents with MikkTSpace where the source has none (and has UVs).</summary>
    public bool GenerateTangents = true;

    public bool GenerateLods = true;

    /// <summary>Levels after LOD 0, at most.</summary>
    public int MaxLods = 4;

    /// <summary>Each level keeps about this share of the last's triangles.</summary>
    public float LodRatio = 0.5f;

    /// <summary>Levels stop once they deviate more than this share of the mesh's size.</summary>
    public float LodMaxError = 0.05f;

    /// <summary>
    /// Joints a skinned vertex follows, at most: 4, or 8 for the faces and
    /// cloth that need more (ADR 0078). Each vertex keeps its strongest.
    /// </summary>
    public int MaxInfluences = 4;

    /// <summary>Samples per second the model's animation clips are resampled at (ADR 0078).</summary>
    public float ClipSampleRate = 30;

    /// <summary>Events and windows on the model's clips, by clip name (ADR 0081).</summary>
    public Dictionary<string, ClipImportSettings> Clips = [];
}

/// <summary>A clip's events and windows, marked on its timeline (ADR 0081).</summary>
[Serialized]
public partial class ClipImportSettings
{
    public List<ClipEventSettings> Events = [];
    public List<ClipWindowSettings> Windows = [];
}

/// <summary>A named moment on a clip, in seconds from its start (a footstep).</summary>
[Serialized]
public partial class ClipEventSettings
{
    public string Name = "";
    public float Time;
}

/// <summary>A named range on a clip, open from <see cref="Start"/> until <see cref="End"/> (a parry window).</summary>
[Serialized]
public partial class ClipWindowSettings
{
    public string Name = "";
    public float Start;
    public float End;
}

/// <summary>
/// Imports glTF models (.gltf with its buffers, or .glb) as MESH binary files
/// (ADR 0043): every mesh of the file, its node hierarchy, and its materials,
/// mirrored into Crucible's space. Buffers in other files are read as
/// dependencies, so changing one reimports the model; textures are other
/// assets, referenced by ID. A skinned model's animations become clips
/// (.crclip), sub-assets of the model (ADR 0078). Names are placeholders
/// (docs/naming.md).
/// </summary>
public sealed class MeshImporter : Importer<MeshImportSettings>
{
    public static readonly uint Kind = Tag.Of("MESH");
    public static readonly uint MaterialSection = Tag.Of("MATL");
    public const uint KindVersion = 3; // as meshFileVersion

    /// <summary>An animation clip's file (ADR 0078), as ClipFile.h lays it out.</summary>
    public static readonly uint ClipKind = Tag.Of("CLIP");
    public const uint ClipVersion = 1; // as clipFileVersion
    public const string ClipExtension = ".crclip";

    public override string Name => "Mesh";

    // 2: generated materials (MTLS); 3: remapped slots (MRMP); 5: skins and
    // clips; 6: clip events and windows.
    public override int Version => 6;

    public override IReadOnlyList<string> Extensions => [".gltf", ".glb"];

    public const string FileExtension = ".crmesh";

    public override string OutputExtension => FileExtension;

    public override void Import(ImportContext context, MeshImportSettings settings)
    {
        GltfSourceHandle source = Native(() => ImportersApi.ParseGltf(context.ReadSource()));
        try
        {
            for (uint i = 0; i < ImportersApi.GltfBufferCount(source); ++i)
            {
                string path = context.Resolve(ImportersApi.GltfBufferUri(source, i));
                byte[] bytes;
                try
                {
                    bytes = context.ReadDependency(path);
                }
                catch (IOException exception)
                {
                    throw new InvalidDataException($"its buffer {path} can't be read: {exception.Message}", exception);
                }
                Native(() => ImportersApi.SetGltfBuffer(source, i, bytes));
            }
            MeshImportHandle mesh = Native(() => ImportersApi.ImportGltf(source, ToNative(settings)));
            try
            {
                Write(context, mesh, settings, uri => [context.Resolve(uri)]);
            }
            finally
            {
                ImportersApi.DestroyMeshImport(mesh);
            }
        }
        finally
        {
            ImportersApi.DestroyGltfSource(source);
        }
    }

    internal static MeshSettings ToNative(MeshImportSettings settings) => new()
    {
        Optimize = settings.Optimize,
        GenerateTangents = settings.GenerateTangents,
        GenerateLods = settings.GenerateLods,
        MaxLods = (uint)Math.Clamp(settings.MaxLods, 0, 8),
        LodRatio = settings.LodRatio,
        LodMaxError = Math.Max(0, settings.LodMaxError),
        MaxInfluences = settings.MaxInfluences > 4 ? 8u : 4u,
        ClipSampleRate = Math.Clamp(settings.ClipSampleRate, 1, 240),
    };

    /// <summary>
    /// Writes an import as a MESH file, shared by every model format. Each
    /// texture's file, as the source names it, becomes the asset at the first
    /// of <paramref name="candidates"/>' paths that is one.
    /// </summary>
    internal static void Write(ImportContext context, MeshImportHandle mesh, MeshImportSettings settings,
        Func<string, IReadOnlyList<string>> candidates)
    {
        ImportedFileWriter output = context.CreateOutput(Kind, KindVersion);
        for (uint i = 0; i < ImportersApi.MeshSectionCount(mesh); ++i)
        {
            MeshSectionInfo section = ImportersApi.MeshSection(mesh, i);
            byte[] bytes = new byte[section.Size];
            ImportersApi.CopyMeshSection(mesh, i, bytes);
            output.Add(section.Tag, section.Version, bytes);
        }

        // Materials, with each texture's file turned into its asset ID.
        MaterialEntry[] materials = new MaterialEntry[ImportersApi.MeshMaterialCount(mesh)];
        for (uint m = 0; m < materials.Length; ++m)
        {
            MaterialEntry material = ImportersApi.MeshMaterial(mesh, m);
            foreach (TextureRole role in Enum.GetValues<TextureRole>())
            {
                string uri = ImportersApi.MeshMaterialTexture(mesh, m, role);
                if (uri.Length == 0)
                {
                    continue;
                }
                IReadOnlyList<string> paths = candidates(uri);
                AssetId texture = paths.Select(context.IdOf).FirstOrDefault(id => !id.IsNone);
                if (texture.IsNone)
                {
                    string looked = paths.Count == 0 ? uri : string.Join(" or ", paths);
                    context.Warn($"material {m}: its {role} texture, {looked}, isn't in Assets/");
                    continue;
                }
                texture.Value.TryWriteBytes(TextureSlot(ref material, role));
            }
            materials[m] = material;
        }
        output.Add(MaterialSection, 1, MemoryMarshal.AsBytes(materials.AsSpan()));
        output.Add(GeneratedMaterialsSection, 1, GeneratedMaterials(materials, settings.Shading, settings.Outline));
        if (settings.Materials.Any(id => !id.IsNone))
        {
            // One asset ID per slot, zero for the generated material.
            byte[] remap = new byte[16 * materials.Length];
            for (int slot = 0; slot < Math.Min(materials.Length, settings.Materials.Count); ++slot)
            {
                if (!settings.Materials[slot].IsNone)
                {
                    settings.Materials[slot].Value.TryWriteBytes(remap.AsSpan(16 * slot, 16));
                }
            }
            if (settings.Materials.Count > materials.Length)
            {
                context.Warn($"Materials lists {settings.Materials.Count} slots, but the model has {materials.Length}");
            }
            output.Add(RemappedMaterialsSection, 1, remap);
        }

        // Each clip a sub-asset, found by its name across reimports, with
        // its events and windows from the settings.
        HashSet<string> clipNames = [];
        for (uint c = 0; c < ImportersApi.MeshClipCount(mesh); ++c)
        {
            string name = ImportersApi.MeshClipName(mesh, c);
            clipNames.Add(name);
            ImportedFileWriter clip = context.AddSubAsset(name, ClipKind, ClipVersion, ClipExtension);
            float duration = 0;
            for (uint i = 0; i < ImportersApi.MeshClipSectionCount(mesh, c); ++i)
            {
                MeshSectionInfo section = ImportersApi.MeshClipSection(mesh, c, i);
                byte[] bytes = new byte[section.Size];
                ImportersApi.CopyMeshClipSection(mesh, c, i, bytes);
                clip.Add(section.Tag, section.Version, bytes);
                if (section.Tag == ClipInfoSection)
                {
                    duration = MemoryMarshal.Read<ClipInfo>(bytes).Duration;
                }
            }
            if (settings.Clips.TryGetValue(name, out ClipImportSettings? marks))
            {
                AddClipMarks(clip, name, duration, marks);
            }
        }
        foreach (string named in settings.Clips.Keys.Where(named => !clipNames.Contains(named)))
        {
            context.Warn($"Clips names {named}, but the model has no clip of that name");
        }

        for (uint i = 0; i < ImportersApi.MeshWarningCount(mesh); ++i)
        {
            context.Warn(ImportersApi.MeshWarning(mesh, i));
        }
    }

    public static readonly uint ClipInfoSection = Tag.Of("INFO");
    public static readonly uint ClipEventSection = Tag.Of("EVNT");
    public static readonly uint ClipWindowSection = Tag.Of("WNDW");

    // A clip's events (by time) and windows (by start), as ClipFile.h lays
    // them out; each named by its hash, as the runtime reports it.
    private static void AddClipMarks(ImportedFileWriter clip, string name, float duration, ClipImportSettings marks)
    {
        foreach (ClipEventSettings mark in marks.Events.Where(mark => mark.Time < 0 || mark.Time > duration))
        {
            throw new InvalidDataException($"{name}: event {mark.Name} at {mark.Time} s is outside the clip (0 to {duration} s)");
        }
        foreach (ClipWindowSettings window in marks.Windows.Where(w => w.Start < 0 || w.End > duration || w.End <= w.Start))
        {
            throw new InvalidDataException(
                $"{name}: window {window.Name} ({window.Start} to {window.End} s) must end after it starts, within the clip (0 to {duration} s)");
        }
        if (marks.Events.Count > 0)
        {
            ClipEvent[] events = marks.Events.OrderBy(mark => mark.Time)
                .Select(mark => new ClipEvent { Time = mark.Time, Name = Animation.AnimationEvent.NameOf(mark.Name) })
                .ToArray();
            clip.Add(ClipEventSection, 1, MemoryMarshal.AsBytes(events.AsSpan()));
        }
        if (marks.Windows.Count > 0)
        {
            ClipWindow[] windows = marks.Windows.OrderBy(window => window.Start)
                .Select(window => new ClipWindow
                {
                    Start = window.Start, End = window.End, Name = Animation.AnimationEvent.NameOf(window.Name),
                })
                .ToArray();
            clip.Add(ClipWindowSection, 1, MemoryMarshal.AsBytes(windows.AsSpan()));
        }
    }

    /// <summary>
    /// One engine material per slot (ADR 0052): a <see cref="MaterialInfo"/>
    /// each, then each one's <see cref="DefaultMaterial"/> parameters and
    /// textures, filled from the imported material.
    /// </summary>
    internal static unsafe byte[] GeneratedMaterials(MaterialEntry[] materials, ShadingModel shading,
        OutlineSettings outline = default)
    {
        int infoSize = sizeof(MaterialInfo) * materials.Length;
        int recordSize = sizeof(DefaultMaterial) + sizeof(MaterialTexture) * TextureRoles.Length;
        byte[] section = new byte[infoSize + recordSize * materials.Length];
        Span<MaterialInfo> infos = MemoryMarshal.Cast<byte, MaterialInfo>(section.AsSpan(0, infoSize));
        for (int m = 0; m < materials.Length; ++m)
        {
            ref MaterialEntry source = ref materials[m];
            AlphaMode alphaMode = (AlphaMode)source.AlphaMode;
            DefaultMaterial parameters = new()
            {
                BaseColor = new System.Numerics.Vector4(source.BaseColor[0], source.BaseColor[1], source.BaseColor[2],
                    source.BaseColor[3]),
                Emissive = new System.Numerics.Vector3(source.Emissive[0], source.Emissive[1], source.Emissive[2]),
                Metallic = source.Metallic,
                Roughness = source.Roughness,
                NormalScale = 1,
                OcclusionStrength = 1,
            };
            int at = infoSize + recordSize * m;
            MemoryMarshal.Write(section.AsSpan(at), in parameters);
            int texturesAt = at + sizeof(DefaultMaterial);
            int textureCount = 0;
            foreach ((TextureRole role, int offset) in TextureRoles)
            {
                ReadOnlySpan<byte> id = TextureSlot(ref source, role);
                if (id.IndexOfAnyExcept((byte)0) < 0)
                {
                    continue;
                }
                MaterialTexture texture = new() { Offset = (uint)offset };
                id.CopyTo(texture.Texture);
                MemoryMarshal.Write(section.AsSpan(texturesAt + sizeof(MaterialTexture) * textureCount++), in texture);
            }
            infos[m] = new MaterialInfo
            {
                Type = (uint)(shading == ShadingModel.Pbr ? DefaultMaterialType.Pbr : DefaultMaterialType.Toon),
                AlphaMode = alphaMode,
                DoubleSided = source.DoubleSided,
                AlphaCutoff = source.AlphaCutoff,
                ParameterOffset = (uint)at,
                ParameterSize = (uint)sizeof(DefaultMaterial),
                TextureOffset = (uint)texturesAt,
                TextureCount = (uint)textureCount,
            };
            Outline(ref infos[m], outline);
        }
        return section;
    }

    // The record's outline, from its settings.
    internal static void Outline(ref MaterialInfo info, OutlineSettings outline)
    {
        info.OutlineWidth = Math.Max(outline.Width, 0);
        info.OutlineColor[0] = outline.Color.X;
        info.OutlineColor[1] = outline.Color.Y;
        info.OutlineColor[2] = outline.Color.Z;
        info.OutlineTint = Math.Clamp(outline.Tint, 0, 1);
    }

    public static readonly uint GeneratedMaterialsSection = Tag.Of("MTLS");
    public static readonly uint RemappedMaterialsSection = Tag.Of("MRMP");

    // Each texture role's slot in DefaultMaterial, from its generated layout.
    private static readonly (TextureRole Role, int Offset)[] TextureRoles = DefaultTextureSlots();

    private static (TextureRole, int)[] DefaultTextureSlots()
    {
        DefaultMaterial probe = default;
        return
        [
            (TextureRole.BaseColor, Of(ref probe, ref probe.BaseColorTexture)),
            (TextureRole.MetallicRoughness, Of(ref probe, ref probe.MetallicRoughnessTexture)),
            (TextureRole.Normal, Of(ref probe, ref probe.NormalTexture)),
            (TextureRole.Occlusion, Of(ref probe, ref probe.OcclusionTexture)),
            (TextureRole.Emissive, Of(ref probe, ref probe.EmissiveTexture)),
        ];

        static int Of(ref DefaultMaterial material, ref uint field) => (int)Unsafe.ByteOffset(
            ref Unsafe.As<DefaultMaterial, byte>(ref material), ref Unsafe.As<uint, byte>(ref field));
    }

    /// <summary>A material's texture ID for a role: the GUID's bytes as <see cref="Guid.TryWriteBytes(Span{byte})"/> writes them.</summary>
    public static Span<byte> TextureSlot(ref MaterialEntry material, TextureRole role) => role switch
    {
        TextureRole.BaseColor => material.BaseColorTexture,
        TextureRole.MetallicRoughness => material.MetallicRoughnessTexture,
        TextureRole.Normal => material.NormalTexture,
        TextureRole.Occlusion => material.OcclusionTexture,
        _ => material.EmissiveTexture,
    };

    // The importers report failure as InvalidData with a reason.
    internal static T Native<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (CrucibleException exception) when (exception.Status == Status.InvalidData)
        {
            throw new InvalidDataException(ImportersApi.LastImportError(), exception);
        }
    }

    internal static void Native(Action call) => Native(() =>
    {
        call();
        return 0;
    });
}
