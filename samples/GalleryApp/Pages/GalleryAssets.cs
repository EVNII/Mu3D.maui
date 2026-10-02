namespace Mu3D.GalleryApp.Pages;

internal static class GalleryAssets
{
    internal static async Task<byte[]> ReadBytesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(path);
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
