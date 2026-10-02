using System.ComponentModel;
using System.Numerics;
using MauiColor = Microsoft.Maui.Graphics.Color;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Native.UltraHdr;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Assets;

namespace Mu3D.GalleryApp.Pages;

/// <summary>One lightweight product record; no native view or presentation surface is stored here.</summary>
public sealed class ProductPreview : INotifyPropertyChanged
{
    private bool isLoading;
    private bool isPlaceholderVisible = true;
    private string stateText = "Poster";

    internal ProductPreview(
        string name,
        string price,
        int index,
        ProductModel model)
    {
        bool usesTransparentBackground = model.AlternatesHdrTransparencyProbe
            ? (index / ProductModels.All.Length) % 2 == 0
            : index % 2 == 0;
        Name = model.AlternatesHdrTransparencyProbe
            ? $"{name} · {(usesTransparentBackground ? "synchronized HDR mask" : "opaque direct HDR")}"
            : name;
        Price = price;
        Index = index;
        Model = model;
        SceneBackgroundColor = usesTransparentBackground
            ? Colors.Transparent
            : MauiColor.FromArgb("#FF7D8998");
    }

    /// <summary>Gets the product display name.</summary>
    public string Name { get; }

    /// <summary>Gets the demonstration price.</summary>
    public string Price { get; }

    /// <summary>Gets whether this product is still represented by its MAUI placeholder.</summary>
    public bool IsPlaceholderVisible => isPlaceholderVisible;

    /// <summary>Gets whether managed loading is currently queued or active.</summary>
    public bool IsLoading => isLoading;

    /// <summary>Gets whether this item can request a shared-device Surface frame.</summary>
    public bool IsLive => !isPlaceholderVisible && Content is not null;

    /// <summary>Gets the transparent-mask or opaque-control scene background.</summary>
    public MauiColor SceneBackgroundColor { get; }

    /// <summary>Gets the borrowed loaded scene, or null while the poster is cold.</summary>
    public Scene? Scene => Content?.Scene;

    /// <summary>Gets the borrowed loaded camera, or null while the poster is cold.</summary>
    public Camera? Camera => Content?.Camera;

    /// <summary>Gets the concise current product rendering state.</summary>
    public string StateText => stateText;

    internal int Index { get; }

    internal ProductModel Model { get; }

    internal ProductSceneContent? Content { get; private set; }

    internal bool AdvanceAnimation(TimeSpan elapsed) =>
        Content?.AdvanceAnimation(elapsed) == true;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void MarkLoading(bool visible) =>
        SetState(visible ? "Loading…" : "Prefetch", placeholder: true, loading: true);

    internal void SetContent(ProductSceneContent content)
    {
        Content = content;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scene)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Camera)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLive)));
        SetState("Ready", placeholder: true, loading: false);
    }

    internal void MarkLive() => SetState("Live", placeholder: false, loading: false);

    internal void MarkWarm() =>
        SetState(Content is null ? "Poster" : "Warm", placeholder: true, loading: false);

    internal void MarkFailed(Exception exception) =>
        SetState($"Failed: {exception.Message}", placeholder: true, loading: false);

    internal void CancelLoading()
    {
        if (isLoading)
        {
            MarkWarm();
        }
    }

    internal void ReleaseContent()
    {
        bool wasLive = IsLive;
        Content = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scene)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Camera)));
        if (wasLive)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLive)));
        }
        SetState("Poster", placeholder: true, loading: false);
    }

    private void SetState(string text, bool placeholder, bool loading)
    {
        if (stateText != text)
        {
            stateText = text;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StateText)));
        }
        if (isPlaceholderVisible != placeholder)
        {
            isPlaceholderVisible = placeholder;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaceholderVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLive)));
        }
        if (isLoading != loading)
        {
            isLoading = loading;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoading)));
        }
    }
}
