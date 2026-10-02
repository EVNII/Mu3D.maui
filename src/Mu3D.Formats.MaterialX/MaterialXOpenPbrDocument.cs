using Mu3D.SceneGraph;

namespace Mu3D.Formats.MaterialX;

/// <summary>A named MaterialX surface node containing mutable backend-independent OpenPBR parameters.</summary>
public sealed class MaterialXOpenPbrSurface
{
    /// <summary>Initializes a named surface. The node name remains stable when the surface's display name changes.</summary>
    public MaterialXOpenPbrSurface(string name, OpenPbrSurface surface)
    {
        MaterialXNames.Validate(name, nameof(name));
        ArgumentNullException.ThrowIfNull(surface);
        Name = name;
        Surface = surface;
    }

    /// <summary>Gets the stable MaterialX node identifier.</summary>
    public string Name { get; }

    /// <summary>Gets the caller-owned mutable surface parameters.</summary>
    public OpenPbrSurface Surface { get; }
}

/// <summary>A named MaterialX surface material referring to one surface node in the same document.</summary>
public sealed class MaterialXSurfaceMaterial
{
    /// <summary>Initializes a material-to-surface reference.</summary>
    public MaterialXSurfaceMaterial(string name, string surfaceNodeName)
    {
        MaterialXNames.Validate(name, nameof(name));
        MaterialXNames.Validate(surfaceNodeName, nameof(surfaceNodeName));
        Name = name;
        SurfaceNodeName = surfaceNodeName;
    }

    /// <summary>Gets the material node identifier.</summary>
    public string Name { get; }

    /// <summary>Gets the referenced surface node identifier.</summary>
    public string SurfaceNodeName { get; }
}

/// <summary>
/// Contains an immutable inventory of MaterialX OpenPBR surface nodes and material references.
/// Surface parameter objects remain caller-owned and mutable. Construction resolves every reference.
/// </summary>
public sealed class MaterialXOpenPbrDocument
{
    internal const int MaximumInventoryCount = 4096;

    /// <summary>Initializes a document from named surfaces and optional material references.</summary>
    public MaterialXOpenPbrDocument(
        IEnumerable<MaterialXOpenPbrSurface> surfaces,
        IEnumerable<MaterialXSurfaceMaterial>? materials = null)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        var surfaceList = CopyBounded(surfaces);
        var materialList = materials is null ? [] : CopyBounded(materials);
        if (surfaceList.Count == 0)
            throw new ArgumentException("At least one OpenPBR surface is required.", nameof(surfaces));

        var allNames = new HashSet<string>(StringComparer.Ordinal);
        var surfaceNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in surfaceList)
        {
            if (!allNames.Add(surface.Name))
                throw new ArgumentException($"Duplicate node name '{surface.Name}'.", nameof(surfaces));
            surfaceNames.Add(surface.Name);
        }
        foreach (var material in materialList)
        {
            if (!allNames.Add(material.Name))
                throw new ArgumentException($"Duplicate node name '{material.Name}'.", nameof(materials));
            if (!surfaceNames.Contains(material.SurfaceNodeName))
                throw new ArgumentException($"Material '{material.Name}' references missing surface '{material.SurfaceNodeName}'.", nameof(materials));
        }
        Surfaces = surfaceList.AsReadOnly();
        Materials = materialList.AsReadOnly();
    }

    /// <summary>Gets the surfaces in document order.</summary>
    public IReadOnlyList<MaterialXOpenPbrSurface> Surfaces { get; }

    /// <summary>Gets the surface material references in document order.</summary>
    public IReadOnlyList<MaterialXSurfaceMaterial> Materials { get; }

    private static List<T> CopyBounded<T>(IEnumerable<T> source) where T : class
    {
        var result = new List<T>();
        foreach (var item in source)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (result.Count == MaximumInventoryCount)
                throw new ArgumentException($"An inventory may contain at most {MaximumInventoryCount} nodes.", nameof(source));
            result.Add(item);
        }
        return result;
    }
}

internal static class MaterialXNames
{
    internal static void Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > 256 || !IsStart(value[0]) || value.Any(c => !IsStart(c) && !char.IsAsciiDigit(c)))
            throw new ArgumentException("This MaterialX subset requires ASCII identifiers matching [A-Za-z_][A-Za-z0-9_]*, at most 256 characters.", parameterName);
    }

    private static bool IsStart(char value) => char.IsAsciiLetter(value) || value == '_';
}
