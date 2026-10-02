namespace Mu3D.Color;

/// <summary>
/// Specifies the storage precision used for color images and intermediate render targets.
/// </summary>
public enum PixelPrecision
{
    /// <summary>Use IEEE 754 binary16 components.</summary>
    Float16,

    /// <summary>Use IEEE 754 binary32 components.</summary>
    Float32,
}
