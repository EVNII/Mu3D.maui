namespace Mu3D.Toolkit.Viewports;

/// <summary>
/// Describes the application-requested lifecycle level for one virtualized scene item.
/// </summary>
/// <remarks>
/// Visibility discovery and the size of each range remain application policy. A virtualized UI
/// normally marks visible items <see cref="Active"/>, a bounded nearby range
/// <see cref="Suspended"/>, and every colder item <see cref="Unloaded"/>.
/// </remarks>
public enum SceneViewportActivity
{
    /// <summary>Load content when needed and own a live viewport while the host is loaded.</summary>
    Active,

    /// <summary>Release the live viewport while retaining already loaded managed scene content.</summary>
    Suspended,

    /// <summary>Release both the live viewport and the control-owned scene content.</summary>
    Unloaded,
}
