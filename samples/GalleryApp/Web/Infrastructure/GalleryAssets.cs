namespace Mu3D.GalleryApp.Pages;

// Same example-owned asset boundary as native Gallery; the browser supplies the transport.
internal static class GalleryAssets
{
    private const int MaximumByteCount = 64 * 1024 * 1024;
    private static HttpClient? client;

    internal static void Configure(Uri applicationBaseUri)
    {
        ArgumentNullException.ThrowIfNull(applicationBaseUri);
        if (!applicationBaseUri.IsAbsoluteUri || applicationBaseUri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Gallery assets require an HTTP(S) application base URI.", nameof(applicationBaseUri));
        client ??= new HttpClient { BaseAddress = new Uri(applicationBaseUri, "assets/") };
    }

    internal static async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\\') || path.Contains('%') || path.Contains(':') || path.Contains('?') || path.Contains('#') ||
            path.Split('/').Any(static segment => segment is "" or "." or ".."))
            throw new ArgumentException("An exact relative Gallery asset path is required.", nameof(path));
        HttpClient transport = client ?? throw new InvalidOperationException("Gallery asset transport is not configured.");
        using HttpResponseMessage response = await transport.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumByteCount)
            throw new InvalidDataException("Gallery asset exceeds the bounded source limit.");
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream destination = new();
        byte[] buffer = new byte[64 * 1024];
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (destination.Length + count > MaximumByteCount)
                throw new InvalidDataException("Gallery asset exceeds the bounded source limit.");
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }
        return destination.ToArray();
    }
}
