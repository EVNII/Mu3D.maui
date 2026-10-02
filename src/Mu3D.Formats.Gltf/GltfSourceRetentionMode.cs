namespace Mu3D.Formats.Gltf;

/// <summary>Specifies which exact encoded source bytes remain attached to a loaded glTF asset.</summary>
public enum GltfSourceRetentionMode
{
    /// <summary>Does not retain exact glTF, GLB, buffer or image source bytes.</summary>
    None,

    /// <summary>Retains only the exact main glTF or GLB source bytes.</summary>
    MainSource,

    /// <summary>Retains the main source and every resolved external buffer or image resource.</summary>
    MainSourceAndExternalResources,
}
