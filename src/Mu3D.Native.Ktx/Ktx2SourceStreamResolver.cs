namespace Mu3D.Native.Ktx;

/// <summary>Opens one application-approved KTX2 source stream for bounded asynchronous loading.</summary>
/// <param name="cancellationToken">Cancels source acquisition.</param>
/// <returns>
/// A readable stream whose ownership transfers to the loader, or a failed value task when the
/// application rejects or cannot open the source.
/// </returns>
public delegate ValueTask<Stream> Ktx2SourceStreamResolver(
    CancellationToken cancellationToken);
