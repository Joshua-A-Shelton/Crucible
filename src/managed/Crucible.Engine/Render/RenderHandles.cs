namespace Crucible.Render;

// Whether a handle's asset is loaded (ADR 0099): acquiring returns at once
// and the load is published on a later frame.
public partial struct MeshHandle
{
    /// <summary>Loaded, with its materials' textures, so it draws.</summary>
    public readonly bool IsReady => Api.IsMeshReady(this);
}

public partial struct TextureHandle
{
    /// <summary>Loaded, so <see cref="RenderAssets.TextureDescriptor"/> gives its slot.</summary>
    public readonly bool IsReady => Api.IsTextureReady(this);
}

public partial struct MaterialHandle
{
    /// <summary>Its textures loaded.</summary>
    public readonly bool IsReady => Api.IsMaterialReady(this);
}
