using Mu3D.Native.OpenColorIO.Interop;

namespace Mu3D.Native.OpenColorIO;

/// <summary>An explicitly loaded OCIO v2 configuration with immutable, independently owned CPU processors.</summary>
/// <remarks>
/// No process-global OCIO configuration is selected or modified. Context variables use the configuration's
/// declared defaults; process environment overrides and active display/view filters are not used by this API.
/// Files and referenced LUTs remain application-owned. Loading a new configuration is an explicit operation.
/// </remarks>
public sealed class OcioConfiguration : IDisposable
{
    private readonly OcioConfigHandle handle;
    private readonly object gate = new();
    private OcioConfiguration(OcioConfigHandle handle)
    {
        this.handle = handle;
        try { CacheId = OcioNative.ConfigText(handle, 4); }
        catch { handle.Dispose(); throw; }
    }
    /// <summary>Gets the exact native OpenColorIO release required by this adapter.</summary>
    public static string Version => "2.5.2";
    /// <summary>Gets this configuration/context's OCIO cache identity when loaded.</summary>
    public string CacheId { get; }
    /// <summary>Gets all declared color-space names, including inactive spaces.</summary>
    public IReadOnlyList<string> ColorSpaces => Enumerate(0);
    /// <summary>Gets all declared displays, independent of environment/active-display filters.</summary>
    public IReadOnlyList<string> Displays => Enumerate(1);
    /// <summary>Gets all declared Look names.</summary>
    public IReadOnlyList<string> Looks => Enumerate(2);

    /// <summary>Loads an explicit config file, resolving its relative assets from the file's directory.</summary>
    /// <param name="path">The explicit path; it is resolved against the caller's current directory once.</param>
    public static OcioConfiguration LoadFile(string path)
    {
        OcioNative.ValidateText(path, nameof(path));
        return Create(0, Path.GetFullPath(path), null);
    }
    /// <summary>Loads explicit YAML text and optionally supplies its asset working directory.</summary>
    /// <param name="configuration">OCIO v2 configuration text.</param>
    /// <param name="workingDirectory">Required when relative external assets are used; null supplies no asset directory.</param>
    public static OcioConfiguration LoadString(string configuration, string? workingDirectory = null)
    {
        OcioNative.ValidateText(configuration, nameof(configuration));
        if (workingDirectory is not null) OcioNative.ValidateText(workingDirectory, nameof(workingDirectory));
        return Create(1, configuration, workingDirectory is null ? null : Path.GetFullPath(workingDirectory));
    }
    /// <summary>Loads an explicitly named built-in configuration from the pinned OCIO release.</summary>
    /// <param name="name">An exact name returned by <see cref="GetBuiltinConfigurations"/>.</param>
    public static OcioConfiguration LoadBuiltin(string name)
    {
        OcioNative.ValidateText(name, nameof(name));
        return Create(2, name, null);
    }
    /// <summary>Lists exact built-in configuration identifiers from the pinned native release.</summary>
    public static IReadOnlyList<string> GetBuiltinConfigurations()
    {
        OcioNative.EnsureAvailable();
        using OcioConfigHandle empty = new(0);
        OcioNative.Check(OcioNative.ConfigCount(empty, 4, null, out int count));
        return Array.AsReadOnly(Enumerable.Range(0, count).Select(i => OcioNative.ConfigText(empty, 6, index: i)).ToArray());
    }
    /// <summary>Lists display-defined and attached shared views, ignoring active-view filters.</summary>
    /// <param name="display">An explicit display identifier.</param>
    public IReadOnlyList<string> GetViews(string display)
    {
        OcioNative.ValidateText(display, nameof(display));
        return Enumerate(3, display);
    }
    /// <summary>Gets the exact encoded output color space of a display/view pair.</summary>
    public string GetDisplayViewOutputColorSpace(string display, string view)
    {
        OcioNative.ValidateText(display, nameof(display)); OcioNative.ValidateText(view, nameof(view));
        lock (gate) { ThrowIfDisposed(); return OcioNative.ConfigText(handle, 5, display, view); }
    }
    /// <summary>Compiles a CPU conversion between explicitly named OCIO color spaces.</summary>
    public OcioProcessor CreateColorSpaceProcessor(string source, string destination)
    {
        OcioNative.ValidateText(source, nameof(source)); OcioNative.ValidateText(destination, nameof(destination));
        lock (gate)
        {
            ThrowIfDisposed(); OcioNative.Check(OcioNative.ColorProcessor(handle, source, destination, out nint processor));
            return new(new(processor), source, destination, CacheId);
        }
    }
    /// <summary>Compiles an explicit display/view with optional Look override. Its RGB output may be encoded.</summary>
    /// <param name="source">Input OCIO color-space identifier.</param>
    /// <param name="display">Explicit display identifier.</param>
    /// <param name="view">Explicit view identifier.</param>
    /// <param name="lookOverride">Null retains configured Looks; empty bypasses them; otherwise an OCIO Look expression.</param>
    public OcioProcessor CreateDisplayViewProcessor(string source, string display, string view, string? lookOverride = null) =>
        DisplayProcessor(source, display, view, lookOverride, null, null);

    /// <summary>Compiles a display/view followed by an explicitly selected output-to-linear OCIO conversion.</summary>
    /// <remarks>
    /// The declared output must match the view and share the destination's OCIO reference-space type.
    /// This avoids silently applying an inverse view transform while decoding. The caller must ensure
    /// the chosen destination is linear; OCIO names/encoding metadata alone do not prove linearity.
    /// </remarks>
    public OcioProcessor CreateDisplayViewToLinearProcessor(string source, string display, string view,
        string displayOutputColorSpace, string linearDestination, string? lookOverride = null)
    {
        OcioNative.ValidateText(displayOutputColorSpace, nameof(displayOutputColorSpace));
        OcioNative.ValidateText(linearDestination, nameof(linearDestination));
        return DisplayProcessor(source, display, view, lookOverride, displayOutputColorSpace, linearDestination);
    }
    /// <summary>Releases the configuration. Previously created processors remain independently usable.</summary>
    public void Dispose() { lock (gate) handle.Dispose(); }

    private OcioProcessor DisplayProcessor(string source, string display, string view, string? looks, string? decodeSource, string? destination)
    {
        OcioNative.ValidateText(source, nameof(source)); OcioNative.ValidateText(display, nameof(display)); OcioNative.ValidateText(view, nameof(view));
        if (looks?.Contains('\0') == true) throw new ArgumentException("Look expression contains an embedded NUL.", nameof(looks));
        lock (gate)
        {
            ThrowIfDisposed();
            string outputSpace = OcioNative.ConfigText(handle, 5, display, view);
            OcioNative.Check(OcioNative.DisplayProcessor(handle, source, display, view, looks, decodeSource, destination, out nint processor));
            return new(new(processor), source, destination ?? outputSpace, CacheId);
        }
    }
    private IReadOnlyList<string> Enumerate(int kind, string? display = null)
    {
        lock (gate)
        {
            ThrowIfDisposed(); OcioNative.Check(OcioNative.ConfigCount(handle, kind, display, out int count));
            return Array.AsReadOnly(Enumerable.Range(0, count).Select(i => OcioNative.ConfigText(handle, kind, display, index: i)).ToArray());
        }
    }
    private static OcioConfiguration Create(int kind, string value, string? directory)
    {
        OcioNative.EnsureAvailable(); OcioNative.Check(OcioNative.ConfigCreate(kind, value, directory, out nint pointer));
        return new(new(pointer));
    }
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(handle.IsClosed, this);
}
