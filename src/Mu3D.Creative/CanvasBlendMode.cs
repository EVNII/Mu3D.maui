namespace Mu3D.Creative;

/// <summary>
/// Selects the RGB blend function applied before source-over alpha compositing in linear light.
/// Functions extend algebraically to negative and above-one values without clipping.
/// </summary>
public enum CanvasBlendMode
{
    /// <summary>Uses the source RGB unchanged.</summary>
    Normal,

    /// <summary>Multiplies source and backdrop RGB component by component.</summary>
    Multiply,

    /// <summary>Uses source plus backdrop minus their componentwise product.</summary>
    Screen,

    /// <summary>Adds source and backdrop RGB in the overlap; alpha still uses source-over.</summary>
    Add,
}
