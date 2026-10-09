using System.Reflection;
using Mu3D.Gallery.Controls;

internal static class GalleryActivationChecks
{
    internal static void Verify()
    {
        HashSet<Page> shown = [];
        using GalleryPageActivation activation = new(page => shown.Contains(page));
        ActivationPage first = new();
        ActivationPage second = new();
        // Portable MAUI defines IsLoaded through logical Window attachment. These objects create
        // no native windows; the separate visibility predicate models the platform readiness gap.
        Window firstWindow = new(first);
        Window secondWindow = new(second);
        activation.Activate(first);
        Lifecycle(first, "SendLoaded");
        Expect(first.StartCount == 0 && first.Gate.Children.Count == 0,
            "managed Loaded before native visibility retains pending selection");
        shown.Add(first);
        activation.OnNativePageVisibilityChanged(first, true);
        Expect(first.StartCount == 1 && first.Gate.Children.Count == 1,
            "native readiness activates first entry without selecting twice");
        activation.OnNativePageVisibilityChanged(first, true);
        Expect(first.StartCount == 1, "duplicate readiness does not start another owner");

        activation.Activate(second);
        Expect(first.StopCount == 1 && first.Gate.Children.Count == 0,
            "selection releases the previous rendering tree");
        Lifecycle(second, "SendLoaded");
        activation.OnNativePageVisibilityChanged(first, true);
        Expect(second.StartCount == 0 && first.StartCount == 1,
            "stale native notification cannot activate a different pending selection");
        shown.Add(second);
        activation.OnNativePageVisibilityChanged(second, true);
        Expect(second.StartCount == 1 && second.Gate.Children.Count == 1, "new selection resumes when ready");
        shown.Remove(second);
        activation.OnNativePageVisibilityChanged(second, false);
        Expect(second.StopCount == 1 && second.Gate.Children.Count == 0, "native hide suspends rendering");
        shown.Add(second);
        activation.OnNativePageVisibilityChanged(second, true);
        Expect(second.StartCount == 2, "same selected page resumes after native visibility returns");
        activation.Dispose();
        activation.OnNativePageVisibilityChanged(second, true);
        Expect(second.StartCount == 2 && second.StopCount == 2, "late readiness after disposal stays inactive");
        GC.KeepAlive(firstWindow);
        GC.KeepAlive(secondWindow);
        VerifyPageRenderingIntent();
    }

    private static void VerifyPageRenderingIntent()
    {
        foreach ((bool initial, bool onActivation) in new[] { (false, true), (true, false) })
        {
            RenderingIntentPage page = new(initial, onActivation);
            Window window = new(page);
            using GalleryPageActivation activation = new(_ => true);
            activation.Activate(page);
            Expect(page.Host.IsRenderingEnabled == onActivation,
                "page initialization owns the final rendering switch rather than the pre-suspension snapshot");
            Expect(page.Host.Parent is Grid, "initialized content reattaches after the activation callback");
            activation.Deactivate();
            Expect(!page.Host.IsRenderingEnabled, "inactive page suspends its host");
            activation.Activate(page);
            Expect(page.Host.IsRenderingEnabled == onActivation, "re-entry preserves the page activation decision");
            GC.KeepAlive(window);
        }
    }

    private sealed class RenderingIntentPage : ContentPage, IGalleryPageActivation
    {
        private readonly bool onActivation;
        internal RenderingIntentPage(bool initial, bool onActivation)
        {
            this.onActivation = onActivation;
            Host = new() { IsRenderingEnabled = initial };
            Content = new Grid { Children = { Host } };
        }
        internal Mu3D.Maui.Toolkit.Controls.SceneViewProxyHost Host { get; }
        void IGalleryPageActivation.SetNavigationActive(bool active)
            => Host.IsRenderingEnabled = active && onActivation;
    }

    private static void Lifecycle(VisualElement element, string name) =>
        typeof(VisualElement).GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null)!
            .Invoke(element, null);

    private static void Expect(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }

    private sealed class ActivationPage : ContentPage, IGalleryPageActivation
    {
        internal ActivationPage() => Content = new Grid();
        internal int StartCount { get; private set; }
        internal int StopCount { get; private set; }
        internal Grid Gate => (Grid)Content!;
        void IGalleryPageActivation.SetNavigationActive(bool active)
        {
            if (active) StartCount++;
            else StopCount++;
        }
    }
}
