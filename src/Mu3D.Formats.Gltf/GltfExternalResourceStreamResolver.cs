namespace Mu3D.Formats.Gltf;

/// <summary>Opens one application-approved external glTF buffer or image URI.</summary>
/// <param name="uri">The exact non-data URI authored in the glTF document.</param>
/// <param name="cancellationToken">Cancels URI resolution and stream opening.</param>
/// <returns>
/// A readable stream positioned at the start of the resource. Ownership transfers to the loader,
/// which asynchronously disposes the stream after bounded reading, cancellation or failure.
/// </returns>
public delegate ValueTask<Stream> GltfExternalResourceStreamResolver(
    string uri,
    CancellationToken cancellationToken);
