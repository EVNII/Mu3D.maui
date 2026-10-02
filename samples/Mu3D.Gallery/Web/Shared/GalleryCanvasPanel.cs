using Mu3D.Graphics;

namespace Mu3D.GalleryApp.Web.Shared;

/// <summary>Defines one native Gallery comparison panel at the shared Graphics draw boundary.</summary>
/// <param name="Title">Accessible panel title.</param>
/// <param name="Draw">Serial draw callback; its device and attachments remain borrowed.</param>
public sealed record GalleryCanvasPanel(string Title,
    Action<GraphicsDevice, GraphicsTexture, GraphicsTexture?, double> Draw);
