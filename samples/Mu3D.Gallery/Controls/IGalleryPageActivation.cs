namespace Mu3D.Gallery.Controls;

/// <summary>Receives navigation visibility independently of native visual-tree attachment.</summary>
internal interface IGalleryPageActivation
{
    void SetNavigationActive(bool active);
}
