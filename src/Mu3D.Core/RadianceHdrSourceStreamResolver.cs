namespace Mu3D.SceneGraph;

/// <summary>Opens a readable Radiance HDR source stream for one exact application-defined key.</summary>
/// <param name="cancellationToken">Cancels opening the source.</param>
/// <returns>A readable stream whose ownership transfers to the loader.</returns>
public delegate ValueTask<Stream> RadianceHdrSourceStreamResolver(
    CancellationToken cancellationToken);
